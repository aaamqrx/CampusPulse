using System.Net;
using System.IO.Compression;
using System.Reflection;
using System.Text;
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
    ("SEC-02/03 replacement and clearing keep passwords out of stored plaintext", CredentialReplacementAndClearing),
    ("SEC-02 rejected responses and transport errors keep secrets out of diagnostics", FailedAuthenticationDiagnostics),
    ("CFG-04 failed settings replacement restores previous credential", ConfigurationRollback),
    ("NET-06 blocked authentication survives settings reload", BlockedAuthenticationPersists),
    ("NET-06 rejected authentication remains blocked after worker restart", WorkerRestartKeepsRejection),
    ("RECOVERY-01 automatic recovery retries ambiguous rejection on the unchanged network path", AutomaticRecoveryAfterAmbiguousRejection),
    ("RECOVERY-02 first rejection survives event rollover and worker reconstruction", RejectionDiagnosticsSurviveRollover),
    ("RECOVERY-03 submission source survives external internet recovery", SubmissionSourceAndExternalRecovery),
    ("RECOVERY-04 preflight refusal is not recorded as a credential submission", PreflightRefusalHasNoSubmission),
    ("RECOVERY-05 ambiguous rejection obeys cooldown and non-authenticating states", AmbiguousRejectionRespectsGuards),
    ("RECOVERY-06 legacy ambiguous block waits before automatic revalidation", LegacyAmbiguousBlockRevalidation),
    ("RECOVERY-07 legacy confirmed payment and account restrictions remain blocked", LegacyConfirmedBlockStaysProtected),
    ("RECOVERY-08 legacy revalidation repeats ambiguous cooldown then preserves confirmed rejection", LegacyRevalidationClassifiesNewOutcome),
    ("RECOVERY-09 legacy history cannot fabricate submission diagnostics", LegacyHistoryLoadsWithoutFabricatedDiagnostics),
    ("RECOVERY-10 explicit retry bypasses automatic cooldown while retaining a minimum submission interval", ManualRetryRetainsMinimumInterval),
    ("RECOVERY-11 failed manual retry does not release a confirmed credential block", ManualUnknownKeepsConfirmedBlock),
    ("RECOVERY-12 an automatic retry deadline survives worker reconstruction", AutomaticCooldownSurvivesRestart),
    ("RECOVERY-13 oversized status keeps its diagnostic summary within the pipe budget", StatusReplyKeepsSummary),
    ("NET-10 one successful probe prevents authentication", PartialConnectivity),
    ("NET-10 Ethernet DNS accepts a public CNAME target and rejects fake IP", BoundDnsResponse),
    ("NET-09 portal identity mismatch prevents authentication", PortalMismatch),
    ("NET-09 gzip portal script is decoded before authentication", GzipPortalScript),
    ("NET-09 oversized gzip portal script prevents authentication", OversizedGzipPortalScript),
    ("NET-02 valid portal permits one login request", LoginOnce),
    ("NET-01 verified internet skips portal and login", AlreadyOnline),
    ("NET-08 HTTP 200 with portal content is not internet", PortalContentIsNotInternet),
    ("SYS-03 changed network path prevents login", NetworkPathChanged),
    ("NET-06 rejected credentials are classified conservatively", Rejection),
    ("NET-05 server retry limit is bounded", ServerRetryLimit),
    ("NET-11 stalled login times out without reporting success", StalledLoginTimesOut),
    ("NET-11 concurrent reconnects serialize credential requests", ConcurrentReconnects),
    ("NET-05 network change bursts keep the retry interval", NetworkEventBurstKeepsInterval),
    ("NET-05 retry delay remains bounded", RetryBounds)
};
tests = tests.Concat(UpdateChecks.All).ToArray();
string? filter = args.Length == 1 && args[0].StartsWith("--filter=", StringComparison.Ordinal)
    ? args[0]["--filter=".Length..] : null;
