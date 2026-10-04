using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CampusPulse.Core;

namespace CampusPulse.Service;

internal sealed record StoredCredential(string Username, string Password)
{
    // Old local credentials lack these fields; never reuse them with the new portal template suffixes.
    public string Carrier { get; init; } = "telecom";
    public string PortalUrl { get; init; } = CampusSettings.SupportedPortal;
    public int ProtocolVersion { get; init; } = 1;
}
internal sealed record EventHistory(DateTimeOffset? LastSuccess, List<StatusEntry> Entries)
{
    public AuthenticationDiagnostics Diagnostics { get; init; } = new();
}

internal sealed class SecureStore
{
    private readonly string directory;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CampusPulse");
    public SecureStore(string? directory = null) => this.directory = directory ?? DefaultDirectory;

    public void Initialize()
    {
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("数据目录不能是链接。");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
        foreach (var name in new[] { "settings.json", "credentials.dat", "events.json" })
        {
            string path = SafePath(name);
            if (!File.Exists(path)) continue;
            var fileAcl = new FileSecurity();
            fileAcl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                fileAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(fileAcl);
        }
    }

    private string SafePath(string name)
    {
        var path = Path.Combine(directory, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("数据文件不能是链接。");
        return path;
    }

    public CampusSettings LoadSettings()
    {
        var path = SafePath("settings.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("配置过大。");
        var settings = JsonSerializer.Deserialize<CampusSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置为空。");
        if (settings.ConfigVersion is not (1 or 2) || !DrComProtocol.TryGetCarrier(settings.Carrier, out _) ||
            !PortalEndpoint.TryCreate(settings.PortalUrl, out _) ||
            settings.Username is null || settings.Username.Length > 128)
            throw new InvalidDataException("不支持的配置格式。");
        return settings with
        {
            ConfigVersion = 2,
            Enabled = settings.ConfigVersion == 2 && settings.Enabled,
            OnlineCheckSeconds = Math.Clamp(settings.OnlineCheckSeconds, 30, 3600)
        };
    }

    public StoredCredential? LoadCredential(string username, string carrier, string portalUrl)
    {
        var path = SafePath("credentials.dat");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("凭据过大。");
        byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine);
        try
        {
            var value = JsonSerializer.Deserialize<StoredCredential>(clear);
            return value is not null && value.ProtocolVersion == 2 && value.Username == username &&
                value.Carrier == carrier && value.PortalUrl == portalUrl &&
                value.Password is { Length: > 0 and <= 256 } ? value : null;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void SaveSettings(CampusSettings settings) => AtomicWrite("settings.json", JsonSerializer.SerializeToUtf8Bytes(settings, Json));

    public void SaveConfiguration(CampusSettings settings, StoredCredential? credential, bool deleteCredential = false)
    {
        var oldSettings = ReadOptional("settings.json");
        var oldCredential = ReadOptional("credentials.dat");
        try
        {
            if (deleteCredential) File.Delete(SafePath("credentials.dat"));
            else if (credential is not null)
            {
                byte[] clear = JsonSerializer.SerializeToUtf8Bytes(credential);
                try { AtomicWrite("credentials.dat", ProtectedData.Protect(clear, null, DataProtectionScope.LocalMachine)); }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            SaveSettings(settings);
        }
        catch
        {
            // An unchanged, locked settings file must not prevent credential rollback.
            // Attempt both restores even if the first restore itself fails.
            try { Restore("settings.json", oldSettings); }
            finally { Restore("credentials.dat", oldCredential); }
            throw;
        }
    }

    public EventHistory LoadHistory()
    {
        var path = SafePath("events.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 131072) return new(null, []);
        try
        {
            var history = JsonSerializer.Deserialize<EventHistory>(File.ReadAllText(path));
            return history is null ? new(null, []) : history with
            {
                Entries = history.Entries ?? [],
                Diagnostics = history.Diagnostics ?? new()
            };
        }
        catch (JsonException) { return new(null, []); }
    }

    public void SaveHistory(DateTimeOffset? lastSuccess, IEnumerable<StatusEntry> events,
        AuthenticationDiagnostics? diagnostics = null) =>
        AtomicWrite("events.json", JsonSerializer.SerializeToUtf8Bytes(new EventHistory(lastSuccess,
            events.Where(x => x.Time > DateTimeOffset.UtcNow.AddDays(-7)).TakeLast(80).ToList())
            { Diagnostics = diagnostics ?? new() }, Json));

    private byte[]? ReadOptional(string name) => File.Exists(SafePath(name)) ? File.ReadAllBytes(SafePath(name)) : null;
    private void Restore(string name, byte[]? bytes)
    {
        byte[]? current = ReadOptional(name);
        if (bytes is null && current is null || bytes is not null && current is not null &&
            bytes.AsSpan().SequenceEqual(current)) return;
        if (bytes is null) File.Delete(SafePath(name));
        else AtomicWrite(name, bytes);
    }

    private void AtomicWrite(string name, byte[] bytes)
    {
        string destination = SafePath(name);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
