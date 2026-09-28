using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CampusPulse.Core;

/// <summary>Conservatively accepts only the physical Ethernet route to the selected portal.</summary>
public sealed class WindowsNetworkPathResolver : INetworkPathResolver
{
    public CampusNetworkPath? Resolve(IPAddress portalAddress)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var matches = NetworkInterface.GetAllNetworkInterfaces().Where(n =>
                n.OperationalStatus == OperationalStatus.Up && !n.IsReceiveOnly &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or
                    NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT).ToArray();
            var paths = new List<CampusNetworkPath>();
            foreach (var adapter in matches)
            {
                try
                {
                    uint index = checked((uint)(adapter.GetIPProperties().GetIPv4Properties()?.Index ?? 0));
                    if (index == 0 || !IsPhysicalConnectedInterface(index)) continue;
                    var addresses = adapter.GetIPProperties().UnicastAddresses
                        .Where(a => OperatingSystem.IsWindows() && a.Address.AddressFamily == AddressFamily.InterNetwork &&
                            a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred &&
                            IsUsableAddress(a.Address)).Select(a => a.Address).Distinct().ToArray();
                    // Ambiguous source addresses require explicit adapter/address selection, not a guess.
                    if (addresses.Length != 1 || !HasRoute(index, addresses[0], portalAddress)) continue;
                    string mac = adapter.GetPhysicalAddress().ToString();
                    if (mac.Length == 12) paths.Add(new(addresses[0], checked((int)index), mac));
                }
                catch (Exception e) when (e is NetworkInformationException or SocketException or InvalidOperationException)
                {
                    // A broken virtual adapter must not hide a valid physical Ethernet candidate.
                }
            }
            return paths.Count == 1 ? paths[0] : null;
        }
        catch (Exception e) when (e is NetworkInformationException or SocketException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool IsUsableAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] is > 0 and < 224 && bytes[0] != 127 && !(bytes[0] == 169 && bytes[1] == 254);
    }

    internal static bool HasRoute(uint interfaceIndex, IPAddress source, IPAddress destination)
    {
        if (!OperatingSystem.IsWindows() || source.AddressFamily != AddressFamily.InterNetwork ||
            destination.AddressFamily != AddressFamily.InterNetwork) return false;
        nint sourceBuffer = Marshal.AllocHGlobal(28);
        nint destinationBuffer = Marshal.AllocHGlobal(28);
        nint routeBuffer = Marshal.AllocHGlobal(512);
        nint bestSourceBuffer = Marshal.AllocHGlobal(28);
        try
        {
            WriteSocketAddress(sourceBuffer, source);
            WriteSocketAddress(destinationBuffer, destination);
            Marshal.Copy(new byte[512], 0, routeBuffer, 512);
            Marshal.Copy(new byte[28], 0, bestSourceBuffer, 28);
            if (GetBestRoute2(0, interfaceIndex, sourceBuffer, destinationBuffer, 0,
                    routeBuffer, bestSourceBuffer) != 0 ||
                Marshal.ReadInt32(routeBuffer, 8) != checked((int)interfaceIndex)) return false;
            var bestSource = new byte[4];
            Marshal.Copy(bestSourceBuffer + 4, bestSource, 0, 4);
            return bestSource.AsSpan().SequenceEqual(source.GetAddressBytes());
        }
        finally
        {
            Marshal.FreeHGlobal(sourceBuffer);
            Marshal.FreeHGlobal(destinationBuffer);
            Marshal.FreeHGlobal(routeBuffer);
            Marshal.FreeHGlobal(bestSourceBuffer);
        }
    }

    private static void WriteSocketAddress(nint buffer, IPAddress address)
    {
        Marshal.Copy(new byte[28], 0, buffer, 28);
        Marshal.WriteInt16(buffer, 0, 2); // AF_INET in SOCKADDR_INET.
        Marshal.Copy(address.GetAddressBytes(), 0, buffer + 4, 4);
    }

    private static bool IsPhysicalConnectedInterface(uint index)
    {
        // MIB_IF_ROW2 has stable native field offsets; allocate more than its full size.
        // HardwareInterface and ConnectorPresent are bits 0 and 2 of the flags byte.
        // Using a native buffer avoids marshalling variable-width friendly names.
        nint row = Marshal.AllocHGlobal(2048);
        try
        {
            Marshal.Copy(new byte[2048], 0, row, 2048);
            Marshal.WriteInt32(row, 8, checked((int)index));
            if (GetIfEntry2(row) != 0) return false;
            byte flags = Marshal.ReadByte(row, 1152);
            return (flags & 0x05) == 0x05;
        }
        finally { Marshal.FreeHGlobal(row); }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetBestRoute2(nint interfaceLuid, uint interfaceIndex, nint sourceAddress,
        nint destinationAddress, uint addressSortOptions, nint bestRoute, nint bestSourceAddress);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfEntry2(nint row);
}
