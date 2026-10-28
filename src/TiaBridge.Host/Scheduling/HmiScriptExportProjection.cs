using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using TiaBridge.Core.Hmi;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Scheduling;

internal static class HmiScriptExportProjection
{
    internal static OperationReport Create(SqliteOperationStore store, OperationReport previous, V19Target target,
        V19HmiExportBinding binding, string reportJson)
    {
        byte[] raw = Encoding.UTF8.GetBytes(reportJson); Protocol.CheckJson(raw);
        var proof = store.Artifacts.Put(previous.OperationId, new MemoryStream(raw));
        using var doc = JsonDocument.Parse(raw);
        var exported = Protocol.Read<V19HmiExportObservation>(doc.RootElement);
        var p = exported.Project;
        if (p is null || previous.Method != OperationMethod.ProjectExport || exported.AuthorizationId != binding.AuthorizationId
            || exported.ScopeToken != binding.ScopeToken || exported.ModuleName != binding.ModuleName || exported.IdentityState != "UNAVAILABLE"
            || p.ProjectKey != target.ProjectKey || p.TiaPid != target.Pid || p.TiaStartTicks != target.StartTicks
            || !string.Equals(Path.GetFullPath(p.ProjectPath), Path.GetFullPath(target.ProjectPath), StringComparison.OrdinalIgnoreCase)
            || p.SessionEpoch == Guid.Empty || !p.PortalAliveAfterDispose || p.IsModifiedBefore || p.IsModifiedAfter
            || p.ApiBuild != binding.TiaBuild || p.OwnerThreadId <= 0 || p.Apartment is not ("STA" or "MTA")
            || exported.Files is not { Length: >= 1 and <= 32 }
            || exported.Files.Any(f => f is null || f.Bytes is < 0 or > 16 * 1024 * 1024)
            || exported.Files.Sum(f => f.Bytes) > 16 * 1024 * 1024)
            throw new ProtocolException();
        string directory = Path.Combine(store.ArtifactRoot, V19HmiExportPolicy.DirectoryName(previous.OperationId.Value));
        var inputs = new List<NativeScriptArtifact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in exported.Files)
        {
            if (!HmiScriptSource.SafeName(file.Name) || !seen.Add(file.Name) || file.Bytes is < 0 or > 16 * 1024 * 1024)
                throw new ProtocolException();
            string path = Path.Combine(directory, "native", file.Name); StorageLocation.CheckChain(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != file.Bytes) throw new ProtocolException();
            byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            if (HmiScriptSource.Hash(bytes) != file.Sha256) throw new ProtocolException();
            inputs.Add(new(file.Name, bytes));
        }
        string package = Path.Combine(directory, "package");
        var manifest = HmiScriptPackage.Create(package, binding.ModuleName,
            binding.ProjectKey + ":" + V19InspectionProfile.HmiExportScopeClass(binding), inputs, binding.ExpectedFunctions);
        var artifacts = new List<ArtifactRef> { new(proof.ArtifactId, proof.Sha256) };
        var index = new List<object>();
        foreach (var file in manifest.NativeArtifacts.Concat(manifest.Parts))
        {
            using var stream = File.OpenRead(Path.Combine(package, file.Path));
            var artifact = store.Artifacts.Put(previous.OperationId, stream);
            if (artifact.Sha256 != file.Sha256) throw new ProtocolException();
            artifacts.Add(new(artifact.ArtifactId, artifact.Sha256));
            index.Add(new { path = file.Path, artifact_id = artifact.ArtifactId, sha256 = artifact.Sha256 });
        }
        var packaged = store.Artifacts.Put(previous.OperationId, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        { schema_version = "tia-bridge-hmi-js-export/1.0", source_manifest = manifest, artifacts = index }, HmiScriptPackage.Json)));
        artifacts.Add(new(packaged.ArtifactId, packaged.Sha256));
        return previous with
        {
            State = manifest.SplitStatus == "PASS" ? OperationState.SUCCEEDED : OperationState.FAILED,
            Outcome = OperationOutcome.CONFIRMED,
            Phase = manifest.SplitStatus == "PASS" ? "COMPLETED" : "FUNCTION_SPLITTING_BLOCKED_NATIVE_PRESERVED",
            Result = manifest.SplitStatus == "PASS" ? new ProjectExportResult(previous.OperationId.ToString(), new(packaged.ArtifactId, packaged.Sha256), []) : null,
            Error = manifest.SplitStatus == "PASS" ? null : new(ErrorCode.COVERAGE_GAP, ErrorCategory.VALIDATION,
                "Native export preserved; function splitting blocked. Inspect manifest.", false, OperationOutcome.CONFIRMED),
            ArtifactRefs = artifacts.ToImmutableArray(),
            SessionEpoch = new(p.SessionEpoch),
            CompletedAt = DateTimeOffset.UtcNow,
            PollAfterMs = 0
        };
    }
}
