using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusPulse.Core;

internal sealed record PortalParameters(string Address, string Mac, string Vlan);

/// <summary>Parses only the supported scalar/JSON contract; it never executes portal JavaScript.</summary>
public static class DrComProtocol
{
    public const string Callback = "campuspulse";
    public const string PortalRoot = "http://10.62.164.14/";
    public const string LoginEndpoint = "http://10.62.164.14:801/eportal/portal/login";
    internal const int MaximumBodyBytes = 512 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static bool TryNormalizeUsername(string? username, out string normalized)
    {
        normalized = (username ?? "").Trim();
        if (normalized.EndsWith("@dx", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^3];
        if (normalized.Length is < 1 or > 128 || normalized.Contains('@') ||
            normalized.StartsWith(",", StringComparison.Ordinal) || normalized.Any(char.IsControl) ||
            normalized.Any(char.IsWhiteSpace))
        {
            normalized = "";
            return false;
        }
        return true;
    }

    public static bool IsValidPassword(string? password) =>
        password is { Length: >= 1 and <= 256 } && !password.Contains('\0') &&
        !password.Contains('\r') && !password.Contains('\n');

    public static JsonDocument ParseJsonp(string body)
    {
        if (body.Length > MaximumBodyBytes) throw new FormatException("response_too_large");
        string value = body.Trim().TrimStart('\uFEFF');
        // Require the exact requested callback, a single argument, and no trailing script.
        string prefix = Callback + "(";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) throw new FormatException("invalid_jsonp");
        if (value.EndsWith(';')) value = value[..^1].TrimEnd();
        if (!value.EndsWith(')')) throw new FormatException("invalid_jsonp");
        var document = JsonDocument.Parse(value[prefix.Length..^1], new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new FormatException("invalid_json_object");
        }
        return document;
    }