if (args.Length > 0 && string.IsNullOrWhiteSpace(filter))
{
    Console.Error.WriteLine("Usage: CampusPulse.Tests [--filter=NAME]");
    return 2;
}
var selected = filter is null ? tests : tests.Where(test =>
    test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
if (selected.Length == 0)
{
    Console.Error.WriteLine($"No offline check matches '{filter}'");
    return 2;
}
int failed = 0;
foreach (var test in selected)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name} {error.Message}"); }
}
Console.WriteLine($"{selected.Length - failed}/{selected.Length} offline checks passed");
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
            await WorkerCheck(first);
            Check(first.Snapshot.State == ConnectionState.AuthenticationRejected,
                "first worker records confirmed credential rejection");
        }
        int submissions = fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal));
        Check(submissions == 1 && store.LoadSettings().AuthenticationBlocked,
            "one rejected login persisted before restart");

        using (var second = new ConnectionWorker(new SecureStore(directory), new StartupManager(), fixture.Client))
        {
            Check(second.Snapshot.State == ConnectionState.AuthenticationRejected,
                "restarted worker starts in rejected state");
            await WorkerCheck(second);
            Check(second.Snapshot.ErrorCode == "AuthenticationBlocked",
                "restarted worker retains automatic authentication block");
            Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == submissions,
                "restarted worker made no new credential request");
        }
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task AutomaticRecoveryAfterAmbiguousRejection()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        var settings = new CampusSettings { Enabled = true, Username = "student", StartWithWindows = false };
        store.SaveConfiguration(settings, new StoredCredential("student", "FAKE-ONLY-RECOVERY-password")
            { ProtocolVersion = 2, Carrier = settings.Carrier, PortalUrl = settings.PortalUrl });
        using var fixture = new Fixture
        {
            LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})",
            InternetAfterSuccessfulLogin = true
        };
        using var worker = new ConnectionWorker(store, new StartupManager(), fixture.Client);
        // Invoke one automatic round directly; never start a service or request a manual reconnect.
        await WorkerCheck(worker);
        Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1,
            "nighttime round submitted one fake login");
        Check(worker.Snapshot.State != ConnectionState.Online, "ambiguous rejection did not report online");

        // The school now permits authentication. The resolver keeps exactly the same address and adapter.
        fixture.LoginBodyOverride = null;
        // Simulate expiry of the retry delay; this is not a real overnight or elapsed-time test.
        ExpireAuthenticationRetry(worker, store);
        await WorkerCheck(worker);
        Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 2,
            "automatic recovery must submit again after an ambiguous rejection without manual action");
        Check(worker.Snapshot.State == ConnectionState.Online && worker.Snapshot.LastSuccess is not null,
            "automatic recovery verifies both public probes after accepted authentication");
        Check(!store.LoadSettings().AuthenticationBlocked, "recovered automatic flow retains no permanent block");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

static async Task WorkerCheck(ConnectionWorker worker, bool explicitReconnect = false, bool readonlyCheck = false)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var check = typeof(ConnectionWorker).GetMethod("CheckAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
    await (Task)check.Invoke(worker, [explicitReconnect, readonlyCheck, deadline.Token])!;
}

static void ExpireAuthenticationRetry(ConnectionWorker worker, SecureStore store)
{
    // Only test-owned time state is changed. No real delay or user settings are involved.
    var settingsField = typeof(ConnectionWorker).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var settings = (CampusSettings)settingsField.GetValue(worker)!;
    settings = settings with { AuthenticationRetryAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
    settingsField.SetValue(worker, settings);
    store.SaveSettings(settings);
    typeof(ConnectionWorker).GetField("nextAuthentication", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(worker, DateTimeOffset.MinValue);
}

static async Task RejectionDiagnosticsSurviveRollover()
{
    using var rig = new WorkerFixture(new Fixture { RejectLogin = true });
    await WorkerCheck(rig.Worker);
    var first = rig.Worker.Snapshot.Diagnostics;
    Check(first.FirstRejectionAt is not null && first.FirstRejectionReasonCode == "credentials_rejected" &&
        first.FirstRejectionSource == "Automatic", "first confirmed rejection records time, classification and source");
    Check(rig.Worker.Snapshot.RecentEvents.Any(entry => entry.ReasonCode == "credentials_rejected"),
        "first rejection also has a structured recent event");
    await WorkerCheck(rig.Worker, readonlyCheck: true);
    int stableEventCount = rig.Worker.Snapshot.RecentEvents.Count;
    for (int i = 0; i < 90; i++) await WorkerCheck(rig.Worker, readonlyCheck: true);
    Check(rig.Worker.Snapshot.RecentEvents.Count == stableEventCount &&
        rig.Worker.Snapshot.RecentEvents.Any(entry => entry.ReasonCode == "credentials_rejected"),
        "repeated identical terminal checks do not flood out the original rejection");
    for (int i = 0; i < 90; i++)
    {
        rig.Network.OneProbeSucceeds = i % 2 == 0;
        await WorkerCheck(rig.Worker, readonlyCheck: true);
    }
    Check(rig.Worker.Snapshot.RecentEvents.Count <= 80 &&
        !rig.Worker.Snapshot.RecentEvents.Any(entry => entry.ReasonCode == "credentials_rejected"),
        "the rolling event window can evict the original rejection");
    Check(rig.Worker.Snapshot.Diagnostics.FirstRejectionAt == first.FirstRejectionAt &&
        rig.Worker.Snapshot.Diagnostics.FirstRejectionReasonCode == first.FirstRejectionReasonCode,
        "first rejection summary outlives the rolling event window");
    using var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client);
    Check(restarted.Snapshot.Diagnostics == rig.Worker.Snapshot.Diagnostics,
        "diagnostic summary reloads after worker reconstruction");
    Check(new SecureStore(rig.DirectoryPath).LoadHistory().Diagnostics == restarted.Snapshot.Diagnostics,
        "persisted event history contains the same durable diagnostic summary");
    Check(rig.LoginCount == 1, "diagnostic-only rounds and reconstruction submit no extra fake credential");
}

static async Task SubmissionSourceAndExternalRecovery()
{
    foreach (bool manual in new[] { false, true })
    {
        using var rig = new WorkerFixture(new Fixture { RejectLogin = true });
        await WorkerCheck(rig.Worker, explicitReconnect: manual);
        var submitted = rig.Worker.Snapshot.Diagnostics;
        string source = manual ? "ManualReconnect" : "Automatic";
        Check(rig.LoginCount == 1 && submitted.LastSubmissionAt is not null &&
            submitted.LastSubmissionSource == source && submitted.LastSubmissionResultCode == "credentials_rejected" &&
            submitted.LastResultAt is not null, "actual fake credential submission records its source and outcome");
        Check(rig.Worker.Snapshot.RecentEvents.Any(entry => entry.Source == source && entry.ReasonCode == "credentials_rejected"),
            "the authentication result event carries the same source");
        // Another application or browser restores internet; CampusPulse only observes it.
        rig.Network.BothProbesSucceed = true;
        await WorkerCheck(rig.Worker, readonlyCheck: true);
        var recovered = rig.Worker.Snapshot.Diagnostics;
        Check(rig.Worker.Snapshot.State == ConnectionState.Online && rig.LoginCount == 1,
            "read-only check observes external internet restoration without authenticating");
        Check(recovered.LastSubmissionAt == submitted.LastSubmissionAt &&
            recovered.LastSubmissionSource == submitted.LastSubmissionSource &&
            recovered.LastSubmissionResultCode == submitted.LastSubmissionResultCode &&
            recovered.LastResultAt == submitted.LastResultAt,
            "observed network recovery cannot be mislabeled as a new successful software login");
        Check(recovered.RejectionResolvedAt is not null &&
            rig.Worker.Snapshot.RecentEvents.Any(entry => entry.Source == "ManualCheck"),
            "resolution and manual check remain distinguishable from authentication");
    }
}

static async Task PreflightRefusalHasNoSubmission()
{
    using var rig = new WorkerFixture(new Fixture { ChangePathDuringWorkerLogin = true });
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 0 && rig.Worker.Snapshot.State != ConnectionState.Online,
        "changed-path preflight refuses the credential HTTP request");
    Check(rig.Worker.Snapshot.Diagnostics.LastSubmissionAt is null &&
        rig.Worker.Snapshot.Diagnostics.LastSubmissionResultCode.Length == 0,
        "planning authentication is not recorded as actual credential submission");
}

