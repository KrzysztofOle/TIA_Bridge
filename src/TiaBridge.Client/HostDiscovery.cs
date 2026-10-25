using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaBridge.Client;

public sealed record HostDiscovery(string SchemaVersion, string BaseUrl, string Token, string PrincipalSid, int HostPid, long HostStartTicks, Guid InstanceId)
{
    public override string ToString() => "Protected Host discovery";
}

/// <summary>Windows user-only NTFS discovery. Never prints a token or modifies a foreign ACL.</summary>
[SupportedOSPlatform("windows")]
public static class DiscoveryFile
{
    public static string ApplicationRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA_Bridge2");
    public static string DefaultPath => Path.Combine(ApplicationRoot, "state", "api", "host.json");
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };
    public static string CurrentSid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
    public static void CheckPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0 || path.Contains("..", StringComparison.Ordinal)) throw new IOException("Unsafe discovery path.");
        string full = Path.GetFullPath(path), app = Path.GetFullPath(ApplicationRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(app, StringComparison.OrdinalIgnoreCase)) throw new IOException("Discovery must be in local application storage.");
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS") throw new IOException("Discovery requires local fixed NTFS.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe discovery path."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static void CreateProtectedDirectory(string path)
    {
        CheckPath(Path.Combine(path, "host.json"));
        if (Directory.Exists(path)) { VerifyAcl(new DirectoryInfo(path).GetAccessControl()); return; }
        var sid = new SecurityIdentifier(CurrentSid); var acl = new DirectorySecurity();
        acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(acl); VerifyAcl(new DirectoryInfo(path).GetAccessControl());
    }
    public static void VerifyAcl(FileSystemSecurity acl)
    {
        string sid = CurrentSid;
        if (acl.GetOwner(typeof(SecurityIdentifier))?.Value != sid) throw new IOException("Discovery owner mismatch.");
        bool ownerRead = false;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow)
            {
                if (rule.IdentityReference.Value != sid) throw new IOException("Discovery ACL grants another principal.");
                ownerRead |= (rule.FileSystemRights & FileSystemRights.ReadData) != 0;
            }
        }
        if (!ownerRead) throw new IOException("Discovery owner cannot read.");
    }
    public static void Publish(string path, HostDiscovery discovery)
    {
        CheckPath(path); CreateProtectedDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) VerifyAcl(new FileInfo(path).GetAccessControl());
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(discovery, Json)); stream.Flush(true);
            VerifyAcl(stream.GetAccessControl());
        }
        File.Move(temporary, path, overwrite: true); VerifyAcl(new FileInfo(path).GetAccessControl());
    }
    public static HostDiscovery Read(string path, bool verifyProcess = true)
    {
        try { return ReadCore(path, verifyProcess); }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { throw new IOException("Discovery or Host binding is invalid."); }
    }
    private static HostDiscovery ReadCore(string path, bool verifyProcess)
    {
        CheckPath(path); VerifyAcl(new DirectoryInfo(Path.GetDirectoryName(path)!).GetAccessControl());
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        VerifyAcl(stream.GetAccessControl()); if (stream.Length is <= 0 or > 16384) throw new IOException("Invalid discovery file.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        _ = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        using (var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }))
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Any(p => !keys.Add(p.Name))) throw new IOException("Invalid discovery file.");
        }
        var result = JsonSerializer.Deserialize<HostDiscovery>(bytes, Json) ?? throw new IOException("Invalid discovery file.");
        if (result.SchemaVersion != "1.0" || result.PrincipalSid != CurrentSid || result.InstanceId == Guid.Empty || result.HostPid <= 0 || result.HostStartTicks <= 0
            || !Uri.TryCreate(result.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0
            || result.Token is null || result.Token.Length != 44 || Convert.FromBase64String(result.Token).Length != 32) throw new IOException("Invalid discovery binding.");
        if (verifyProcess)
        {
            using var process = Process.GetProcessById(result.HostPid);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != result.HostStartTicks) throw new IOException("Host process binding expired.");
        }
        return result;
    }
    public static void RemoveOwn(string path, Guid instance)
    {
        if (!File.Exists(path)) return;
        var saved = Read(path, verifyProcess: false); if (saved.InstanceId == instance) File.Delete(path);
    }
}
