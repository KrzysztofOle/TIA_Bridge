using System.Text.Json;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;

// Test-only process. It never starts a server or dispatches to a worker.
if (args.Length != 4) return 2;
string root = args[0], repository = args[1], mode = args[2], checkpoint = args[3];
var faults = new ExitFault(checkpoint);
var location = StorageLocation.Open(root, repository);
var store = new SqliteOperationStore(location, faults);
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
