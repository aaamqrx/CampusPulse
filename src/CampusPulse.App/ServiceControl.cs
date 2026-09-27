using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CampusPulse.Core;

namespace CampusPulse.App;

internal enum BackgroundState { Unknown = 0, Stopped = 1, Starting = 2, Stopping = 3, Running = 4, Continuing = 5, Pausing = 6, Paused = 7, NotInstalled = 8 }

/// <summary>Controls only the fixed CampusPulse service; never changes its startup type.</summary>
internal static class ServiceControl
{
    private const uint QueryStatus = 0x0004;
    private const uint Start = 0x0010;
    private const uint Stop = 0x0020;

    public static BackgroundState Query()
    {
        using var manager = OpenManager();
        using var service = OpenService(manager, ProductInfo.ServiceName, QueryStatus);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1060) return BackgroundState.NotInstalled;
            throw new Win32Exception(error);
        }
        return ReadState(service);
    }

    public static async Task ChangeAsync(bool start, CancellationToken token)
    {
        using var manager = OpenManager();
        using var service = OpenService(manager, ProductInfo.ServiceName, QueryStatus | (start ? Start : Stop));
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var desired = start ? BackgroundState.Running : BackgroundState.Stopped;
        if (ReadState(service) == desired) return;

        if (start)
        {
            if (!StartService(service, 0, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1056) throw new Win32Exception(error);
            }
        }
        else if (!ControlService(service, 1, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1062) throw new Win32Exception(error);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (ReadState(service) != desired)
                await Task.Delay(250, deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(start ? "后台启动尚未完成，请稍后刷新状态。" : "后台停止尚未完成，请稍后刷新状态。");
        }
    }

    private static ServiceHandle OpenManager()
    {
        var handle = OpenSCManager(null, null, 0x0001);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    private static BackgroundState ReadState(ServiceHandle service)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (BackgroundState)status.CurrentState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(ServiceHandle service, int count, IntPtr arguments);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(ServiceHandle service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
