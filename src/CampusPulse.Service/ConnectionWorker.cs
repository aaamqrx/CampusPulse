using System.Net.NetworkInformation;
using CampusPulse.Core;
using Microsoft.Extensions.Hosting;

namespace CampusPulse.Service;

internal sealed class ConnectionWorker : BackgroundService
{
    private readonly SecureStore store;
    private readonly StartupManager startup;
    private readonly CampusNetworkClient network;
    private readonly PowerKeeper power = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly object sync = new();
    private readonly List<StatusEntry> events = [];
    private CampusSettings settings = new();
    private StoredCredential? credential;
    private ServiceSnapshot snapshot = new();
    private CancellationTokenSource? activeCheck;
    private DateTimeOffset? lastSuccess;
    private DateTimeOffset? lastCheck;
    private DateTimeOffset? nextCheck;
    private DateTimeOffset nextAuthentication = DateTimeOffset.MinValue;
    private bool? actualStartup;
    private bool keepingAwake;
    private bool stopping;
    private int triggers;
    private int failures;
    private static readonly int[] RetrySeconds = [30, 60, 120, 300];

    public ConnectionWorker(SecureStore store, StartupManager startup, CampusNetworkClient network)
    {
        this.store = store; this.startup = startup; this.network = network;
        try
        {
            settings = store.LoadSettings();
            credential = store.LoadCredential(settings.Username, settings.Carrier, settings.PortalUrl);
            var history = store.LoadHistory();
            lastSuccess = history.LastSuccess;
            events.AddRange(history.Entries.Where(x => x.Time > DateTimeOffset.UtcNow.AddDays(-7)).TakeLast(80));
            SetState(settings.AuthenticationBlocked ? ConnectionState.AuthenticationRejected :
                settings.Enabled && credential is null ? ConnectionState.NeedsConfiguration : ConnectionState.Paused,
                settings.AuthenticationBlocked ? settings.BlockedReason : "后台已运行，等待检查或配置");
        }
        catch
        {
            settings = new(); credential = null;
            SetState(ConnectionState.NeedsConfiguration, "配置或凭据无法读取，已暂停；请重新保存配置", "ConfigurationUnreadable");
        }
    }

    public ServiceSnapshot Snapshot { get { lock (sync) return snapshot; } }

    public void Trigger(bool allowAuthentication)
    {
        if (stopping) return;
        Interlocked.Or(ref triggers, allowAuthentication ? 4 : 2);
        Wake();
    }

    private void OnNetworkChanged(object? sender, EventArgs args)
    {
        CancelCurrent();
        Interlocked.Or(ref triggers, 1);
        Wake();
    }

    private void Wake() { try { signal.Release(); } catch (SemaphoreFullException) { } }
    private void CancelCurrent() { lock (sync) { try { activeCheck?.Cancel(); } catch (ObjectDisposedException) { } } }

