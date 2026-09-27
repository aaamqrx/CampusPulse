using System.Net;
using CampusPulse.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("NET-12 normalizes carrier suffix and encodes credentials", LoginEncoding),
    ("NET-10 one successful probe prevents authentication", PartialConnectivity),
    ("NET-09 portal identity mismatch prevents authentication", PortalMismatch),
    ("NET-02 valid portal permits one login request", LoginOnce),
    ("NET-01 verified internet skips portal and login", AlreadyOnline),
    ("NET-08 HTTP 200 with portal content is not internet", PortalContentIsNotInternet),
    ("SYS-03 changed network path prevents login", NetworkPathChanged),
    ("NET-06 rejected credentials are classified conservatively", Rejection),
    ("NET-05 retry delay remains bounded", RetryBounds)
};
int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name} {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} offline checks passed");
return failed == 0 ? 0 : 1;

static async Task LoginEncoding()
{
    Check(DrComProtocol.TryNormalizeUsername(" student@dx ", out var name) && name == "student", "suffix normalization");
    Check(!DrComProtocol.TryNormalizeUsername("student@other", out _), "unsupported suffix rejected");
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student@dx", "dummy+&?#", CancellationToken.None);
    Check(result.Accepted, "login accepted fixture");
    string query = fixture.Requests.Single(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)).Query;
    Check(query.Contains("user_account=%2C0%2Cstudent%40dx", StringComparison.Ordinal), "account encoded once");
    Check(query.Contains("user_password=dummy%2B%26%3F%23", StringComparison.Ordinal), "password encoded");
}

static async Task PartialConnectivity()
{
    using var fixture = new Fixture { OneProbeSucceeds = true };
    var check = await fixture.Client.CheckAsync(CancellationToken.None);
    Check(check.PartialConnectivity && !check.NeedsAuthentication, "partial state");
    var login = await fixture.Client.LoginAsync("student", "dummy", CancellationToken.None);
    Check(login.ReasonCode == "authentication_not_required", "login suppressed");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static async Task PortalMismatch()
{
    using var fixture = new Fixture { BadPortal = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "portal_unrecognized", "portal refused");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static async Task LoginOnce()
{
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student", "dummy", CancellationToken.None);
    Check(result.Accepted && result.ReasonCode == "authentication_accepted", "accepted is not internet verified");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1, "one login request");
    Check(fixture.Requests.All(uri => uri.Host is "10.62.164.14" or "www.msftconnecttest.com" or "detectportal.firefox.com"), "fixed hosts");
}

static async Task AlreadyOnline()
{
    using var fixture = new Fixture { BothProbesSucceed = true };
    var check = await fixture.Client.CheckAsync(CancellationToken.None);
    Check(check.InternetAvailable && check.ReasonCode == "internet_verified", "both probes verified");
    var login = await fixture.Client.LoginAsync("student", "dummy", CancellationToken.None);
    Check(login.ReasonCode == "authentication_not_required", "login skipped");
    Check(fixture.Requests.All(uri => uri.Host != "10.62.164.14"), "portal not contacted");
}

static async Task PortalContentIsNotInternet()
{
    using var fixture = new Fixture();
    var check = await fixture.Client.CheckAsync(CancellationToken.None);
    Check(!check.InternetAvailable && check.NeedsAuthentication, "fake HTTP 200 is not internet");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "read-only check sent no password");
}

static async Task NetworkPathChanged()
{
    using var fixture = new Fixture { ChangePathBeforeLogin = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "network_path_changed", "changed path blocked");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static Task Rejection()
{
    var rejected = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":1})");
    var unknown = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":7,\"msg\":\"unfamiliar\"})");
    Check(rejected.CredentialsRejected && rejected.ReasonCode == "credentials_rejected", "known code");
    Check(!unknown.CredentialsRejected && unknown.ReasonCode == "authentication_unknown", "unknown code");
    return Task.CompletedTask;
}

static Task RetryBounds()
{
    for (int count = 1; count <= 20; count++)
    {
        var delay = RetryPolicy.GetDelay(count, 1, TimeSpan.FromHours(1));
        Check(delay >= TimeSpan.FromSeconds(5) && delay <= TimeSpan.FromMinutes(5), "bounded delay");
    }
    return Task.CompletedTask;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class Fixture : IDisposable
{
    private const string Portal = "<!-- Dr.COMWebLoginID_0.htm --><script>carrier = '{\"yys\":{\"data\":[{\"id\":\"2\",\"suffix\":\"@dx\"}]}}'; v46ip = '10.1.2.3'; ss4 = 'ABCDEF123456'; vlanid = '1';</script>";
    private const string Config = "campuspulse({\"code\":1,\"data\":{\"login_method\":1,\"account_prefix\":1,\"en_md5\":0,\"password_cut\":0,\"enable_r3\":0,\"enable_https\":0,\"ep_http_port\":801,\"check_online_method\":0,\"io_mode\":0,\"ipv6_state\":0,\"account_suffix\":\"\",\"domain_name\":\"\"}})";
    public bool OneProbeSucceeds { get; set; }
    public bool BothProbesSucceed { get; set; }
    public bool BadPortal { get; set; }
    public bool ChangePathBeforeLogin { get; set; }
    public List<Uri> Requests { get; } = [];
    public CampusNetworkClient Client { get; }

    public Fixture()
    {
        Client = new CampusNetworkClient(new FixedResolver(this), _ => new FakeHandler(this));
    }
    public void Dispose() => Client.Dispose();

    private sealed class FixedResolver(Fixture fixture) : INetworkPathResolver
    {
        private int calls;
        public CampusNetworkPath? Resolve() => new(IPAddress.Parse("10.1.2.3"),
            fixture.ChangePathBeforeLogin && ++calls > 1 ? 8 : 7, "ABCDEF123456");
    }
    private sealed class FakeHandler(Fixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new Exception("missing target");
            fixture.Requests.Add(uri);
            string body = uri.AbsolutePath switch
            {
                "/connecttest.txt" => fixture.OneProbeSucceeds || fixture.BothProbesSucceed ? "Microsoft Connect Test" : "portal page",
                "/success.txt" => fixture.BothProbesSucceed ? "success\n" : "portal page",
                "/" => fixture.BadPortal ? "unexpected page" : Portal,
                "/eportal/portal/page/loadConfig" => Config,
                "/drcom/chkstatus" => "campuspulse({\"result\":0,\"v46ip\":\"10.1.2.3\"})",
                "/a40.js" => "jsVersion = '4.2.0';",
                "/eportal/portal/login" => "campuspulse({\"result\":1})",
                _ => throw new Exception("unexpected target")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