static async Task AmbiguousRejectionRespectsGuards()
{
    using var rig = new WorkerFixture(new Fixture { LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})" });
    await WorkerCheck(rig.Worker);
    var cooldown = rig.Store.LoadSettings();
    Check(!cooldown.AuthenticationBlocked && cooldown.BlockedReasonCode == "unconfirmed_rejection" &&
        cooldown.AuthenticationRetryAt is { } retry && retry >= DateTimeOffset.UtcNow.AddMinutes(4).AddSeconds(50) &&
        retry <= DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(5),
        "ambiguous rejection persists a five-minute retry instead of permanent protection");
    var submission = rig.Worker.Snapshot.Diagnostics.LastSubmissionAt;
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1, "automatic round before the retry deadline makes no request");
    // Expiring the minimum in-memory interval must not bypass the persisted automatic cooldown.
    typeof(ConnectionWorker).GetField("nextAuthentication", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(rig.Worker, DateTimeOffset.MinValue);
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1, "persisted retry deadline still gates an automatic round");
    await WorkerCheck(rig.Worker, readonlyCheck: true);
    Check(rig.LoginCount == 1 && rig.Store.LoadSettings().AuthenticationRetryAt == cooldown.AuthenticationRetryAt,
        "manual read-only detection does not authenticate or alter the retry deadline");
    ExpireAuthenticationRetry(rig.Worker, rig.Store);
    await WorkerCheck(rig.Worker, readonlyCheck: true);
    Check(rig.LoginCount == 1, "read-only detection remains read-only after cooldown expiry");
    rig.Network.OneProbeSucceeds = true;
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1 && rig.Worker.Snapshot.State == ConnectionState.LimitedConnectivity,
        "partial public connectivity prevents automatic credential submission");
    rig.Network.OneProbeSucceeds = false;
    await rig.Worker.HandleAsync(new ServiceRequest("pause"), CancellationToken.None);
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1 && rig.Worker.Snapshot.State == ConnectionState.Paused,
        "paused automatic reconnect submits no credential even after retry expiry");
    Check(rig.Worker.Snapshot.Diagnostics.LastSubmissionAt == submission,
        "cooldown, read-only, partial and paused rounds retain the last actual submission timestamp");
}