    internal static PortalParameters ParsePortal(string html, CampusNetworkPath path)
    {
        if (!Regex.IsMatch(html, @"<!--\s*Dr\.COMWebLoginID_[013]\.htm\s*-->", RegexOptions.CultureInvariant, RegexTimeout))
            throw new FormatException("portal_identity_mismatch");

        string carrier = Scalar(html, "carrier");
        using (var json = JsonDocument.Parse(carrier))
        {
            var entries = json.RootElement.GetProperty("yys").GetProperty("data");
            if (!entries.EnumerateArray().Any(entry => Value(entry, "id") == "2" && Value(entry, "suffix") == "@dx"))
                throw new FormatException("unsupported_carrier");
        }

        string address = Scalar(html, "v46ip", required: false);
        if (string.IsNullOrWhiteSpace(address)) address = Scalar(html, "ss5");
        address = address.Trim();
        if (!IPAddress.TryParse(address, out var parsed) || !parsed.Equals(path.SourceAddress))
            throw new FormatException("portal_address_mismatch");

        string mac = Scalar(html, "ss4").Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(mac, "^[0-9A-F]{12}$", RegexOptions.CultureInvariant, RegexTimeout))
            throw new FormatException("portal_mac_invalid");
        if (mac != "000000000000" && mac != "111111111111" && !mac.Equals(path.MacAddress, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("portal_mac_mismatch");
        // An all-zero portal MAC is an intentional Dr.COM placeholder, not a remembered machine address.
        string vlan = Scalar(html, "vlanid", required: false);
        if (vlan.Length == 0) vlan = "1";
        if (!int.TryParse(vlan, out int vlanNumber) || vlanNumber is < 0 or > 4095)
            throw new FormatException("portal_vlan_invalid");
        return new(address, mac, vlan);
    }

    internal static void ValidateConfiguration(string body)
    {
        using var document = ParseJsonp(body);
        var root = document.RootElement;
        if (Value(root, "code") != "1" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new FormatException("unsupported_configuration");
        var expected = new Dictionary<string, string>
        {
            ["login_method"] = "1", ["account_prefix"] = "1", ["en_md5"] = "0",
            ["password_cut"] = "0", ["enable_r3"] = "0", ["enable_https"] = "0",
            ["ep_http_port"] = "801", ["check_online_method"] = "0", ["io_mode"] = "0",
            ["ipv6_state"] = "0", ["account_suffix"] = "", ["domain_name"] = ""
        };
        foreach (var field in expected)
            if (!data.TryGetProperty(field.Key, out _) || Value(data, field.Key) != field.Value)
                throw new FormatException("unsupported_configuration");
    }

    internal static bool? ParseOnlineStatus(string body, CampusNetworkPath path)
    {
        using var document = ParseJsonp(body);
        var root = document.RootElement;
        foreach (string field in new[] { "v46ip", "ss5", "v4ip" })
            if (root.TryGetProperty(field, out _) &&
                (!IPAddress.TryParse(Value(root, field).Trim(), out var address) || !address.Equals(path.SourceAddress)))
                throw new FormatException("status_address_mismatch");
        return Value(root, "result") switch { "0" => false, "1" => true, _ => null };
    }

    internal static string ParseJavaScriptVersion(string script)
    {
        string version = Scalar(script, "jsVersion");
        if (!Regex.IsMatch(version, "^[0-9]{1,2}\\.[0-9]{1,2}\\.[0-9]{1,3}$", RegexOptions.CultureInvariant, RegexTimeout))
            throw new FormatException("unsupported_script_version");
        return version;
    }

    internal static Uri ConfigurationUri(PortalParameters terminal) => Query(
        "http://10.62.164.14:801/eportal/portal/page/loadConfig", new()
        {
            ["callback"] = Callback, ["program_index"] = "", ["wlan_vlan_id"] = terminal.Vlan,
            ["wlan_user_ip"] = Base64(terminal.Address), ["wlan_user_ipv6"] = "",
            ["wlan_user_ssid"] = "", ["wlan_user_areaid"] = "", ["wlan_ac_ip"] = "",
            ["wlan_ap_mac"] = "000000000000", ["gw_id"] = "000000000000", ["jsVersion"] = "4.X"
        });

    internal static Uri LoginUri(string username, string password, PortalParameters terminal, string version) => Query(
        LoginEndpoint, new()
        {
            ["callback"] = Callback, ["login_method"] = "1", ["user_account"] = ",0," + username + "@dx",
            ["user_password"] = password, ["wlan_user_ip"] = terminal.Address, ["wlan_user_ipv6"] = "",
            ["wlan_user_mac"] = terminal.Mac, ["wlan_ac_ip"] = "", ["wlan_ac_name"] = "",
            ["jsVersion"] = version, ["terminal_type"] = "1", ["lang"] = "zh-cn"
        });

    public static LoginResult ClassifyLoginResponse(string body)
    {
        using var document = ParseJsonp(body);
        var root = document.RootElement;
        string result = Value(root, "result");
        if (result is "1" or "ok") return new(true, false, "校园认证已接受，仍需验证互联网") { ReasonCode = "authentication_accepted" };
        if (result != "0") return Unknown();
        string code = Value(root, "ret_code");
        // ret_code values are documented by this portal's own a43.js. Unknown message text is never logged.
        return code switch
        {
            "1" => new(false, true, "校园认证拒绝账号或密码，请检查后主动重试") { ReasonCode = "credentials_rejected" },
            "2" => new(true, false, "门户报告终端已在线，仍需验证互联网") { ReasonCode = "already_online" },
            "3" or "11" => new(false, false, "认证服务繁忙，请稍后重试") { ReasonCode = "portal_busy", RetryAfter = TimeSpan.FromMinutes(1) },
            "5" or "6" or "8" or "9" or "10" => new(false, false, "认证服务暂时故障，请稍后重试") { ReasonCode = "portal_temporary_failure" },
            // ret_code 7 is a generic Radius failure, not evidence of a wrong password.
            "7" or "4" => ClassifyExactAccountMessage(Value(root, "msg")),
            "998" => new(false, false, "门户协议参数不受支持，请检查适配版本") { ReasonCode = "unsupported_configuration" },
            _ => Unknown()
        };
    }

    private static LoginResult ClassifyExactAccountMessage(string message) => message.Trim() switch
    {
        // Conservative, exact messages only. These categories need additional real-school fixtures.
        "账号或密码不对，请重新输入" or "账号或密码不对，请重新输入！" or "用户名或密码错误" =>
            new(false, true, "校园认证拒绝账号或密码，请检查后主动重试") { ReasonCode = "credentials_rejected" },
        "账号欠费" or "账户余额不足" => new(false, true, "校园账号余额或缴费状态异常，请处理后主动重试") { ReasonCode = "account_payment_required" },
        "账号已停用" or "账号被禁用" => new(false, true, "校园账号受限，请处理后主动重试") { ReasonCode = "account_restricted" },
        _ => Unknown()
    };

    private static LoginResult Unknown() => new(false, false, "无法识别认证结果，请稍后检查或反馈脱敏诊断") { ReasonCode = "authentication_unknown" };
    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    internal static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString() : "";

    private static string Scalar(string script, string name, bool required = true)
    {
        var match = Regex.Match(script, @"(?<![\w.])" + Regex.Escape(name) + @"\s*=\s*(['" + "\"" + @"])(?<value>[^\r\n]*?)\1\s*[;,]",
            RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success)
        {
            if (required) throw new FormatException("portal_parameter_missing");
            return "";
        }
        string result = match.Groups["value"].Value;
        if (result.Length > 16384 || result.Contains('\\')) throw new FormatException("portal_parameter_unsupported");
        return result;
    }

    private static Uri Query(string endpoint, Dictionary<string, string> values) => new(endpoint + "?" +
        string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
}
