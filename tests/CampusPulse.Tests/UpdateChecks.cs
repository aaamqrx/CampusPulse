using System.Net;
using System.Text.Json;
using CampusPulse.Core;

internal static class UpdateChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("UPDATE-01 preview numeric order and fixed public request", NumericOrder),
        ("UPDATE-02 preview accepts same-base stable release", PreviewToStable),
        ("UPDATE-03 stable ignores preview and API prerelease flags", StableChannel),
        ("UPDATE-04 draft unpublished invalid and overflow tags are ignored", InvalidReleases),
        ("UPDATE-05 empty and older releases are up to date", EmptyAndOlder),
        ("UPDATE-06 newer version on second page is discovered", Pagination),
        ("UPDATE-07 network and malformed responses remain unavailable", Failures),
        ("UPDATE-08 HTTP rate limit supplies a retry deadline", RateLimit),
        ("UPDATE-09 timed out check does not claim up to date", Timeout),
        ("UPDATE-10 caller cancellation propagates", Cancellation),
        ("UPDATE-11 browser target is derived from a validated tag", BrowserTarget),
        ("UPDATE-12 stable numeric version beats a later-published old version", StableOrder)
    ];

    private static object Release(string tag, bool draft = false, bool? preview = null, bool published = true) =>
        new { tag_name = tag, draft, prerelease = preview ?? tag.Contains("-preview."),
            published_at = published ? "2026-10-09T00:00:00Z" : null, html_url = "https://untrusted.invalid/ignored" };
    private static HttpResponseMessage Response(params object[] releases) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) };
    private static async Task<UpdateCheckResult> Run(string installed, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply,
        TimeSpan? timeout = null, CancellationToken token = default)
    {
        using var client = new HttpClient(new Handler(reply)) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        return await new ReleaseUpdateChecker(client, timeout).CheckAsync(installed, token);
    }
    private static Task<UpdateCheckResult> From(string installed, params object[] releases) =>
        Run(installed, (_, _) => Task.FromResult(Response(releases)));
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static async Task NumericOrder()
    {
        var result = await Run("0.1.0-preview.3", (request, _) =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri ==
                "https://api.github.com/repos/aaamqrx/CampusPulse/releases?per_page=100&page=1", "fixed API");
            Check(request.Headers.Authorization is null && request.Headers.UserAgent.Count > 0, "anonymous request");
            return Task.FromResult(Response(Release("v0.1.0-preview.4"), Release("v0.1.0-preview.10"), Release("v0.1.0-preview.2")));
        });
        Check(result.State == UpdateCheckState.Available && result.Version == "0.1.0-preview.10", "numeric preview ordering");
    }
    private static async Task PreviewToStable()
    {
        var result = await From("0.1.0-preview.3", Release("v0.1.0"));
        Check(result.State == UpdateCheckState.Available && result.Version == "0.1.0", "same-base stable wins");
    }
    private static async Task StableChannel()
    {
        var result = await From("0.1.0", Release("v0.2.0-preview.10"), Release("v0.2.0", preview: true), Release("v0.1.0"));
        Check(result.State == UpdateCheckState.UpToDate, "stable channel skips all previews");
    }
    private static async Task InvalidReleases()
    {
        var result = await From("0.1.0-preview.3", Release("v1.0.0", draft: true), Release("v2.0.0", published: false),
            Release("v2147483648.0.0"), Release("v9.0.0-beta.1"), Release("../evil"), Release("v0.1.0-preview.3"));
        Check(result.State == UpdateCheckState.UpToDate, "ignore unsafe or unpublished versions");
    }
    private static async Task EmptyAndOlder()
    {
        Check((await From("0.1.0-preview.3")).State == UpdateCheckState.UpToDate, "empty");
        Check((await From("0.1.0-preview.3", Release("v0.1.0-preview.2"))).State == UpdateCheckState.UpToDate, "older");
    }
    private static async Task Pagination()
    {
        int calls = 0;
        var result = await Run("0.1.0-preview.3", (request, _) =>
        {
            calls++;
            Check(request.RequestUri!.Query.EndsWith($"page={calls}", StringComparison.Ordinal), "page number");
            return Task.FromResult(calls == 1 ? Response(Enumerable.Range(0,100).Select(_ => Release("v0.0.1")).ToArray())
                : Response(Release("v0.1.0-preview.10")));
        });
        Check(calls == 2 && result.Version == "0.1.0-preview.10", "read subsequent page");
    }
    private static async Task Failures()
    {
        Check((await Run("0.1.0", (_, _) => throw new HttpRequestException("offline"))).State == UpdateCheckState.Unavailable, "transport");
        Check((await Run("0.1.0", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)))).State == UpdateCheckState.Unavailable, "HTTP");
        Check((await Run("0.1.0", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{not JSON") }))).State == UpdateCheckState.Unavailable, "JSON");
        Check((await Run("0.1.0", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://untrusted.invalid/") } }))).State == UpdateCheckState.Unavailable, "redirect");
    }
    private static async Task RateLimit()
    {
        foreach (var code in new[] { HttpStatusCode.Forbidden, (HttpStatusCode)429 })
        {
            var result = await Run("0.1.0", (_, _) =>
            {
                var response = new HttpResponseMessage(code);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
                return Task.FromResult(response);
            });
            Check(result.State == UpdateCheckState.Unavailable && result.RetryAt > DateTimeOffset.UtcNow.AddMinutes(4), "respect rate limit");
        }
        var excessive = await Run("0.1.0", (_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(int.MaxValue));
            return Task.FromResult(response);
        });
        Check(excessive.State == UpdateCheckState.Unavailable && excessive.RetryAt <= DateTimeOffset.UtcNow.AddDays(1), "oversized delay is bounded");
    }
    private static async Task Timeout()
    {
        var result = await Run("0.1.0", async (_, token) =>
        { await Task.Delay(System.Threading.Timeout.Infinite, token); return Response(); }, TimeSpan.FromMilliseconds(30));
        Check(result.State == UpdateCheckState.Unavailable, "timeout is unavailable");
    }
    private static async Task Cancellation()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await Run("0.1.0", (_, _) => Task.FromResult(Response()), token: canceled.Token); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("caller cancellation must propagate");
    }
    private static async Task BrowserTarget()
    {
        var result = await From("0.1.0-preview.3", Release("v0.1.0-preview.4"));
        Check(result.ReleasePage!.AbsoluteUri == "https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.4", "ignore html_url");
        try { ReleaseUpdateChecker.CreateReleasePage("../evil"); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("unsafe tag accepted");
    }
    private static async Task StableOrder()
    {
        var result = await From("0.1.0", Release("v0.2.0"), Release("v0.1.1"), Release("v0.10.0"));
        Check(result.Version == "0.10.0", "compare version, not list order");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request, cancellationToken);
    }
}