static async Task LegacyAmbiguousBlockRevalidation()
{
    var legacy = new CampusSettings
    {
        Enabled = true, StartWithWindows = false, Username = "student", AuthenticationBlocked = true,
        BlockedReason = "校园认证拒绝账号或密码，请检查后主动重试"
    };
    using var rig = new WorkerFixture(new Fixture { InternetAfterSuccessfulLogin = true }, legacy);
    await WorkerCheck(rig.Worker);
    var migrated = rig.Store.LoadSettings();
    Check(rig.LoginCount == 0 && migrated.BlockedReasonCode == "legacy_unconfirmed" &&
        migrated.AuthenticationRetryAt is { } retry && retry > DateTimeOffset.UtcNow.AddMinutes(4),
        "legacy generic block conservatively waits five minutes before one automatic revalidation");
    Check(migrated.Username == legacy.Username && migrated.Carrier == legacy.Carrier &&
        migrated.StartWithWindows == legacy.StartWithWindows && migrated.UnattendedMode == legacy.UnattendedMode &&
        migrated.Enabled == legacy.Enabled && migrated.PortalUrl == legacy.PortalUrl,
        "legacy revalidation preserves credentials identity and all three switches");
    using (var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client))
    {
        await WorkerCheck(restarted);
        Check(rig.LoginCount == 0 && rig.Store.LoadSettings().AuthenticationRetryAt == migrated.AuthenticationRetryAt,
            "worker reconstruction neither bypasses nor postpones the persisted retry deadline");
        ExpireAuthenticationRetry(restarted, rig.Store);
        await WorkerCheck(restarted);
        Check(rig.LoginCount == 1 && restarted.Snapshot.State == ConnectionState.Online,
            "legacy ambiguous block eventually revalidates automatically on the same network path");
        Check(!rig.Store.LoadSettings().AuthenticationBlocked &&
            rig.Store.LoadSettings().AuthenticationRetryAt is null,
            "verified internet resolution clears legacy protection and pending retry");
    }
}

static async Task LegacyConfirmedBlockStaysProtected()
{
    foreach (var (message, code) in new[]
    {
        ("校园账号余额或缴费状态异常，请处理后主动重试", "account_payment_required"),
        ("校园账号受限，请处理后主动重试", "account_restricted")
    })
    {
        var legacy = new CampusSettings
        {
            Enabled = true, StartWithWindows = false, Username = "student", AuthenticationBlocked = true,
            BlockedReason = message
        };
        using var rig = new WorkerFixture(new Fixture { InternetAfterSuccessfulLogin = true }, legacy);
        await WorkerCheck(rig.Worker);
        Check(rig.LoginCount == 0 && rig.Store.LoadSettings().AuthenticationBlocked &&
            rig.Store.LoadSettings().BlockedReasonCode == code,
            "legacy fixed confirmed reason remains classified and protected");
        ExpireAuthenticationRetry(rig.Worker, rig.Store);
        await WorkerCheck(rig.Worker);
        using var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client);
        await WorkerCheck(restarted);
        Check(rig.LoginCount == 0 && restarted.Snapshot.State == ConnectionState.AuthenticationRejected &&
            rig.Store.LoadSettings().AuthenticationBlocked,
            "retry expiry and worker reconstruction cannot clear a confirmed account restriction");
    }
}

static async Task LegacyRevalidationClassifiesNewOutcome()
{
    var legacy = new CampusSettings
    {
        Enabled = true, StartWithWindows = false, Username = "student", AuthenticationBlocked = true,
        BlockedReason = "校园认证拒绝账号或密码，请检查后主动重试"
    };
    using var rig = new WorkerFixture(new Fixture { LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})" }, legacy);
    await WorkerCheck(rig.Worker);
    ExpireAuthenticationRetry(rig.Worker, rig.Store);
    await WorkerCheck(rig.Worker);
    var unclear = rig.Store.LoadSettings();
    Check(rig.LoginCount == 1 && unclear.BlockedReasonCode == "unconfirmed_rejection" &&
        unclear.AuthenticationRetryAt > DateTimeOffset.UtcNow.AddMinutes(4),
        "an ambiguous legacy revalidation response schedules another bounded five-minute retry");
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1, "the newly scheduled retry prevents an immediate repeated submission");
    rig.Network.LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1,\"msg\":\"账号欠费\"})";
    ExpireAuthenticationRetry(rig.Worker, rig.Store);
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 2 && rig.Store.LoadSettings().AuthenticationBlocked &&
        rig.Store.LoadSettings().BlockedReasonCode == "account_payment_required",
        "an exact confirmed payment response converts legacy revalidation into account protection");
    ExpireAuthenticationRetry(rig.Worker, rig.Store);
    await WorkerCheck(rig.Worker);
    using var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client);
    await WorkerCheck(restarted);
    Check(rig.LoginCount == 2 && restarted.Snapshot.State == ConnectionState.AuthenticationRejected,
        "confirmed protection persists through retry expiry and worker reconstruction");
}

