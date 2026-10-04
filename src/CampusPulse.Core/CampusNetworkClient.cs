using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusPulse.Core;

/// <summary>Serial, fixed-target, cancellable campus authentication. All returned messages are fixed text.</summary>
public sealed class CampusNetworkClient : IDisposable
{
    public static readonly Uri MicrosoftProbe = new("http://www.msftconnecttest.com/connecttest.txt");
    public static readonly Uri MozillaProbe = new("https://detectportal.firefox.com/success.txt");
    private readonly INetworkPathResolver resolver;
    private readonly Func<CampusNetworkPath, HttpMessageHandler> handlerFactory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public CampusNetworkClient() : this(new WindowsNetworkPathResolver(), CreateBoundHandler) { }

    /// <summary>Injection is for offline tests. Production callers must use the parameterless constructor.</summary>
    public CampusNetworkClient(INetworkPathResolver resolver, Func<CampusNetworkPath, HttpMessageHandler> handlerFactory)
    {
        this.resolver = resolver;
        this.handlerFactory = handlerFactory;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<NetworkCheckResult> CheckAsync(string carrier, string portalUrl, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!PortalEndpoint.TryCreate(portalUrl, out var endpoint))
            return new(false, false, false, "登录地址不受支持；请输入校内 HTTP IPv4 门户首页") { ReasonCode = "unsupported_portal_address" };
        if (!DrComProtocol.TryGetCarrier(carrier, out _))
            return new(false, false, false, "不支持的运营商选项，未访问校园门户") { ReasonCode = "unsupported_carrier" };
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(35));
            var path = resolver.Resolve(endpoint!.Address);
            if (path is null) return NoPath();
            using var client = CreateClient(path);
            var probes = await ProbeAsync(client, endpoint, deadline.Token);
            if (probes == 2) return new(true, false, false, "校园有线网络已通过两项公网验证") { ReasonCode = "internet_verified" };
            if (probes == 1) return new(false, false, false, "公网部分连通或探测受限，暂不重复认证", true) { ReasonCode = "partial_connectivity" };

