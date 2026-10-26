using System.Text;
using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Scheduling;

/// <summary>One owner, one active call, durable intent before IPC. All status reads bypass this loop.</summary>
public sealed class OperationScheduler : IAsyncDisposable
{
    private readonly SqliteOperationStore store;
    private readonly IWorkerSupervisor supervisor;
    private readonly IExecutionProfile profile;
    private readonly TimeProvider clock;
    private readonly IJitter jitter;
    private readonly IDisposable singleton;
    private readonly SemaphoreSlim pump = new(1);
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<(Guid, int), AttemptBinding> history = new();
    private IWorkerConnection? worker;
    private Active? active;
    private Task? loop;
    private DateTimeOffset renewedAt;
    private DateTimeOffset queueScannedAt = DateTimeOffset.MinValue;
    private WorkerObservation observation;
    public OwnerToken Owner { get; }
    public WorkerObservation Observation => Volatile.Read(ref observation);
    public string? LastError { get; private set; }
    public OperationScheduler(SqliteOperationStore store, IWorkerSupervisor supervisor, IExecutionProfile profile, TimeProvider? clock = null, IJitter? jitter = null)
    {
        this.store = store; this.supervisor = supervisor; this.profile = profile; this.clock = clock ?? TimeProvider.System; this.jitter = jitter ?? new RandomJitter();
        singleton = store.LockScheduler();
        try { Owner = store.BeginOwnership(); store.Recover(Owner); }
        catch { singleton.Dispose(); throw; }
        renewedAt = this.clock.GetUtcNow(); observation = new(renewedAt, null, null, null, "IDLE", null, null);
    }
    public void Start()
    {
        if (loop is not null) throw new InvalidOperationException("Scheduler already started.");
        loop = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await PumpOnceAsync(stop.Token);
                    // Idle polling must not open/validate SQLite connections fifty times per second.
                    // Active calls retain the short observation interval; status/health never wait here.
                    await Task.Delay(Observation.State == "IDLE" ? 200 : 20, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception e) when (e is IOException or StorageException or ContractValidationException or InvalidOperationException or ArgumentException or OperationCanceledException or System.ComponentModel.Win32Exception)
            { LastError = "SCHEDULER_STOPPED"; Volatile.Write(ref observation, Observation with { State = "STORE_OR_EXECUTOR_ERROR" }); }
        });
    }
    public async Task PumpOnceAsync(CancellationToken ct = default)
    {
        await pump.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (now - renewedAt >= Watchdog.HeartbeatInterval)
            {
                if (!store.RenewOwner(Owner)) throw new StorageException(ErrorCode.NOT_OWNER);
                renewedAt = now;
            }
            if (now - queueScannedAt >= Watchdog.HeartbeatInterval)
            {
                // Queue deadlines remain enforceable while a worker call hangs or its project is quarantined.
                foreach (var id in store.ExpiredWaitingOperations(now))
                {
                    var queued = store.Read(id); FailQueued(queued, ErrorCode.WAIT_TIMEOUT);
                }
                queueScannedAt = now;
            }
            if (worker is not null)
            {
                while (worker.TryRead(out var message)) await Handle(message, ct);
                if (active is { } current)
                {
                    var report = store.ReadExecutionStatus(current.Binding.OperationId);
                    if (report.CancelRequested && current.RetryAt is not null && !current.TimedOut)
                    {
                        var previous = store.Read(current.Binding.OperationId).Report;
                        store.Complete(Owner, current.Binding, previous with { State = OperationState.CANCELLED, Outcome = OperationOutcome.NOT_STARTED, Phase = "CANCELLED_BEFORE_RETRY", CompletedAt = now, PollAfterMs = 0 });
                        active = null;
                    }
                    if (active is not null && report.CancelRequested && !current.CancelSent && !current.TimedOut)
                    {
                        current.CancelSent = true;
                        await Send(MessageKind.Cancel, current.Binding.OperationId.Value, Owner.Fence, Protocol.Body(new { requested = true }), ct);
                    }
                    var error = Watchdog.Evaluate(now, current.Binding.Deadline, worker.LastHeartbeat, worker.Connected, worker.HasExited);
                    if (active is not null && !current.TimedOut && error is { } cause)
                    {
                        current.TimedOut = true;
                        var previous = store.Read(current.Binding.OperationId).Report;
                        store.Complete(Owner, current.Binding, store.Failure(previous, current.Binding.Mutation, cause, cause == ErrorCode.TIMEOUT ? "EXECUTION_TIMEOUT" : "WORKER_LOST"));
                        // No release or parallel retry: current remains active until independent quiescence evidence.
                    }
                    if (current.RetryAt is { } retryAt && now >= retryAt && now < current.Binding.Deadline && !current.TimedOut && !report.CancelRequested)
                    {
                        var next = store.PrepareDispatch(Owner, current.Binding.OperationId, worker.Epoch, current.Spec, current.Binding.Deadline, current.Binding.Attempt + 1);
                        if (next is not null) { active = current with { Binding = next, RetryAt = null }; await Dispatch(active, ct); }
                    }
                    if (current.TimedOut && worker.HasExited && !current.Binding.Mutation)
                    {
                        store.ConfirmReadProcessExit(Owner, current.Binding, worker.Pid, worker.StartTicks);
                        active = null; await worker.DisposeAsync(); worker = null;
                    }
                }
                else if (worker.HasExited || !worker.Connected)
                {
                    // A broken pipe alone is not proof of the end of a call; no active call exists here.
                    await worker.DisposeAsync(); worker = null;
                }
            }
            if (active is null && !store.OwnerBlocked)
            {
                // Bound per-epoch replay correlation without exhausting a long-lived Host.
                // Only a quiescent owned worker can be recycled; UNKNOWN keeps active/blocked and never enters here.
                if (worker is not null && history.Count >= 1000)
                {
                    await worker.DisposeAsync(); worker = null; history.Clear();
                }
                foreach (var pending in store.Pending())
                {
                    var operation = store.Read(pending.OperationId);
                    var spec = profile.Resolve(operation);
                    using var json = JsonDocument.Parse(operation.RequestJson); var parameters = json.RootElement.GetProperty("params");
                    int queueSeconds = parameters.TryGetProperty("queue_timeout_s", out var queue) ? queue.GetInt32() : 300;
                    if (now - operation.Report.AcceptedAt >= TimeSpan.FromSeconds(queueSeconds)) { FailQueued(operation, ErrorCode.WAIT_TIMEOUT); continue; }
                    if (spec is null) { FailQueued(operation, ErrorCode.UNSUPPORTED); continue; }
                    if (string.IsNullOrWhiteSpace(spec.PortalKey) || spec.PortalKey.Length > 512 || string.IsNullOrWhiteSpace(spec.Capability) || spec.Capability.Length > 128) throw new ArgumentException("Invalid execution scope.");
                    var breaker = LoadBreaker(spec);
                    if (!breaker.TryEnter(now, spec.QualifiedEffectFreeRead && !RetryPolicy.IsMutation(operation.Report.Method))) { FailQueued(operation, ErrorCode.CIRCUIT_OPEN); continue; }
                    PersistBreaker(spec, breaker);
                    if (worker is null)
                    {
                        try { worker = await supervisor.StartAsync(ct); }
                        catch (Exception e) when (e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception && !ct.IsCancellationRequested)
                        {
                            // Startup failed before any durable dispatch; the supervisor owns cleanup of that child.
                            breaker.Failure(clock.GetUtcNow()); PersistBreaker(spec, breaker);
                            FailQueued(operation, ErrorCode.WORKER_UNAVAILABLE); break;
                        }
                        store.RegisterWorker(Owner, worker.Epoch, worker.Pid, worker.StartTicks, worker.WorkerId);
                    }
                    // Materialize the immutable input before entering the dispatch transaction.
                    var input = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(operation.RequestJson));
                    if (spec.SuccessReport is { } successTemplate)
                        using (var template = store.Artifacts.OpenVerified(successTemplate.ArtifactId))
                        {
                            if (template.Descriptor != spec.SuccessReport || template.Descriptor.Size > Protocol.MaxReceiptBytes) throw new StorageException(ErrorCode.VERIFY_FAILED);
                        }
                    int? timeoutSeconds = parameters.TryGetProperty("timeout_s", out var timeout) ? timeout.GetInt32() : null;
                    var budget = new ExecutionBudget(operation.Report.Method, timeoutSeconds, queueSeconds);
                    var binding = store.PrepareDispatch(Owner, pending.OperationId, worker.Epoch, spec, budget.ExecutionDeadline(clock.GetUtcNow()));
                    if (binding is not null)
                    {
                        active = new(binding, spec, new(input.ArtifactId, input.Sha256)); await Dispatch(active, ct);
                    }
                    break;
                }
            }
            Volatile.Write(ref observation, new(now, worker?.LastHeartbeat, worker?.LastProgress, null, store.OwnerBlocked ? "QUARANTINED" : active?.TimedOut == true ? "SUSPECT" : active is null ? "IDLE" : "BUSY", worker?.Pid, worker?.Epoch));
        }
        catch (Exception e) when (e is ProtocolException or ContractValidationException)
        {
            if (active is { TimedOut: false } call)
            {
                call.TimedOut = true;
                store.Complete(Owner, call.Binding, store.Failure(store.Read(call.Binding.OperationId).Report, call.Binding.Mutation, ErrorCode.WORKER_UNAVAILABLE, "INVALID_WORKER_RECEIPT"));
            }
            Volatile.Write(ref observation, Observation with { HostHeartbeat = clock.GetUtcNow(), State = "INVALID_WORKER_RECEIPT" });
        }
        finally { pump.Release(); }
    }
    private async Task Dispatch(Active call, CancellationToken ct)
    {
        var b = call.Binding; history[(b.OperationId.Value, b.Attempt)] = b;
        if (history.Count > 1000) throw new InvalidOperationException("Bounded scheduler session exhausted.");
        if (call.Spec.V19Target is not null)
        {
            if (worker is not WorkerConnection native) throw new ProtocolException();
            call.WorkerResourcesBefore = native.SampleResources();
            await Send(MessageKind.Dispatch, b.OperationId.Value, b.Fence, Protocol.Body(new InspectDispatch(call.Request, b.Attempt, b.InputHash, null)), ct); return;
        }
        var command = new Ipc.Dispatch(call.Request, new(call.Spec.SuccessReport!.ArtifactId, call.Spec.SuccessReport.Sha256), call.Spec.Scenario, call.Spec.DelayMs, b.Attempt, b.InputHash, b.PlanHash, call.Spec.FailureCode.ToString());
        await Send(MessageKind.Dispatch, b.OperationId.Value, b.Fence, Protocol.Body(command), ct);
        if (call.Spec.Scenario == FakeScenario.DuplicateDispatch) await Send(MessageKind.Dispatch, b.OperationId.Value, b.Fence, Protocol.Body(command), ct);
    }
    private async Task Send(MessageKind kind, Guid? id, long fence, JsonElement body, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(1));
        try { await worker!.SendAsync(kind, id, fence, body, deadline.Token); }
        catch (Exception e) when (e is IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (active is { TimedOut: false } call)
            {
                call.TimedOut = true;
                var previous = store.Read(call.Binding.OperationId).Report;
                store.Complete(Owner, call.Binding, store.Failure(previous, call.Binding.Mutation, ErrorCode.CONNECTION_LOST, "IPC_SEND_UNCONFIRMED"));
            }
        }
    }
    private async Task Handle(WireMessage message, CancellationToken ct)
    {
        if (message.Kind is MessageKind.Started or MessageKind.Progress)
        {
            if (active is { TimedOut: false } progress && message.OperationId == progress.Binding.OperationId.Value && message.Fence == progress.Binding.Fence)
            {
                var previous = store.Read(progress.Binding.OperationId);
                if (!OperationLifecycle.IsTerminal(previous.Report.State)) store.Record(previous.Report.OperationId, previous.Revision, previous.Report with { Phase = "EXECUTING", LastProgressAt = clock.GetUtcNow() });
            }
            return;
        }
        var reply = Protocol.Read<WorkerReply>(message.Body);
        if (message.OperationId is not Guid id || !history.TryGetValue((id, reply.Attempt), out var binding)) throw new ProtocolException();
        bool valid = message.WorkerEpoch == binding.WorkerEpoch && message.Fence == binding.Fence && reply.InputHash == binding.InputHash && reply.PlanHash == binding.PlanHash;
        var current = active;
        // A queued receipt must not outrun the deadline check when the host was descheduled.
        if (valid && current?.Binding == binding && !current.TimedOut && clock.GetUtcNow() >= binding.Deadline)
        {
            current.TimedOut = true;
            store.Complete(Owner, binding, store.Failure(store.Read(binding.OperationId).Report, binding.Mutation, ErrorCode.TIMEOUT, "EXECUTION_TIMEOUT"));
        }
        if (!valid || current is null || current.Binding != binding || current.TimedOut || current.RetryAt is not null || OperationLifecycle.IsTerminal(Enum.Parse<OperationState>(store.ReadExecutionStatus(current.Binding.OperationId).State.ToString())))
        {
            store.RecordReceipt(Owner, binding, message, valid ? "LATE_OR_DUPLICATE" : "FENCED_REJECTED");
            if (valid && current?.TimedOut == true && !binding.Mutation && reply.Receipt.Quiescent) { store.RecordReceipt(Owner, binding, message, "QUIESCENT"); active = null; }
            return;
        }
        var receipt = reply.Receipt;
        if (!receipt.Quiescent || receipt.EffectCount is < 0 or > 1) throw new ProtocolException();
        var previousReport = store.Read(binding.OperationId).Report;
        if (receipt.Disposition == "Rejected" && receipt.EffectCount == 0 && Enum.TryParse<ErrorCode>(receipt.ErrorCode, out var error))
        {
            store.RecordReceipt(Owner, binding, message, "REJECTED_NO_EFFECT");
            var breaker = LoadBreaker(current.Spec); breaker.Failure(clock.GetUtcNow()); PersistBreaker(current.Spec, breaker);
            TimeSpan delay = binding.Attempt < 3 ? RetryPolicy.Delay(binding.Attempt, jitter) : TimeSpan.Zero;
            if (!previousReport.CancelRequested && RetryPolicy.CanRetry(current.Spec.QualifiedEffectFreeRead, binding.Mutation, true, binding.Attempt, error)
                && clock.GetUtcNow() + delay < binding.Deadline && breaker.State.OpenedAt is null)
            { current.RetryAt = clock.GetUtcNow() + delay; return; }
            store.Complete(Owner, binding, store.Failure(previousReport, false, error, "REJECTED_NO_EFFECT")); active = null; return;
        }
        store.RecordReceipt(Owner, binding, message, "QUIESCENT");
        if (receipt.Disposition == "Succeeded" && receipt.Report is { } reportJson && Encoding.UTF8.GetByteCount(reportJson) <= Protocol.MaxReceiptBytes)
        {
            var template = current.Spec.V19Target is { } target
                ? V19InspectionProjection.Create(store, previousReport, target, reportJson,
                    worker is WorkerConnection native ? native.Build : throw new ProtocolException(),
                    current.Spec.CapabilityBinding ?? throw new ProtocolException(),
                    current.WorkerResourcesBefore ?? throw new ProtocolException(), native.SampleResources())
                : ContractJson.ReadReport(Encoding.UTF8.GetBytes(reportJson));
            if (template.Method != previousReport.Method || template.State != OperationState.SUCCEEDED) throw new ProtocolException();
            store.Complete(Owner, binding, previousReport with { State = OperationState.SUCCEEDED, Outcome = OperationOutcome.CONFIRMED, Phase = "COMPLETED", Result = template.Result, ArtifactRefs = template.ArtifactRefs, SessionEpoch = template.SessionEpoch ?? previousReport.SessionEpoch, CompletedAt = clock.GetUtcNow(), PollAfterMs = 0 });
            var breaker = LoadBreaker(current.Spec); breaker.Success(); PersistBreaker(current.Spec, breaker);
        }
        else if (receipt.Disposition == "Cancelled" && receipt.EffectCount == 0)
            store.Complete(Owner, binding, previousReport with { State = OperationState.CANCELLED, Outcome = OperationOutcome.NOT_STARTED, Phase = "CANCELLED_AT_SAFE_BOUNDARY", CompletedAt = clock.GetUtcNow(), PollAfterMs = 0 });
        else
        {
            var code = Enum.TryParse<ErrorCode>(receipt.ErrorCode, out var parsed) ? parsed : ErrorCode.WORKER_UNAVAILABLE;
            store.Complete(Owner, binding, store.Failure(previousReport, binding.Mutation, code, "EXECUTION_FAILED"));
            var breaker = LoadBreaker(current.Spec); breaker.Failure(clock.GetUtcNow()); PersistBreaker(current.Spec, breaker);
        }
        active = null;
        await Task.CompletedTask;
    }
    private CircuitBreaker LoadBreaker(ExecutionSpec spec) => new(store.ReadBreaker(spec.PortalKey, spec.Capability) is { } json ? JsonSerializer.Deserialize<CircuitState>(json) : null);
    private void PersistBreaker(ExecutionSpec spec, CircuitBreaker breaker) => store.WriteBreaker(Owner, spec.PortalKey, spec.Capability, JsonSerializer.Serialize(breaker.State));
    private void FailQueued(StoredOperation operation, ErrorCode code)
    {
        try { store.Record(operation.Report.OperationId, operation.Revision, store.Failure(operation.Report, false, code, "NOT_DISPATCHED")); }
        catch (StorageException e) when (e.Code == ErrorCode.CONFLICT) { } // Cancellation won the CAS.
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); if (loop is not null) await loop;
        await pump.WaitAsync();
        try { if (worker is not null) await worker.DisposeAsync(); }
        finally { singleton.Dispose(); pump.Release(); stop.Dispose(); }
        // RUNNING is deliberately left for restart recovery. Shutdown is not proof of native outcome.
    }
    private sealed record Active(AttemptBinding Binding, ExecutionSpec Spec, ArtifactPointer Request)
    {
        public bool TimedOut { get; set; }
        public bool CancelSent { get; set; }
        public DateTimeOffset? RetryAt { get; set; }
        public TiaBridge.Core.Discovery.DiscoveryResourceSample? WorkerResourcesBefore { get; set; }
    }
}
