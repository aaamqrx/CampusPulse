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
            uint index = BestInterface(portalAddress);
            if (index == 0 || !IsPhysicalConnectedInterface(index)) return null;
            var matches = NetworkInterface.GetAllNetworkInterfaces().Where(n =>
                n.OperationalStatus == OperationalStatus.Up && !n.IsReceiveOnly &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or
                    NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT &&
                n.GetIPProperties().GetIPv4Properties()?.Index == index).ToArray();
            if (matches.Length != 1) return null;
            var adapter = matches[0];
            var addresses = adapter.GetIPProperties().UnicastAddresses
                .Where(a => OperatingSystem.IsWindows() && a.Address.AddressFamily == AddressFamily.InterNetwork &&
                    a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred &&
                    IsUsableAddress(a.Address)).Select(a => a.Address).Distinct().ToArray();
            // Multiple usable addresses require a future explicit adapter/address selection UX.
            if (addresses.Length != 1) return null;
            string mac = adapter.GetPhysicalAddress().ToString();
            return mac.Length == 12 ? new(addresses[0], checked((int)index), mac) : null;
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

    internal static uint BestInterface(IPAddress address)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return GetBestInterface(BitConverter.ToUInt32(address.GetAddressBytes()), out uint index) == 0 ? index : 0;
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
    private static extern uint GetBestInterface(uint destination, out uint bestInterfaceIndex);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfEntry2(nint row);
}