static Task LegacyHistoryLoadsWithoutFabricatedDiagnostics()
{
    foreach (bool explicitNull in new[] { false, true })
    {
        using var rig = new WorkerFixture(new Fixture());
        var legacy = new Dictionary<string, object?>
        {
            ["LastSuccess"] = null,
            ["Entries"] = new[] { new { Time = DateTimeOffset.UtcNow, Message = "正在进行一次校园账号认证" } }
        };
        if (explicitNull) legacy["Diagnostics"] = null;
        File.WriteAllText(Path.Combine(rig.DirectoryPath, "events.json"), JsonSerializer.Serialize(legacy));
        var history = rig.Store.LoadHistory();
        Check(history.Diagnostics is not null && history.Diagnostics.LastSubmissionAt is null &&
            history.Diagnostics.FirstRejectionAt is null, "absent or null legacy summary loads as unknown diagnostics");
        Check(history.Entries.Single().Kind.Length == 0 && history.Entries.Single().ReasonCode.Length == 0 &&
            history.Entries.Single().Source.Length == 0, "old human-readable events retain unknown structured metadata");
        using var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client);
        Check(restarted.Snapshot.Diagnostics.LastSubmissionAt is null &&
            restarted.Snapshot.Diagnostics.LastSubmissionResultCode.Length == 0,
            "a legacy authenticating message cannot prove any actual password submission or successful login");
    }
    return Task.CompletedTask;
}

static async Task ManualRetryRetainsMinimumInterval()
{
    using var rig = new WorkerFixture(new Fixture { LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})" });
    await WorkerCheck(rig.Worker);
    var automatic = rig.Worker.Snapshot.Diagnostics;
    Check(rig.LoginCount == 1 && rig.Store.LoadSettings().AuthenticationRetryAt > DateTimeOffset.UtcNow.AddMinutes(4),
        "the automatic ambiguous failure starts its five-minute cooldown");
    // Simulate only the minimum five-second dispatch interval expiring, leaving the automatic deadline untouched.
    typeof(ConnectionWorker).GetField("nextAuthentication", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(rig.Worker, DateTimeOffset.MinValue);
    rig.Network.LoginBodyOverride = null;
    rig.Network.RejectLogin = true;
    await WorkerCheck(rig.Worker, explicitReconnect: true);
    var manual = rig.Worker.Snapshot.Diagnostics;
    Check(rig.LoginCount == 2 && manual.LastSubmissionAt != automatic.LastSubmissionAt &&
        manual.LastSubmissionSource == "ManualReconnect" && manual.LastSubmissionResultCode == "credentials_rejected",
        "an explicit manual retry can attempt once before the automatic deadline");
    await WorkerCheck(rig.Worker, explicitReconnect: true);
    Check(rig.LoginCount == 2 && rig.Worker.Snapshot.Diagnostics.LastSubmissionAt == manual.LastSubmissionAt,
        "a repeated manual request cannot bypass the minimum credential submission interval");
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 2 && rig.Store.LoadSettings().AuthenticationBlocked,
        "the confirmed manual rejection keeps later automatic rounds protected");
}

static async Task ManualUnknownKeepsConfirmedBlock()
{
    using var rig = new WorkerFixture(new Fixture { RejectLogin = true });
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 1 && rig.Store.LoadSettings().BlockedReasonCode == "credentials_rejected",
        "a confirmed refusal starts credential protection");
    typeof(ConnectionWorker).GetField("nextAuthentication", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(rig.Worker, DateTimeOffset.MinValue);
    rig.Network.LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})";
    await WorkerCheck(rig.Worker, explicitReconnect: true);
    var settings = rig.Store.LoadSettings();
    Check(rig.LoginCount == 2 && settings.AuthenticationBlocked && settings.BlockedReasonCode == "credentials_rejected",
        "one unsuccessful manual retry does not reset confirmed account protection");
    await WorkerCheck(rig.Worker);
    Check(rig.LoginCount == 2, "later automatic checks still do not submit confirmed bad credentials");
}

static async Task AutomaticCooldownSurvivesRestart()
{
    using var rig = new WorkerFixture(new Fixture { LoginBodyOverride = "campuspulse({\"result\":0,\"ret_code\":1})" });
    await WorkerCheck(rig.Worker);
    var before = rig.Store.LoadSettings();
    byte[] cipher = File.ReadAllBytes(Path.Combine(rig.DirectoryPath, "credentials.dat"));
    using var restarted = new ConnectionWorker(new SecureStore(rig.DirectoryPath), new StartupManager(), rig.Network.Client);
    await WorkerCheck(restarted);
    var after = rig.Store.LoadSettings();
    Check(rig.LoginCount == 1 && after.AuthenticationRetryAt == before.AuthenticationRetryAt,
        "reconstruction neither bypasses nor extends a new automatic cooldown");
    Check(after.Enabled == before.Enabled && after.StartWithWindows == before.StartWithWindows &&
        after.UnattendedMode == before.UnattendedMode &&
        cipher.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(rig.DirectoryPath, "credentials.dat"))),
        "reconstruction keeps the three switches and credential ciphertext unchanged");
}

