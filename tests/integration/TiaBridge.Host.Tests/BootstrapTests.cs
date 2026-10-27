using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public sealed class BootstrapTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("approval")]
    [InlineData("hash")]
    [InlineData("json")]
    [InlineData("operator")]
    public async Task Missing_invalid_or_unapproved_local_authorization_never_dispatches(string fault)
    {
        using var f = new BootstrapFixture();
        if (fault == "json") { File.WriteAllText(f.DocumentPath, "{\"schema_version\":\"1.0\",\"schema_version\":\"1.0\"}"); Assert.ThrowsAny<Exception>(() => f.Local.Authorize("bad", true)); }
        if (fault == "approval") Assert.Throws<IOException>(() => f.Local.Authorize(f.DocumentSha, false));
        if (fault == "hash") Assert.Throws<IOException>(() => f.Local.Authorize(new string('0', 64), true));
        if (fault == "operator") Assert.ThrowsAny<Exception>(() => new BootstrapLocal(f.Config with { QualificationBootstrap = f.Config.QualificationBootstrap! with { OperatorSid = "S-1-5-21-1-2-3-1001" } }, f.Store).Authorize(f.DocumentSha, true));
        await using var scheduler = f.Scheduler(); var operation = f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Connection.Dispatches); Assert.Equal(OperationState.FAILED, f.Store.Read(operation).Report.State);
    }

    [Theory]
    [InlineData("project-key")]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("exe")]
    [InlineData("sid")]
    [InlineData("host-build")]
    [InlineData("worker-build")]
    [InlineData("tia-build")]
    [InlineData("profile")]
    [InlineData("read-scope")]
    [InlineData("write")]
    [InlineData("import")]
    [InlineData("export")]
    [InlineData("second-project")]
    [InlineData("limits")]
    [InlineData("plan")]
    public async Task Exact_authorization_rejects_each_scope_or_binding_drift(string fault)
    {
        using var f = new BootstrapFixture();
        var auth = fault switch
        {
            "project-key" => f.Auth with { ProjectKey = Guid.NewGuid().ToString() },
            "pid" => f.Auth with { TiaPid = 999 },
            "start" => f.Auth with { TiaStartTicks = 999 },
            "exe" => f.Auth with { ExecutablePath = "C:\\other.exe" },
            "sid" => f.Auth with { SessionSid = "S-1-5-21-1-2-3-1001" },
            "host-build" => f.Auth with { HostBuild = "1.0+" + new string('b', 64) },
            "worker-build" => f.Auth with { WorkerBuild = "1.0+" + new string('b', 64) },
            "tia-build" => f.Auth with { TiaBuild = "19.other" },
            "profile" => f.Auth with { Profile = "v19.wider" },
            "read-scope" => f.Auth with { ReadScope = [.. f.Auth.ReadScope, "Project.Save"] },
            "write" => f.Auth with { OperationMethod = "sync.apply" },
            "import" => f.Auth with { OperationMethod = "project.import" },
            "export" => f.Auth with { OperationMethod = "project.export" },
            "second-project" => f.Auth with { ProjectName = "T04_T05-000_Testowy" },
            "limits" => f.Auth with { MaximumNodes = 257 },
            "plan" => f.Auth with { PlanId = "E2-B2" },
            _ => throw new ArgumentException()
        };
        f.WriteAuthorization(auth);
        try { f.Local.Authorize(f.DocumentSha, true); } catch (IOException) { }
        await using var scheduler = f.Scheduler(); f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Connection.Dispatches);
    }

    [Fact]
    public async Task Twenty_cycles_are_durable_idempotent_and_do_not_enroll_or_grant_normal_E1()
    {
        using var f = new BootstrapFixture(); f.Authorize();
        await using (var scheduler = f.Scheduler())
        {
            for (int cycle = 1; cycle <= 20; cycle++)
            {
                var id = f.Admit(); await f.Complete(scheduler, id);
                Assert.Equal(cycle, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
                var operation = f.Store.Read(id);
                Assert.True(f.Store.Accept("synthetic", operation.RequestJson).AlreadyAccepted);
                Assert.Equal(1, f.Store.AttemptCount(id));
            }
            var overBudget = f.Admit(); await scheduler.PumpOnceAsync();
            Assert.Equal(OperationState.FAILED, f.Store.Read(overBudget).Report.State);
            Assert.Equal(20, f.Connection.Dispatches);
            Assert.Null(new V19InspectionProfile(f.Config with { QualificationBootstrap = null }).Resolve(f.Store.Read(overBudget)));
        }
        Assert.Equal("FINISHED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.False(Directory.Exists(Path.Combine(f.Files.Root, "qualification-evidence")));
        var automatic = new BootstrapEvidence(f.Config, f.Store).Capture(); Assert.Equal("REVIEW_REQUIRED", automatic.Status);
        f.WriteReview("GROWING"); Assert.Equal("REVIEW_REQUIRED", new BootstrapEvidence(f.Config, f.Store).Capture(true).Status);
        f.WriteReview("STABLE_WITHIN_OBSERVATION"); var reviewed = new BootstrapEvidence(f.Config, f.Store).Capture(true);
        Assert.Equal("PASS", reviewed.Status); BootstrapEvidence.Verify(reviewed.Directory);
        File.AppendAllText(Path.Combine(reviewed.Directory, "report.json"), "tampered");
        Assert.Throws<IOException>(() => BootstrapEvidence.Verify(reviewed.Directory));
        Assert.Equal(20, f.Store.BootstrapCycles(f.Auth.AuthorizationId).Count);
        Assert.ThrowsAny<Exception>(() => f.Local.Authorize(f.DocumentSha, true));
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expiry")]
    [InlineData("queued-binding")]
    [InlineData("config-drift")]
    public async Task Queue_revalidation_stops_revoked_expired_or_changed_grant(string cause)
    {
        using var f = new BootstrapFixture();
        if (cause == "expiry") f.WriteAuthorization(f.Auth with { ExpiresAtUtc = f.Clock.GetUtcNow().AddSeconds(5) });
        f.Authorize();
        await using var scheduler = f.Scheduler(); var id = f.Admit();
        Assert.NotNull(f.Profile!.Resolve(f.Store.Read(id)));
        if (cause == "revoke") f.Local.Revoke();
        if (cause == "expiry") f.Clock.Advance(TimeSpan.FromSeconds(6));
        if (cause == "queued-binding") f.BindingValid = false;
        if (cause == "config-drift") File.AppendAllText(f.ConfigPath, " ");
        await scheduler.PumpOnceAsync(); Assert.Equal(0, f.Connection.Dispatches);
        Assert.Equal(OperationState.FAILED, f.Store.Read(id).Report.State);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Revoke_at_either_dispatch_boundary_sends_no_native_call(int boundary)
    {
        using var f = new BootstrapFixture(); f.Authorize(); var native = f.NewProfile();
        await using var scheduler = new OperationScheduler(f.Store, f.Supervisor, new BoundaryProfile(native, boundary, f.Local.Revoke), f.Clock);
        var id = f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Connection.Dispatches);
        Assert.Equal(boundary == 3 ? 1 : 0, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal(OperationState.FAILED, f.Store.Read(id).Report.State);
    }

    [Theory]
    [InlineData("true-true")]
    [InlineData("false-true")]
    [InlineData("true-false")]
    [InlineData("worker-loss")]
    [InlineData("stale-epoch")]
    public async Task Dirty_project_and_uncertain_worker_results_stop_remaining_budget_and_retain_raw(string fault)
    {
        using var f = new BootstrapFixture(); f.Authorize(); f.Connection.Fault = fault;
        await using var scheduler = f.Scheduler(); var id = f.Admit(); await scheduler.PumpOnceAsync();
        await scheduler.PumpOnceAsync();
        Assert.NotEqual(OperationState.SUCCEEDED, f.Store.Read(id).Report.State);
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        if (fault != "worker-loss") Assert.True(f.Store.BootstrapReceipts(id).Length > 0);
        f.Admit(); await scheduler.PumpOnceAsync(); Assert.Equal(1, f.Connection.Dispatches);
    }

    [Fact]
    public async Task Host_restart_closes_authorization_and_keeps_reserved_cycle_uncertain_without_replay()
    {
        using var f = new BootstrapFixture(); f.Authorize();
        var first = f.Scheduler(); var id = f.Admit(); await first.PumpOnceAsync();
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        // Dispose does not drain the held result: models the durable reservation after a lost Host.
        await first.DisposeAsync();
        await using var restarted = f.Scheduler(); await restarted.PumpOnceAsync();
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("UNCERTAIN_AFTER_RESTART", f.Store.BootstrapCycles(f.Auth.AuthorizationId).Single().State);
        Assert.Equal(1, f.Store.AttemptCount(id)); Assert.NotEqual(OperationState.SUCCEEDED, f.Store.Read(id).Report.State);
        f.Admit(); await restarted.PumpOnceAsync(); Assert.Equal(1, f.Connection.Dispatches);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("missing-artifact")]
    [InlineData("corrupt-artifact")]
    [InlineData("missing-audit")]
    [InlineData("corrupt-telemetry")]
    [InlineData("corrupt-initial")]
    [InlineData("wrong-cycle-report")]
    public async Task Missing_corrupt_and_partial_evidence_never_produces_PASS(string fault)
    {
        using var f = new BootstrapFixture(); f.Authorize();
        await using (var scheduler = f.Scheduler())
        {
            var id = f.Admit(); await f.Complete(scheduler, id);
            var reference = f.Store.Read(id).Report.ArtifactRefs.First();
            string file = Path.Combine(f.Files.Root, "artifacts", reference.ArtifactId + ".blob");
            if (fault is "missing-artifact" or "corrupt-artifact") { File.SetAttributes(file, FileAttributes.Normal); if (fault == "missing-artifact") File.Delete(file); else File.WriteAllText(file, "bad"); }
            if (fault == "missing-audit") File.Delete(Path.Combine(f.Files.Root, "v19-audit-" + f.Connection.Epoch.ToString("N") + ".jsonl"));
            if (fault == "corrupt-telemetry") f.Files.Sql("UPDATE bootstrap_cycles SET telemetry=x'7b7d'");
            if (fault == "corrupt-initial") f.Files.Sql("UPDATE bootstrap_cycles SET initial_telemetry=x'7b7d'");
            if (fault == "wrong-cycle-report") f.Files.Sql("UPDATE bootstrap_cycles SET terminal_report=x'7b7d'");
        }
        Assert.NotEqual("PASS", new BootstrapEvidence(f.Config, f.Store).Capture().Status);
    }

    [Fact]
    public async Task Abrupt_process_exit_after_reservation_consumes_cycle_and_restart_cannot_replay()
    {
        using var f = new BootstrapFixture(); f.Authorize();
        var crash = f.Files.Probe("bootstrap-reservation-crash"); Assert.Equal(76, crash.Code);
        var operationId = new OperationId(Guid.Parse(crash.Output.Trim()));
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("RESERVED", f.Store.BootstrapCycles(f.Auth.AuthorizationId).Single().State);
        await using var restarted = f.Scheduler(); await restarted.PumpOnceAsync();
        Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.Equal("UNCERTAIN_AFTER_RESTART", f.Store.BootstrapCycles(f.Auth.AuthorizationId).Single().State);
        Assert.Equal(1, f.Store.AttemptCount(operationId)); Assert.Equal(0, f.Connection.Dispatches);
    }

    [Fact]
    public async Task Idle_worker_loss_closes_grant_instead_of_restarting_and_masking_resource_trend()
    {
        using var f = new BootstrapFixture(); f.Authorize();
        await using var scheduler = f.Scheduler(); var first = f.Admit(); await f.Complete(scheduler, first);
        f.Connection.Fault = "worker-loss"; var second = f.Admit(); await scheduler.PumpOnceAsync();
        Assert.Equal(1, f.Connection.Dispatches); Assert.Equal(1, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.Equal(OperationState.FAILED, f.Store.Read(second).Report.State);
    }

    [Fact]
    public async Task Stale_receipt_from_previous_cycle_stops_current_cycle_without_rewriting_history()
    {
        using var f = new BootstrapFixture(); f.Authorize();
        await using var scheduler = f.Scheduler(); var first = f.Admit(); await f.Complete(scheduler, first);
        byte[] historical = ContractJson.WriteReport(f.Store.Read(first).Report);
        var previous = f.Connection.LastReceipt!;
        var second = f.Admit(); await scheduler.PumpOnceAsync();
        f.Connection.InjectFirst(previous with { WorkerEpoch = Guid.NewGuid() });
        await scheduler.PumpOnceAsync();
        Assert.Equal(historical, ContractJson.WriteReport(f.Store.Read(first).Report));
        Assert.Equal(OperationState.FAILED, f.Store.Read(second).Report.State);
        Assert.Equal("STOPPED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.Equal(2, f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.UsedCycles);
    }

    [Fact]
    public void Production_binding_checker_rejects_synthetic_executable_without_loading_vendor()
    {
        using var f = new BootstrapFixture();
        Assert.False(BootstrapLocal.VerifyLiveBinding(f.Config, f.Auth));
    }

    [Theory]
    [InlineData("sync.apply")]
    [InlineData("project.export")]
    public async Task Valid_bootstrap_authorization_does_not_allow_other_request_methods(string method)
    {
        using var f = new BootstrapFixture(); f.Authorize();
        await using var scheduler = f.Scheduler();
        var operation = f.Files.Accept(f.Store, method).Operation;
        await scheduler.PumpOnceAsync(); Assert.Equal(0, f.Connection.Dispatches);
        Assert.Equal(OperationState.FAILED, f.Store.Read(operation.Report.OperationId).Report.State);
    }

    [Fact]
    public async Task Local_operator_CLI_requires_confirmation_and_exact_document_hash()
    {
        using var f = new BootstrapFixture();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string dll = Path.Combine(f.Files.Repository, "src", "TiaBridge.Qualification", "bin", configuration, "net10.0", "TiaBridge.Qualification.dll");
        async Task<int> Run(params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { dll, "--config", f.ConfigPath }.Concat(arguments)) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); await Task.WhenAll(output, error); return process.ExitCode;
        }
        Assert.Equal(3, await Run("bootstrap-authorize"));
        Assert.Null(f.Store.ReadBootstrap(f.Auth.AuthorizationId));
        Assert.Equal(3, await Run("bootstrap-authorize", "--document-sha256", new string('0', 64), "--confirm-operator-approval"));
        Assert.Equal(0, await Run("bootstrap-authorize", "--document-sha256", f.DocumentSha, "--confirm-operator-approval"));
        Assert.Equal("READY", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
        Assert.Equal(0, await Run("bootstrap-revoke")); Assert.Equal("REVOKED", f.Store.ReadBootstrap(f.Auth.AuthorizationId)!.State);
    }

    [Fact]
    public void Normal_configuration_has_no_bootstrap_and_client_cannot_supply_one()
    {
        using var f = new BootstrapFixture();
        var wire = System.Text.Json.Nodes.JsonNode.Parse(f.Request())!;
        wire["qualification_bootstrap"] = new System.Text.Json.Nodes.JsonObject { ["enabled"] = true };
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(wire)));
        Assert.Null(new HostConfiguration("1.0", f.Files.Root, f.Files.Repository, []).QualificationBootstrap);
        Assert.DoesNotContain(typeof(HostConfiguration).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Siemens", StringComparison.Ordinal));
    }

    private sealed class BoundaryProfile(BootstrapInspectionProfile inner, int boundary, Action revoke) : IExecutionProfile
    {
        private int checks;
        public void BeginSession(OwnerToken owner) => inner.BeginSession(owner);
        public void EndSession(bool released) => inner.EndSession(released);
        public void WorkerLost() => inner.WorkerLost();
        public ExecutionSpec? Resolve(StoredOperation operation) { if (++checks == boundary) revoke(); return inner.Resolve(operation); }
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class BootstrapFixture : IDisposable
{
    internal StoreFixture Files { get; } = new();
    internal SqliteOperationStore Store { get; }
    internal ManualClock Clock { get; } = new();
    internal HostConfiguration Config { get; }
    internal BootstrapAuthorization Auth { get; }
    internal BootstrapLocal Local { get; }
    internal BootstrapConnection Connection { get; }
    internal IWorkerSupervisor Supervisor { get; }
    internal BootstrapInspectionProfile? Profile;
    internal bool BindingValid = true;
    internal string ConfigPath => Path.Combine(Files.Root, "bootstrap-config.json");
    internal string DocumentPath => Path.Combine(Local.Root, "authorization.json");
    internal string DocumentSha => BootstrapAuthorization.Sha(File.ReadAllBytes(DocumentPath));
    internal BootstrapFixture()
    {
        Store = new(Files.Location, clock: Clock);
        string sid = LocalTrustAcl.CurrentSid, project = "11111111-1111-4111-8111-111111111111";
        string projectPath = Path.Combine(Files.Root, BootstrapAuthorization.B1 + ".ap19"), workerPath = Path.Combine(Files.Root, "TiaBridge.Worker.V19.exe");
        File.WriteAllText(projectPath, "synthetic project metadata"); File.WriteAllText(workerPath, "synthetic non-executable");
        string worker = "1.0+" + new string('a', 64), host = "1.0+" + new string('c', 64);
        Config = new("1.0", Files.Root, Files.Repository, [project], MaxInFlight: 1, V19WorkerExe: workerPath,
            V19: new([new(project, 123, 456, projectPath)], Files.Root), QualificationBootstrap: new(Guid.NewGuid().ToString(), sid, sid))
        { StartupConfigurationPath = ConfigPath };
        File.WriteAllBytes(ConfigPath, JsonSerializer.SerializeToUtf8Bytes(Config, HostConfiguration.Json)); LocalTrustedFixture.ProtectConfig(ConfigPath);
        Local = new(Config, Store);
        var acl = new DirectorySecurity(); acl.SetOwner(new SecurityIdentifier(sid)); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Local.Root).Create(acl);
        Auth = new("1.0", Config.QualificationBootstrap!.AuthorizationId, sid, Clock.GetUtcNow().AddSeconds(-2), Clock.GetUtcNow().AddSeconds(-1), Clock.GetUtcNow().AddHours(1),
            BootstrapAuthorization.B1, project, projectPath, Files.Root, 123, 456, "C:\\synthetic-portal.exe", sid, "19.synthetic", worker, host,
            "project.inspect", "PROJECT", V19DiscoveryProfile.Id, V19InspectionProfile.DiscoveryReadScope.ToArray(), "all_supported", true, 120, 300, 20, 64, 256, 8,
            "E2-B1", new string('d', 64), new string('e', 40), BootstrapAuthorization.RequiredStops);
        WriteAuthorization(Auth); Store.RegisterProject(new(Guid.Parse(project)));
        Connection = new(Auth, Clock, Files.Root); Supervisor = new BootstrapSupervisor(Connection);
    }
    internal BootstrapInspectionProfile NewProfile() => new(Config, Store, Clock, a => BindingValid && a.ExecutablePath == Auth.ExecutablePath && a.SessionSid == Auth.SessionSid && a.HostBuild == Auth.HostBuild && a.WorkerBuild == Auth.WorkerBuild && a.TiaBuild == Auth.TiaBuild);
    internal OperationScheduler Scheduler() => new(Store, Supervisor, Profile = NewProfile(), Clock);
    internal void WriteAuthorization(BootstrapAuthorization auth) => File.WriteAllBytes(DocumentPath, JsonSerializer.SerializeToUtf8Bytes(auth, HostConfiguration.Json));
    internal void Authorize() => Local.Authorize(DocumentSha, true);
    internal byte[] Request() => JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.0", method = "project.inspect", project_key = Auth.ProjectKey, idempotency_key = Guid.NewGuid().ToString(), @params = new { inventory_scope = "all_supported", fresh = true, timeout_s = 120, queue_timeout_s = 300 } });
    internal OperationId Admit() => Store.Accept("synthetic", Request()).Operation.Report.OperationId;
    internal async Task Complete(OperationScheduler scheduler, OperationId id)
    {
        await scheduler.PumpOnceAsync(); await scheduler.PumpOnceAsync();
        Assert.Equal(OperationState.SUCCEEDED, Store.Read(id).Report.State);
    }
    internal void WriteReview(string trend)
    {
        string directory = Path.Combine(Local.Root, "review-data"); Directory.CreateDirectory(directory);
        string[] files = ["project-state.txt", "resource-review.txt", "plan-checks.txt"];
        foreach (string file in files) File.WriteAllText(Path.Combine(directory, file), "synthetic reviewed evidence " + file);
        var review = new BootstrapReview(DocumentSha, LocalTrustAcl.CurrentSid, DateTimeOffset.UtcNow, true, true, trend, files);
        File.WriteAllBytes(Path.Combine(Local.Root, "review.json"), JsonSerializer.SerializeToUtf8Bytes(review, HostConfiguration.Json));
    }
    public void Dispose() => Files.Dispose();
    private sealed class BootstrapSupervisor(BootstrapConnection connection) : IWorkerSupervisor
    { public Task<IWorkerConnection> StartAsync(CancellationToken ct) => Task.FromResult<IWorkerConnection>(connection); }
}

internal sealed class BootstrapConnection(BootstrapAuthorization authorization, TimeProvider clock, string artifactRoot) : IV19WorkerConnection
{
    internal int Dispatches;
    internal string? Fault;
    private readonly Queue<WireMessage> messages = new();
    internal WireMessage? LastReceipt;
    internal void InjectFirst(WireMessage message) { var rest = messages.ToArray(); messages.Clear(); messages.Enqueue(message); foreach (var item in rest) messages.Enqueue(item); }
    public Guid Epoch { get; } = Guid.NewGuid();
    public int Pid => 123456789;
    public long StartTicks => 987654321;
    public string WorkerId => "tia-bridge.v19.read-only";
    public string Build => authorization.WorkerBuild;
    public bool HasExited => Fault == "worker-loss" && Dispatches > 0;
    public bool Connected => !HasExited;
    public DateTimeOffset LastHeartbeat => clock.GetUtcNow();
    public DateTimeOffset? LastProgress => clock.GetUtcNow();
    public TiaBridge.Core.Discovery.DiscoveryResourceSample SampleResources() => new(100, 4096, 8192);
    public bool TryRead(out WireMessage message) => messages.TryDequeue(out message!);
    public Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct)
    {
        if (kind != MessageKind.Dispatch) return Task.CompletedTask;
        Dispatches++; if (Fault == "worker-loss") return Task.CompletedTask;
        var request = Protocol.Read<InspectDispatch>(body);
        var node = new V19Node(V19NodeLocatorCodec.Encode(new(0, null, 0, "synthetic-device", "Synthetic.Device", "NOT_APPLICABLE", "UNKNOWN", "UNAVAILABLE")), "unknown");
        var observation = new V19Observation(authorization.ProjectKey, Guid.NewGuid(), authorization.TiaPid, authorization.TiaStartTicks,
            authorization.ProjectPath, authorization.ProjectName, Fault is "true-true" or "true-false", Fault is "true-true" or "false-true",
            authorization.TiaBuild, [new("synthetic-device", "UNKNOWN")], [node], 1, 2, 3, 4, 1, "STA", [], true);
        var receipt = new Receipt("Succeeded", JsonSerializer.Serialize(observation, Protocol.Json), null, 0, true);
        LastReceipt = new("1.1", Fault == "stale-epoch" ? Guid.NewGuid() : Epoch, 1, MessageKind.Receipt, operationId, fence, Protocol.Body(new WorkerReply(request.Attempt, request.InputHash, null, receipt)));
        messages.Enqueue(LastReceipt);
        foreach (string call in new[] { "TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.DisposeAttachedSession.Returned" })
            File.AppendAllText(Path.Combine(artifactRoot, "v19-audit-" + Epoch.ToString("N") + ".jsonl"), JsonSerializer.Serialize(new { operation_id = operationId, worker_epoch = Epoch, at = clock.GetUtcNow(), call, thread_id = 1 }) + "\n");
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
