using System.Net;
using System.Text.Json;
using CampusPulse.Core;
using CampusPulse.Service;

var tests = new (string Name, Func<Task> Run)[]
{
    ("NET-12 normalizes carrier suffix and encodes credentials", LoginEncoding),
    ("NET-12 all four portal choices use their rendered suffixes", CarrierChoices),
    ("NET-09 missing selected option prevents credential submission", CarrierOptionMissing),
    ("CFG-06 mismatched suffix and unknown carrier are rejected", CarrierInputRejected),
    ("NET-02 intranet login is not reported as public internet", IntranetOnly),
    ("CFG-06 custom portal address rejects unsafe roots", PortalAddressValidation),
    ("NET-09 custom supported portal keeps all credential requests on chosen host", CustomPortal),
    ("SEC-03 old credentials pause and new credentials bind to carrier and portal", CredentialIdentity),
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
    Check(DrComProtocol.TryNormalizeUsername(" student@telecom ", "telecom", out var name) && name == "student", "suffix normalization");
    Check(!DrComProtocol.TryNormalizeUsername("student@other", "telecom", out _), "unsupported suffix rejected");
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student@telecom", "dummy+&?#", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.Accepted, "login accepted fixture");
    string query = fixture.Requests.Single(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)).Query;
    Check(query.Contains("user_account=%2C0%2Cstudent%40telecom", StringComparison.Ordinal), "account encoded once");
    Check(query.Contains("user_password=dummy%2B%26%3F%23", StringComparison.Ordinal), "password encoded");
}

static async Task CarrierChoices()
{
    foreach (var (carrier, suffix) in new[]
    {
        ("unicom", "@unicom"), ("mobile", "@cmcc"), ("telecom", "@telecom"), ("intranet", "")
    })
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.LoginAsync("student", "dummy", carrier,
            CampusSettings.SupportedPortal, CancellationToken.None);
        Check(result.Accepted, $"{carrier} accepted by fixture");
        var login = fixture.Requests.Single(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal));
        Check(login.Query.Contains("user_account=" + Uri.EscapeDataString(",0,student" + suffix), StringComparison.Ordinal),
            $"{carrier} suffix");
    }
}

static async Task CarrierOptionMissing()
{
    using var fixture = new Fixture { WithoutMobileOption = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "mobile",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "portal_unrecognized", "missing template option rejected");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static async Task CarrierInputRejected()
{
    Check(!DrComProtocol.TryNormalizeUsername("student@dx", "telecom", out _), "old suffix rejected");
    Check(!DrComProtocol.TryNormalizeUsername("student@cmcc", "unicom", out _), "other carrier suffix rejected");
    Check(!DrComProtocol.TryNormalizeUsername("student@cmcc@cmcc", "mobile", out _), "duplicate suffix rejected");
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student", "dummy", "invalid",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.ReasonCode == "unsupported_carrier" && fixture.Requests.Count == 0, "unknown carrier makes no request");
}

static async Task IntranetOnly()
{
    using var fixture = new Fixture { PortalOnline = true };
    var result = await fixture.Client.CheckAsync("intranet", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.IntranetAvailable && !result.InternetAvailable && !result.NeedsAuthentication, "intranet is not public internet");
}

static Task PortalAddressValidation()
{
    Check(PortalEndpoint.TryCreate("http://10.42.1.5/", out var endpoint) &&
        endpoint.Root.AbsoluteUri == "http://10.42.1.5/", "private portal accepted");
    foreach (var address in new[]
    {
        "http://127.0.0.1/", "http://169.254.1.2/", "http://8.8.8.8/", "https://10.42.1.5/",
        "http://10.42.1.5:801/", "http://10.42.1.5/login", "http://user@10.42.1.5/",
        "http://10.42.1.5/?token=1", "http://localhost/"
    })
        Check(!PortalEndpoint.TryCreate(address, out _), $"unsafe portal rejected: {address}");
    return Task.CompletedTask;
}

static async Task CustomPortal()
{
    const string portal = "http://10.42.1.5/";
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student", "dummy", "unicom", portal, CancellationToken.None);
    Check(result.Accepted, "supported Dr.COM fixture accepted");
    Check(fixture.Requests.Where(uri => uri.Host != CampusNetworkClient.MicrosoftProbe.Host &&
        uri.Host != CampusNetworkClient.MozillaProbe.Host).All(uri => uri.Host == "10.42.1.5"),
        "portal requests restricted to selected host");
    Check(fixture.Requests.Any(uri => uri.Host == "10.42.1.5" && uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)),
        "login used chosen host");
}