static Task StatusReplyKeepsSummary()
{
    var time = DateTimeOffset.UtcNow;
    var diagnostic = new AuthenticationDiagnostics
    { FirstRejectionAt = time, FirstRejectionReasonCode = "unconfirmed_rejection", FirstRejectionSource = "Automatic" };
    var entries = Enumerable.Range(0, 80).Select(index => new StatusEntry(time.AddSeconds(-index), new string('测', 60))
    { Kind = "authentication_skipped", ReasonCode = "AuthenticationBlocked", Source = "Automatic" }).ToArray();
    var reply = new ServiceReply(true, "状态已读取", new ServiceSnapshot { RecentEvents = entries, Diagnostics = diagnostic });
    byte[] bytes = ControlPipeServer.EncodeReply(reply);
    var decoded = JsonSerializer.Deserialize<ServiceReply>(bytes)!;
    Check(bytes.Length <= 16 * 1024 && decoded.Success && decoded.Snapshot?.Diagnostics == diagnostic,
        "oversized recent events do not destroy the status or independent failure summary");
    Check(decoded.Snapshot!.RecentEvents.Count < 80 && decoded.Snapshot.RecentEvents[0].Time == time,
        "the bounded reply keeps the newest optional events");
    return Task.CompletedTask;
}

static async Task NetworkEventBurstKeepsInterval()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        store.SaveSettings(new CampusSettings { Enabled = true });
        using var fixture = new Fixture { BothProbesSucceed = true };
        using var worker = new ConnectionWorker(store, new StartupManager(), fixture.Client);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(() => worker.Snapshot.State == ConnectionState.Online &&
                worker.Snapshot.NextCheck is { } next && next > DateTimeOffset.UtcNow,
                "initial check schedules the next interval");
            int requests = fixture.Requests.Count;
            var change = typeof(ConnectionWorker).GetMethod("OnNetworkChanged",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            for (int i = 0; i < 5; i++)
            {
                change.Invoke(worker, [null, EventArgs.Empty]);
                await Task.Delay(40);
            }
            await Task.Delay(250);
            Check(fixture.Requests.Count == requests,
                "network change burst did not start an early automatic check");
        }
        finally { await worker.StopAsync(CancellationToken.None); }
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

static Task CredentialReplacementAndClearing()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        var settings = new CampusSettings { Username = "student" };
        const string first = "FAKE-ONLY-FIRST-password-913";
        const string second = "FAKE-ONLY-SECOND-password-728";
        StoredCredential Credential(string password) => new("student", password)
            { ProtocolVersion = 2, Carrier = settings.Carrier, PortalUrl = settings.PortalUrl };
        store.SaveConfiguration(settings, Credential(first));
        store.SaveConfiguration(settings, Credential(second));
        Check(store.LoadCredential("student", settings.Carrier, settings.PortalUrl)?.Password == second,
            "replacement returns only new fake password");
        store.SaveHistory(null, [new(DateTimeOffset.UtcNow, "Configuration saved")]);
        foreach (string path in Directory.GetFiles(directory))
        {
            string bytesAsText = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Check(!bytesAsText.Contains(first, StringComparison.Ordinal) &&
                !bytesAsText.Contains(second, StringComparison.Ordinal), "no fake password in persisted plaintext");
        }
        store.SaveConfiguration(settings with { Username = "", Enabled = false }, null, deleteCredential: true);
        Check(!File.Exists(Path.Combine(directory, "credentials.dat")) &&
            store.LoadCredential("student", settings.Carrier, settings.PortalUrl) is null,
            "cleared credential cannot be reloaded");
    }
    finally { Directory.Delete(directory, recursive: true); }
    return Task.CompletedTask;
}

