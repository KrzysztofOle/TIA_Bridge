using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using TiaBridge.Ipc;

namespace TiaBridge.Diagnostics;

internal static class InfrastructureEntry
{
    internal static void NoVendor()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Siemens.", StringComparison.Ordinal)))
            throw new InvalidOperationException("VENDOR_ASSEMBLY_FORBIDDEN");
    }
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "sender-lifetime") return SenderLifetimeProbe.Run(args[1]).GetAwaiter().GetResult();
        if (args.Length != 2 || args[0] is not ("baseline" or "worker" or "heartbeat" or "no-heartbeat" or "sampler" or "audit" or "poll-only" or "send-only" or "timer-only" or "send-only-no-flush")) return 2;
        string mode = args[0], root = Path.GetFullPath(args[1]);
        if (!Environment.Is64BitProcess || !root.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA_Bridge2", "dev") + "\\", StringComparison.OrdinalIgnoreCase)) return 2;
        NoVendor(); using var self = Process.GetCurrentProcess(); int selfPid = self.Id;
        using var stop = new CancellationTokenSource();
        using var samples = new StreamWriter(new FileStream(Path.Combine(root, "internal.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var clock = Stopwatch.StartNew();
        string previousPhase = ""; int sequence = 0;
        bool diagnosticGc = false;
        object GcCheckpoint()
        {
            NoVendor(); using var own = Process.GetCurrentProcess(); own.Refresh();
            return new
            {
                elapsed_ticks = clock.ElapsedTicks,
                frequency = Stopwatch.Frequency,
                handles = own.HandleCount,
                private_bytes = own.PrivateMemorySize64,
                working_set_bytes = own.WorkingSet64,
                managed_bytes = GC.GetTotalMemory(false),
                gc0 = GC.CollectionCount(0),
                gc1 = GC.CollectionCount(1),
                gc2 = GC.CollectionCount(2),
                snapshot = PssHandleSnapshot.Capture(own)
            };
        }
        void Sample()
        {
            NoVendor();
            string phase = new[] { "shutdown", "post-gc", "post-idle", "activity", "initial-idle" }.FirstOrDefault(p => File.Exists(Path.Combine(root, "phase-" + p))) ?? "startup";
            using var own = Process.GetCurrentProcess(); own.Refresh();
            var threads = own.Threads; int threadCount;
            try { threadCount = threads.Count; } finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
            samples.WriteLine(JsonSerializer.Serialize(new
            {
                sequence = ++sequence,
                phase,
                elapsed_ticks = clock.ElapsedTicks,
                frequency = Stopwatch.Frequency,
                utc = DateTimeOffset.UtcNow,
                pid = own.Id,
                start_ticks = own.StartTime.ToUniversalTime().Ticks,
                handles = own.HandleCount,
                private_bytes = own.PrivateMemorySize64,
                working_set_bytes = own.WorkingSet64,
                managed_bytes = GC.GetTotalMemory(false),
                gc0 = GC.CollectionCount(0),
                gc1 = GC.CollectionCount(1),
                gc2 = GC.CollectionCount(2),
                threads = threadCount
            }, Protocol.Json)); samples.Flush();
            if (phase != previousPhase)
            {
                using var f = new FileStream(Path.Combine(root, "types-" + sequence + ".json"), FileMode.CreateNew, FileAccess.Write);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { phase, sequence, snapshot = PssHandleSnapshot.Capture(own) }, Protocol.Json);
                f.Write(bytes, 0, bytes.Length); previousPhase = phase;
            }
        }
        Sample(); File.WriteAllText(Path.Combine(root, "ready.json"), "{\"vendor_loaded\":false}");
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(1000, stop.Token).ConfigureAwait(false); Sample();
                if (!diagnosticGc && File.Exists(Path.Combine(root, "collect-gc")))
                {
                    if (mode != "worker") throw new InvalidOperationException("GC control is isolated to the synthetic worker experiment");
                    diagnosticGc = true; var before = GcCheckpoint();
                    // Explicit separate diagnostic only. This code is never linked into production Worker.
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    var after = GcCheckpoint(); NoVendor();
                    using var file = new FileStream(Path.Combine(root, "controlled-gc.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { purpose = "ISOLATED_CONTROLLED_GC", before, after }, Protocol.Json);
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (previousPhase == "activity" && mode is "sampler" or "audit")
                    for (int n = 0; n < 100; n++)
                    {
                        using var own = Process.GetProcessById(selfPid); own.Refresh();
                        if (mode == "sampler")
                        {
                            _ = own.HandleCount; _ = own.PrivateMemorySize64; _ = own.WorkingSet64;
                            var threads = own.Threads;
                            try { _ = threads.Count; } finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
                            using var identity = WindowsIdentity.GetCurrent(); _ = identity.User;
                        }
                        else
                        {
                            using (var file = new FileStream(Path.Combine(root, "audit.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                            { byte[] line = System.Text.Encoding.UTF8.GetBytes("{\"synthetic\":true}\n"); file.Write(line, 0, line.Length); file.Flush(true); }
                            using var exclusive = new FileStream(Path.Combine(root, "audit.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
                        }
                    }
            }
        });
        int result;
        try
        {
            if (mode == "worker")
                result = (int)typeof(TiaBridge.Worker.V19.Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { Array.Empty<string>() });
            else if (mode is "heartbeat" or "no-heartbeat" or "poll-only" or "send-only" or "timer-only" or "send-only-no-flush") result = Transport(mode).GetAwaiter().GetResult();
            else { while (!File.Exists(Path.Combine(root, "stop"))) Thread.Sleep(50); result = 0; }
        }
        finally
        {
            stop.Cancel(); try { sampler.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Sample(); NoVendor();
            File.WriteAllText(Path.Combine(root, "ended.json"), JsonSerializer.Serialize(new { vendor_loaded = false, runtime = Environment.Version.ToString(), x64 = Environment.Is64BitProcess }));
        }
        return result;
    }
    private static async Task<int> Transport(string mode)
    {
        using var doc = JsonDocument.Parse(Console.ReadLine()!); var setup = Protocol.Read<Bootstrap>(doc.RootElement);
        using var host = Process.GetProcessById(setup.HostPid);
        if (host.StartTime.ToUniversalTime().Ticks != setup.HostStartTicks) throw new ProtocolException();
        using var pipe = new NamedPipeClientStream(".", setup.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource();
        await pipe.ConnectAsync(10000, stop.Token).ConfigureAwait(false); PipePeer.Verify(pipe, setup.HostPid, false);
        Stream transport = mode == "send-only-no-flush" ? new InlinePipeFlush(pipe) : pipe;
        await using var framed = new FramedConnection(transport, Convert.FromBase64String(setup.Secret), setup.WorkerEpoch, "1.1");
        using var own = Process.GetCurrentProcess();
        await framed.SendAsync(MessageKind.Hello, null, 0, Protocol.Body(new Hello(own.Id, own.StartTime.ToUniversalTime().Ticks, setup.Nonce, "infrastructure-probe", "1.0")), stop.Token);
        if ((await framed.ReadAsync(stop.Token)).Kind != MessageKind.Ready) throw new ProtocolException();
        async Task Beat()
        {
            if (mode == "no-heartbeat") return;
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(2000, stop.Token).ConfigureAwait(false);
                if (mode is "heartbeat" or "poll-only") { host.Refresh(); if (host.HasExited) throw new IOException("Parent lost"); }
                if (mode is "heartbeat" or "send-only" or "send-only-no-flush")
                    await framed.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)), stop.Token).ConfigureAwait(false);
            }
        }
        Task beat = Beat();
        try
        {
            while (true)
            {
                var message = await framed.ReadAsync(stop.Token).ConfigureAwait(false);
                if (message.Kind == MessageKind.Stop) break;
                if (message.Kind != MessageKind.Dispatch) throw new ProtocolException();
                var dispatch = Protocol.Read<InspectDispatch>(message.Body);
                await framed.SendAsync(MessageKind.Receipt, message.OperationId, message.Fence,
                    Protocol.Body(new WorkerReply(1, dispatch.InputHash, null, new Receipt("Rejected", null, "UNSUPPORTED", 0, true))), stop.Token).ConfigureAwait(false);
            }
        }
        finally { stop.Cancel(); try { await beat.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        return 0;
    }

    // Counterfactual only: all IO is forwarded unchanged; no inherited async flush scheduling.
    private sealed class InlinePipeFlush(PipeStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); inner.Flush(); return Task.CompletedTask; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.WriteAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
