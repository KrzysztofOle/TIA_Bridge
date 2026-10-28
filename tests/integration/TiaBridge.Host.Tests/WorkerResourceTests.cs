using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TiaBridge.Ipc;
using TiaBridge.Worker.V19;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WorkerResourceTests
{
    private static V19Target Target(SyntheticVendorScenario scenario) => new(Guid.NewGuid().ToString("D"), Environment.ProcessId, 1, scenario.Path);
    private static V19Observation Read(SyntheticVendorScenario scenario, Action<string>? audit = null, Action<string>? phase = null)
    {
        SyntheticVendorScenario.Current.Value = scenario;
        try { return VendorReader.Inspect(Target(scenario), audit ?? (_ => { }), (_, _) => { }, phase); }
        finally { SyntheticVendorScenario.Current.Value = null; }
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("devices")]
    [InlineData("service")]
    public void Getter_exception_disposes_attached_session_and_all_active_enumerators(string point)
    {
        var scenario = new SyntheticVendorScenario { FailAt = point }; var calls = new List<string>();
        var failure = Assert.Throws<VendorReadFailure>(() => Read(scenario, calls.Add));
        Assert.True(failure.Quiescent); Assert.Equal(1, scenario.Disposes); Assert.Equal(0, scenario.ActiveEnumerators);
        Assert.Contains("TiaPortal.DisposeAttachedSession.Returned", calls);
    }

    [Fact]
    public void Unreturned_dispose_is_not_quiescent_and_has_no_returned_marker()
    {
        var scenario = new SyntheticVendorScenario { FailAt = "dispose" }; var calls = new List<string>();
        var failure = Assert.Throws<VendorReadFailure>(() => Read(scenario, calls.Add));
        Assert.False(failure.Quiescent); Assert.Equal(1, scenario.Disposes);
        Assert.DoesNotContain("TiaPortal.DisposeAttachedSession.Returned", calls);
    }

    [Fact]
    public void Trace_failure_before_dispose_cannot_skip_actual_cleanup()
    {
        var scenario = new SyntheticVendorScenario();
        var failure = Assert.Throws<VendorReadFailure>(() => Read(scenario, phase: phase =>
        { if (phase == "dispose.enter") throw new IOException("synthetic trace fault"); }));
        Assert.True(failure.Quiescent); Assert.Equal(1, scenario.Disposes); Assert.Equal(0, scenario.ActiveEnumerators);
    }

    [Fact]
    public void Resource_phase_boundaries_follow_real_reader_order_without_leaving_enumerators_open()
    {
        var scenario = new SyntheticVendorScenario(); var phases = new List<string>();
        var result = Read(scenario, phase: phases.Add);
        Assert.Equal(new[] { "cycle.enter", "enumeration.returned", "attach.enter", "attach.returned", "project.checked", "devices.enter", "devices.returned",
            "software.returned.present", "device_items.traversed", "device.classified", "inventory.returned",
            "dispose.enter", "dispose.returned", "cycle.returned" }, phases);
        Assert.Single(result.CallbackThreads); Assert.Equal(0, scenario.ActiveEnumerators); Assert.Equal(1, scenario.Disposes);
    }

    [Fact]
    public void Dirty_entry_skips_inventory_and_still_disposes()
    {
        var scenario = new SyntheticVendorScenario { Dirty = true, FailAt = "devices" };
        var observation = Read(scenario);
        Assert.True(observation.IsModifiedBefore); Assert.True(observation.IsModifiedAfter);
        Assert.Empty(observation.Devices); Assert.Equal(1, scenario.Disposes);
    }

    [Fact]
    public void Null_service_is_preserved_and_trace_distinguishes_it_without_new_getters()
    {
        var scenario = new SyntheticVendorScenario { FailAt = "null-service" }; var phases = new List<string>();
        var observation = Read(scenario, phase: phases.Add);
        Assert.Contains("software.returned.null", phases); Assert.DoesNotContain("software.returned.present", phases);
        Assert.Equal("UNKNOWN", observation.Devices.Single().Family);
        Assert.Equal(1, scenario.Disposes); Assert.Equal(0, scenario.ActiveEnumerators);
    }

    [Fact]
    public async Task Expired_observation_budget_does_not_abort_native_owner_or_dispose_before_return()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var scenario = new SyntheticVendorScenario { ReadSoftware = () => { entered.TrySetResult(); release.Wait(); } };
        // Match the real dedicated native owner; do not queue a blocking owner on
        // the same ThreadPool as the observer and concurrent resilience tests.
        var running = Task.Factory.StartNew(() => Read(scenario), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotSame(running, await Task.WhenAny(running, Task.Delay(20)));
            Assert.Equal(0, scenario.Disposes);
        }
        finally { release.Set(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True((await running).PortalAliveAfterDispose); Assert.Equal(1, scenario.Disposes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<WeakReference>, V19Observation[]) Sequential()
    {
        var scenario = new SyntheticVendorScenario(); var results = Enumerable.Range(0, 20).Select(_ => Read(scenario)).ToArray();
        Assert.Equal(20, scenario.Disposes); Assert.Equal(0, scenario.ActiveEnumerators);
        return (scenario.References, results);
    }

    [Fact]
    public void Twenty_sequential_reads_return_only_dtos_without_rooting_synthetic_sessions()
    {
        var (references, results) = Sequential();
        // Controlled collection only in this synthetic retention experiment, never production.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.All(results, observation => Assert.Single(observation.Devices)); GC.KeepAlive(results);
    }

    [Fact]
    public void Trace_is_disabled_without_opt_in_and_rejects_invalid_switch_or_external_path()
    {
        Assert.Null(WorkerResourceTrace.Open("does-not-exist", Guid.Empty, null));
        Assert.Throws<ArgumentException>(() => WorkerResourceTrace.Open("does-not-exist", Guid.NewGuid(), "true"));
        Assert.Throws<IOException>(() => WorkerResourceTrace.Open(Path.GetTempPath(), Guid.NewGuid(), "1"));
    }

    [Fact]
    public void Trace_closes_each_stream_retains_operation_epoch_and_never_overwrites_prior_file()
    {
        using var files = new StoreFixture(); _ = files.Location;
        Guid epoch = Guid.NewGuid(), operation = Guid.NewGuid();
        var trace = WorkerResourceTrace.Open(files.Root, epoch, "1")!;
        string path = Path.Combine(files.Root, "v19-resources-" + epoch.ToString("N") + ".jsonl");
        for (int i = 0; i < 20; i++)
        {
            trace.Record(operation, "cycle.enter");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        var lines = File.ReadAllLines(path).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Equal(21, lines.Length);
            Assert.All(lines, line => Assert.Equal(epoch, line.RootElement.GetProperty("worker_epoch").GetGuid()));
            Assert.All(lines.Skip(1), line => Assert.Equal(operation, line.RootElement.GetProperty("operation_id").GetGuid()));
            Assert.Equal(Enumerable.Range(1, 21), lines.Select(line => line.RootElement.GetProperty("sequence").GetInt32()));
            Assert.All(lines, line => Assert.True(line.RootElement.GetProperty("handles").GetInt32() > 0));
        }
        finally { foreach (var line in lines) line.Dispose(); }
        byte[] prior = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => WorkerResourceTrace.Open(files.Root, epoch, "1"));
        Assert.Equal(prior, File.ReadAllBytes(path));
    }

    [Fact]
    public void Trace_byte_limit_and_write_failure_preserve_evidence_and_fail_closed()
    {
        using var files = new StoreFixture(); _ = files.Location; Guid epoch = Guid.NewGuid();
        var trace = WorkerResourceTrace.Open(files.Root, epoch, "1")!;
        string path = Path.Combine(files.Root, "v19-resources-" + epoch.ToString("N") + ".jsonl");
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            Assert.Throws<IOException>(() => trace.Record(Guid.NewGuid(), "cycle.enter"));
            file.SetLength(WorkerResourceTrace.MaximumBytes);
        }
        Assert.Throws<IOException>(() => trace.Record(Guid.NewGuid(), "cycle.enter"));
        Assert.Equal(WorkerResourceTrace.MaximumBytes, new FileInfo(path).Length);
    }
}
