namespace CampusPulse.Core;

public static class DiagnosticText
{
    public static string Format(IEnumerable<StatusEntry> entries) => string.Join(Environment.NewLine,
        entries.OrderByDescending(entry => entry.Time)
            .Select(entry => $"{Time(entry.Time)}  {FormatEvent(entry)}"));

    public static string Format(ServiceSnapshot snapshot)
    {
        var diagnostic = snapshot.Diagnostics ?? new();
        bool hasRejection = diagnostic.FirstRejectionAt.HasValue ||
            !string.IsNullOrEmpty(diagnostic.FirstRejectionReasonCode) || snapshot.Settings.AuthenticationBlocked;
        var lines = new List<string>
        {
            $"CampusPulse {ProductInfo.Version} 脱敏诊断",
            $"首次认证拒绝：{(hasRejection ? Time(diagnostic.FirstRejectionAt) : "未记录")}",
            $"首次拒绝分类：{(hasRejection ? DescribeReason(diagnostic.FirstRejectionReasonCode) : "未记录")}",
            $"首次拒绝来源：{DescribeSource(diagnostic.FirstRejectionSource)}",
            $"最后实际提交：{Time(diagnostic.LastSubmissionAt)}",
            $"最后提交来源：{DescribeSource(diagnostic.LastSubmissionSource)}",
            $"最后提交结果：{(diagnostic.LastSubmissionAt.HasValue ? DescribeReason(diagnostic.LastSubmissionResultCode) : "未记录")}",
            $"最后结果时间：{Time(diagnostic.LastResultAt)}",
            $"拒绝后公网恢复：{Time(diagnostic.RejectionResolvedAt)}",
            $"自动认证保护：{(snapshot.Settings.AuthenticationBlocked ? "已启用" : "未启用")}",
            $"计划自动认证重试：{Time(snapshot.Settings.AuthenticationRetryAt)}",
            "实际提交表示客户端已发起请求；结果未确认时不能推定服务器收到或认证成功。",
            "旧版本未记录的时间及来源保持未知；检测到公网恢复不等于软件提交过认证。",
            "",
            "最近事件："
        };
        lines.Add(Format(snapshot.RecentEvents));
        return string.Join(Environment.NewLine, lines);
    }

