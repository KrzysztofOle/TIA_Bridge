using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResilienceArtifactTests
{
    [Fact]
    public async Task Crash_after_artifact_publish_retains_the_orphan_without_claiming_a_result_or_dispatch()
    {
        using var evidence = new ResilienceEvidence("orphan-published", "RT-03", "NF-12");
        await using var f = new ApiFixture(); await f.Start(resilienceMode: "crash.ArtifactPublished"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request()); ResilienceEvidence.Release(f, "profile"); await f.Crash();
        var view = f.Files.Open(); Assert.Equal(0, view.AttemptCount(id)); Assert.Equal(0, f.Files.Count("operation_results"));
        var orphan = Assert.Single(view.Artifacts.RetainedOrphans()); Assert.True(File.Exists(Path.Combine(f.Files.Root, orphan.RelativePath)));
        await f.Start(); using var fresh = f.Client(); await f.Until(fresh, id, r => r.State == OperationState.SUCCEEDED);
        Assert.True(File.Exists(Path.Combine(f.Files.Root, orphan.RelativePath))); Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id));
        evidence.Data["orphan_retained"] = true; evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Pass();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Damaged_final_result_WAL_frame_recovers_valid_intent_without_false_success(bool truncate)
    {
        using var evidence = new ResilienceEvidence("wal-result-tail-" + truncate, "RT-03", "NF-12");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "crash.result-after"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply")); ResilienceEvidence.Release(f, "profile"); await f.Crash();
        await ResilienceEvidence.WorkerAbsent(await ResilienceEvidence.Marker(f, "worker.json"));
        // No SQLite connection is opened before injection: opening/closing the last writer could checkpoint the WAL.
        string wal = Path.Combine(f.Files.Root, "operations.sqlite3-wal"); Assert.True(File.Exists(wal));
        byte[] before = File.ReadAllBytes(wal); Assert.True(before.Length > 4096);
        using (var stream = new FileStream(wal, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (truncate) stream.SetLength(stream.Length - 16);
            else { stream.Position = stream.Length - 1; int value = stream.ReadByte(); stream.Position--; stream.WriteByte((byte)(value ^ 1)); }
            stream.Flush(true);
        }
        evidence.Data["wal_size_before"] = before.Length; evidence.Data["wal_sha256_before"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(before));
        evidence.Data["wal_sha256_after"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(wal)));
        await f.Start(mutation: true); using var fresh = f.Client(); var recovered = ContractJson.ReadReport((await fresh.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json);
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, recovered.State); Assert.Equal(OperationOutcome.UNKNOWN, recovered.Outcome); Assert.Equal(1, f.Files.Open().AttemptCount(id));
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id)); Assert.True(f.Files.Open().OwnerBlocked);
        using (var c = f.Files.Connection()) { using var command = c.CreateCommand(); command.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", command.ExecuteScalar()); }
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Data["scope"] = "Final result transaction tail only; not loss of the entire WAL, hardware power loss or recovery of destroyed bytes."; evidence.Pass();
    }
    [Theory]
    [InlineData(false, "truncated")]
    [InlineData(false, "missing")]
    [InlineData(true, "modified")]
    public async Task Corrupt_artifact_never_confirms_a_result_and_does_not_destroy_historical_status(bool afterCompletion, string defect)
    {
        using var evidence = new ResilienceEvidence("artifact-" + afterCompletion + "-" + defect, "NF-12");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "artifact"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply")); await ResilienceEvidence.Marker(f, "receipt-received.reached");
        var artifact = await ResilienceEvidence.Marker(f, "result-artifact.json"); string relative = artifact.GetProperty("RelativePath").GetString()!;
        var received = ContractJson.ReadReport(await File.ReadAllBytesAsync(Path.Combine(f.Files.Root, "received-report.json")));
        Assert.Equal(OperationState.RUNNING, f.Files.Open().Read(id).Report.State);
        Assert.Equal(0, f.Files.Open().ReceiptCount(id));
        evidence.Data["selected_artifact_id"] = artifact.GetProperty("ArtifactId").GetString();
        evidence.Data["receipt_artifact_id"] = Assert.Single(received.ArtifactRefs).ArtifactId;
        evidence.Data["profile_resolutions"] = File.ReadAllLines(Path.Combine(f.Files.Root, "artifact-profile-resolutions.jsonl"));
        try
        {
            Assert.Equal(artifact.GetProperty("ArtifactId").GetString(), Assert.Single(received.ArtifactRefs).ArtifactId);
            Assert.Equal(artifact.GetProperty("Sha256").GetString(), Assert.Single(received.ArtifactRefs).Sha256);
            Assert.Single(File.ReadAllLines(Path.Combine(f.Files.Root, "artifact-profile-resolutions.jsonl")));
        }
        catch { ResilienceEvidence.Release(f, "receipt-received"); throw; }
        string path = Path.GetFullPath(Path.Combine(f.Files.Root, relative)); Assert.StartsWith(Path.GetFullPath(f.Files.Root) + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        if (afterCompletion) { ResilienceEvidence.Release(f, "receipt-received"); await f.Until(client, id, r => r.State == OperationState.SUCCEEDED); }
        File.SetAttributes(path, FileAttributes.Normal); if (defect == "missing") File.Delete(path); else File.WriteAllText(path, defect == "truncated" ? "x" : "corrupt proof");
        if (!afterCompletion) ResilienceEvidence.Release(f, "receipt-received");
        if (afterCompletion)
        {
            var result = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result"); Assert.Equal(422, result.StatusCode); Assert.Equal(ErrorCode.VERIFY_FAILED, ContractJson.ReadError(result.Json).Code);
            Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json).State);
        }
        else
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var health = ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health")).Json);
                if (Assert.IsType<DiagnosticsHealthResult>(health.Result).Components.Any(c => c.Component == "scheduler" && c.Status == ComponentHealth_Status.DEGRADED)) break;
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10)); await Task.Delay(20);
            }
            Assert.Equal(0, f.Files.Count("operation_results")); Assert.Equal(202, (await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result")).StatusCode);
            Assert.Equal(1, f.Files.Open().ReceiptCount(id)); Assert.Equal(1, f.Files.Open().AttemptCount(id));
            Assert.DoesNotContain(f.Files.Open().Events(id), e => e.Report.State == OperationState.SUCCEEDED);
            evidence.Data["before_restart"] = ResilienceEvidence.Journal(f, id);
            await f.Stop(); await f.Start(mutation: true); Assert.Equal(OperationOutcome.UNKNOWN, f.Files.Open().Read(id).Report.Outcome); Assert.True(f.Files.Open().OwnerBlocked);
            Assert.Equal(1, f.Files.Open().AttemptCount(id)); Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id));
            Assert.False(File.Exists(path) && defect == "missing");
            evidence.Data["after_restart"] = ResilienceEvidence.Journal(f, id);
        }
        evidence.Data["artifact_id"] = artifact.GetProperty("ArtifactId").GetString(); evidence.Data["after_completion"] = afterCompletion; evidence.Data["defect"] = defect; evidence.Pass();
    }
}
