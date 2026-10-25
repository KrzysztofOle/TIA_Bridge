using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Supervision;

public interface IWorkerConnection : IAsyncDisposable
{
    Guid Epoch { get; }
    int Pid { get; }
    long StartTicks { get; }
    string WorkerId { get; }
    bool HasExited { get; }
    bool Connected { get; }
    DateTimeOffset LastHeartbeat { get; }
    DateTimeOffset? LastProgress { get; }
    bool TryRead(out WireMessage message);
    Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct);
}
public interface IWorkerSupervisor
{
    Task<IWorkerConnection> StartAsync(CancellationToken ct);
}

/// <summary>Starts only the explicitly configured Fake Worker. No process discovery or Siemens activation.</summary>
public sealed class FakeWorkerSupervisor(string workerDll, string artifactRoot, TimeProvider? time = null) : IWorkerSupervisor
{
    public async Task<IWorkerConnection> StartAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || Path.GetFileName(workerDll) != "TiaBridge.FakeWorker.dll" || !File.Exists(workerDll)) throw new ArgumentException("Explicit built Fake Worker required.");
        var clock = time ?? TimeProvider.System;
        string name = "tia-bridge2-" + Guid.NewGuid().ToString("N"); Guid epoch = Guid.NewGuid(), nonce = Guid.NewGuid(); byte[] secret = RandomNumberGenerator.GetBytes(32);
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        using var current = Process.GetCurrentProcess();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        start.ArgumentList.Add(Path.GetFullPath(workerDll));
        Process? process = null; FramedConnection? framed = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            process = Process.Start(start) ?? throw new IOException("Own worker failed to start.");
            long ticks = process.StartTime.ToUniversalTime().Ticks;
            var bootstrap = new Bootstrap(name, Convert.ToBase64String(secret), epoch, nonce, current.Id, current.StartTime.ToUniversalTime().Ticks, artifactRoot);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(bootstrap, Protocol.Json).AsMemory(), deadline.Token); process.StandardInput.Close();
            await pipe.WaitForConnectionAsync(deadline.Token); PipePeer.Verify(pipe, process.Id, server: true);
            framed = new FramedConnection(pipe, secret, epoch);
            var hello = await framed.ReadAsync(deadline.Token); var identity = Protocol.Read<Hello>(hello.Body);
            if (hello.Kind != MessageKind.Hello || hello.OperationId is not null || hello.Fence != 0 || identity.Pid != process.Id || identity.StartTicks != ticks || identity.Nonce != nonce || identity.WorkerId != "tia-bridge.fake" || identity.Build != "1.0") throw new ProtocolException();
            await framed.SendAsync(MessageKind.Ready, null, 0, Protocol.Body(new { accepted = true }), deadline.Token);
            return new WorkerConnection(process, framed, epoch, ticks, identity.WorkerId, clock);
        }
        catch
        {
            if (framed is not null) await framed.DisposeAsync(); else pipe.Dispose();
            // No dispatch has occurred during startup. Only this exact owned child can be terminated here.
            if (process is not null) { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); } process.Dispose(); }
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}

internal sealed class WorkerConnection : IWorkerConnection
{
    private readonly Process process;
    private readonly FramedConnection framed;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<WireMessage> inbox = Channel.CreateBounded<WireMessage>(64);
    private readonly Task reader;
    private long heartbeatTicks, progressTicks;
    private int connected = 1;
    public Guid Epoch { get; }
    public int Pid { get; }
    public long StartTicks { get; }
    public string WorkerId { get; }
    public bool HasExited => process.HasExited;
    public bool Connected => Volatile.Read(ref connected) == 1;
    public DateTimeOffset LastHeartbeat => new(Interlocked.Read(ref heartbeatTicks), TimeSpan.Zero);
    public DateTimeOffset? LastProgress => Interlocked.Read(ref progressTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    internal WorkerConnection(Process process, FramedConnection framed, Guid epoch, long ticks, string workerId, TimeProvider clock)
    {
        this.process = process; this.framed = framed; this.clock = clock; Epoch = epoch; Pid = process.Id; StartTicks = ticks; WorkerId = workerId;
        heartbeatTicks = clock.GetUtcNow().UtcTicks;
        reader = ReadLoop();
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var message = await framed.ReadAsync(stop.Token);
                if (message.Kind == MessageKind.Heartbeat)
                {
                    if (message.OperationId is not null || message.Fence != 0) throw new ProtocolException();
                    Interlocked.Exchange(ref heartbeatTicks, clock.GetUtcNow().UtcTicks); continue;
                }
                if (message.Kind is not (MessageKind.Started or MessageKind.Progress or MessageKind.Receipt) || message.OperationId is null || message.Fence <= 0) throw new ProtocolException();
                if (message.Kind is MessageKind.Started or MessageKind.Progress) Interlocked.Exchange(ref progressTicks, clock.GetUtcNow().UtcTicks);
                if (!inbox.Writer.TryWrite(message)) throw new ProtocolException();
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { Interlocked.Exchange(ref connected, 0); inbox.Writer.TryComplete(); }
    }
    public bool TryRead(out WireMessage message) => inbox.Reader.TryRead(out message!);
    public Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct) => framed.SendAsync(kind, operationId, fence, body, ct);
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { if (Connected) await framed.SendAsync(MessageKind.Stop, null, 0, Protocol.Body(new { synthetic_shutdown = true }), deadline.Token); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        stop.Cancel(); await framed.DisposeAsync(); await reader;
        // Closing IPC lets this synthetic child cancel its own fake delay. Never kill TIA or infer vendor quiescence.
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(wait.Token); }
        catch (OperationCanceledException) { throw new IOException("Own Fake Worker shutdown unconfirmed."); }
        finally { process.Dispose(); stop.Dispose(); }
    }
}
