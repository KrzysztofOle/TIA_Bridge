using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using TiaBridge.Host.Api;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;
using TiaBridge.Qualification;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class LocalTrustedFixture : IDisposable
{
    private readonly bool ownsFiles;
    internal StoreFixture Files { get; }
    internal ECDsa Signer { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal HostConfiguration Config { get; }
    internal LocalEnrollment Tool { get; }
    internal QualificationEvidence Evidence { get; }
    internal string Root => Tool.Store.Root;
    internal string ConfigPath => Path.Combine(Files.Root, "qualification-host.json");
    internal string RecordPath => Path.Combine(Tool.Store.RevisionPath("records", Evidence.EvidenceId, Evidence.Revision), "evidence.signed.json");
    internal string ArtifactPath => Path.Combine(Path.GetDirectoryName(RecordPath)!, "sample.txt");
    internal LocalTrustedFixture(StoreFixture? files = null)
    {
        Files = files ?? new StoreFixture(); ownsFiles = files is null; _ = Files.Open();
        string project = "11111111-1111-4111-8111-111111111111", worker = "1.0+" + new string('a', 64);
        string sid = LocalTrustAcl.CurrentSid;
        Config = new("1.0", Files.Root, Files.Repository, [project], QualificationTrust: new(["synthetic=" + Convert.ToBase64String(Signer.ExportSubjectPublicKeyInfo())], sid, sid));
        Tool = new(Config);
        // Only new UUID-owned synthetic fixture paths are provisioned. Production code never changes ACLs.
        var acl = new DirectorySecurity(); acl.SetOwner(new SecurityIdentifier(sid)); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).Create(acl);
        File.WriteAllBytes(Tool.Store.JournalPath, []);
        Evidence = new("synthetic-local-evidence", 1, QualificationEvidenceKind.OFFLINE_TEST, "synthetic test", "ephemeral signer",
            "19.0.3.0", "tia-bridge.worker.v19", worker, worker, OperationMethod.ProjectInspect, OperationScope.PROJECT,
            null, "project", "v19.discovery.v1", V19InspectionProfile.DiscoveryReadScope,
            [new(CoverageDimension.STRUCTURE, CoverageState.COMPLETE)], QualificationResult.PASS, "synthetic-local-run", project, null,
            [new("sample.txt", LocalTrustStore.Sha("synthetic qualification sample"u8.ToArray()))], DateTimeOffset.UtcNow);
        WritePackage(Evidence);
        File.WriteAllBytes(ConfigPath, JsonSerializer.SerializeToUtf8Bytes(Config, HostConfiguration.Json)); ProtectConfig(ConfigPath);
    }
    internal void WritePackage(QualificationEvidence evidence)
    {
        string directory = Tool.Store.RevisionPath("inbox", evidence.EvidenceId, evidence.Revision); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "evidence.json"), JsonSerializer.SerializeToUtf8Bytes(evidence, LocalTrustStore.Json));
        File.WriteAllText(Path.Combine(directory, "sample.txt"), "synthetic qualification sample");
    }
    internal void Enroll()
    {
        Tool.Approve(Evidence.EvidenceId, Evidence.Revision, Tool.Inspect(Evidence.EvidenceId, Evidence.Revision).PackageSha256, true);
        Tool.Enroll(Evidence.EvidenceId, Evidence.Revision, Signer);
    }
    internal QualificationRequirement Requirement => new(Evidence.EvidenceId, Evidence.TiaBuild, Evidence.AdapterId, Evidence.AdapterBuild,
        Evidence.WorkerBuild, Evidence.Operation, Evidence.Scope, Evidence.DeviceFamily, Evidence.ObjectKind, Evidence.Profile,
        Evidence.ReadScope, [CoverageDimension.STRUCTURE], Evidence.ProjectKey, null);
    internal QualificationEvidenceDecision Assess(HostConfiguration? config = null, QualificationRequirement? requirement = null)
        => new QualificationEvidenceProvider(config ?? Config).Assess(requirement ?? Requirement);
    internal V19InspectionProfile Profile()
    {
        var key = new CapabilityKeyV2(OperationScope.PROJECT, Evidence.TiaBuild, Evidence.AdapterId, Evidence.AdapterBuild,
            null, "project", Evidence.Profile, null, OperationMethod.ProjectInspect);
        var entry = new CapabilityEntryV2(key, true, true, "1.0", Evidence.Profile, Evidence.ReadScope, [],
            new Coverage([new(CoverageDimension.STRUCTURE, CoverageState.COMPLETE)]), [Evidence.EvidenceId]);
        var registry = new CapabilityRegistry(new CapabilityConfigurationV2("2.0", "synthetic-local", [entry]));
        var target = new TiaBridge.Ipc.V19Target(Evidence.ProjectKey!, 123, 456, "X:\\synthetic.ap19");
        var config = Config with { V19WorkerExe = "not-executed", V19 = new([target], "X:\\") };
        return new(config, registry, new QualificationEvidenceProvider(config).Assess, () => Evidence.TiaBuild, () => Evidence.WorkerBuild);
    }
    internal static void ProtectConfig(string path)
    {
        var sid = new SecurityIdentifier(LocalTrustAcl.CurrentSid); var acl = new FileSecurity();
        acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(acl);
    }
    public void Dispose() { Signer.Dispose(); if (ownsFiles) Files.Dispose(); }
}