            var portal = await ReadPortalAsync(client, path, carrier, endpoint, deadline.Token);
            return portal.Online switch
            {
                false => new(false, true, true, "校园门户明确要求认证") { ReasonCode = "authentication_required" },
                true when carrier == "intranet" => new(false, true, false, "校内网门户报告已在线；此选项不提供外网")
                    { ReasonCode = "intranet_online", IntranetAvailable = true },
                true => new(false, true, false, "门户报告已在线，但互联网尚未验证可用") { ReasonCode = "portal_online_internet_unverified" },
                _ => new(false, true, false, "门户状态不明确，未提交认证") { ReasonCode = "portal_status_unknown" }
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, false, false, "网络检查超时，等待下次检查") { ReasonCode = "check_timeout" }; }
        catch (Exception e) when (IsExpectedNetworkFailure(e))
        { return new(false, false, false, FailureMessage(e)) { ReasonCode = FailureCode(e) }; }
        finally { gate.Release(); }
    }

    public async Task<LoginResult> LoginAsync(string username, string password, string carrier, string portalUrl,
        CancellationToken cancellationToken, Action<DateTimeOffset>? onSubmitting = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!PortalEndpoint.TryCreate(portalUrl, out var endpoint))
            return new(false, false, "登录地址不受支持；未提交凭据") { ReasonCode = "unsupported_portal_address" };
        if (!DrComProtocol.TryGetCarrier(carrier, out _))
            return new(false, false, "不支持的运营商选项") { ReasonCode = "unsupported_carrier" };
        if (!DrComProtocol.TryNormalizeUsername(username, carrier, out string normalized) || !DrComProtocol.IsValidPassword(password))
            return new(false, false, "请检查账号及密码格式") { ReasonCode = "invalid_credentials_format" };

        await gate.WaitAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var path = resolver.Resolve(endpoint!.Address);
            if (path is null) return new(false, false, "未能确认校园有线路径，请检查网线、地址及 VPN 路由") { ReasonCode = "network_path_unconfirmed" };
            using var client = CreateClient(path);
            // Recheck immediately before authentication. Manual reconnect is also an as-needed operation.
            int probes = await ProbeAsync(client, endpoint, deadline.Token);
            if (probes > 0)
                return new(false, false, "已有公网连通证据，本次无需提交校园认证") { ReasonCode = "authentication_not_required" };
            var portal = await ReadPortalAsync(client, path, carrier, endpoint, deadline.Token);
            if (portal.Online is not false)
                return new(portal.Online is true, false, "门户未明确要求认证，本次未提交凭据")
                { ReasonCode = portal.Online is true ? "already_online" : "portal_status_unknown" };
            string version = DrComProtocol.ParseJavaScriptVersion(await GetBodyAsync(client, endpoint.At("/a40.js"), endpoint, deadline.Token));
            // Do not reuse terminal parameters across network changes, even within the same operation.
            if (resolver.Resolve(endpoint.Address) != path)
                return new(false, false, "网络路径已变化，请重新检查") { ReasonCode = "network_path_changed" };
            cancellationToken.ThrowIfCancellationRequested();
            Uri login = DrComProtocol.LoginUri(normalized, password, portal.Terminal, version, carrier, endpoint);
            string body = await GetBodyAsync(client, login, endpoint, deadline.Token, onSubmitting: onSubmitting);
            return DrComProtocol.ClassifyLoginResponse(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, false, "认证请求超时，结果未确认；稍后先检查网络") { ReasonCode = "authentication_timeout" }; }
        catch (RateLimitedException e)
        { return new(false, false, "认证服务暂时繁忙，请稍后重试") { ReasonCode = "portal_rate_limited", RetryAfter = e.RetryAfter }; }
        catch (Exception e) when (IsExpectedNetworkFailure(e))
        { return new(false, false, FailureMessage(e)) { ReasonCode = FailureCode(e) }; }
        finally { gate.Release(); }
    }

    private async Task<(PortalParameters Terminal, bool? Online)> ReadPortalAsync(HttpClient client,
        CampusNetworkPath path, string carrier, PortalEndpoint endpoint, CancellationToken token)
    {
        string html = await GetBodyAsync(client, endpoint.Root, endpoint, token);
        var terminal = DrComProtocol.ParsePortal(html, path);
        string configuration = await GetBodyAsync(client, DrComProtocol.ConfigurationUri(terminal, endpoint), endpoint, token);
        Uri template = DrComProtocol.ValidateConfiguration(configuration, endpoint);
        DrComProtocol.ValidateCarrierTemplate(await GetBodyAsync(client, template, endpoint, token), carrier);
        string status = await GetBodyAsync(client,
            endpoint.At("/drcom/chkstatus?callback=campuspulse&jsVersion=4.X"), endpoint, token);
        return (terminal, DrComProtocol.ParseOnlineStatus(status, path));
    }

    private HttpClient CreateClient(CampusNetworkPath path)
    {
        var client = new HttpClient(handlerFactory(path), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CampusPulse/0.1");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return client;
    }

    private static async Task<int> ProbeAsync(HttpClient client, PortalEndpoint endpoint, CancellationToken token)
    {
        var results = await Task.WhenAll(ProbeOneAsync(client, MicrosoftProbe, "Microsoft Connect Test", endpoint, token),
            ProbeOneAsync(client, MozillaProbe, "success\n", endpoint, token));
        return results.Count(result => result);
    }

    private static async Task<bool> ProbeOneAsync(HttpClient client, Uri target, string expected,
        PortalEndpoint endpoint, CancellationToken token)
    {
        try
        {
            string body = await GetBodyAsync(client, target, endpoint, token, maximumBytes: 256);
            return string.Equals(body, expected, StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (Exception e) when (IsExpectedNetworkFailure(e)) { return false; }
    }

    private static async Task<string> GetBodyAsync(HttpClient client, Uri target, PortalEndpoint endpoint,
        CancellationToken token, int maximumBytes = DrComProtocol.MaximumBodyBytes,
        Action<DateTimeOffset>? onSubmitting = null)
    {
        if (!IsAllowedTarget(target, endpoint)) throw new FormatException("target_not_allowed");
        using var request = new HttpRequestMessage(HttpMethod.Get, target) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        token.ThrowIfCancellationRequested();
        // This marks client dispatch, not server receipt. Never expose the credential-bearing URI.
        onSubmitting?.Invoke(DateTimeOffset.UtcNow);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is 429 or 503)
        {
            TimeSpan retry = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(60));
            throw new RateLimitedException(TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 5, 300)));
        }
        if ((int)response.StatusCode is >= 300 and <= 399) throw new FormatException("redirect_refused");
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("http_status_unexpected");
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
            throw new FormatException("response_too_large");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        string[] encodings = response.Content.Headers.ContentEncoding.ToArray();
        if (encodings.Length > 1 || encodings.Length == 1 &&
            !string.Equals(encodings[0], "gzip", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(encodings[0], "identity", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("unsupported_content_encoding");
        await using Stream decoded = encodings.Length == 1 &&
            string.Equals(encodings[0], "gzip", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: true) : source;
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            int read = await decoded.ReadAsync(buffer, token);
            if (read == 0) break;
            if (bytes.Length + read > maximumBytes) throw new FormatException("response_too_large");
            bytes.Write(buffer, 0, read);
        }
        string charset = response.Content.Headers.ContentType?.CharSet?.Trim('"').ToLowerInvariant() ?? "utf-8";
        Encoding encoding = charset switch
        {
            "gb2312" or "gbk" or "gb18030" => Encoding.GetEncoding(936),
            "utf-8" or "utf8" or "us-ascii" or "" => Encoding.UTF8,
            _ => throw new FormatException("unsupported_response_encoding")
        };
        return encoding.GetString(bytes.ToArray());
    }

    private static bool IsAllowedTarget(Uri target, PortalEndpoint endpoint)
    {
        if (!target.IsAbsoluteUri || target.UserInfo.Length != 0 || target.Fragment.Length != 0) return false;
        if (target == MicrosoftProbe || target == MozillaProbe) return true;
        if (target.Scheme != "http" || target.Host != endpoint.Address.ToString()) return false;
        return target.Port switch
        {
            80 => target.AbsolutePath is "/" or "/drcom/chkstatus" or "/a40.js",
            801 => target.AbsolutePath is "/eportal/portal/page/loadConfig" or "/eportal/portal/login" ||
                Regex.IsMatch(target.AbsolutePath,
                    "^/eportal/extern/[A-Za-z0-9]{1,64}/[A-Za-z0-9]{1,64}/pc\\.js$", RegexOptions.CultureInvariant),
            _ => false
        };
    }

    private static HttpMessageHandler CreateBoundHandler(CampusNetworkPath path)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(6),
            MaxResponseHeadersLength = 16, PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            ConnectCallback = async (context, token) =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                IPAddress[] addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                    ? [literal] : await BoundDnsResolver.ResolveAsync(context.DnsEndPoint.Host, path, token);
                foreach (var address in addresses)
                {
                    if (!WindowsNetworkPathResolver.IsUsableAddress(address) ||
                        !WindowsNetworkPathResolver.HasRoute(checked((uint)path.InterfaceIndex),
                            path.SourceAddress, address)) continue;
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        // IP_UNICAST_IF takes an interface index in network byte order on Windows.
                        // IP_UNICAST_IF (31) is available on Windows although .NET does not name it.
                        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31,
                            IPAddress.HostToNetworkOrder(path.InterfaceIndex));
                        socket.Bind(new IPEndPoint(path.SourceAddress, 0));
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException) { socket.Dispose(); }
                    catch { socket.Dispose(); throw; }
                }
                throw new HttpRequestException("network_path_unconfirmed");
            }
        };
        // The default TLS policy remains intact; no certificate validation override is installed.
        return handler;
    }

    private static NetworkCheckResult NoPath() => new(false, false, false, "未能确认校园有线路径，请检查网线、地址及 VPN 路由") { ReasonCode = "network_path_unconfirmed" };
    private static bool IsExpectedNetworkFailure(Exception e) => e is HttpRequestException or IOException or InvalidDataException or SocketException or
        JsonException or FormatException or RegexMatchTimeoutException or InvalidOperationException or KeyNotFoundException;
    private static string FailureCode(Exception e) => e switch
    {
        RateLimitedException => "portal_rate_limited",
        FormatException or InvalidDataException or JsonException or RegexMatchTimeoutException or KeyNotFoundException => "portal_unrecognized",
        _ => "network_request_failed"
    };
    private static string FailureMessage(Exception e) => e switch
    {
        RateLimitedException => "校园门户暂时繁忙，等待下次检查",
        FormatException or InvalidDataException or JsonException or RegexMatchTimeoutException or KeyNotFoundException => "门户内容或认证配置不受支持，未继续提交凭据",
        _ => "网络或校园门户暂时不可达，等待下次检查"
    };

    public void Dispose()
    {
        disposed = true;
        // The caller cancels and awaits in-flight work before disposing; do not destroy an in-flight semaphore.
        GC.SuppressFinalize(this);
    }

    private sealed class RateLimitedException(TimeSpan retryAfter) : HttpRequestException("portal_rate_limited")
    {
        public TimeSpan RetryAfter { get; } = retryAfter;
    }
}
