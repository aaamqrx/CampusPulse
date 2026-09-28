using System.Net;
using System.Text.Json;
using CampusPulse.Core;
using CampusPulse.Service;

var tests = new (string Name, Func<Task> Run)[]
{
    ("NET-12 normalizes carrier suffix and encodes credentials", LoginEncoding),
    ("NET-12 all four portal choices use their rendered suffixes", CarrierChoices),
    ("NET-09 missing selected option prevents credential submission", CarrierOptionMissing),
    ("NET-09 unrelated or duplicate carrier options prevent credential submission", CarrierTemplateAmbiguity),
    ("CFG-06 mismatched suffix and unknown carrier are rejected", CarrierInputRejected),
    ("NET-02 intranet login is not reported as public internet", IntranetOnly),
    ("CFG-06 custom portal address rejects unsafe roots", PortalAddressValidation),
    ("NET-09 custom supported portal keeps all credential requests on chosen host", CustomPortal),
    ("NET-09 redirected template refuses credential submission", RedirectedTemplate),
    ("SEC-03 old credentials pause and new credentials bind to carrier and portal", CredentialIdentity),
    ("NET-06 blocked authentication survives settings reload", BlockedAuthenticationPersists),
    ("NET-06 rejected authentication remains blocked after worker restart", WorkerRestartKeepsRejection),
    ("NET-10 one successful probe prevents authentication", PartialConnectivity),
    ("NET-10 Ethernet DNS accepts a public CNAME target and rejects fake IP", BoundDnsResponse),
    ("NET-09 portal identity mismatch prevents authentication", PortalMismatch),
    ("NET-02 valid portal permits one login request", LoginOnce),
    ("NET-01 verified internet skips portal and login", AlreadyOnline),
    ("NET-08 HTTP 200 with portal content is not internet", PortalContentIsNotInternet),
    ("SYS-03 changed network path prevents login", NetworkPathChanged),
    ("NET-06 rejected credentials are classified conservatively", Rejection),
    ("NET-05 server retry limit is bounded", ServerRetryLimit),
    ("NET-11 stalled login times out without reporting success", StalledLoginTimesOut),
    ("NET-11 concurrent reconnects serialize credential requests", ConcurrentReconnects),
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

static async Task CarrierTemplateAmbiguity()
{
    using var unrelated = new Fixture
    {
        TemplateOverride = "<select name=\"ISP_select\"><option value=\"@telecom\">中国电信</option></select>" +
            "<select name=\"other\"><option value=\"@cmcc\">中国移动</option></select>"
    };
    var unrelatedResult = await unrelated.Client.LoginAsync("student", "dummy", "mobile",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(unrelatedResult.ReasonCode == "portal_unrecognized" &&
        unrelated.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)),
        "unrelated dropdown cannot authorize credential submission");

    using var duplicate = new Fixture
    {
        TemplateOverride = "<select name=\"ISP_select\"><option value=\"@cmcc\">中国移动</option>" +
            "<option value=\"@cmcc\">重复选项</option></select>"
    };
    var duplicateResult = await duplicate.Client.LoginAsync("student", "dummy", "mobile",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(duplicateResult.ReasonCode == "portal_unrecognized" &&
        duplicate.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)),
        "duplicate carrier suffix cannot authorize credential submission");
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

static async Task RedirectedTemplate()
{
    using var fixture = new Fixture { RedirectTemplate = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "portal_unrecognized", "redirect refused");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)),
        "credentials not submitted after template redirect");
}

static Task BlockedAuthenticationPersists()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        store.SaveSettings(new CampusSettings
        {
            Enabled = true, Username = "student", AuthenticationBlocked = true,
            BlockedReason = "校园认证拒绝账号或密码，请检查后主动重试"
        });
        var reloaded = new SecureStore(directory).LoadSettings();
        Check(reloaded.Enabled && reloaded.AuthenticationBlocked && reloaded.BlockedReason.Length > 0,
            "rejected state retained after settings reload");
    }
    finally { Directory.Delete(directory, recursive: true); }
    return Task.CompletedTask;
}

static async Task WorkerRestartKeepsRejection()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        var settings = new CampusSettings { Enabled = true, Username = "student", Carrier = "telecom" };
        store.SaveConfiguration(settings, new StoredCredential("student", "dummy")
        { ProtocolVersion = 2, Carrier = "telecom", PortalUrl = settings.PortalUrl });
        using var fixture = new Fixture { RejectLogin = true };
        using (var first = new ConnectionWorker(store, new StartupManager(), fixture.Client))
        {
            await first.StartAsync(CancellationToken.None);
            try
            {
                await WaitUntil(() => first.Snapshot.State == ConnectionState.AuthenticationRejected,
                    "first worker records credential rejection");
            }
            finally { await first.StopAsync(CancellationToken.None); }
        }
        int submissions = fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal));
        Check(submissions == 1 && store.LoadSettings().AuthenticationBlocked,
            "one rejected login persisted before restart");

        using (var second = new ConnectionWorker(new SecureStore(directory), new StartupManager(), fixture.Client))
        {
            Check(second.Snapshot.State == ConnectionState.AuthenticationRejected,
                "restarted worker starts in rejected state");
            await second.StartAsync(CancellationToken.None);
            try
            {
                await WaitUntil(() => second.Snapshot.ErrorCode == "AuthenticationBlocked",
                    "restarted worker retains automatic authentication block");
                Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == submissions,
                    "restarted worker made no new credential request");
            }
            finally { await second.StopAsync(CancellationToken.None); }
        }
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task WaitUntil(Func<bool> condition, string message)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition())
    {
        if (deadline.IsCancellationRequested) throw new Exception(message);
        await Task.Delay(25);
    }
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