static Task CredentialIdentity()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(
            new CampusSettings { ConfigVersion = 1, Enabled = true, Username = "student" }));
        var migrated = store.LoadSettings();
        Check(migrated.ConfigVersion == 2 && !migrated.Enabled, "old automatic reconnect paused");
        Check(migrated.PortalUrl == CampusSettings.SupportedPortal, "old portal default retained");

        var settings = new CampusSettings { Username = "student", Carrier = "mobile", PortalUrl = "http://10.42.1.5/" };
        store.SaveConfiguration(settings, new StoredCredential("student", "dummy"));
        Check(store.LoadCredential("student", "telecom", CampusSettings.SupportedPortal) is null,
            "legacy credential cannot be reused");

        store.SaveConfiguration(settings, new StoredCredential("student", "dummy")
            { ProtocolVersion = 2, Carrier = "mobile", PortalUrl = settings.PortalUrl });
        Check(store.LoadCredential("student", "mobile", settings.PortalUrl) is not null, "matching credential loads");
        Check(store.LoadCredential("student", "telecom", settings.PortalUrl) is null, "carrier mismatch blocks credential");
        Check(store.LoadCredential("student", "mobile", CampusSettings.SupportedPortal) is null,
            "portal mismatch blocks credential");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
    return Task.CompletedTask;
}

static async Task PartialConnectivity()
{
    using var fixture = new Fixture { OneProbeSucceeds = true };
    var check = await fixture.Client.CheckAsync("telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(check.PartialConnectivity && !check.NeedsAuthentication, "partial state");
    var login = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(login.ReasonCode == "authentication_not_required", "login suppressed");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static async Task PortalMismatch()
{
    using var fixture = new Fixture { BadPortal = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "portal_unrecognized", "portal refused");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "no credential request");
}

static async Task LoginOnce()
{
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.Accepted && result.ReasonCode == "authentication_accepted", "accepted is not internet verified");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1, "one login request");
    Check(fixture.Requests.All(uri => uri.Host is "10.62.164.14" or "www.msftconnecttest.com" or "detectportal.firefox.com"), "fixed hosts");
}

static async Task AlreadyOnline()
{
    using var fixture = new Fixture { BothProbesSucceed = true };
    var check = await fixture.Client.CheckAsync("telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(check.InternetAvailable && check.ReasonCode == "internet_verified", "both probes verified");
    var login = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(login.ReasonCode == "authentication_not_required", "login skipped");
    Check(fixture.Requests.All(uri => uri.Host != "10.62.164.14"), "portal not contacted");
}

static async Task PortalContentIsNotInternet()
{
    using var fixture = new Fixture();
    var check = await fixture.Client.CheckAsync("telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!check.InternetAvailable && check.NeedsAuthentication, "fake HTTP 200 is not internet");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)), "read-only check sent no password");
}

static async Task NetworkPathChanged()
{
    using var fixture = new Fixture { ChangePathBeforeLogin = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
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
    private const string Portal = "<!-- Dr.COMWebLoginID_0.htm --><script>v46ip = '10.1.2.3'; ss4 = 'ABCDEF123456'; vlanid = '1';</script>";
    private const string Config = "campuspulse({\"code\":1,\"data\":{\"login_method\":1,\"account_prefix\":1,\"en_md5\":0,\"password_cut\":0,\"enable_r3\":0,\"enable_https\":0,\"ep_http_port\":801,\"check_online_method\":0,\"io_mode\":0,\"ipv6_state\":0,\"account_suffix\":\"\",\"domain_name\":\"\",\"program_index\":\"testProgram\",\"page_index\":\"testPage\"}})";
    private const string Template = "<select name=\"ISP_select\"><option value=\"-1\">请选择运营商</option><option value=\"@unicom\">中国联通</option><option value=\"@cmcc\">中国移动</option><option value=\"@telecom\">中国电信</option><option value=\"\">校内网（无外网）</option></select>";
    public bool OneProbeSucceeds { get; set; }
    public bool BothProbesSucceed { get; set; }
    public bool BadPortal { get; set; }
    public bool ChangePathBeforeLogin { get; set; }
    public bool WithoutMobileOption { get; set; }
    public bool PortalOnline { get; set; }
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
        public CampusNetworkPath? Resolve(IPAddress portalAddress) => new(IPAddress.Parse("10.1.2.3"),
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
                "/eportal/extern/testProgram/testPage/pc.js" => fixture.WithoutMobileOption
                    ? Template.Replace("<option value=\"@cmcc\">中国移动</option>", "", StringComparison.Ordinal) : Template,
                "/drcom/chkstatus" => fixture.PortalOnline
                    ? "campuspulse({\"result\":1,\"v46ip\":\"10.1.2.3\"})"
                    : "campuspulse({\"result\":0,\"v46ip\":\"10.1.2.3\"})",
                "/a40.js" => "jsVersion = '4.2.0';",
                "/eportal/portal/login" => "campuspulse({\"result\":1})",
                _ => throw new Exception("unexpected target")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
