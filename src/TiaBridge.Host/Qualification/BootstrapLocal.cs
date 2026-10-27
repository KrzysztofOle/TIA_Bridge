using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Runtime.Versioning;
using System.Security.Principal;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;

namespace TiaBridge.Host.Qualification;

/// <summary>Fixed local operator boundary. Does not create keys, ACLs, or Siemens sessions.</summary>
[SupportedOSPlatform("windows")]
internal sealed class BootstrapLocal(HostConfiguration config, SqliteOperationStore store)
{
    internal string Root => Path.Combine(config.StatePath, "qualification-bootstrap");
    internal QualificationTrustConfiguration Policy => config.QualificationBootstrap is { } p ? new([], p.OperatorSid, p.HostSid) : throw new IOException("BOOTSTRAP_DISABLED");
    internal void Check(bool asOperator = false)
    {
        if (config.StartupConfigurationPath is not { } path) throw new IOException("BOOTSTRAP_CONFIG_REQUIRED");
        LocalTrustAcl.Configuration(path, Policy);
        LocalTrustAcl.Parents(Root, Policy); LocalTrustAcl.Directory(Root, Policy, protectedRoot: true);
        if (asOperator) LocalTrustAcl.Operator(Policy);
        else if (LocalTrustAcl.CurrentSid != Policy.HostSid) throw new IOException("BOOTSTRAP_HOST_SID");
    }
    internal (BootstrapAuthorization Authorization, byte[] Bytes, string Sha256) Inspect()
    {
        Check(asOperator: true);
        string path = Path.Combine(Root, "authorization.json"); StorageLocation.CheckChain(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LocalTrustAcl.Verify(file.GetAccessControl(), Policy);
        if (file.Length > 64 * 1024) throw new IOException("BOOTSTRAP_DOCUMENT_LIMIT");
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        var auth = BootstrapAuthorization.Read(bytes); auth.Validate(config, DateTimeOffset.UtcNow);
        return (auth, bytes, BootstrapAuthorization.Sha(bytes));
    }
    internal void Authorize(string reviewedHash, bool explicitApproval)
    {
        var package = Inspect();
        if (!explicitApproval || package.Sha256 != reviewedHash) throw new IOException("BOOTSTRAP_EXPLICIT_APPROVAL_REQUIRED");
        store.AuthorizeBootstrap(package.Bytes, LocalTrustAcl.CurrentSid);
    }
    internal void Revoke()
    {
        Check(asOperator: true); store.StopBootstrap(config.QualificationBootstrap!.AuthorizationId, "OPERATOR_REVOKED", revoke: true);
    }
    internal static string HostBuild => FileBuild(typeof(BootstrapLocal).Assembly.Location);
    internal static string FileBuild(string path)
    {
        StorageLocation.CheckChain(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return "1.0+" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file));
    }
    internal static bool VerifyLiveBinding(HostConfiguration config, BootstrapAuthorization auth)
    {
        // Windows/file inspection only; the existing Worker independently checks the public project after Attach.
        V19InspectionProfile.Validate(config);
        var projectDrive = new DriveInfo(Path.GetPathRoot(auth.ProjectPath)!);
        if (projectDrive.DriveType != DriveType.Fixed || projectDrive.DriveFormat != "NTFS") return false;
        if (FileBuild(config.V19WorkerExe!) != auth.WorkerBuild || HostBuild != auth.HostBuild) return false;
        string plan = Path.Combine(config.RepositoryRoot, "docs", "E2-B1_TEST_PLAN.md");
        StorageLocation.CheckChain(plan);
        if (BootstrapAuthorization.Sha(File.ReadAllBytes(plan)) != auth.PlanSha256) return false;
        string portalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "Portal V19");
        string exe = Path.Combine(portalRoot, "Bin", "Siemens.Automation.Portal.exe");
        string api = Path.Combine(portalRoot, "PublicAPI", "V19", "Siemens.Engineering.dll");
        StorageLocation.CheckChain(exe); StorageLocation.CheckChain(api);
        if (!string.Equals(auth.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase) || FileVersionInfo.GetVersionInfo(api).FileVersion != auth.TiaBuild) return false;
        using var process = Process.GetProcessById(auth.TiaPid); process.Refresh();
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != auth.TiaStartTicks || !string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return false;
        if (!OpenProcessToken(process.Handle, 8, out var token)) return false;
        try { using var identity = new WindowsIdentity(token); return identity.User?.Value == auth.SessionSid; }
        finally { CloseHandle(token); }
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
