using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using TiaBridge.Host.Api;
using TiaBridge.Host.Storage;

namespace TiaBridge.Host.Qualification;

/// <summary>Conservative allowlist of explicit Windows identities. Never changes an ACL.</summary>
[SupportedOSPlatform("windows")]
internal static class LocalTrustAcl
{
    internal static string CurrentSid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
    private static readonly string SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
    private static readonly string AdminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
    private static readonly Lazy<string?> InstallerSid = new(() =>
    {
        try { return ((SecurityIdentifier)new NTAccount("NT SERVICE", "TrustedInstaller").Translate(typeof(SecurityIdentifier))).Value; }
        catch (IdentityNotMappedException) { return null; }
    });
    private const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    internal static void Policy(QualificationTrustConfiguration policy)
    {
        _ = new SecurityIdentifier(policy.OperatorSid ?? throw new IOException("LOCAL_POLICY_REQUIRED"));
        _ = new SecurityIdentifier(policy.HostSid ?? throw new IOException("LOCAL_POLICY_REQUIRED"));
    }

    internal static void Configuration(string path, QualificationTrustConfiguration policy)
    {
        Policy(policy); StorageLocation.CheckChain(path);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS") throw new IOException("LOCAL_NTFS_CONFIG_REQUIRED");
        Parents(path, policy);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Verify(file.GetAccessControl(), policy, protectedRoot: true);
    }

    internal static void Parents(string path, QualificationTrustConfiguration policy)
    {
        bool immediate = true;
        for (string? parent = Path.GetDirectoryName(Path.GetFullPath(path)); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            StorageLocation.CheckChain(parent);
            var acl = new DirectoryInfo(parent).GetAccessControl();
            bool Trusted(string sid) => sid == policy.OperatorSid || sid == policy.HostSid || sid == SystemSid || sid == AdminSid || sid == InstallerSid.Value;
            if (!Trusted(acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "")) throw new IOException("UNTRUSTED_PARENT_OWNER");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 || Trusted(rule.IdentityReference.Value)) continue;
                var unsafeRights = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
                if (immediate) unsafeRights |= FileSystemRights.WriteData | FileSystemRights.AppendData;
                if ((rule.FileSystemRights & unsafeRights) != 0 || ((uint)rule.FileSystemRights & 0x50000000) != 0)
                    throw new IOException("UNTRUSTED_PARENT_WRITE_ACCESS");
            }
            immediate = false;
        }
    }

    internal static void Directory(string path, QualificationTrustConfiguration policy, bool protectedRoot = false)
    {
        StorageLocation.CheckChain(path);
        Verify(new DirectoryInfo(path).GetAccessControl(), policy, protectedRoot);
    }

    internal static void Verify(FileSystemSecurity acl, QualificationTrustConfiguration policy, bool protectedRoot = false)
    {
        Policy(policy);
        string owner = acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "";
        if (owner != policy.OperatorSid && owner != SystemSid && owner != AdminSid
            || protectedRoot && !acl.AreAccessRulesProtected) throw new IOException("UNSAFE_QUALIFICATION_ACL");
        FileSystemRights hostRead = 0, operatorRights = 0;
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            string sid = rule.IdentityReference.Value;
            if (rule.AccessControlType != AccessControlType.Allow
                || sid != policy.OperatorSid && sid != policy.HostSid && sid != SystemSid && sid != AdminSid)
                throw new IOException("UNSAFE_QUALIFICATION_ACL");
            if (sid == policy.HostSid) hostRead |= rule.FileSystemRights;
            if (sid == policy.OperatorSid) operatorRights |= rule.FileSystemRights;
            if ((rule.FileSystemRights & WriteRights) != 0 && sid == policy.HostSid && sid != policy.OperatorSid)
                throw new IOException("HOST_STORE_MUST_BE_READ_ONLY");
            if (identity.User!.Value == policy.HostSid && policy.HostSid != policy.OperatorSid
                && (rule.FileSystemRights & WriteRights) != 0
                && principal.IsInRole((SecurityIdentifier)rule.IdentityReference))
                throw new IOException("HOST_EFFECTIVE_WRITE_ACCESS");
        }
        const FileSystemRights read = FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadPermissions;
        if ((hostRead & read) != read || (operatorRights & read) != read)
            throw new IOException("QUALIFICATION_READ_ACCESS_REQUIRED");
        if (protectedRoot && (operatorRights & (FileSystemRights.WriteData | FileSystemRights.AppendData)) != (FileSystemRights.WriteData | FileSystemRights.AppendData))
            throw new IOException("OPERATOR_WRITE_ACCESS_REQUIRED");
    }

    internal static void Operator(QualificationTrustConfiguration policy)
    {
        Policy(policy);
        if (CurrentSid != policy.OperatorSid) throw new UnauthorizedAccessException("OPERATOR_REQUIRED");
    }
}