    public async Task<ServiceReply> HandleAsync(ServiceRequest request, CancellationToken token)
    {
        if (request.Command == "status") return new(true, "状态已读取", Snapshot);
        if (request.Command is "check" or "reconnect")
        {
            Trigger(request.Command == "reconnect");
            return new(true, request.Command == "check" ? "已安排只读网络检测" : "已安排一次必要的重连", Snapshot);
        }
        if (request.Command is not ("save" or "pause" or "deleteCredentials"))
            return new(false, "不支持的操作", Snapshot);
        CancelCurrent();
        await operations.WaitAsync(token);
        try
        {
            if (request.Command == "pause")
            {
                store.SaveSettings(settings with { Enabled = false });
                settings = settings with { Enabled = false };
                nextCheck = null;
                SetState(ConnectionState.Paused, "自动重连已暂停；其他开关保持原设置");
                Wake();
                return new(true, "自动重连已暂停", Snapshot);
            }
            if (request.Command == "deleteCredentials")
            {
                var cleared = settings with { Enabled = false, Username = "", AuthenticationBlocked = false, BlockedReason = "" };
                store.SaveConfiguration(cleared, null, deleteCredential: true);
                settings = cleared; credential = null; nextCheck = null;
                SetState(ConnectionState.NeedsConfiguration, "凭据已清除，自动重连已暂停");
                Wake();
                return new(true, "凭据已清除", Snapshot);
            }

            if (request.Settings is null) return new(false, "缺少设置", Snapshot);
            var desired = request.Settings;
            if (desired.ConfigVersion != 2 || !DrComProtocol.TryGetCarrier(desired.Carrier, out _))
                return new(false, "当前版本不支持所选运营商", Snapshot);
            if (!PortalEndpoint.TryCreate(desired.PortalUrl, out var portal))
                return new(false, "登录地址须为校内 HTTP IPv4 门户首页", Snapshot);
            if (!DrComProtocol.TryNormalizeUsername(desired.Username, desired.Carrier, out string name) &&
                !string.IsNullOrWhiteSpace(desired.Username))
                return new(false, "账号格式或运营商后缀不正确，请输入基础校园账号", Snapshot);
            if (!string.IsNullOrEmpty(request.Password) && !DrComProtocol.IsValidPassword(request.Password))
                return new(false, "密码格式不正确", Snapshot);
            bool changedName = name != settings.Username;
            bool changedCarrier = desired.Carrier != settings.Carrier;
            bool changedPortal = portal.Root.AbsoluteUri != settings.PortalUrl;
            bool changedPassword = !string.IsNullOrEmpty(request.Password);
            if ((changedName || changedCarrier || changedPortal) && !changedPassword && name.Length > 0)
                return new(false, "更换账号、运营商或登录地址时请重新输入密码", Snapshot);
            var newCredential = changedPassword ? new StoredCredential(name, request.Password!)
                { Carrier = desired.Carrier, PortalUrl = portal.Root.AbsoluteUri, ProtocolVersion = 2 } : credential;
            if (desired.Enabled && (string.IsNullOrEmpty(name) || newCredential is null ||
                newCredential.Username != name || newCredential.Carrier != desired.Carrier ||
                newCredential.PortalUrl != portal.Root.AbsoluteUri))
                return new(false, "启用自动重连前，请填写账号和密码", Snapshot);
            if (changedPassword && string.IsNullOrEmpty(name)) return new(false, "请同时填写账号", Snapshot);
            desired = desired with
            {
                Username = name, OnlineCheckSeconds = Math.Clamp(desired.OnlineCheckSeconds, 30, 3600),
                PortalUrl = portal.Root.AbsoluteUri,
                AuthenticationBlocked = changedName || changedCarrier || changedPortal || changedPassword ? false : settings.AuthenticationBlocked,
                BlockedReason = changedName || changedCarrier || changedPortal || changedPassword ? "" : settings.BlockedReason
            };
            bool previousStartup = startup.Read();
            try
            {
                startup.Set(desired.StartWithWindows);
                store.SaveConfiguration(desired, changedPassword ? newCredential : null, changedName && name.Length == 0);
            }
            catch
            {
                try { startup.Set(previousStartup); } catch { }
                throw;
            }
            settings = desired; credential = name.Length == 0 ? null : newCredential;
            if (changedName || changedCarrier || changedPortal) lastSuccess = null;
            actualStartup = startup.Read();
            if (changedName || changedCarrier || changedPortal || changedPassword) { failures = 0; nextAuthentication = DateTimeOffset.MinValue; }
            nextCheck = settings.Enabled ? DateTimeOffset.UtcNow : null;
            SetState(settings.AuthenticationBlocked ? ConnectionState.AuthenticationRejected :
                settings.Enabled ? ConnectionState.Checking : ConnectionState.Paused,
                settings.AuthenticationBlocked ? settings.BlockedReason : "设置已保存并核对开机启动状态");
            Interlocked.Or(ref triggers, 1); Wake();
            return new(true, "设置已保存", Snapshot);
        }
        catch
        {
            SetState(Snapshot.State, "保存失败；请检查后台权限及磁盘状态，设置未确认生效", "ConfigurationWriteFailed");
            return new(false, "设置保存失败，请检查权限及磁盘状态", Snapshot);
        }
        finally { operations.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        Interlocked.Or(ref triggers, 1); Wake();
        try { await Task.WhenAll(NetworkLoop(stoppingToken), PowerLoop(stoppingToken)); }
        finally
        {
            stopping = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            CancelCurrent(); power.Dispose();
        }
    }

    private async Task PowerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                power.Update(settings.UnattendedMode);
                keepingAwake = power.Active;
                try { actualStartup = startup.Read(); } catch { actualStartup = null; }
                RefreshSnapshot();
            }
            catch { keepingAwake = false; SetState(Snapshot.State, "防睡眠请求未生效，请检查系统电源策略", "PowerRequestFailed"); }
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }

    private async Task NetworkLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TimeSpan delay = settings.Enabled && nextCheck.HasValue
                ? nextCheck.Value - DateTimeOffset.UtcNow : Timeout.InfiniteTimeSpan;
            if (delay != Timeout.InfiniteTimeSpan && delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            await signal.WaitAsync(delay, stop);
            int reason = Interlocked.Exchange(ref triggers, 0);
            bool explicitReconnect = (reason & 4) != 0;
            bool readonlyCheck = !explicitReconnect && (reason & 2) != 0;
            if (!explicitReconnect && !readonlyCheck && !settings.Enabled) continue;
            await operations.WaitAsync(stop);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(stop);
            operation.CancelAfter(TimeSpan.FromSeconds(95));
            lock (sync) activeCheck = operation;
            try
            {
                await CheckAsync(explicitReconnect, readonlyCheck, operation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!stop.IsCancellationRequested) SetState(ConnectionState.Checking, "检查已取消或超时，将按当前设置重新检查", "CheckCanceled");
            }
            catch
            {
                failures++;
                SetState(ConnectionState.PortalUnavailable, "网络检查暂未完成，将稍后重试", "CheckFailed");
            }
            finally
            {
                lock (sync) activeCheck = null;
                int seconds = failures == 0 ? settings.OnlineCheckSeconds : RetrySeconds[Math.Min(failures - 1, RetrySeconds.Length - 1)];
                nextCheck = settings.Enabled ? DateTimeOffset.UtcNow.AddSeconds(seconds + (failures == 0 ? 0 : Random.Shared.Next(0, 6))) : null;
                RefreshSnapshot(); operations.Release();
            }
        }
    }

    private async Task CheckAsync(bool explicitReconnect, bool readonlyCheck, CancellationToken token)
    {
        lastCheck = DateTimeOffset.UtcNow;
        SetState(ConnectionState.Checking, "正在检测校园有线网络");
        var check = await network.CheckAsync(settings.Carrier, settings.PortalUrl, token);
        token.ThrowIfCancellationRequested();
        if (check.InternetAvailable) { MarkOnline(); return; }
        if (check.IntranetAvailable) { MarkIntranetOnline(); return; }
        if (check.PartialConnectivity)
        { failures++; SetState(ConnectionState.LimitedConnectivity, check.Message, "PartialConnectivity"); return; }
        if (!check.PortalRecognized)
        { failures++; SetState(ConnectionState.WaitingNetwork, check.Message, "PortalUnrecognized"); return; }
        if (!check.NeedsAuthentication)
        { failures++; SetState(ConnectionState.LimitedConnectivity, check.Message, "AuthenticationNotRequiredOrUnknown"); return; }
        if (readonlyCheck)
        { SetState(ConnectionState.LimitedConnectivity, "检测到需要认证；本次仅检测，没有提交密码", "NeedsAuthentication"); return; }
        if (settings.AuthenticationBlocked && !explicitReconnect)
        { failures++; SetState(ConnectionState.AuthenticationRejected, settings.BlockedReason, "AuthenticationBlocked"); return; }
        if (credential is null || credential.Username != settings.Username || credential.Carrier != settings.Carrier ||
            credential.PortalUrl != settings.PortalUrl)
        { SetState(ConnectionState.NeedsConfiguration, "请先配置账号和密码", "MissingCredentials"); return; }
        if (DateTimeOffset.UtcNow < nextAuthentication)
        { failures++; SetState(ConnectionState.LimitedConnectivity, "正在等待重试间隔，请稍后再试", "AuthenticationCooldown"); return; }
        token.ThrowIfCancellationRequested();
        nextAuthentication = DateTimeOffset.UtcNow.AddSeconds(5);
        SetState(ConnectionState.Authenticating, "正在进行一次校园账号认证");
        var login = await network.LoginAsync(credential.Username, credential.Password, settings.Carrier, settings.PortalUrl, token);
        token.ThrowIfCancellationRequested();
        if (login.CredentialsRejected)
        {
            settings = settings with { AuthenticationBlocked = true, BlockedReason = login.Message };
            store.SaveSettings(settings);
            failures++;
            SetState(ConnectionState.AuthenticationRejected, login.Message, "AuthenticationRejected");
            return;
        }
        if (!login.Accepted)
        {
            failures++;
            nextAuthentication = DateTimeOffset.UtcNow.Add(RetryPolicy.GetDelay(failures, retryAfter: login.RetryAfter));
            SetState(ConnectionState.PortalUnavailable, login.Message, "AuthenticationFailed"); return;
        }
        nextAuthentication = DateTimeOffset.UtcNow.AddMinutes(5);
        foreach (int seconds in new[] { 2, 3, 10 })
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            var verified = await network.CheckAsync(settings.Carrier, settings.PortalUrl, token);
            token.ThrowIfCancellationRequested();
            if (verified.InternetAvailable) { MarkOnline(); return; }
            if (verified.IntranetAvailable) { MarkIntranetOnline(); return; }
        }
        failures++;
        SetState(ConnectionState.LimitedConnectivity, "校园认证已接受，互联网尚未验证可用；等待复查", "AwaitingInternetVerification");
    }

    private void MarkOnline()
    {
        lastSuccess = DateTimeOffset.UtcNow; failures = 0;
        if (settings.AuthenticationBlocked)
        {
            settings = settings with { AuthenticationBlocked = false, BlockedReason = "" };
            store.SaveSettings(settings);
        }
        SetState(ConnectionState.Online, "校园有线网络已通过两个公网探测");
        PersistHistory();
    }

    private void MarkIntranetOnline()
    {
        failures = 0;
        SetState(ConnectionState.IntranetOnline, "校内网门户报告已在线；所选服务不提供外网");
    }

    private void SetState(ConnectionState state, string message, string error = "")
    {
        lock (sync)
        {
            if (snapshot.State != state || snapshot.Message != message)
            {
                events.Add(new(DateTimeOffset.UtcNow, message));
                events.RemoveAll(x => x.Time < DateTimeOffset.UtcNow.AddDays(-7));
                while (events.Count > 80) events.RemoveAt(0);
            }
            snapshot = snapshot with { State = state, Message = message, ErrorCode = error };
            RefreshSnapshot(); PersistHistory();
        }
    }
    private void RefreshSnapshot()
    {
        lock (sync) snapshot = snapshot with
        {
            Settings = settings, HasPassword = credential is not null, LastCheck = lastCheck, LastSuccess = lastSuccess,
            NextCheck = nextCheck, KeepingAwake = keepingAwake, ActualStartWithWindows = actualStartup,
            RecentEvents = events.AsEnumerable().Reverse().ToArray()
        };
    }
    private void PersistHistory()
    {
        lock (sync) { try { store.SaveHistory(lastSuccess, events); } catch { /* Status remains available in memory if disk is full. */ } }
    }
}
