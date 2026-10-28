using System.Text.Json;
using System.Security.AccessControl;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Qualification;

internal sealed record BootstrapReview(string AuthorizationSha256, string OperatorSid, DateTimeOffset ReviewedAtUtc,
    bool ProjectUnchanged, bool PlanChecksPassed, string ResourceTrend, string[] SupportingFiles);
internal sealed record BootstrapBundle(string Status, string Directory, string ManifestSha256, string[] Issues);

/// <summary>Offline, create-new evidence packaging. PASS is a reviewed candidate; never enrollment.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class BootstrapEvidence(HostConfiguration config, SqliteOperationStore store)
{
    internal BootstrapBundle Capture(bool reviewRequested = false)
    {
        var local = new BootstrapLocal(config, store); local.Check(asOperator: true);
        string id = config.QualificationBootstrap!.AuthorizationId;
        var state = store.ReadBootstrap(id) ?? throw new IOException("BOOTSTRAP_NOT_FOUND");
        bool diagnostic = state.Authorization.IsHandleDiagnostics;
        if (diagnostic && reviewRequested) throw new IOException("DIAGNOSTIC_IS_NOT_QUALIFICATION");
        string directory = Path.Combine(local.Root, "reports", Guid.NewGuid().ToString("N"));
        StorageLocation.CheckChain(directory); System.IO.Directory.CreateDirectory(directory);
        var issues = new List<string>(); var files = new List<object>();
        void Write(string name, byte[] bytes)
        {
            LocalTrustStore.Name(name);
            string path = Path.Combine(directory, name); StorageLocation.CheckChain(path);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes); file.Flush(true);
            files.Add(new { name, size = bytes.Length, sha256 = BootstrapAuthorization.Sha(bytes) });
        }
        void Json(string name, object value) => Write(name, JsonSerializer.SerializeToUtf8Bytes(value, HostConfiguration.Json));
        byte[] authorization = store.BootstrapDocument(id);
        if (BootstrapAuthorization.Sha(authorization) != state.Sha256) throw new IOException("BOOTSTRAP_AUTHORIZATION_CORRUPT");
        Write("authorization.json", authorization);
        var cycles = store.BootstrapCycles(id); Json("cycles.json", cycles);
        bool confirmed = cycles.Count == state.Authorization.CycleBudget && state.UsedCycles == state.Authorization.CycleBudget
            && cycles.Select(c => (c.WorkerEpoch, c.WorkerPid, c.WorkerStartTicks)).Distinct().Count() == 1;
        var audited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var epoch in cycles.Select(c => c.WorkerEpoch).Distinct())
        {
            try
            {
                string name = "v19-audit-" + epoch.ToString("N") + ".jsonl";
                string path = Path.Combine(config.StatePath, name); StorageLocation.CheckChain(path);
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (source.Length > 1024 * 1024) throw new IOException("AUDIT_LIMIT");
                byte[] bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes); Write(name, bytes);
                foreach (string line in System.Text.Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    Protocol.CheckJson(System.Text.Encoding.UTF8.GetBytes(line)); using var entry = JsonDocument.Parse(line);
                    if (entry.RootElement.GetProperty("worker_epoch").GetGuid() != epoch) throw new IOException("AUDIT_EPOCH_MISMATCH");
                    if (entry.RootElement.GetProperty("call").GetString() == "TiaPortal.DisposeAttachedSession.Returned")
                        audited.Add(entry.RootElement.GetProperty("operation_id").GetString()!);
                }
                if (diagnostic)
                {
                    string traceName = "v19-resources-" + epoch.ToString("N") + ".jsonl";
                    string tracePath = Path.Combine(config.StatePath, traceName); StorageLocation.CheckChain(tracePath);
                    using var trace = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (trace.Length > 1024 * 1024) throw new IOException("PHASE_TRACE_LIMIT");
                    byte[] traceBytes = new byte[checked((int)trace.Length)]; trace.ReadExactly(traceBytes); Write(traceName, traceBytes);
                    BootstrapPhaseTrace.Verify(traceBytes, epoch, cycles.Where(c => c.WorkerEpoch == epoch).ToArray());
                }
            }
            catch (Exception e) when (e is IOException or JsonException or ProtocolException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { confirmed = false; issues.Add("AUDIT:" + e.GetType().Name); }
        }
        foreach (var cycle in cycles)
        {
            try
            {
                var operationId = new OperationId(Guid.Parse(cycle.OperationId));
                var operation = store.Read(operationId);
                Write($"request-{cycle.Cycle:D2}.json", operation.RequestJson);
                var events = new List<JournalEvent>(); long afterSequence = 0;
                while (true)
                {
                    var page = store.Events(operationId, afterSequence, 100);
                    if (page.Count == 0) break;
                    events.AddRange(page); afterSequence = page[^1].Sequence;
                }
                Json($"events-{cycle.Cycle:D2}.json", events);
                var receipts = store.BootstrapReceipts(operationId);
                if (receipts.Length == 0) throw new IOException("MISSING_RECEIPT");
                for (int r = 0; r < receipts.Length; r++)
                {
                    Write($"receipt-{cycle.Cycle:D2}-{r:D2}.json", receipts[r]);
                    var wire = JsonSerializer.Deserialize<WireMessage>(receipts[r], Protocol.Json)!;
                    var reply = Protocol.Read<WorkerReply>(wire.Body);
                    if (reply.Receipt.Report is { } rawResponse) Write($"raw-worker-{cycle.Cycle:D2}-{r:D2}.json", System.Text.Encoding.UTF8.GetBytes(rawResponse));
                }
                if (cycle.InitialTelemetry is { } initial)
                {
                    Write($"initial-telemetry-{cycle.Cycle:D2}.json", initial); Protocol.CheckJson(initial);
                    using var first = JsonDocument.Parse(initial);
                    if (first.RootElement.GetProperty("authorization_id").GetString() != id || first.RootElement.GetProperty("operation_id").GetString() != cycle.OperationId
                        || first.RootElement.GetProperty("worker_epoch").GetGuid() != cycle.WorkerEpoch) throw new IOException("INITIAL_TELEMETRY_MISMATCH");
                }
                if (cycle.Telemetry is null || cycle.TerminalReport is null) throw new IOException("MISSING_CYCLE_EVIDENCE");
                Write($"telemetry-{cycle.Cycle:D2}.json", cycle.Telemetry); Write($"result-{cycle.Cycle:D2}.json", cycle.TerminalReport);
                Protocol.CheckJson(cycle.Telemetry);
                using var telemetry = JsonDocument.Parse(cycle.Telemetry);
                var measured = telemetry.RootElement;
                if (measured.GetProperty("authorization_id").GetString() != id || measured.GetProperty("operation_id").GetString() != cycle.OperationId
                    || measured.GetProperty("worker_epoch").GetGuid() != cycle.WorkerEpoch || measured.GetProperty("worker_pid").GetInt32() != cycle.WorkerPid
                    || !double.IsFinite(measured.GetProperty("total_ms").GetDouble()) || measured.GetProperty("total_ms").GetDouble() < 0) throw new IOException("TELEMETRY_BINDING_MISMATCH");
                foreach (string point in new[] { "before", "after" })
                {
                    var sample = Protocol.Read<TiaBridge.Core.Discovery.DiscoveryResourceSample>(measured.GetProperty(point));
                    if (sample.Handles < 0 || sample.PrivateBytes < 0 || sample.WorkingSetBytes < 0) throw new IOException("INVALID_RESOURCE_SAMPLE");
                }
                if (!cycle.TerminalReport.AsSpan().SequenceEqual(ContractJson.WriteReport(operation.Report))) throw new IOException("CYCLE_REPORT_MISMATCH");
                var report = ContractJson.ReadReport(cycle.TerminalReport);
                if (cycle.State != "COMPLETED" || report.State != OperationState.SUCCEEDED || report.Outcome != OperationOutcome.CONFIRMED || report.ArtifactRefs.Length != 2)
                { confirmed = false; continue; }
                if (!audited.Contains(cycle.OperationId) || cycle.InitialTelemetry is null) throw new IOException("MISSING_DISPOSE_OR_INITIAL_SAMPLE");
                bool raw = false;
                foreach (var reference in report.ArtifactRefs)
                {
                    using var artifact = store.Artifacts.OpenVerified(reference.ArtifactId);
                    if (artifact.Descriptor.Sha256 != reference.Sha256) throw new IOException("ARTIFACT_HASH_MISMATCH");
                    using var bytes = new MemoryStream(); artifact.Content.CopyTo(bytes); byte[] content = bytes.ToArray();
                    Write(reference.ArtifactId + ".json", content);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("tia_pid", out _))
                    {
                        var observed = Protocol.Read<V19Observation>(doc.RootElement); raw = true;
                        if (observed.IsModifiedBefore || observed.IsModifiedAfter || !observed.PortalAliveAfterDispose
                            || observed.ProjectKey != state.Authorization.ProjectKey || observed.TiaPid != state.Authorization.TiaPid
                            || observed.TiaStartTicks != state.Authorization.TiaStartTicks || observed.ProjectPath != state.Authorization.ProjectPath
                            || observed.ApiBuild != state.Authorization.TiaBuild) throw new IOException("RAW_OBSERVATION_DENIED");
                    }
                }
                if (!raw) throw new IOException("MISSING_RAW_OBSERVATION");
            }
            catch (Exception e) when (e is IOException or StorageException or JsonException or ArgumentException or ProtocolException or ContractValidationException or InvalidOperationException or KeyNotFoundException)
            { confirmed = false; issues.Add("CYCLE_" + cycle.Cycle + ":" + e.GetType().Name); }
        }
        bool released = store.BootstrapReleased(id);
        bool reviewed = false;
        if (reviewRequested)
        {
            try
            {
                string reviewPath = Path.Combine(local.Root, "review.json"); StorageLocation.CheckChain(reviewPath);
                byte[] bytes = ReadLocal(reviewPath, local); Protocol.CheckJson(bytes);
                var review = JsonSerializer.Deserialize<BootstrapReview>(bytes, HostConfiguration.Json) ?? throw new IOException("MISSING_REVIEW");
                Write("review.json", bytes);
                if (review.AuthorizationSha256 != state.Sha256 || review.OperatorSid != local.Policy.OperatorSid || review.ReviewedAtUtc > DateTimeOffset.UtcNow
                    || review.ReviewedAtUtc < state.Authorization.AuthorizedAtUtc || !review.ProjectUnchanged || !review.PlanChecksPassed
                    || review.ResourceTrend is not ("STABLE_WITHIN_OBSERVATION" or "GROWING" or "INCONCLUSIVE") || review.SupportingFiles is not { Length: >= 3 and <= 16 }) throw new IOException("REVIEW_REQUIRED");
                foreach (string name in review.SupportingFiles)
                {
                    LocalTrustStore.Name(name);
                    Write("review-" + name, ReadLocal(Path.Combine(local.Root, "review-data", name), local));
                }
                reviewed = review.ResourceTrend == "STABLE_WITHIN_OBSERVATION";
            }
            catch (Exception e) when (e is IOException or JsonException or ArgumentException or ProtocolException)
            { issues.Add("REVIEW:" + e.GetType().Name); }
        }
        string status = issues.Count > 0 ? "BLOCKED" : cycles.Any(c => c.State == "UNCERTAIN_OR_FAILED") ? "FAIL"
            : !confirmed ? "INCOMPLETE" : diagnostic ? released ? "DIAGNOSTIC_COMPLETE" : "RELEASE_REQUIRED"
            : !released || !reviewed ? "REVIEW_REQUIRED" : "PASS";
        Json("report.json", new
        {
            status,
            authorization_id = id,
            authorization_sha256 = state.Sha256,
            state.Authorization.PlanId,
            state.Authorization.PlanSha256,
            state.Authorization.GitSha,
            state.Authorization.HostBuild,
            state.Authorization.WorkerBuild,
            state.Authorization.TiaBuild,
            state.State,
            state.StopReason,
            cycles = cycles.Count,
            released,
            reviewed,
            issues,
            first_cycle = "cold",
            subsequent_cycles = "warm",
            enrollment = "NOT_REGISTERED",
            engineering_scope = diagnostic ? "HANDLE_DIAGNOSTICS only; B1 qualification NOT_RUN; no enrollment candidate" : "qualification-only candidate; no capability grant",
            nf11 = "NOT_RUN",
            worker_handle_risk = "UNKNOWN",
            production = "NOT_RUN"
        });
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { authorization_id = id, files }, HostConfiguration.Json);
        Write("manifest.json", manifest);
        // Independent read of every published byte, including the manifest; no circular self-hash.
        foreach (var file in System.IO.Directory.GetFiles(directory))
        {
            using var handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (handle.Length > 1024 * 1024) throw new IOException("BOOTSTRAP_BUNDLE_LIMIT");
        }
        Verify(directory);
        return new(status, directory, BootstrapAuthorization.Sha(manifest), issues.ToArray());
    }
    private static byte[] ReadLocal(string path, BootstrapLocal local)
    {
        StorageLocation.CheckChain(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LocalTrustAcl.Verify(file.GetAccessControl(), local.Policy);
        if (file.Length > 1024 * 1024) throw new IOException("BOOTSTRAP_REVIEW_LIMIT");
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes); return bytes;
    }
    internal static void Verify(string directory)
    {
        StorageLocation.CheckChain(directory);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        foreach (var entry in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString()!; LocalTrustStore.Name(name);
            if (!names.Add(name)) throw new IOException("DUPLICATE_EVIDENCE");
            string path = Path.Combine(directory, name); StorageLocation.CheckChain(path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != entry.GetProperty("size").GetInt32() || BootstrapAuthorization.Sha(bytes) != entry.GetProperty("sha256").GetString()) throw new IOException("EVIDENCE_HASH_MISMATCH");
        }
        if (!names.SetEquals(System.IO.Directory.GetFiles(directory).Select(Path.GetFileName)!)) throw new IOException("EVIDENCE_SET_MISMATCH");
    }
}
