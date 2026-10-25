using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TiaBridge.Ipc;

// Synthetic executor only. Bootstrap credentials arrive on inherited stdin, never argv/logs.
try { return await Run(); }
catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or ArgumentException or InvalidOperationException)
{ return 74; }

static async Task<int> Run()
{
    var bootstrapBytes = new List<byte>();
    using (var input = Console.OpenStandardInput())
    {
        var one = new byte[1];
        while (await input.ReadAsync(one) == 1 && one[0] != 10)
        { if (bootstrapBytes.Count >= 16384) throw new ProtocolException(); bootstrapBytes.Add(one[0]); }
    }
    Protocol.CheckJson(bootstrapBytes.ToArray());
    var bootstrap = JsonSerializer.Deserialize<Bootstrap>(bootstrapBytes.ToArray(), Protocol.Json) ?? throw new ProtocolException();
    using var host = Process.GetProcessById(bootstrap.HostPid);
    if (host.StartTime.ToUniversalTime().Ticks != bootstrap.HostStartTicks) throw new ProtocolException();
    using var pipe = new NamedPipeClientStream(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var stop = new CancellationTokenSource();
    await pipe.ConnectAsync(10000, stop.Token); PipePeer.Verify(pipe, bootstrap.HostPid, server: false);
    await using var connection = new FramedConnection(pipe, Convert.FromBase64String(bootstrap.Secret), bootstrap.WorkerEpoch);
    using var own = Process.GetCurrentProcess();
    await connection.SendAsync(MessageKind.Hello, null, 0, Protocol.Body(new Hello(Environment.ProcessId, own.StartTime.ToUniversalTime().Ticks, bootstrap.Nonce, "tia-bridge.fake", "1.0")), stop.Token);
    var ready = await connection.ReadAsync(stop.Token); if (ready.Kind != MessageKind.Ready) throw new ProtocolException();
    var cached = new ConcurrentDictionary<(Guid, int), (Dispatch Command, WorkerReply? Reply)>();
    var cancellations = new ConcurrentDictionary<Guid, bool>();
    var effects = new ConcurrentDictionary<Guid, int>();
    var lateRelease = new ConcurrentDictionary<Guid, bool>();
    int busy = 0, suppressHeartbeat = 0; long highestFence = 0;
    Task? execution = null;
    var heartbeat = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
            if (Volatile.Read(ref suppressHeartbeat) == 0) await connection.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new { synthetic = true }), stop.Token);
        }
    });
    try
    {
        while (!stop.IsCancellationRequested)
        {
            var message = await connection.ReadAsync(stop.Token);
            if (message.Kind == MessageKind.Stop) break;
            if (message.Kind == MessageKind.Cancel && message.OperationId is Guid cancel) { cancellations[cancel] = true; continue; }
            if (message.Kind != MessageKind.Dispatch || message.OperationId is not Guid id || message.Fence <= 0) throw new ProtocolException();
            var command = Protocol.Read<Dispatch>(message.Body);
            if (!Protocol.IsHash(command.InputHash) || command.PlanHash is not null && !Protocol.IsHash(command.PlanHash)
                || command.Attempt is < 1 or > 3 || command.DelayMs is < 0 or > 600000 || !Enum.IsDefined(command.Scenario)) throw new ProtocolException();
            var key = (id, command.Attempt);
            if (cached.TryGetValue(key, out var previous))
            {
                if (previous.Command != command || message.Fence != highestFence) throw new ProtocolException();
                if (command.Scenario == FakeScenario.LateSuccess && previous.Reply is null) lateRelease[id] = true;
                if (previous.Reply is not null) await connection.SendAsync(MessageKind.Receipt, id, message.Fence, Protocol.Body(previous.Reply), stop.Token);
                continue;
            }
            if (message.Fence < highestFence || Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new ProtocolException();
            highestFence = message.Fence; cached[key] = (command, null);
            if (cached.Count > 1000) throw new ProtocolException(); // bounded session; idle recycling is the supervisor's choice.
            execution = Task.Run(async () =>
            {
                try
                {
                    _ = Protocol.ReadArtifact(bootstrap.ArtifactRoot, command.Request, 1024 * 1024);
                    string report = Encoding.UTF8.GetString(Protocol.ReadArtifact(bootstrap.ArtifactRoot, command.SuccessReport, Protocol.MaxReceiptBytes));
                    Receipt receipt;
                    if (cancellations.ContainsKey(id)) receipt = new("Cancelled", null, null, 0, true);
                    else if (command.Scenario == FakeScenario.FailBefore || command.Scenario == FakeScenario.SuccessAfterRetry && command.Attempt == 1)
                        receipt = new("Rejected", null, command.FailureCode, 0, true);
                    else
                    {
                        await connection.SendAsync(MessageKind.Started, id, message.Fence, Protocol.Body(new { attempt = command.Attempt }), stop.Token);
                        if (command.Scenario == FakeScenario.Exit) Environment.Exit(75);
                        if (command.Scenario == FakeScenario.Disconnect) { pipe.Dispose(); stop.Cancel(); return; }
                        if (command.Scenario == FakeScenario.NoHeartbeat) Volatile.Write(ref suppressHeartbeat, 1);
                        if (command.Scenario is FakeScenario.Hang or FakeScenario.NoHeartbeat or FakeScenario.HeartbeatWithoutProgress or FakeScenario.Timeout or FakeScenario.UnknownMutation)
                            await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
                        if (command.Scenario == FakeScenario.Cancellable)
                        {
                            while (!cancellations.ContainsKey(id)) await Task.Delay(10, stop.Token);
                            receipt = new("Cancelled", null, null, 0, true);
                        }
                        else
                        {
                            if (command.Scenario == FakeScenario.LateSuccess)
                                while (!lateRelease.ContainsKey(id)) await Task.Delay(10, stop.Token);
                            await Task.Delay(command.DelayMs, stop.Token);
                            int effectCount = effects.AddOrUpdate(id, 1, (_, count) => count + 1);
                            receipt = command.Scenario == FakeScenario.FailAfter ? new("Failed", null, command.FailureCode, effectCount, true) : new("Succeeded", report, null, effectCount, true);
                        }
                    }
                    var reply = new WorkerReply(command.Attempt, command.InputHash, command.PlanHash, receipt);
                    if (command.Scenario == FakeScenario.WrongPlanHash) reply = reply with { PlanHash = new string('f', 64) };
                    cached[key] = (command, reply); Interlocked.Exchange(ref busy, 0);
                    await connection.SendAsync(MessageKind.Receipt, id, command.Scenario == FakeScenario.WrongFence ? message.Fence + 1 : message.Fence, Protocol.Body(reply), stop.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ArgumentException)
                { stop.Cancel(); pipe.Dispose(); }
            });
        }
    }
    finally
    {
        stop.Cancel();
        try { await heartbeat; } catch (Exception e) when (e is IOException or OperationCanceledException) { }
        if (execution is not null) try { await execution; } catch (OperationCanceledException) { }
    }
    return 0;
}
