using System.Net;

namespace CampusPulse.Core;

public sealed record NetworkCheckResult(bool InternetAvailable, bool PortalRecognized,
    bool NeedsAuthentication, string Message, bool PartialConnectivity = false)
{
    public string ReasonCode { get; init; } = "";
}

public sealed record LoginResult(bool Accepted, bool CredentialsRejected, string Message)
{
    public string ReasonCode { get; init; } = "";
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>Ephemeral, local-only interface identity. Never persist or log this record.</summary>
public sealed record CampusNetworkPath(IPAddress SourceAddress, int InterfaceIndex, string MacAddress);

public interface INetworkPathResolver
{
    CampusNetworkPath? Resolve();
}

public static class RetryPolicy
{
    public static readonly TimeSpan AcceptedLoginGuard = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumAuthenticationInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan[] PostLoginChecks = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    // failureCount is one-based. Inject jitter in tests; keep the upper bound at 300 seconds.
    public static TimeSpan GetDelay(int failureCount, double? jitter = null, TimeSpan? retryAfter = null)
    {
        int seconds = failureCount switch { <= 1 => 30, 2 => 60, 3 => 120, _ => 300 };
        double factor = 0.95 + 0.1 * Math.Clamp(jitter ?? Random.Shared.NextDouble(), 0, 1);
        double delay = Math.Clamp(seconds * factor, 5, 300);
        if (retryAfter is { } requested)
            delay = Math.Max(delay, Math.Clamp(requested.TotalSeconds, 5, 300));
        return TimeSpan.FromSeconds(delay);
    }
}
