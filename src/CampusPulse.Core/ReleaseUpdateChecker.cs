using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusPulse.Core;

public enum UpdateCheckState { UpToDate, Available, Unavailable }

public sealed record UpdateCheckResult(UpdateCheckState State, string? Version = null,
    Uri? ReleasePage = null, DateTimeOffset? RetryAt = null);

/// <summary>Public release metadata only. This client is never used by the service.</summary>
public sealed class ReleaseUpdateChecker(HttpClient client, TimeSpan? timeout = null)
{
    private readonly TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(10);
    private const string ApiRoot = "https://api.github.com/repos/aaamqrx/CampusPulse/releases";

    public async Task<UpdateCheckResult> CheckAsync(string installedVersion, CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(installedVersion, out var installed))
            return new(UpdateCheckState.Unavailable);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(deadline);
        ReleaseVersion? newest = null;
        string? newestTag = null;
        try
        {
            for (int page = 1; ; page++)
            {
                limit.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoot}?per_page=100&page={page}");
                request.Headers.UserAgent.ParseAdd($"CampusPulse/{ProductInfo.Version}");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                    return new(UpdateCheckState.Unavailable, RetryAt: GetRetryAt(response));
                if (!response.IsSuccessStatusCode) return new(UpdateCheckState.Unavailable);
                await response.Content.LoadIntoBufferAsync(1024 * 1024, limit.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return new(UpdateCheckState.Unavailable);
                foreach (var release in document.RootElement.EnumerateArray())
                {
                    if (release.ValueKind != JsonValueKind.Object ||
                        !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                        !release.TryGetProperty("prerelease", out var preview) ||
                        preview.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                        !release.TryGetProperty("published_at", out var published) || published.ValueKind != JsonValueKind.String ||
                        !DateTimeOffset.TryParse(published.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                        !release.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
                        continue;
                    string tag = tagElement.GetString()!;
                    if (!ReleaseVersion.TryParse(tag, out var version) ||
                        (!installed!.IsPreview && (version!.IsPreview || preview.GetBoolean())) ||
                        version!.CompareTo(installed) <= 0 || newest is not null && version.CompareTo(newest) <= 0)
                        continue;
                    newest = version;
                    newestTag = tag;
                }
                if (document.RootElement.GetArrayLength() < 100) break;
            }
            return newestTag is null ? new(UpdateCheckState.UpToDate)
                : new(UpdateCheckState.Available, newestTag.TrimStart('v'), CreateReleasePage(newestTag));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(UpdateCheckState.Unavailable); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        { return new(UpdateCheckState.Unavailable); }
    }

    public static Uri CreateReleasePage(string tag)
    {
        if (!ReleaseVersion.TryParse(tag, out _)) throw new ArgumentException("Unsupported release tag.", nameof(tag));
        return new Uri($"https://github.com/aaamqrx/CampusPulse/releases/tag/{Uri.EscapeDataString(tag)}");
    }

    private static DateTimeOffset GetRetryAt(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        var retry = now.AddMinutes(1);
        if (response.Headers.RetryAfter?.Date is { } date && date > retry) retry = date;
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.FromMinutes(1))
            retry = now.Add(delta > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delta);
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), out long seconds) && seconds is >= 0 and <= 253402300799)
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (resetAt > retry) retry = resetAt;
        }
        return retry > now.AddDays(1) ? now.AddDays(1) : retry;
    }
}

internal sealed record ReleaseVersion(int Major, int Minor, int Patch, int? Preview) : IComparable<ReleaseVersion>
{
    public bool IsPreview => Preview.HasValue;
    private static readonly Regex Pattern = new(@"^v?(\d+)\.(\d+)\.(\d+)(?:-preview\.(\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (text is null || text.Length > 100) return false;
        var match = Pattern.Match(text);
        if (!match.Success) return false;
        var numbers = new int[4];
        for (int i = 1; i <= 4; i++)
            if (match.Groups[i].Success && !int.TryParse(match.Groups[i].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out numbers[i - 1])) return false;
        version = new(numbers[0], numbers[1], numbers[2], match.Groups[4].Success ? numbers[3] : null);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int comparison = Major.CompareTo(other.Major);
        if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
        if (comparison == 0) comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;
        if (!Preview.HasValue) return other.Preview.HasValue ? 1 : 0;
        return other.Preview.HasValue ? Preview.Value.CompareTo(other.Preview.Value) : -1;
    }
}