static async Task FailedAuthenticationDiagnostics()
{
    const string password = "FAKE-ONLY-LEAK-CHECK+728";
    foreach (bool transportFailure in new[] { false, true })
    {
        string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SecureStore(directory);
            var settings = new CampusSettings { Enabled = true, Username = "student", StartWithWindows = false };
            store.SaveConfiguration(settings, new StoredCredential("student", password)
                { ProtocolVersion = 2, Carrier = settings.Carrier, PortalUrl = settings.PortalUrl });
            using var fixture = new Fixture
            {
                ThrowLoginTransportError = transportFailure,
                LoginBodyOverride = "campuspulse(" + JsonSerializer.Serialize(new
                {
                    result = 0, ret_code = 1,
                    msg = password + " http://10.1.2.3/eportal/portal/login?user_password=" + Uri.EscapeDataString(password)
                }) + ")"
            };
            using var worker = new ConnectionWorker(store, new StartupManager(), fixture.Client);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                await WaitUntil(() => worker.Snapshot.State is ConnectionState.AuthenticationRejected or ConnectionState.PortalUnavailable,
                    "simulated login failure reaches a diagnostic state");
                Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1,
                    "failure scenario exercises exactly one fake credential request");
                File.WriteAllText(Path.Combine(directory, "status-reply.json"),
                    JsonSerializer.Serialize(await worker.HandleAsync(new ServiceRequest("status"), CancellationToken.None)));
                // The production Copy Events action uses this formatter. Do not touch the user clipboard.
                string diagnostic = DiagnosticText.Format(worker.Snapshot);
                Check(diagnostic.Length > 0, "diagnostic export is populated");
                File.WriteAllText(Path.Combine(directory, "diagnostic.txt"), diagnostic);
            }
            finally { await worker.StopAsync(CancellationToken.None); }
            foreach (string path in Directory.GetFiles(directory))
            {
                string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                Check(!text.Contains("FAKE-ONLY-LEAK-CHECK", StringComparison.Ordinal) &&
                    !text.Contains("user_password", StringComparison.Ordinal),
                    "persisted data, status reply and exported events contain no secret or credential URL");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

static Task ConfigurationRollback()
{
    string directory = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SecureStore(directory);
        var settings = new CampusSettings { Username = "student" };
        StoredCredential Credential(string password) => new("student", password)
            { ProtocolVersion = 2, Carrier = settings.Carrier, PortalUrl = settings.PortalUrl };
        store.SaveConfiguration(settings, Credential("dummy-old"));
        byte[] before = File.ReadAllBytes(Path.Combine(directory, "credentials.dat"));
        bool failed = false;
        // Permit reading the old settings, but prevent atomic replacement until rollback finishes.
        using (var locked = new FileStream(Path.Combine(directory, "settings.json"), FileMode.Open,
            FileAccess.Read, FileShare.Read))
        {
            try { store.SaveConfiguration(settings with { UnattendedMode = true }, Credential("dummy-new")); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
        }
        Check(failed, "locked settings force save failure");
        Check(store.LoadSettings() == settings, "old settings preserved");
        Check(File.ReadAllBytes(Path.Combine(directory, "credentials.dat")).SequenceEqual(before) &&
            store.LoadCredential("student", settings.Carrier, settings.PortalUrl)?.Password == "dummy-old",
            "old credential restored despite locked unchanged settings");
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed transaction leaves no temporary file");
    }
    finally { Directory.Delete(directory, recursive: true); }
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

static async Task GzipPortalScript()
{
    using var fixture = new Fixture { GzipScript = true };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.Accepted, "gzip script version decoded");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1,
        "one simulated login after gzip validation");
}

static async Task OversizedGzipPortalScript()
{
    using var fixture = new Fixture { GzipScript = true, ScriptOverride = new string('x', 600_000) };
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom",
        CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.ReasonCode == "portal_unrecognized", "decompressed size limit enforced");
    Check(fixture.Requests.All(uri => !uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)),
        "oversized script cannot authorize credential submission");
}

static async Task LoginOnce()
{
    using var fixture = new Fixture();
    var result = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(result.Accepted && result.ReasonCode == "authentication_accepted", "accepted is not internet verified");
    Check(fixture.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) == 1, "one login request");
    Check(fixture.Requests.All(uri => uri.Host is "10.62.164.38" or "www.msftconnecttest.com" or "detectportal.firefox.com"), "fixed hosts");
}

static async Task AlreadyOnline()
{
    using var fixture = new Fixture { BothProbesSucceed = true };
    var check = await fixture.Client.CheckAsync("telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(check.InternetAvailable && check.ReasonCode == "internet_verified", "both probes verified");
    var login = await fixture.Client.LoginAsync("student", "dummy", "telecom", CampusSettings.SupportedPortal, CancellationToken.None);
    Check(login.ReasonCode == "authentication_not_required", "login skipped");
    Check(fixture.Requests.All(uri => uri.Host != "10.62.164.38"), "portal not contacted");
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
    var ambiguous = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":1})");
    Check(!ambiguous.Accepted && !ambiguous.CredentialsRejected && ambiguous.ReasonCode == "unconfirmed_rejection" &&
        ambiguous.RetryAfter == TimeSpan.FromMinutes(5), "bare generic failure is retriable after five minutes");
    var unfamiliar = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":1,\"msg\":\"unfamiliar\"})");
    Check(!unfamiliar.CredentialsRejected && unfamiliar.ReasonCode == "unconfirmed_rejection" &&
        unfamiliar.RetryAfter == TimeSpan.FromMinutes(5), "unknown generic message cannot become a permanent account block");
    var unknown = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":7,\"msg\":\"unfamiliar\"})");
    Check(!unknown.CredentialsRejected && unknown.ReasonCode == "authentication_unknown", "unknown code");
    foreach (var (message, code) in new[]
    {
        ("用户名或密码错误", "credentials_rejected"),
        ("账号或密码不对，请重新输入！", "credentials_rejected"),
        ("账号欠费", "account_payment_required"),
        ("账户余额不足", "account_payment_required"),
        ("账号已停用", "account_restricted"),
        ("账号被禁用", "account_restricted")
    })
    {
        var confirmed = DrComProtocol.ClassifyLoginResponse("campuspulse(" +
            JsonSerializer.Serialize(new { result = 0, ret_code = 1, msg = message }) + ")");
        Check(!confirmed.Accepted && confirmed.CredentialsRejected && confirmed.ReasonCode == code,
            "exact recognized account message retains its confirmed protection category");
    }
    var misleading = DrComProtocol.ClassifyLoginResponse("campuspulse({\"result\":0,\"ret_code\":1,\"msg\":\"账号欠费 unknown text\"})");
    Check(!misleading.CredentialsRejected && misleading.ReasonCode == "unconfirmed_rejection",
        "substring matching cannot confirm a payment problem");
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

