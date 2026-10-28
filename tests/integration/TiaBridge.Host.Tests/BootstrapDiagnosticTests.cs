using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Qualification;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public sealed class BootstrapDiagnosticTests
{
    [Fact]
    public void HTTP_request_cannot_choose_diagnostic_purpose_or_budget()
    {
        using var f = new BootstrapFixture(diagnostic: true);
        foreach (string field in new[] { "purpose", "maximum_cycles" })
        {
            var request = System.Text.Json.Nodes.JsonNode.Parse(f.Request())!;
            request["params"]![field] = field == "purpose" ? System.Text.Json.Nodes.JsonValue.Create("HANDLE_DIAGNOSTICS") : System.Text.Json.Nodes.JsonValue.Create(3);
            Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(request)));
        }
    }
    [Fact]
    public async Task Three_cycles_finish_durably_and_never_become_B1_PASS_or_enrollment()
    {
        using var f = new BootstrapFixture(diagnostic: true); f.Authorize();
        await using (var scheduler = f.Scheduler())
        {
            for (int n = 1; n <= 3; n++)
            {
                var id = f.Admit(); await f.Complete(scheduler, id);
                Assert.Equal(n, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
                Assert.True(f.Store.Accept("synthetic", f.Store.Read(id).RequestJson).AlreadyAccepted);
                Assert.Equal(1, f.Store.AttemptCount(id));
            }
            Assert.Equal("FINISHED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
            var fourth = f.Admit(); await scheduler.PumpOnceAsync();
            Assert.Equal(OperationState.FAILED, f.Store.Read(fourth).Report.State); Assert.Equal(3, f.Connection.Dispatches);
        }
        WriteTrace(f);
        var bundle = new BootstrapEvidence(f.Config, f.Store).Capture();
        Assert.Equal("DIAGNOSTIC_COMPLETE", bundle.Status); BootstrapEvidence.Verify(bundle.Directory);
        Assert.Throws<IOException>(() => new BootstrapEvidence(f.Config, f.Store).Capture(true));
        Assert.False(Directory.Exists(Path.Combine(f.Files.Root, "qualification-evidence")));
        Assert.ThrowsAny<Exception>(() => f.Local.Authorize(f.DocumentSha, true));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("purpose")]
    [InlineData("plan")]
    [InlineData("budget")]
    [InlineData("legacy-purpose")]
    [InlineData("legacy-three")]
    public void Crossed_schema_kind_plan_or_budget_is_denied(string change)
    {
        using var f = new BootstrapFixture(diagnostic: true);
        var auth = change switch
        {
            "schema" => f.Auth with { SchemaVersion = "1.2" },
            "purpose" => f.Auth with { Purpose = "QUALIFICATION" },
            "plan" => f.Auth with { PlanId = "E2-B1" },
            "budget" => f.Auth with { MaximumCycles = 20 },
            "legacy-purpose" => f.Auth with { SchemaVersion = "1.0", MaximumCycles = 20, PlanId = "E2-B1" },
            "legacy-three" => f.Auth with { SchemaVersion = "1.0", Purpose = null, PlanId = "E2-B1" },
            _ => throw new ArgumentException()
        };
        f.WriteAuthorization(auth); Assert.ThrowsAny<Exception>(() => f.Authorize());
        Assert.Null(f.Store.ReadBootstrap(f.Auth.AuthorizationId));
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expiry")]
    [InlineData("binding")]
    [InlineData("config")]
    public async Task Diagnostic_revalidation_denies_queued_work_without_consuming_or_dispatching(string fault)
    {
        using var f = new BootstrapFixture(diagnostic: true);
        if (fault == "expiry") f.WriteAuthorization(f.Auth with { ExpiresAtUtc = f.Clock.GetUtcNow().AddSeconds(5) });
        f.Authorize();
        await using var scheduler = f.Scheduler(); var id = f.Admit();
        Assert.NotNull(f.Profile!.Resolve(f.Store.Read(id)));
        if (fault == "revoke") f.Local.Revoke();
        if (fault == "expiry") f.Clock.Advance(TimeSpan.FromSeconds(6));
        if (fault == "binding") f.BindingValid = false;
        if (fault == "config") File.AppendAllText(f.ConfigPath, " ");
        await scheduler.PumpOnceAsync(); Assert.Equal(0, f.Connection.Dispatches);
        Assert.Equal(0, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
    }

    [Fact]
    public async Task Diagnostic_crash_reservation_remains_consumed_and_cannot_replay_on_restart()
    {
        using var f = new BootstrapFixture(diagnostic: true); f.Authorize();
        var crash = f.Files.Probe("bootstrap-reservation-crash"); Assert.Equal(76, crash.Code);
        var id = new OperationId(Guid.Parse(crash.Output.Trim()));
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        await using var scheduler = f.Scheduler(); await scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Connection.Dispatches); Assert.Equal(1, f.Store.AttemptCount(id));
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.Equal("UNCERTAIN_AFTER_RESTART", f.Store.BootstrapCycles(f.Auth.AuthorizationId).Single().State);
    }

    [Theory]
    [InlineData("true-true")]
    [InlineData("false-true")]
    [InlineData("stale-epoch")]
    [InlineData("worker-loss")]
    public async Task Diagnostic_uncertainty_stops_remaining_budget(string fault)
    {
        using var f = new BootstrapFixture(diagnostic: true); f.Authorize(); f.Connection.Fault = fault;
        await using var scheduler = f.Scheduler(); f.Admit(); await scheduler.PumpOnceAsync();
        await scheduler.PumpOnceAsync();
        var next = f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(1, f.Connection.Dispatches); Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.NotEqual(OperationState.SUCCEEDED, f.Store.Read(next).Report.State);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
    }

    [Fact]
    public async Task Diagnostic_native_timeout_consumes_one_cycle_without_retry_or_worker_replacement()
    {
        using var f = new BootstrapFixture(diagnostic: true); f.Authorize(); f.Connection.Fault = "hang";
        await using var scheduler = f.Scheduler(); var id = f.Admit(); await scheduler.PumpOnceAsync();
        // Renew the same owner normally while the native result stays unknown.
        for (int n = 0; n < 61; n++) { f.Clock.Advance(TimeSpan.FromSeconds(2)); await scheduler.PumpOnceAsync(); }
        f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(1, f.Connection.Dispatches); Assert.Equal(1, f.Store.AttemptCount(id));
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.NotEqual(OperationState.SUCCEEDED, f.Store.Read(id).Report.State);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("epoch")]
    [InlineData("phase")]
    [InlineData("clock")]
    public async Task Diagnostic_missing_or_invalid_phase_evidence_never_completes(string fault)
    {
        using var f = new BootstrapFixture(diagnostic: true); f.Authorize();
        await using (var scheduler = f.Scheduler()) for (int n = 0; n < 3; n++) await f.Complete(scheduler, f.Admit());
        if (fault != "missing") WriteTrace(f, fault);
        Assert.Equal("BLOCKED", new BootstrapEvidence(f.Config, f.Store).Capture().Status);
    }

    private static void WriteTrace(BootstrapFixture f, string? fault = null)
    {
        var phases = new[] { "cycle.enter", "enumeration.returned", "attach.enter", "attach.returned", "project.checked", "devices.enter", "devices.returned",
            "software.returned.present", "device_items.traversed", "device.classified", "inventory.returned", "dispose.enter", "dispose.returned", "cycle.returned", "worker.idle" };
        int n = 0; var lines = new List<string>();
        void Record(string? id, string phase) => lines.Add(JsonSerializer.Serialize(new
        {
            schema_version = "v19-resource-trace/1",
            worker_epoch = fault == "epoch" ? Guid.NewGuid() : f.Connection.Epoch,
            worker_pid = f.Connection.Pid,
            worker_start_ticks = f.Connection.StartTicks,
            operation_id = id,
            sequence = ++n,
            phase,
            utc = f.Clock.GetUtcNow(),
            elapsed_ticks = fault == "clock" ? -n : n,
            stopwatch_frequency = 1000,
            handles = 100,
            private_bytes = 4096,
            working_set_bytes = 8192,
            managed_bytes = 1024,
            threads = 1,
            gc0 = 0,
            gc1 = 0,
            gc2 = 0
        }));
        Record(null, "worker.start");
        foreach (var cycle in f.Store.BootstrapCycles(f.Auth.AuthorizationId))
            foreach (string phase in phases) if (fault != "phase" || phase != "devices.enter") Record(cycle.OperationId, phase);
        File.WriteAllText(Path.Combine(f.Files.Root, "v19-resources-" + f.Connection.Epoch.ToString("N") + ".jsonl"), string.Join('\n', lines));
    }
}
