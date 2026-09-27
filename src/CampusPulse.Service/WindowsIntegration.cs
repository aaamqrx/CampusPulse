using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CampusPulse.Service;

internal sealed class StartupManager
{
    public bool Read()
    {
        nint manager = OpenSCManager(null, null, 1);
        if (manager == 0) throw new Win32Exception();
        nint service = 0, buffer = 0;
        try
        {
            service = OpenService(manager, "CampusPulse", 1);
            if (service == 0) throw new Win32Exception();
            QueryServiceConfig(service, 0, 0, out uint size);
            buffer = Marshal.AllocHGlobal(checked((int)size));
            if (!QueryServiceConfig(service, buffer, size, out _)) throw new Win32Exception();
            return Marshal.ReadInt32(buffer, 4) == 2;
        }
        finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); if (service != 0) CloseServiceHandle(service); CloseServiceHandle(manager); }
    }

    public void Set(bool enabled)
    {
        nint manager = OpenSCManager(null, null, 1);
        if (manager == 0) throw new Win32Exception();
        nint service = 0;
        try
        {
            service = OpenService(manager, "CampusPulse", 2);
            if (service == 0) throw new Win32Exception();
            if (!ChangeServiceConfig(service, uint.MaxValue, enabled ? 2u : 3u, uint.MaxValue, null, null, 0, null, null, null, null))
                throw new Win32Exception();
            var delayed = new DelayedAutoStart { Enabled = enabled };
            if (!ChangeServiceConfig2(service, 3, ref delayed)) throw new Win32Exception();
        }
        finally { if (service != 0) CloseServiceHandle(service); CloseServiceHandle(manager); }
        if (Read() != enabled) throw new InvalidOperationException("启动类型校验失败。");
    }

    [StructLayout(LayoutKind.Sequential)] private struct DelayedAutoStart { [MarshalAs(UnmanagedType.Bool)] public bool Enabled; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)] private static extern bool QueryServiceConfig(nint service, nint buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ChangeServiceConfig(nint service, uint type, uint start, uint error, string? binary, string? group, nint tag, string? dependencies, string? account, string? password, string? display);
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)] private static extern bool ChangeServiceConfig2(nint service, uint level, ref DelayedAutoStart value);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint handle);
}

internal sealed class PowerKeeper : IDisposable
{
    private nint request;
    public bool Active { get; private set; }
    public void Update(bool requested)
    {
        bool use = requested && GetSystemPowerStatus(out var power) && power.ACLineStatus == 1;
        if (use == Active) return;
        if (!use) { Dispose(); return; }
        var reason = new ReasonContext { Version = 0, Flags = 1, SimpleReasonString = "CampusPulse 插电无人值守：保持校园网自动恢复" };
        request = PowerCreateRequest(ref reason);
        if (request == 0 || request == new nint(-1)) { request = 0; throw new Win32Exception(); }
        if (!PowerSetRequest(request, 1)) { Dispose(); throw new Win32Exception(); }
        Active = true;
    }
    public void Dispose()
    {
        if (request != 0) { if (Active) PowerClearRequest(request, 1); CloseHandle(request); }
        request = 0; Active = false;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext { public uint Version; public uint Flags; [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint PowerCreateRequest(ref ReasonContext reason);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerSetRequest(nint handle, int type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerClearRequest(nint handle, int type);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