sealed class WorkerFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "CampusPulse-Test-" + Guid.NewGuid().ToString("N"));
    public SecureStore Store { get; }
    public Fixture Network { get; }
    public ConnectionWorker Worker { get; }
    public int LoginCount => Network.Requests.Count(uri => uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal));

    public WorkerFixture(Fixture network, CampusSettings? settings = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        Store = new SecureStore(DirectoryPath);
        Network = network;
        settings ??= new CampusSettings { Enabled = true, StartWithWindows = false, Username = "student" };
        Store.SaveConfiguration(settings, new StoredCredential(settings.Username, "FAKE-ONLY-RECOVERY-password")
            { ProtocolVersion = 2, Carrier = settings.Carrier, PortalUrl = settings.PortalUrl });
        Worker = new ConnectionWorker(Store, new StartupManager(), Network.Client);
    }

    public void Dispose()
    {
        Worker.Dispose();
        Network.Dispose();
        string resolved = Path.GetFullPath(DirectoryPath);
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("CampusPulse-Test-", StringComparison.Ordinal))
            throw new InvalidOperationException("test cleanup path is outside its random temporary directory");
        Directory.Delete(resolved, recursive: true);
    }
}

sealed class Fixture : IDisposable
{
    private const string Portal = "<!-- Dr.COMWebLoginID_0.htm --><script>v46ip = '10.1.2.3'; ss4 = 'ABCDEF123456'; vlanid = '1';</script>";
    private const string Config = "campuspulse({\"code\":1,\"data\":{\"login_method\":1,\"account_prefix\":1,\"en_md5\":0,\"password_cut\":0,\"enable_r3\":0,\"enable_https\":0,\"ep_http_port\":801,\"check_online_method\":0,\"io_mode\":0,\"ipv6_state\":0,\"account_suffix\":\"\",\"domain_name\":\"\",\"program_index\":\"testProgram\",\"page_index\":\"testPage\"}})";
    private const string Template = "<select name=\"ISP_select\"><option value=\"-1\">请选择运营商</option><option value=\"@unicom\">中国联通</option><option value=\"@cmcc\">中国移动</option><option value=\"@telecom\">中国电信</option><option value=\"\">校内网（无外网）</option></select>";
    public bool OneProbeSucceeds { get; set; }
    public bool BothProbesSucceed { get; set; }
    public bool InternetAfterSuccessfulLogin { get; set; }
    public bool BadPortal { get; set; }
    public bool ChangePathBeforeLogin { get; set; }
    public bool ChangePathDuringWorkerLogin { get; set; }
    public bool WithoutMobileOption { get; set; }
    public string? TemplateOverride { get; set; }
    public bool PortalOnline { get; set; }
    public bool RedirectTemplate { get; set; }
    public bool RateLimitLogin { get; set; }
    public bool DelayLogin { get; set; }
    public bool StallLogin { get; set; }
    public bool RejectLogin { get; set; }
    public bool ThrowLoginTransportError { get; set; }
    public string? LoginBodyOverride { get; set; }
    public bool GzipScript { get; set; }
    public string? ScriptOverride { get; set; }
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
        public CampusNetworkPath? Resolve(IPAddress portalAddress)
        {
            int current = ++calls;
            int adapter = fixture.ChangePathBeforeLogin && current > 1 ||
                fixture.ChangePathDuringWorkerLogin && current >= 3 ? 8 : 7;
            return new(IPAddress.Parse("10.1.2.3"), adapter, "ABCDEF123456");
        }
    }
    private sealed class FakeHandler(Fixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new Exception("missing target");
            fixture.Requests.Add(uri);
            if (fixture.ThrowLoginTransportError && uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                throw new HttpRequestException(uri.AbsoluteUri);
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
            if (fixture.GzipScript && uri.AbsolutePath == "/a40.js")
            {
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                {
                    byte[] script = Encoding.UTF8.GetBytes(fixture.ScriptOverride ?? "jsVersion = '4.2.0';");
                    gzip.Write(script);
                }
                var content = new ByteArrayContent(compressed.ToArray());
                content.Headers.ContentEncoding.Add("gzip");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
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
                "/eportal/portal/login" => fixture.LoginBodyOverride ?? (fixture.RejectLogin
                    ? "campuspulse({\"result\":0,\"ret_code\":1,\"msg\":\"用户名或密码错误\"})" : "campuspulse({\"result\":1})"),
                _ => throw new Exception("unexpected target")
            };
            if (fixture.InternetAfterSuccessfulLogin && uri.AbsolutePath == "/eportal/portal/login" &&
                body == "campuspulse({\"result\":1})")
                fixture.BothProbesSucceed = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
