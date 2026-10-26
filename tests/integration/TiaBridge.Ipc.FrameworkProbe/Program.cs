using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using TiaBridge.Ipc;

try
{
    var line = Console.ReadLine();
    if (line is null || line.Length > 16384) return 74;
    using var doc = JsonDocument.Parse(line); var bootstrap = Protocol.Read<Bootstrap>(doc.RootElement);
    using var host = Process.GetProcessById(bootstrap.HostPid);
    if (host.StartTime.ToUniversalTime().Ticks != bootstrap.HostStartTicks) return 74;
    using var pipe = new NamedPipeClientStream(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(10000); PipePeer.Verify(pipe, host.Id, false);
    await using var framed = new FramedConnection(pipe, Convert.FromBase64String(bootstrap.Secret), bootstrap.WorkerEpoch, "1.1");
    using var own = Process.GetCurrentProcess();
    await framed.SendAsync(MessageKind.Hello, null, 0, Protocol.Body(new Hello(own.Id, own.StartTime.ToUniversalTime().Ticks, bootstrap.Nonce, "framework-probe", "1.0")));
    if ((await framed.ReadAsync()).Kind != MessageKind.Ready) return 74;
    var dispatch = await framed.ReadAsync(); var body = Protocol.Read<InspectDispatch>(dispatch.Body);
    await framed.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)));
    await framed.SendAsync(MessageKind.Receipt, dispatch.OperationId, dispatch.Fence, Protocol.Body(new WorkerReply(body.Attempt, body.InputHash, null, new Receipt("Rejected", null, "UNSUPPORTED", 0, true))));
    return (await framed.ReadAsync()).Kind == MessageKind.Stop ? 0 : 74;
}
catch { return 74; }