    // The WPF event row already supplies its timestamp. New metadata is displayed only
    // through fixed mappings; never echo an unknown code or source into diagnostic output.
    // Message is fixed, sanitized text authored by the service, never a server response body.
    public static string FormatEvent(StatusEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Kind) && string.IsNullOrEmpty(entry.ReasonCode) && string.IsNullOrEmpty(entry.Source))
            return $"【旧记录·来源未知】{entry.Message}";
        return $"【{DescribeSource(entry.Source)}·{DescribeKind(entry.Kind)}】{entry.Message}";
    }

    public static string DescribeSource(string? source) => source switch
    {
        "automatic" or "Automatic" => "自动",
        "manual_reconnect" or "ManualReconnect" => "主动重连",
        "manual_check" or "ManualCheck" => "只读检测",
        _ => "来源未知"
    };

    private static string DescribeKind(string? kind) => kind switch
    {
        "authentication_submitted" => "实际提交",
        "authentication_result" => "认证结果",
        "authentication_skipped" => "未提交",
        "network_result" => "网络检测",
        "operation_requested" or "user_operation" => "用户操作",
        "state" or "state_change" => "状态变化",
        _ => "事件"
    };

    public static string DescribeReason(string? reason) => reason switch
    {
        "authentication_submitted" => "已发起一次校园认证请求，等待结果",
        "authentication_accepted" => "认证响应已接受，仍须独立验证公网",
        "credentials_rejected" => "门户明确拒绝账号或密码",
        "account_payment_required" => "门户明确提示余额或缴费状态异常",
        "account_restricted" => "门户明确提示账号受限",
        "unconfirmed_rejection" => "通用认证拒绝原因未确认，按五分钟间隔自动复查",
        "legacy_unconfirmed" => "旧版通用拒绝首因未知，等待低频复查",
        "unclassified_block" => "旧保护分类无法确认，请核对后主动重试",
        "pending" => "请求已发起，结果尚未确认",
        "authentication_rejected" or "authentication_rejected_unknown" => "认证被拒绝，具体原因未确认",
        "authentication_unknown" => "认证结果无法识别",
        "authentication_timeout" => "认证请求超时，结果未确认",
        "authentication_failed" or "AuthenticationFailed" => "认证请求未成功，将稍后检查",
        "authentication_blocked" or "AuthenticationBlocked" => "自动认证受到保护，本次未提交",
        "legacy_rejection_unknown" or "legacy_authentication_blocked" => "旧版拒绝首因未知",
        "authentication_cooldown" or "AuthenticationCooldown" => "尚在认证重试间隔，本次未提交",
        "authentication_retry_wait" => "等待已安排的认证重试，本次未提交",
        "authentication_not_required" => "已有公网连通证据，本次未提交认证",
        "already_online" => "门户报告已在线，本次无需再次提交",
        "internet_verified" => "检测到校园有线公网通过两项验证",
        "intranet_online" => "门户报告校内网已在线，此选项不提供外网",
        "authentication_required" or "NeedsAuthentication" => "门户明确需要认证，本次仅检测",
        "manual_readonly" or "readonly_check" => "本次仅检测，没有提交认证",
        "partial_connectivity" or "PartialConnectivity" => "公网部分连通或探测受限，本次未提交",
        "portal_online_internet_unverified" => "门户报告已在线，公网尚未验证",
        "portal_status_unknown" or "AuthenticationNotRequiredOrUnknown" => "门户状态不明确，本次未提交",
        "portal_unrecognized" or "PortalUnrecognized" => "门户身份或模板未确认，本次未提交",
        "network_path_unconfirmed" => "未能确认校园有线路径，本次未提交",
        "network_path_changed" => "网络路径发生变化，本次未提交",
        "missing_credentials" or "MissingCredentials" => "尚无匹配的本机凭据，本次未提交",
        "invalid_credentials_format" => "账号或密码格式不受支持，本次未提交",
        "unsupported_portal_address" => "登录地址不受支持，本次未提交",
        "unsupported_carrier" => "运营商选项不受支持，本次未提交",
        "unsupported_configuration" => "门户协议配置不受支持",
        "portal_busy" or "portal_rate_limited" => "认证服务繁忙或限流，将按等待提示重试",
        "portal_temporary_failure" => "认证服务暂时故障，将稍后重试",
        "check_timeout" => "网络检查超时，等待下次检查",
        "check_canceled" or "CheckCanceled" => "检查已取消或超时，结果未确认",
        "check_failed" or "CheckFailed" => "网络检查暂未完成，将稍后重试",
        "network_request_failed" => "网络请求失败，结果未确认",
        "portal_unavailable" => "门户暂不可用，等待下次检查",
        "AwaitingInternetVerification" or "awaiting_internet_verification" => "认证响应已接受，公网尚未验证可用",
        "ConfigurationUnreadable" or "configuration_unreadable" => "本机配置或凭据无法读取，已暂停",
        "ConfigurationWriteFailed" or "configuration_write_failed" => "本机设置保存失败，未确认生效",
        "PowerRequestFailed" or "power_request_failed" => "防睡眠请求未生效",
        "check_requested" => "已安排一次只读检测",
        "reconnect_requested" => "已安排一次按需主动重连",
        "settings_saved" => "设置已保存",
        "automatic_paused" or "AutomaticPaused" => "自动重连已暂停",
        "credentials_cleared" => "本机凭据已清除，自动重连已暂停",
        "" or null => "结果或分类未知",
        _ => "未知分类"
    };

    private static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "未知或未记录";
}
