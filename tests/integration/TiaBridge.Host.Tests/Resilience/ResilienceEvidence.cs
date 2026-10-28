using System.Diagnostics;
using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ResilienceEvidence(string scenario, params string[] requirements) : IDisposable
{
    private static readonly string RunId = Guid.TryParseExact(Environment.GetEnvironmentVariable("TIA_BRIDGE_RESILIENCE_RUN_ID"), "N", out var id) ? id.ToString("N") : Guid.NewGuid().ToString("N");
    internal static string CurrentRunId => RunId;
    internal static void WriteNew(string path, byte[] bytes) => PerformanceEvidence.WriteNew(path, bytes);
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private bool passed;
    internal Dictionary<string, object?> Data { get; } = new();
    internal void Pass() => passed = true;
    public void Dispose()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "TiaBridge.slnx"))) root = Directory.GetParent(root)!.FullName;
        string output = Path.Combine(root, ".tia-bridge", "resilience", RunId); Directory.CreateDirectory(output);
        if (scenario.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))) throw new ArgumentException("Unsafe evidence ID.");
        PerformanceEvidence.WriteNew(Path.Combine(output, scenario + ".json"), JsonSerializer.SerializeToUtf8Bytes(new
        { schema_version = "1.0", run_id = RunId, scenario, requirements, status = passed ? "PASS" : "FAIL", started_at = started, elapsed_ms = timer.Elapsed.TotalMilliseconds, data = Data }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static async Task<JsonElement> Marker(ApiFixture f, string name)
    {
        string path = Path.Combine(f.Files.Root, name); var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            try { using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path)); return json.RootElement.Clone(); }
            catch (Exception e) when (e is IOException or JsonException) { await Task.Delay(10); }
        }
        throw new TimeoutException("Owned test marker was not reached: " + name);
    }
    internal static void Release(ApiFixture f, string point) => File.WriteAllText(Path.Combine(f.Files.Root, point + ".release"), "release");
    internal static async Task WorkerAbsent(JsonElement identity)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var process = Process.GetProcessById(identity.GetProperty("pid").GetInt32());
            if (process.StartTime.ToUniversalTime().Ticks == identity.GetProperty("start_ticks").GetInt64()) await process.WaitForExitAsync(deadline.Token);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
    }
    internal static object Journal(ApiFixture f, OperationId id)
    {
        var store = f.Files.Open(); var events = store.Events(id, limit: 1000);
        Assert.Equal(Enumerable.Range(1, events.Count).Select(n => (long)n), events.Select(e => e.Sequence));
        return new
        {
            operation_id = id.ToString(),
            request_sha256 = store.Read(id).BodySha256,
            attempts = store.AttemptCount(id),
            events = events.Select(e => new { sequence = e.Sequence, state = e.Report.State.ToString(), outcome = e.Report.Outcome.ToString(), e.Report.Phase, at = e.OccurredAt }),
            receipt_count = store.ReceiptCount(id),
            quarantine = store.OwnerBlocked
        };
    }
    internal static int Effects(ApiFixture f, OperationId id)
    {
        using var c = f.Files.Connection(); using var command = c.CreateCommand(); command.CommandText = "SELECT message_json FROM worker_receipts WHERE operation_id=$id"; command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader(); int maximum = 0;
        while (reader.Read())
        { var message = JsonSerializer.Deserialize<WireMessage>((byte[])reader[0], Protocol.Json)!; maximum = Math.Max(maximum, Protocol.Read<WorkerReply>(message.Body).Receipt.EffectCount); }
        return maximum;
    }
    internal static int DurableEffects(ApiFixture f, OperationId id) => Directory.EnumerateFiles(f.Files.Root, "fake-effects-*.jsonl")
        .SelectMany(File.ReadAllLines).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonDocument.Parse(line))
        .Sum(doc => { using (doc) return doc.RootElement.GetProperty("operation_id").GetGuid() == id.Value ? 1 : 0; });
    internal static async Task IndependentWork(ApiFixture f)
    {
        // Own bounded synthetic W, independent of the immutable request artifacts and worker's locks.
        string path = Path.Combine(f.Files.Root, "independent.scl"); byte[] data = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray();
        await File.WriteAllBytesAsync(path, data); Assert.Equal(data, await File.ReadAllBytesAsync(path)); File.Delete(path);
    }
}

/// <summary>Run-bound append-only evidence for the opt-in synthetic NF-11 soak.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class Nf11SyntheticEvidence : IAsyncDisposable
{
    private readonly string folder;
    private readonly FileStream samples;
    private readonly FileStream operations;
    private readonly JsonSerializerOptions json = new() { WriteIndented = false };
    private int sampleSequence;

    internal Nf11SyntheticEvidence(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(this.folder);
        samples = new FileStream(Path.Combine(this.folder, "nf11-synthetic-samples.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        operations = new FileStream(Path.Combine(this.folder, "nf11-synthetic-operations.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    internal void Accepted(Guid operationId, DateTimeOffset at)
    {
        Append(operations, new { kind = "accepted", operation_id = operationId, at_utc = at });
    }
    internal void Completed(Guid operationId, string state, DateTimeOffset at)
    {
        Append(operations, new { kind = "completed", operation_id = operationId, state, at_utc = at });
    }

    internal void Sample(object value)
    {
        Append(samples, new { sequence = sampleSequence++, value });
    }

    internal int SampleCount => sampleSequence;

    private void Append<T>(FileStream stream, T value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, json);
        stream.Write(payload);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    internal void CompleteManifest(string runId)
    {
        samples.Flush(flushToDisk: true);
        operations.Flush(flushToDisk: true);
        samples.Dispose();
        operations.Dispose();
        var files = new Dictionary<string, object>();
        foreach (string name in new[] { "nf11-synthetic-samples.jsonl", "nf11-synthetic-operations.jsonl", "nf11-synthetic-result.json" })
        {
            string path = Path.Combine(folder, name);
            files.Add(name, new { bytes = new FileInfo(path).Length, sha256 = PerformanceEvidence.Hash(path) });
        }
        ResilienceEvidenceWriteNew(Path.Combine(folder, "nf11-synthetic-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new
        { schema_version = "1.0", run_id = runId, files }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ResilienceEvidenceWriteNew(string path, byte[] bytes) => PerformanceEvidence.WriteNew(path, bytes);

    public async ValueTask DisposeAsync()
    {
        await samples.DisposeAsync();
        await operations.DisposeAsync();
    }
}
