namespace CampusPulse.Core;

public sealed record CampusSettings
{
    public const string SupportedPortal = "http://10.62.164.38/";
    public int ConfigVersion { get; init; } = 2;
    public bool Enabled { get; init; }
    public bool StartWithWindows { get; init; } = true;
    public bool UnattendedMode { get; init; }
    public string Username { get; init; } = "";
    public string PortalUrl { get; init; } = SupportedPortal;
    public string Carrier { get; init; } = "telecom";
    public int OnlineCheckSeconds { get; init; } = 120;
    public bool AuthenticationBlocked { get; init; }
    public string BlockedReason { get; init; } = "";
    public string BlockedReasonCode { get; init; } = "";
    public DateTimeOffset? AuthenticationRetryAt { get; init; }
}

public enum ConnectionState
{
    Paused, NeedsConfiguration, Checking, WaitingNetwork, Authenticating,
    Online, AuthenticationRejected, PortalUnavailable, LimitedConnectivity, IntranetOnline
}

public sealed record StatusEntry(DateTimeOffset Time, string Message)
{
    // Empty metadata denotes a legacy event; never infer a submission from its message.
    public string Kind { get; init; } = "";
    public string ReasonCode { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed record AuthenticationDiagnostics
{
    public DateTimeOffset? FirstRejectionAt { get; init; }
    public string FirstRejectionReasonCode { get; init; } = "";
    public string FirstRejectionSource { get; init; } = "";
    public DateTimeOffset? LastSubmissionAt { get; init; }
    public string LastSubmissionSource { get; init; } = "";
    public string LastSubmissionResultCode { get; init; } = "";
    public DateTimeOffset? LastResultAt { get; init; }
    public DateTimeOffset? RejectionResolvedAt { get; init; }
}

public sealed record ServiceSnapshot
{
    public CampusSettings Settings { get; init; } = new();
    public bool HasPassword { get; init; }
    public ConnectionState State { get; init; } = ConnectionState.Paused;
    public string Message { get; init; } = "尚未启用自动连接";
    public DateTimeOffset? LastCheck { get; init; }
    public DateTimeOffset? LastSuccess { get; init; }
    public DateTimeOffset? NextCheck { get; init; }
    public bool KeepingAwake { get; init; }
    public bool? ActualStartWithWindows { get; init; }
    public string ErrorCode { get; init; } = "";
    public AuthenticationDiagnostics Diagnostics { get; init; } = new();
    public IReadOnlyList<StatusEntry> RecentEvents { get; init; } = [];
}

public sealed record ServiceRequest(string Command, CampusSettings? Settings = null, string? Password = null);
public sealed record ServiceReply(bool Success, string Message, ServiceSnapshot? Snapshot = null);

public static class ProductInfo
{
    public const string ServiceName = "CampusPulse";
    public const string PipeName = "CampusPulse.Control.v1";
    public const string Version = "0.1.0-preview.3";
}
