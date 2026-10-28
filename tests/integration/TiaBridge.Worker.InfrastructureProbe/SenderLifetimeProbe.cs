using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using TiaBridge.Ipc;

namespace TiaBridge.Diagnostics;

// Test assembly only. No vendor references, production switches or forced production GC.
internal static class SenderLifetimeProbe
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private sealed class ThreadReference(uint nativeId, string role, WeakReference reference)
    {
        internal uint NativeId = nativeId;
        internal string Role = role;
        internal WeakReference Reference = reference;
        internal bool ThreadPool = Thread.CurrentThread.IsThreadPoolThread;
    }
    private static readonly ConcurrentDictionary<int, ThreadReference> threads = new();
    private static readonly ConcurrentBag<WeakReference> tasks = new();
    private static long apmEnded, apmClosed, apmMissing;
    private static readonly ConcurrentQueue<object> apmExamples = new();
    private static void Track(string role) => threads[Thread.CurrentThread.ManagedThreadId] = new(GetCurrentThreadId(), role, new WeakReference(Thread.CurrentThread));
    private static void Write(string path, object data)
    { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Protocol.Json); file.Write(bytes, 0, bytes.Length); }

    internal static async Task<int> Run(string root)
    {
        root = Path.GetFullPath(root);
        if (!Environment.Is64BitProcess || !root.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA_Bridge2", "dev") + "\\", StringComparison.OrdinalIgnoreCase)) return 2;
        InfrastructureEntry.NoVendor();
        using var specDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "lifetime.json")));
        var spec = specDoc.RootElement;
        bool beatEnabled = spec.GetProperty("heartbeat").GetBoolean(), observed = spec.GetProperty("observe_flush").GetBoolean(), inline = spec.GetProperty("inline_flush").GetBoolean();
        using var input = JsonDocument.Parse(Console.ReadLine()!);
        var setup = Protocol.Read<Bootstrap>(input.RootElement);
        using var host = Process.GetProcessById(setup.HostPid);
        if (host.StartTime.ToUniversalTime().Ticks != setup.HostStartTicks) throw new ProtocolException();
        using var own = Process.GetCurrentProcess();
        using var pipe = new NamedPipeClientStream(".", setup.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource();
        await pipe.ConnectAsync(10000, stop.Token).ConfigureAwait(false); PipePeer.Verify(pipe, host.Id, false);
        bool apm = spec.TryGetProperty("observe_apm", out var apmSwitch) && apmSwitch.GetBoolean();
        Stream transport = observed || inline || apm ? new FlushObservation(pipe, inline, apm) : pipe;
        var framed = new FramedConnection(transport, Convert.FromBase64String(setup.Secret), setup.WorkerEpoch, "1.1");
        string phase = "cold"; long sent = 0, attempted = 0, errors = 0, heartbeats = 0; int sequence = 0;
        bool disposed = false; string termination = "UNKNOWN";
        var clock = Stopwatch.StartNew(); object sync = new();
        using var output = new StreamWriter(new FileStream(Path.Combine(root, "metrics.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        object WeakState() => new
        {
            threads = threads.Select(p => new { managed_id = p.Key, native_id = p.Value.NativeId, role = p.Value.Role, thread_pool = p.Value.ThreadPool, managed_object_alive = p.Value.Reference.IsAlive }).ToArray(),
            flush_tasks_seen = tasks.Count,
            flush_tasks_alive = tasks.Count(t => t.IsAlive),
            apm_ended = apmEnded,
            apm_closed = apmClosed,
            apm_missing = apmMissing,
            apm_examples = apmExamples.ToArray()
        };
        void Sample(string step, bool snapshot = false)
        {
            lock (sync)
            {
                InfrastructureEntry.NoVendor(); own.Refresh();
                var list = own.Threads; int count;
                try { count = list.Count; } finally { foreach (ProcessThread t in list) t.Dispose(); }
                int index = ++sequence;
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    run_id = Environment.GetEnvironmentVariable("QA_RUN_ID") ?? "NOT_AVAILABLE",
                    scenario_id = spec.GetProperty("id").GetString(),
                    step_id = step,
                    git_sha = spec.GetProperty("git_sha").GetString(),
                    build_sha = spec.GetProperty("build_sha").GetString(),
                    worker_pid = own.Id,
                    worker_epoch = setup.WorkerEpoch,
                    utc = DateTimeOffset.UtcNow,
                    elapsed_ticks = clock.ElapsedTicks,
                    frequency = Stopwatch.Frequency,
                    phase,
                    handles = own.HandleCount,
                    threads = count,
                    private_bytes = own.PrivateMemorySize64,
                    working_set = own.WorkingSet64,
                    managed_heap = GC.GetTotalMemory(false),
                    gc0 = GC.CollectionCount(0),
                    gc1 = GC.CollectionCount(1),
                    gc2 = GC.CollectionCount(2),
                    ipc_messages = Interlocked.Read(ref attempted),
                    completed_sends = Interlocked.Read(ref sent),
                    errors = Interlocked.Read(ref errors),
                    heartbeats = Interlocked.Read(ref heartbeats),
                    cleanup = disposed ? "DISPOSED" : "ACTIVE",
                    sequence = index,
                    handle_types = snapshot ? "types-" + index + ".json" : "NOT_AVAILABLE"
                }, Protocol.Json)); output.Flush();
                if (snapshot) Write(Path.Combine(root, "types-" + index + ".json"), new { phase, sequence = index, utc = DateTimeOffset.UtcNow, snapshot = PssHandleSnapshot.Capture(own), ownership = WeakState() });
            }
        }
        async Task Send(MessageKind kind, Guid? id, long fence, JsonElement body)
        {
            if (observed) Track(kind.ToString());
            Interlocked.Increment(ref attempted);
            try { await framed.SendAsync(kind, id, fence, body, stop.Token).ConfigureAwait(false); Interlocked.Increment(ref sent); }
            catch { Interlocked.Increment(ref errors); throw; }
        }
        Write(Path.Combine(root, "runtime.json"), new
        {
            runtime = Environment.Version.ToString(),
            mscorlib = typeof(object).Assembly.Location,
            system_core = typeof(PipeStream).Assembly.Location,
            flush_declaring_type = typeof(NamedPipeClientStream).GetMethod("FlushAsync", new[] { typeof(CancellationToken) })!.DeclaringType!.FullName,
            write_declaring_type = typeof(NamedPipeClientStream).GetMethod("WriteAsync", new[] { typeof(byte[]), typeof(int), typeof(int), typeof(CancellationToken) })!.DeclaringType!.FullName,
            heartbeat_ms = 2000,
            vendor_loaded = false,
            observe_flush = observed,
            inline_flush = inline
        });
        await Send(MessageKind.Hello, null, 0, Protocol.Body(new Hello(own.Id, own.StartTime.ToUniversalTime().Ticks, setup.Nonce, "infrastructure-probe", "1.0")));
        if ((await framed.ReadAsync(stop.Token).ConfigureAwait(false)).Kind != MessageKind.Ready) throw new ProtocolException();
        Write(Path.Combine(root, "ready.json"), new { vendor_loaded = false });
        Sample("startup", true);
        async Task Beat()
        {
            if (!beatEnabled) return;
            while (true)
            {
                await Task.Delay(2000, stop.Token).ConfigureAwait(false);
                await Send(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false))); Interlocked.Increment(ref heartbeats);
            }
        }
        async Task Sampler()
        { while (true) { await Task.Delay(1000, stop.Token).ConfigureAwait(false); Sample("timer"); } }
        Task beat = Beat(), sampler = Sampler();
        try
        {
            while (true)
            {
                var message = await framed.ReadAsync(stop.Token).ConfigureAwait(false);
                if (message.Kind == MessageKind.Stop) { termination = "STOP"; break; }
                if (message.Kind != MessageKind.Dispatch) throw new ProtocolException();
                var dispatch = Protocol.Read<InspectDispatch>(message.Body);
                using var command = JsonDocument.Parse(Protocol.ReadArtifact(root, dispatch.Request, 1024));
                string action = command.RootElement.GetProperty("action").GetString()!;
                phase = command.RootElement.GetProperty("phase").GetString()!;
                if (action == "gc")
                {
                    if (!spec.GetProperty("controlled_gc").GetBoolean()) throw new InvalidOperationException("GC_NOT_AUTHORIZED_IN_TEST");
                    Sample("pre-gc", true);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    Sample("post-gc", true);
                }
                else if (action == "checks") Write(Path.Combine(root, "cleanup-checks.json"), await FailureChecks());
                else if (action is not ("sample" or "snapshot" or "send")) throw new ProtocolException();
                await Send(MessageKind.Receipt, message.OperationId, message.Fence, Protocol.Body(new WorkerReply(1, dispatch.InputHash, null, new Receipt("Succeeded", null, null, 0, true))));
                if (phase != "warmup") Sample(action, action == "snapshot");
            }
        }
        catch (IOException) { termination = "PIPE_LOSS"; }
        finally
        {
            stop.Cancel();
            try { await beat.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (IOException) { }
            try { await sampler.ConfigureAwait(false); } catch (OperationCanceledException) { }
            await framed.DisposeAsync(); disposed = true; phase = "shutdown"; Sample("cleanup", true);
            Write(Path.Combine(root, "ended.json"), new { status = "PASS", cleanup = "DISPOSED", termination, vendor_loaded = false, sends = sent, errors });
        }
        return 0;
    }

    private static async Task<object> FailureChecks()
    {
        var results = new List<object>();
        foreach (string mode in new[] { "normal", "exception", "timeout", "cancelled", "queued-cancel" })
        {
            var stream = new FaultStream(mode); var connection = new FramedConnection(stream, new byte[32], Guid.NewGuid(), "1.1");
            using var cancellation = new CancellationTokenSource();
            string result = "SUCCESS";
            try
            {
                if (mode == "cancelled") cancellation.Cancel();
                if (mode == "timeout") cancellation.CancelAfter(30);
                if (mode == "queued-cancel")
                {
                    Task first = connection.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)), cancellation.Token);
                    using var queued = new CancellationTokenSource();
                    Task waiting = connection.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)), queued.Token);
                    if (waiting.IsCompleted) throw new InvalidOperationException("Send did not wait at gate");
                    queued.Cancel();
                    try { await waiting; throw new InvalidOperationException("Queued cancellation ignored"); }
                    catch (OperationCanceledException) { }
                    cancellation.Cancel(); try { await first; } catch (OperationCanceledException) { }
                    result = "CANCELLED";
                }
                else await connection.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)), cancellation.Token);
            }
            catch (OperationCanceledException) { result = "CANCELLED"; }
            catch (IOException) { result = "IO_EXCEPTION"; }
            finally
            {
                stream.Recover = true;
                // Check gate release on an in-memory fault stream only; a partial real frame
                // requires closing the connection, never reusing a damaged pipe.
                await connection.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)));
                await connection.DisposeAsync();
            }
            string expected = mode == "normal" ? "SUCCESS" : mode == "exception" ? "IO_EXCEPTION" : "CANCELLED";
            if (result != expected || !stream.Disposed) throw new InvalidOperationException("Cleanup regression " + mode);
            results.Add(new { mode, result, disposed = stream.Disposed, status = "PASS" });
        }
        return results;
    }

    private sealed class FaultStream(string mode) : MemoryStream
    {
        internal bool Disposed;
        internal bool Recover;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (!Recover && mode == "exception") throw new IOException("Synthetic write fault");
            if (!Recover && mode is "timeout" or "queued-cancel") return Task.Delay(Timeout.Infinite, ct);
            return base.WriteAsync(buffer, offset, count, ct);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    // Counterfactual only. Forwarded reads/writes are identical. Base FlushAsync schedules
    // Flush on ThreadPool just as the installed PipeStream inherits from Stream.
    private sealed class FlushObservation(PipeStream inner, bool inline, bool apm) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { Track("scheduled-flush"); inner.Flush(); }
        public override Task FlushAsync(CancellationToken ct)
        {
            if (inline) { ct.ThrowIfCancellationRequested(); inner.Flush(); return Task.CompletedTask; }
            Task task = base.FlushAsync(ct); tasks.Add(new WeakReference(task)); return task;
        }
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => apm ? base.ReadAsync(b, o, c, ct) : inner.ReadAsync(b, o, c, ct);
        public override Task WriteAsync(byte[] b, int o, int c, CancellationToken ct) => apm ? base.WriteAsync(b, o, c, ct) : inner.WriteAsync(b, o, c, ct);
        public override IAsyncResult BeginRead(byte[] b, int o, int c, AsyncCallback callback, object state) => inner.BeginRead(b, o, c, callback, state);
        public override IAsyncResult BeginWrite(byte[] b, int o, int c, AsyncCallback callback, object state) => inner.BeginWrite(b, o, c, callback, state);
        public override int EndRead(IAsyncResult result)
        {
            WaitHandle? wait = ExistingWait(result); long? value = wait?.SafeWaitHandle.DangerousGetHandle().ToInt64();
            try { return inner.EndRead(result); }
            finally { RecordEnd(result, wait, value, "read"); }
        }
        public override void EndWrite(IAsyncResult result)
        {
            WaitHandle? wait = ExistingWait(result); long? value = wait?.SafeWaitHandle.DangerousGetHandle().ToInt64();
            try { inner.EndWrite(result); }
            finally { RecordEnd(result, wait, value, "write"); }
        }
        private static WaitHandle? ExistingWait(IAsyncResult result)
        {
            // Never request AsyncWaitHandle: that property could allocate the evidence.
            return result.GetType().GetField("_waitHandle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(result) as WaitHandle;
        }
        private static void RecordEnd(IAsyncResult result, WaitHandle? wait, long? value, string direction)
        {
            Interlocked.Increment(ref apmEnded);
            if (wait is null) Interlocked.Increment(ref apmMissing);
            else if (wait.SafeWaitHandle.IsClosed) Interlocked.Increment(ref apmClosed);
            if (apmExamples.Count < 16) apmExamples.Enqueue(new { direction, result_type = result.GetType().FullName, handle = value, closed_after_end = wait is null ? (bool?)null : wait.SafeWaitHandle.IsClosed });
        }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
