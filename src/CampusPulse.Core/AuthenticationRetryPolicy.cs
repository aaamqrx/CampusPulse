namespace CampusPulse.Core;

public static class AuthenticationRetryPolicy
{
    public static readonly TimeSpan UnconfirmedRetryDelay = TimeSpan.FromMinutes(5);
    private const string PasswordMessage = "校园认证拒绝账号或密码，请检查后主动重试";
    private const string PaymentMessage = "校园账号余额或缴费状态异常，请处理后主动重试";
    private const string RestrictedMessage = "校园账号受限，请处理后主动重试";
    private const string LegacyMessage = "旧版认证拒绝原因未确认，等待低频自动复查";

    public static bool AllowsLegacyRetry(CampusSettings settings) =>
        settings.AuthenticationBlocked && settings.BlockedReasonCode == "legacy_unconfirmed";

    // These are only fixed client-generated messages, never raw portal response text.
    // The old password message conflated a generic result code with a confirmed error.
    public static CampusSettings NormalizePersistedBlock(CampusSettings settings, DateTimeOffset now)
    {
        DateTimeOffset? retry = settings.AuthenticationRetryAt is { } stored
            ? (stored > now.Add(UnconfirmedRetryDelay) ? now.Add(UnconfirmedRetryDelay) : stored)
            : null;
        if (!settings.AuthenticationBlocked)
            return settings with
            {
                AuthenticationRetryAt = retry,
                BlockedReasonCode = settings.BlockedReasonCode == "unconfirmed_rejection" ? "unconfirmed_rejection" : "",
                BlockedReason = settings.BlockedReasonCode == "unconfirmed_rejection"
                    ? "校园认证暂被拒绝，原因未确认；五分钟后自动重试" : ""
            };

        string code = settings.BlockedReasonCode ?? "";
        if (code.Length == 0)
            code = settings.BlockedReason switch
            {
                PaymentMessage => "account_payment_required",
                RestrictedMessage => "account_restricted",
                PasswordMessage => "legacy_unconfirmed",
                _ => "unclassified_block"
            };
        return code switch
        {
            "credentials_rejected" => settings with
            { BlockedReasonCode = code, BlockedReason = PasswordMessage, AuthenticationRetryAt = null },
            "account_payment_required" => settings with
            { BlockedReasonCode = code, BlockedReason = PaymentMessage, AuthenticationRetryAt = null },
            "account_restricted" => settings with
            { BlockedReasonCode = code, BlockedReason = RestrictedMessage, AuthenticationRetryAt = null },
            "legacy_unconfirmed" => settings with
            { BlockedReasonCode = code, BlockedReason = LegacyMessage, AuthenticationRetryAt = retry ?? now.Add(UnconfirmedRetryDelay) },
            _ => settings with
            {
                BlockedReasonCode = "unclassified_block", AuthenticationRetryAt = null,
                BlockedReason = "上次认证保护分类无法读取，请核对配置后主动重试"
            }
        };
    }
}