static Task BoundDnsResponse()
{
    const string response = "FFBE818000010002000000000C646574656374706F7274616C0766697265666F7803636F6D0000010001" +
        "C00C000500010000002D0018076D6F7A696C6C61036D617006666173746C79036E657400" +
        "C03600010001000000480004C7E8A15B";
    byte[] reply = Convert.FromHexString(response);
    var addresses = BoundDnsResolver.ParseResponse(reply, "detectportal.firefox.com", 0xFFBE);
    Check(addresses.Length == 1 && addresses[0].Equals(IPAddress.Parse("199.232.161.91")),
        "public CNAME target accepted");
    reply[^4] = 198; reply[^3] = 18; reply[^2] = 0; reply[^1] = 77;
    Check(BoundDnsResolver.ParseResponse(reply, "detectportal.firefox.com", 0xFFBE).Length == 0,
        "virtual fake IP rejected");
    Check(BoundDnsResolver.ParseResponse(reply, "detectportal.firefox.com", 0x1234).Length == 0,
        "unmatched transaction rejected");
    return Task.CompletedTask;
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

static async Task ServerRetryLimit()
{
    using var fixture = new Fixture { RateLimitLogin = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "portal_rate_limited", "rate limit classified");
    Check(result.RetryAfter == TimeSpan.FromMinutes(5), "server retry delay capped at five minutes");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1,
        "one credential request before cooldown");
}

static async Task StalledLoginTimesOut()
{
    using var fixture = new Fixture { StallLogin = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(!result.Accepted && result.ReasonCode == "authentication_timeout", "stalled request classified as timeout");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1,
        "timeout did not create another credential request");
}

static async Task ConcurrentReconnects()
{
    using var fixture = new Fixture { DelayLogin = true };
    var first = fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    var second = fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    var results = await Task.WhenAll(first, second);
    Check(results.All(result => result.Accepted), "both simulated requests completed");
    Check(fixture.MaxLoginInFlight == 1, "credential submissions serialized");
}

static Task RetryBounds()
{
    Check(RetryPolicy.GetDelay(1, jitter: 0, retryAfter: TimeSpan.FromMinutes(20)) == TimeSpan.FromMinutes(5),
        "server requested delay remains bounded at five minutes");
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
    public string? TemplateOverride { get; set; }
    public bool PortalOnline { get; set; }
    public bool RedirectTemplate { get; set; }
    public bool RateLimitLogin { get; set; }
    public bool DelayLogin { get; set; }
    public bool StallLogin { get; set; }
    public bool RejectLogin { get; set; }
    public int MaxLoginInFlight { get; private set; }
    private int activeLogins;
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
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new Exception("missing target");
            fixture.Requests.Add(uri);
            if (fixture.RedirectTemplate && uri.AbsolutePath.EndsWith("/pc.js", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                { Headers = { Location = new Uri("http://example.com/other-template") } };
            if (fixture.RateLimitLogin && uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                var limited = new HttpResponseMessage((HttpStatusCode)429);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
                return limited;
            }
            if (fixture.DelayLogin && uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                int active = Interlocked.Increment(ref fixture.activeLogins);
                fixture.MaxLoginInFlight = Math.Max(fixture.MaxLoginInFlight, active);
                try { await Task.Delay(100, cancellationToken); }
                finally { Interlocked.Decrement(ref fixture.activeLogins); }
            }
            if (fixture.StallLogin && uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            string body = uri.AbsolutePath switch
            {
                "/connecttest.txt" => fixture.OneProbeSucceeds || fixture.BothProbesSucceed ? "Microsoft Connect Test" : "portal page",
                "/success.txt" => fixture.BothProbesSucceed ? "success\n" : "portal page",
                "/" => fixture.BadPortal ? "unexpected page" : Portal,
                "/eportal/portal/page/loadConfig" => Config,
                "/eportal/extern/testProgram/testPage/pc.js" => fixture.TemplateOverride ??
                    (fixture.WithoutMobileOption
                        ? Template.Replace("<option value=\"@cmcc\">中国移动</option>", "", StringComparison.Ordinal) : Template),
                "/drcom/chkstatus" => fixture.PortalOnline
                    ? "campuspulse({\"result\":1,\"v46ip\":\"10.1.2.3\"})"
                    : "campuspulse({\"result\":0,\"v46ip\":\"10.1.2.3\"})",
                "/a40.js" => "jsVersion = '4.2.0';",
                "/eportal/portal/login" => fixture.RejectLogin
                    ? "campuspulse({\"result\":0,\"ret_code\":1})" : "campuspulse({\"result\":1})",
                _ => throw new Exception("unexpected target")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
