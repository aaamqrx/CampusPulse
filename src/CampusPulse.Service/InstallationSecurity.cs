using System.Security.AccessControl;
using System.Security.Principal;

namespace CampusPulse.Service;

internal static class InstallationSecurity
{
    // A fixed command protects only the installed pair containing this service.
    // No path can be supplied through arguments or the control pipe.
    public static void ProtectCurrentInstallation()
    {
        var service = new DirectoryInfo(AppContext.BaseDirectory);
        var root = service.Parent ?? throw new IOException("Installation root unavailable.");
        if (!service.Name.Equals("Service", StringComparison.OrdinalIgnoreCase) ||
            root.FullName.Equals(Path.GetPathRoot(root.FullName), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(root.FullName, "App", "CampusPulse.App.exe")) ||
            !File.Exists(Path.Combine(service.FullName, "CampusPulse.Service.exe")))
            throw new IOException("Unsupported installation tree.");
        for (DirectoryInfo? directory = root; directory is not null; directory = directory.Parent)
            RejectLink(directory);

        var directories = new List<DirectoryInfo>();
        var files = new List<FileInfo>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            RejectLink(directory);
            directories.Add(directory);
            foreach (var child in directory.EnumerateFileSystemInfos())
            {
                RejectLink(child);
                if (child is DirectoryInfo folder) pending.Push(folder);
                else if (child is FileInfo file) files.Add(file);
            }
        }
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        foreach (var directory in directories)
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(administrators);
            foreach (var identity in new[] { administrators, system, users })
                acl.AddAccessRule(new FileSystemAccessRule(identity,
                    identity == users ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }
        foreach (var file in files)
        {
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(administrators);
            foreach (var identity in new[] { administrators, system, users })
                acl.AddAccessRule(new FileSystemAccessRule(identity,
                    identity == users ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                    AccessControlType.Allow));
            file.SetAccessControl(acl);
        }
    }

    private static void RejectLink(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked installation path refused.");
    }
}
