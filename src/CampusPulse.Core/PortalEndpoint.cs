using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace CampusPulse.Core;

/// <summary>A user-selected campus portal root. Only private IPv4 HTTP roots are supported.</summary>
public sealed record PortalEndpoint(Uri Root, IPAddress Address)
{
    public static bool TryCreate(string? input, [NotNullWhen(true)] out PortalEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 256 ||
            !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || uri.HostNameType != UriHostNameType.IPv4 ||
            uri.Port != 80 || uri.AbsolutePath != "/" || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
            !IPAddress.TryParse(uri.Host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        bool privateAddress = bytes[0] == 10 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
        if (!privateAddress) return false;
        endpoint = new(new Uri($"http://{address}/"), address);
        return true;
    }

    internal Uri At(string path, int port = 80) => new Uri($"http://{Address}:{port}{path}");
}
