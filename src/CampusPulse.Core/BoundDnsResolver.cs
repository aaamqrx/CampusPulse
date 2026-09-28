using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CampusPulse.Core;

/// <summary>Resolves the two fixed public probes through DNS servers on the selected Ethernet path.</summary>
internal static class BoundDnsResolver
{
    private const int MaximumReplyBytes = 1500;

    internal static async Task<IPAddress[]> ResolveAsync(string host, CampusNetworkPath path, CancellationToken token)
    {
        if (host is not ("www.msftconnecttest.com" or "detectportal.firefox.com"))
            throw new HttpRequestException("probe_host_not_allowed");
        foreach (var server in DnsServers(path.InterfaceIndex))
        {
            if (!WindowsNetworkPathResolver.HasRoute(checked((uint)path.InterfaceIndex), path.SourceAddress, server))
                continue;
            try
            {
                var addresses = await QueryAsync(host, server, path, token);
                if (addresses.Length > 0) return addresses;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception e) when (e is SocketException or IOException or FormatException) { }
        }
        // Some networks provide DNS outside the adapter configuration. The fallback is still
        // restricted to public addresses and the selected physical interface at connect time.
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token);
            return addresses.Where(IsPublicProbeAddress).Distinct().ToArray();
        }
        catch (Exception e) when (e is SocketException or HttpRequestException) { return []; }
    }

    private static IEnumerable<IPAddress> DnsServers(int interfaceIndex)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (adapter.GetIPProperties().GetIPv4Properties()?.Index != interfaceIndex) continue;
                return adapter.GetIPProperties().DnsAddresses
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                        WindowsNetworkPathResolver.IsUsableAddress(address)).Distinct().ToArray();
            }
            catch (NetworkInformationException) { }
        }
        return [];
    }

    private static async Task<IPAddress[]> QueryAsync(string host, IPAddress server,
        CampusNetworkPath path, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31,
            IPAddress.HostToNetworkOrder(path.InterfaceIndex));
        socket.Bind(new IPEndPoint(path.SourceAddress, 0));
        socket.Connect(new IPEndPoint(server, 53));
        ushort id = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
        byte[] query = CreateQuery(host, id);
        await socket.SendAsync(query, SocketFlags.None, deadline.Token);
        var buffer = new byte[MaximumReplyBytes];
        int count = await socket.ReceiveAsync(buffer, SocketFlags.None, deadline.Token);
        return ParseResponse(buffer.AsSpan(0, count), host, id);
    }

    private static byte[] CreateQuery(string host, ushort id)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (string label in host.Split('.'))
        {
            if (label.Length is < 1 or > 63) throw new FormatException("dns_name_invalid");
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange([0, 0, 1, 0, 1]);
        return bytes.ToArray();
    }

    internal static IPAddress[] ParseResponse(ReadOnlySpan<byte> reply, string host, ushort id)
    {
        if (reply.Length < 12 || Read16(reply, 0) != id || (reply[2] & 0xFA) != 0x80 ||
            (reply[3] & 0x0F) != 0 || Read16(reply, 4) != 1 || Read16(reply, 6) > 32)
            return [];
        int position = 12;
        if (!string.Equals(ReadName(reply, ref position), host, StringComparison.OrdinalIgnoreCase) ||
            Read16(reply, position) != 1 || Read16(reply, position + 2) != 1) return [];
        position += 4;
        var cnames = new List<(string From, string To)>();
        var addresses = new List<(string Name, IPAddress Address)>();
        for (int i = 0; i < Read16(reply, 6); i++)
        {
            string name = ReadName(reply, ref position);
            if (position + 10 > reply.Length) throw new FormatException("dns_record_truncated");
            int type = Read16(reply, position), recordClass = Read16(reply, position + 2);
            int length = Read16(reply, position + 8);
            position += 10;
            if (position + length > reply.Length) throw new FormatException("dns_record_truncated");
            if (recordClass == 1 && type == 1 && length == 4)
            {
                var address = new IPAddress(reply.Slice(position, 4));
                if (IsPublicProbeAddress(address)) addresses.Add((name, address));
            }
            else if (recordClass == 1 && type == 5)
            {
                int aliasPosition = position;
                string alias = ReadName(reply, ref aliasPosition);
                if (aliasPosition <= position + length) cnames.Add((name, alias));
            }
            position += length;
        }
        var acceptedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host };
        for (int i = 0; i < 8; i++)
        {
            bool changed = false;
            foreach (var (from, to) in cnames)
                if (acceptedNames.Contains(from)) changed |= acceptedNames.Add(to);
            if (!changed) break;
        }
        return addresses.Where(item => acceptedNames.Contains(item.Name))
            .Select(item => item.Address).Distinct().ToArray();
    }

    private static string ReadName(ReadOnlySpan<byte> reply, ref int position)
    {
        int cursor = position, jumps = 0;
        bool redirected = false;
        var labels = new List<string>();
        while (true)
        {
            if (cursor >= reply.Length || ++jumps > 32) throw new FormatException("dns_name_invalid");
            byte length = reply[cursor];
            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= reply.Length) throw new FormatException("dns_name_invalid");
                if (!redirected) position = cursor + 2;
                cursor = ((length & 0x3F) << 8) | reply[cursor + 1];
                redirected = true;
                continue;
            }
            if (length == 0)
            {
                if (!redirected) position = cursor + 1;
                return string.Join('.', labels);
            }
            if (length > 63 || cursor + 1 + length > reply.Length)
                throw new FormatException("dns_name_invalid");
            labels.Add(Encoding.ASCII.GetString(reply.Slice(cursor + 1, length)));
            cursor += 1 + length;
        }
    }

    private static int Read16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + 2 > bytes.Length) throw new FormatException("dns_record_truncated");
        return (bytes[offset] << 8) | bytes[offset + 1];
    }

    internal static bool IsPublicProbeAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] is > 0 and < 224 && b[0] != 10 && b[0] != 127 &&
            !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
            !(b[0] == 169 && b[1] == 254) &&
            !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
            !(b[0] == 192 && b[1] == 168) &&
            !(b[0] == 198 && b[1] is 18 or 19);
    }
}
