using System.Text.Json;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;

// Test-only process. Explicit synthetic lanes may start their own Host/Fake or offline probe; never Siemens.
if (args.Length != 4) return 2;
string root = args[0], repository = args[1], mode = args[2], checkpoint = args[3];
if (mode == "sender-lifetime")
{
    if (!OperatingSystem.IsWindows()) return 2;
    return await SenderLifetimeInvestigation.Run(root, repository, checkpoint);
}
if (mode == "handle-investigation")
{
    if (!OperatingSystem.IsWindows()) return 2;
    return await InfrastructureInvestigation.Run(root, repository, checkpoint);
}
if (mode == "http-resilience") { try { await HttpResilienceProbe.Run(root, checkpoint); return 0; } catch (IOException) { return 87; } }
if (mode == "ipc-adversary") return await IpcAdversary.Run(checkpoint);
if (mode == "lock-child") return await HttpResilienceProbe.HoldLock(root, repository);
var faults = new ExitFault(checkpoint);
var location = StorageLocation.Open(root, repository);
var store = new SqliteOperationStore(location, faults);
if (mode == "bootstrap-reservation-crash")
{
    var document = File.ReadAllBytes(Path.Combine(root, "qualification-bootstrap", "authorization.json"));
    var authorization = TiaBridge.Host.Qualification.BootstrapAuthorization.Read(document);
    var owner = store.BeginOwnership(); store.Recover(owner); store.BindBootstrap(authorization.AuthorizationId, owner);
    var epoch = Guid.NewGuid(); store.RegisterWorker(owner, epoch, int.MaxValue, 123, "synthetic.v19");
    byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.0", method = "project.inspect", project_key = authorization.ProjectKey, idempotency_key = Guid.NewGuid().ToString(), @params = new { inventory_scope = "all_supported", fresh = true, timeout_s = 120, queue_timeout_s = 300 } });
    var accepted = store.Accept("synthetic", requestBytes).Operation;
    var spec = new TiaBridge.Host.Scheduling.ExecutionSpec("synthetic.bootstrap", "qualification-only.v19.discovery.v1", false, null,
        V19Target: new(authorization.ProjectKey, authorization.TiaPid, authorization.TiaStartTicks, authorization.ProjectPath))
    { Bootstrap = new(authorization.AuthorizationId, TiaBridge.Host.Qualification.BootstrapAuthorization.Sha(document)) };
    _ = store.PrepareDispatch(owner, accepted.Report.OperationId, epoch, spec, DateTimeOffset.UtcNow.AddSeconds(120));
    Console.WriteLine(accepted.Report.OperationId.ToString());
    Environment.Exit(76); // Real process loss after committed reservation, before any IPC or vendor activation.
}
if (mode is "scheduler-crash" or "scheduler-crash-read")
{
    string method = mode == "scheduler-crash" ? "sync.apply" : "project.inspect";
    byte[] wire = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "request." + method + ".json"));
    store.RegisterProject(ContractJson.ReadRequest(wire).Identity.Project!);
    var operation = store.Accept("synthetic-principal", wire).Operation;
    var artifact = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response." + method + ".json"))));
    string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var worker = new TiaBridge.Host.Supervision.FakeWorkerSupervisor(Path.Combine(repository, "src", "TiaBridge.FakeWorker", "bin", configuration, "net10.0", "TiaBridge.FakeWorker.dll"), root);
    await using var scheduler = new TiaBridge.Host.Scheduling.OperationScheduler(store, worker, new CrashProfile(artifact));
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while (store.ReadStatus(operation.Report.OperationId).Phase != "EXECUTING")
    {
        if (deadline.Elapsed > TimeSpan.FromSeconds(10)) return 77;
        await scheduler.PumpOnceAsync(); await Task.Delay(10);
    }
    Environment.Exit(76);
}
if (mode == "inspect")
{
    var id = new TiaBridge.Core.Identity.OperationId(Guid.Parse(checkpoint));
    var record = store.Read(id);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        operation_id = id.ToString(),
        state = record.Report.State.ToString(),
        terminal = store.ReadResult(id) is not null,
        events = store.Events(id).Count,
        pending = store.Pending().Count
    }));
    return 0;
}
byte[] request = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "request.sync.apply.json"));
var parsed = ContractJson.ReadRequest(request);
store.RegisterProject(parsed.Identity.Project!);
if (mode == "artifact")
{
    var admitted = new SqliteOperationStore(location).Accept("synthetic-principal", request);
    _ = store.Artifacts.Put(admitted.Operation.Report.OperationId, new MemoryStream("synthetic process artifact"u8.ToArray()));
    return 0;
}
var admission = store.Accept("synthetic-principal", request);
if (mode == "claim")
{
    Console.WriteLine(JsonSerializer.Serialize(new { operation_id = admission.Operation.Report.OperationId.ToString(), claim_id = store.TryClaim(admission.Operation.Report.OperationId) }));
    return 0;
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    operation_id = admission.Operation.Report.OperationId.ToString(),
    duplicate = admission.AlreadyAccepted,
    state = admission.Operation.Report.State.ToString(),
    events = store.Events(admission.Operation.Report.OperationId).Count,
    pending = store.Pending().Count
}));
return 0;

sealed class ExitFault(string checkpoint) : IStorageFaults
{
    public void Checkpoint(StorageFaultPoint point)
    {
        if (point.ToString() == checkpoint) Environment.Exit(73); // Abrupt deterministic exit; no managed cleanup/finally.
    }
}

sealed class CrashProfile(ArtifactDescriptor report) : TiaBridge.Host.Scheduling.IExecutionProfile
{
    public TiaBridge.Host.Scheduling.ExecutionSpec? Resolve(StoredOperation operation) => new("synthetic-portal", "fake.1.sync.apply", false, report, TiaBridge.Ipc.FakeScenario.Hang);
}
