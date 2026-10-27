using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class LocalTrustedTests
{
    [Fact]
    public void Explicit_approval_enrolls_exact_signed_read_evidence_and_keeps_private_key_out_of_Host()
    {
        using var f = new LocalTrustedFixture();
        Assert.False(f.Assess().Available);
        f.Enroll(); Assert.True(f.Assess().Available);
        var active = Assert.Single(f.Tool.List()); Assert.Equal(f.Evidence.EvidenceId, active.Evidence.EvidenceId);
        Assert.Single(f.Tool.Store.Events()); Assert.Equal(1, active.Evidence.Revision);
        Assert.Equal(JsonSerializer.Serialize(f.Config, HostConfiguration.Json), JsonSerializer.Serialize(HostConfiguration.Read(f.ConfigPath), HostConfiguration.Json));
        Assert.DoesNotContain(typeof(HostConfiguration).Assembly.GetReferencedAssemblies(), a => a.Name == "TiaBridge.Qualification");
        Assert.Equal(f.Signer.ExportSubjectPublicKeyInfo(), QualificationEvidenceProvider.ReadAnchors(f.Config.QualificationTrust).Single().Value);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("no-confirmation")]
    [InlineData("wrong-reviewed-hash")]
    [InlineData("changed-after-approval")]
    public void Enrollment_requires_explicit_approval_of_the_exact_immutable_package(string defect)
    {
        using var f = new LocalTrustedFixture(); var package = f.Tool.Inspect(f.Evidence.EvidenceId, 1);
        if (defect == "no-confirmation") Assert.Throws<IOException>(() => f.Tool.Approve(f.Evidence.EvidenceId, 1, package.PackageSha256, false));
        if (defect == "wrong-reviewed-hash") Assert.Throws<IOException>(() => f.Tool.Approve(f.Evidence.EvidenceId, 1, new string('f', 64), true));
        if (defect == "changed-after-approval")
        {
            f.Tool.Approve(f.Evidence.EvidenceId, 1, package.PackageSha256, true);
            f.WritePackage(f.Evidence with { RunId = "different-run" });
        }
        Assert.ThrowsAny<IOException>(() => f.Tool.Enroll(f.Evidence.EvidenceId, 1, f.Signer));
        Assert.Empty(f.Tool.Store.Events()); Assert.False(f.Assess().Available);
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("different-key")]
    [InlineData("missing-public-key")]
    [InlineData("artifact")]
    [InlineData("artifact-missing")]
    [InlineData("artifact-truncated")]
    [InlineData("evidence-id")]
    [InlineData("project")]
    [InlineData("worker")]
    [InlineData("tia")]
    [InlineData("profile")]
    [InlineData("read-scope")]
    public void Signature_integrity_and_exact_scope_drift_fail_closed(string defect)
    {
        using var f = new LocalTrustedFixture(); f.Enroll(); Assert.True(f.Assess().Available);
        var config = f.Config; var requirement = f.Requirement;
        if (defect == "missing-public-key") config = config with { QualificationTrust = null };
        if (defect == "different-key")
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            config = config with { QualificationTrust = config.QualificationTrust! with { TrustedSignerKeys = ["synthetic=" + Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())] } };
        }
        if (defect == "artifact") File.AppendAllText(f.ArtifactPath, "tampered");
        if (defect == "artifact-missing") File.Delete(f.ArtifactPath);
        if (defect == "artifact-truncated") File.WriteAllText(f.ArtifactPath, "x");
        if (defect == "signature")
        {
            var envelope = LocalTrustStore.Parse<SignedQualificationEnvelope>(File.ReadAllBytes(f.RecordPath));
            byte[] bytes = Convert.FromBase64String(envelope.SignatureBase64); bytes[0] ^= 1;
            File.WriteAllBytes(f.RecordPath, JsonSerializer.SerializeToUtf8Bytes(envelope with { SignatureBase64 = Convert.ToBase64String(bytes) }, LocalTrustStore.Json));
            // Keep the activation hash consistent so this case reaches ECDSA validation.
            var entry = Assert.Single(f.Tool.Store.Events()) with { EnvelopeSha256 = LocalTrustStore.Sha(File.ReadAllBytes(f.RecordPath)) };
            File.WriteAllText(f.Tool.Store.JournalPath, JsonSerializer.Serialize(entry, LocalTrustStore.Json) + "\n");
        }
        requirement = defect switch
        {
            "evidence-id" => requirement with { EvidenceId = "another-id" },
            "project" => requirement with { ProjectKey = Guid.NewGuid().ToString() },
            "worker" => requirement with { WorkerBuild = "1.0+" + new string('b', 64) },
            "tia" => requirement with { TiaBuild = "19.0.4.0" },
            "profile" => requirement with { Profile = "different-profile" },
            "read-scope" => requirement with { ReadScope = ["Project.Name"] },
            _ => requirement
        };
        Assert.False(f.Assess(config, requirement).Available);
    }
    [Theory]
    [InlineData(OperationMethod.ProjectExport)]
    [InlineData(OperationMethod.SyncApply)]
    [InlineData(OperationMethod.CompilePlc)]
    public void Local_enrollment_never_authorizes_export_import_or_write(OperationMethod method)
    {
        using var f = new LocalTrustedFixture(); f.WritePackage(f.Evidence with { Operation = method });
        var package = f.Tool.Inspect(f.Evidence.EvidenceId, 1); Assert.False(package.Eligible);
        Assert.Throws<IOException>(() => f.Tool.Approve(f.Evidence.EvidenceId, 1, package.PackageSha256, true));
        Assert.False(f.Assess(requirement: f.Requirement with { Operation = method }).Available);
    }
    [Fact]
    public void Revocation_survives_new_provider_and_preserves_all_evidence_and_revision_history()
    {
        using var f = new LocalTrustedFixture(); f.Enroll(); byte[] before = File.ReadAllBytes(f.RecordPath);
        f.Tool.Revoke(f.Evidence.EvidenceId, 1, "synthetic operator decision");
        Assert.False(f.Assess().Available); Assert.Empty(f.Tool.List());
        Assert.Equal(before, File.ReadAllBytes(f.RecordPath)); Assert.True(File.Exists(f.ArtifactPath));
        Assert.Equal(new[] { "ENROLLED", "REVOKED" }, f.Tool.Store.Events().Select(e => e.Action));
        Assert.Throws<IOException>(() => f.Tool.Enroll(f.Evidence.EvidenceId, 1, f.Signer));
        var next = f.Evidence with { Revision = 2, RunId = "synthetic-second-run" }; f.WritePackage(next);
        f.Tool.Approve(next.EvidenceId, 2, f.Tool.Inspect(next.EvidenceId, 2).PackageSha256, true);
        f.Tool.Enroll(next.EvidenceId, 2, f.Signer); Assert.True(f.Assess().Available);
        f.Tool.Revoke(next.EvidenceId, 2, "revoke latest; never fall back"); Assert.False(f.Assess().Available);
        Assert.Equal(4, f.Tool.Store.Events().Count); Assert.Equal(before, File.ReadAllBytes(f.RecordPath));
    }
    [Theory]
    [InlineData("root-acl")]
    [InlineData("file-acl")]
    [InlineData("parent-acl")]
    [InlineData("host-identity")]
    [InlineData("missing-store")]
    [InlineData("inaccessible-store")]
    [InlineData("missing-journal")]
    [InlineData("torn-journal")]
    public void Unsafe_ACL_identity_or_missing_unavailable_history_never_qualifies(string defect)
    {
        using var f = new LocalTrustedFixture(); f.Enroll(); var config = f.Config;
        if (defect == "root-acl")
        {
            var acl = new DirectoryInfo(f.Root).GetAccessControl(); acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            new DirectoryInfo(f.Root).SetAccessControl(acl);
        }
        if (defect == "file-acl")
        {
            var acl = new FileInfo(f.ArtifactPath).GetAccessControl(); acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write, AccessControlType.Allow));
            new FileInfo(f.ArtifactPath).SetAccessControl(acl);
        }
        if (defect == "parent-acl")
        {
            var acl = new DirectoryInfo(f.Files.Root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Allow));
            new DirectoryInfo(f.Files.Root).SetAccessControl(acl);
            Assert.Throws<IOException>(() => HostConfiguration.Read(f.ConfigPath));
        }
        if (defect == "host-identity") config = config with { QualificationTrust = config.QualificationTrust! with { HostSid = "S-1-5-21-1-2-3-1001" } };
        if (defect == "missing-store") config = config with { StatePath = Path.Combine(f.Files.Root, "missing-store") };
        if (defect == "missing-journal") File.Delete(f.Tool.Store.JournalPath);
        if (defect == "torn-journal") File.AppendAllText(f.Tool.Store.JournalPath, "{\"sequence\":");
        if (defect == "inaccessible-store")
        {
            using var held = new FileStream(f.Tool.Store.JournalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.False(f.Assess().Available); return;
        }
        Assert.False(f.Assess(config).Available);
    }
    [Fact]
    public void Wrong_operator_and_widened_startup_configuration_cannot_enroll()
    {
        using var f = new LocalTrustedFixture();
        var other = f.Config with { QualificationTrust = f.Config.QualificationTrust! with { OperatorSid = "S-1-5-21-1-2-3-1001" } };
        Assert.Throws<UnauthorizedAccessException>(() => new TiaBridge.Qualification.LocalEnrollment(other).Approve(f.Evidence.EvidenceId, 1, "", true));
        var acl = new FileInfo(f.ConfigPath).GetAccessControl(); acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write, AccessControlType.Allow));
        new FileInfo(f.ConfigPath).SetAccessControl(acl); Assert.Throws<IOException>(() => HostConfiguration.Read(f.ConfigPath));
    }
    [Fact]
    public void Wrong_local_signing_key_cannot_activate_an_approved_package()
    {
        using var f = new LocalTrustedFixture(); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        f.Tool.Approve(f.Evidence.EvidenceId, 1, f.Tool.Inspect(f.Evidence.EvidenceId, 1).PackageSha256, true);
        Assert.Throws<IOException>(() => f.Tool.Enroll(f.Evidence.EvidenceId, 1, other)); Assert.Empty(f.Tool.Store.Events());
    }
    [Theory]
    [InlineData("duplicate-field")]
    [InlineData("unknown-field")]
    [InlineData("null-artifacts")]
    [InlineData("traversal")]
    [InlineData("case-collision")]
    [InlineData("oversize")]
    public void Untrusted_package_boundaries_are_rejected_before_approval_or_activation(string defect)
    {
        using var f = new LocalTrustedFixture();
        string path = Path.Combine(f.Tool.Store.RevisionPath("inbox", f.Evidence.EvidenceId, 1), "evidence.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(path))!;
        if (defect == "unknown-field") node["unrecognized_required_feature"] = true;
        if (defect == "null-artifacts") node["artifacts"] = null;
        if (defect == "traversal") node["artifacts"]![0]!["name"] = "../outside.txt";
        if (defect == "case-collision") node["artifacts"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["name"] = "Sample.txt", ["sha256"] = f.Evidence.Artifacts[0].Sha256 });
        string text = node.ToJsonString();
        if (defect == "duplicate-field") text = "{\"revision\":1," + text[1..];
        File.WriteAllText(path, text);
        if (defect == "oversize") File.WriteAllBytes(path, new byte[LocalTrustStore.MaxJsonBytes + 1]);
        var error = Record.Exception(() => f.Tool.Inspect(f.Evidence.EvidenceId, 1));
        Assert.True(error is IOException or TiaBridge.Ipc.ProtocolException or JsonException);
        Assert.Empty(f.Tool.Store.Events()); Assert.False(f.Assess().Available);
    }
    [Fact]
    public void Valid_signed_files_without_an_activation_record_are_never_adopted()
    {
        using var f = new LocalTrustedFixture(); f.Enroll(); byte[] envelope = File.ReadAllBytes(f.RecordPath);
        File.WriteAllBytes(f.Tool.Store.JournalPath, []); // Simulated lost lifecycle history; files are not permission to activate.
        Assert.False(f.Assess().Available); Assert.Empty(f.Tool.List());
        Assert.Equal(envelope, File.ReadAllBytes(f.RecordPath));
    }
    [Fact]
    public void Large_valid_payload_enrolls_with_its_larger_base64_envelope_and_remains_verifiable()
    {
        using var f = new LocalTrustedFixture();
        f.WritePackage(f.Evidence with { Provenance = new string('p', 800 * 1024) });
        f.Enroll();
        Assert.True(new FileInfo(f.RecordPath).Length > LocalTrustStore.MaxJsonBytes);
        Assert.True(f.Assess().Available);
    }
    [Theory]
    [InlineData("invalid-base64")]
    [InlineData("wrong-curve")]
    public void Malformed_or_non_P256_startup_anchor_is_rejected_as_a_configuration_error(string defect)
    {
        using var f = new LocalTrustedFixture(); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string anchor = defect == "invalid-base64" ? "not-base64" : Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        var config = f.Config with { QualificationTrust = f.Config.QualificationTrust! with { TrustedSignerKeys = ["synthetic=" + anchor] } };
        File.WriteAllBytes(f.ConfigPath, JsonSerializer.SerializeToUtf8Bytes(config, HostConfiguration.Json));
        Assert.Throws<ArgumentException>(() => HostConfiguration.Read(f.ConfigPath));
    }
    [Fact]
    public void Full_lifecycle_journal_rejects_a_write_without_truncating_or_exceeding_the_readable_history()
    {
        using var f = new LocalTrustedFixture(); f.Enroll();
        var first = Assert.Single(f.Tool.Store.Events());
        static byte[] Line(LocalTrustEvent entry) => System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, LocalTrustStore.Json) + "\n");
        const int limit = 8 * LocalTrustStore.MaxJsonBytes;
        using var content = new MemoryStream(); content.Write(Line(first));
        long next = 2;
        while (true)
        {
            var entry = first with { Sequence = next, EvidenceId = "h" + next.ToString("D10") + new string('p', 100), Reason = new string('r', 512) };
            byte[] line = Line(entry);
            if (content.Length + line.Length > limit) break;
            content.Write(line); next++;
        }
        var revoke = first with { Sequence = next, Action = "REVOKED", Reason = new string('r', 512) };
        if (limit - content.Length >= Line(revoke).Length)
        {
            content.Write(Line(first with { Sequence = next++, EvidenceId = "padding-entry", Reason = "padding" }));
            revoke = revoke with { Sequence = next };
        }
        Assert.True(content.Length <= limit && limit - content.Length < Line(revoke).Length);
        byte[] before = content.ToArray(); File.WriteAllBytes(f.Tool.Store.JournalPath, before);
        Assert.Throws<IOException>(() => f.Tool.Revoke(f.Evidence.EvidenceId, 1, revoke.Reason));
        Assert.Equal(before, File.ReadAllBytes(f.Tool.Store.JournalPath));
    }
    [Fact]
    public async Task Host_process_restart_rechecks_revocation_without_rewriting_historical_results_or_exposing_enrollment()
    {
        await using var host = new ApiFixture(); using var f = new LocalTrustedFixture(host.Files); f.Enroll();
        File.WriteAllBytes(Path.Combine(f.Files.Root, "qualification-requirement.json"), JsonSerializer.SerializeToUtf8Bytes(f.Requirement, LocalTrustStore.Json));
        await host.Start(resilienceMode: "qualification", qualificationTrust: f.Config.QualificationTrust);
        using var client = host.Client();
        var completed = await host.Submit(client); await host.Until(client, completed, r => r.State == OperationState.SUCCEEDED);
        f.Tool.Revoke(f.Evidence.EvidenceId, 1, "synthetic revoke while Host runs");
        byte[] history = File.ReadAllBytes(f.Tool.Store.JournalPath);
        Assert.Equal(404, (await client.SendAsync(HttpMethod.Post, "/v1/qualification/enroll", "{}"u8.ToArray())).StatusCode);
        var denied = await host.Submit(client); await host.Until(client, denied, r => r.State == OperationState.FAILED);
        Assert.Equal(0, f.Files.Open().AttemptCount(denied));
        Assert.Equal(history, File.ReadAllBytes(f.Tool.Store.JournalPath));
        await host.Stop(); await host.Start(resilienceMode: "qualification", qualificationTrust: f.Config.QualificationTrust);
        using var fresh = host.Client();
        var afterRestart = await host.Submit(fresh); await host.Until(fresh, afterRestart, r => r.State == OperationState.FAILED);
        Assert.Equal(0, f.Files.Open().AttemptCount(afterRestart));
        Assert.Equal(200, (await fresh.SendAsync(HttpMethod.Get, "/v1/operations/" + completed + "/result")).StatusCode);
        Assert.Equal(OperationState.SUCCEEDED, f.Files.Open().ReadResult(completed)!.State);
        Assert.Equal(history, File.ReadAllBytes(f.Tool.Store.JournalPath));
    }
    [Fact]
    public async Task Real_operator_CLI_inspects_approves_lists_verifies_and_revokes_without_HTTP()
    {
        using var f = new LocalTrustedFixture();
        var inspect = await Cli(f, "inspect", f.Evidence.EvidenceId, "1"); Assert.Equal(0, inspect.Code);
        using var doc = JsonDocument.Parse(inspect.Output); string hash = doc.RootElement.GetProperty("package_sha256").GetString()!;
        Assert.Equal(3, (await Cli(f, "approve", f.Evidence.EvidenceId, "1")).Code);
        Assert.Empty(f.Tool.Store.Events());
        Assert.Equal(0, (await Cli(f, "approve", f.Evidence.EvidenceId, "1", "--package-sha256", hash, "--confirm-operator-approval")).Code);
        f.Tool.Enroll(f.Evidence.EvidenceId, 1, f.Signer); // Ephemeral in-memory key; never provision a production CNG key in tests.
        Assert.Equal(0, (await Cli(f, "verify", f.Evidence.EvidenceId, "1")).Code);
        Assert.Equal(0, (await Cli(f, "list")).Code);
        Assert.Equal(0, (await Cli(f, "revoke", f.Evidence.EvidenceId, "1", "--reason", "synthetic CLI revoke")).Code);
        Assert.Equal(3, (await Cli(f, "verify", f.Evidence.EvidenceId, "1")).Code);
        Assert.Equal(3, (await Cli(f, "inspect", "../outside", "1")).Code);
        Assert.Equal(3, (await Cli(f, "enroll", f.Evidence.EvidenceId, "1", "--key-name", "nonexistent-synthetic-key")).Code);
        Assert.False(f.Assess().Available);
    }
    private static async Task<(int Code, string Output)> Cli(LocalTrustedFixture f, params string[] arguments)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(f.Files.Repository, "src/TiaBridge.Qualification/bin", configuration, "net10.0/TiaBridge.Qualification.dll"));
        foreach (string arg in new[] { "--config", f.ConfigPath }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
        Assert.True(string.IsNullOrWhiteSpace(await errors)); return (process.ExitCode, await output);
    }
}
