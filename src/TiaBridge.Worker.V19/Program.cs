using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TiaBridge.Ipc;

namespace TiaBridge.Worker.V19;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (!Environment.Is64BitProcess) throw new InvalidOperationException("X64_REQUIRED");
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { net48_x64 = Environment.Is64BitProcess, vendor_loaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Siemens.", StringComparison.Ordinal)) })); return 0;
            }
            VendorResolver.Install();
            if (args.SequenceEqual(new[] { "--loader-check" }))
            {
                var assembly = System.Reflection.Assembly.Load("Siemens.Engineering, Version=19.0.0.0, Culture=neutral, PublicKeyToken=d29ec89bac048f84");
                _ = VendorResolver.Validate(assembly.Location, "Siemens.Engineering");
                Console.WriteLine(JsonSerializer.Serialize(new { identity = assembly.FullName, location = assembly.Location, sha256 = VendorResolver.Hash(assembly.Location), x64 = Environment.Is64BitProcess })); return 0;
            }
            if (args.SequenceEqual(new[] { "--enumerate" }))
            {
                using var gate = VendorGate.Enter();
                Console.WriteLine(VendorReader.Enumerate().GetRawText()); return 0;
            }
            if (args.Length != 0) throw new ArgumentException("Unknown worker arguments.");
            return Run().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // Never print bootstrap credentials, vendor exception messages, project content or stack traces.
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error_type = e.GetType().FullName, hresult = e.HResult })); return 74;
        }
    }
    private static async Task<int> Run()
    {
        var bytes = new List<byte>(); using (var stdin = Console.OpenStandardInput())
        { int value; while ((value = stdin.ReadByte()) != -1 && value != 10) { if (bytes.Count >= 16384) throw new ProtocolException(); bytes.Add((byte)value); } }
        Protocol.CheckJson(bytes.ToArray());
        var setup = JsonSerializer.Deserialize<V19Bootstrap>(bytes.ToArray(), Protocol.Json) ?? throw new ProtocolException();
        var bootstrap = setup.Transport;
        ValidateSettings(setup.Settings);
        using var host = Process.GetProcessById(bootstrap.HostPid);
        if (host.StartTime.ToUniversalTime().Ticks != bootstrap.HostStartTicks) throw new ProtocolException();
        // net48 lacks CurrentUserOnly on the client. The server enforces its SID ACL;
        // PipePeer additionally checks exact server PID and token SID on this runtime.
        using var pipe = new NamedPipeClientStream(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource();
        await pipe.ConnectAsync(10000, stop.Token).ConfigureAwait(false); PipePeer.Verify(pipe, bootstrap.HostPid, false);
        await using var framed = new FramedConnection(pipe, Convert.FromBase64String(bootstrap.Secret), bootstrap.WorkerEpoch, "1.1");
        using var own = Process.GetCurrentProcess();
        await framed.SendAsync(MessageKind.Hello, null, 0, Protocol.Body(new Hello(own.Id, own.StartTime.ToUniversalTime().Ticks, bootstrap.Nonce, "tia-bridge.v19.read-only", "1.0+" + VendorResolver.Hash(own.MainModule!.FileName))), stop.Token).ConfigureAwait(false);
        if ((await framed.ReadAsync(stop.Token).ConfigureAwait(false)).Kind != MessageKind.Ready) throw new ProtocolException();
        var work = new BlockingCollection<Action>(1);
        var owner = new Thread(() => { foreach (var action in work.GetConsumingEnumerable()) action(); }) { IsBackground = false, Name = "Siemens owner" };
        owner.SetApartmentState(setup.Settings.Apartment == "STA" ? ApartmentState.STA : ApartmentState.MTA); owner.Start();
        int busy = 0;
        var completed = new ConcurrentDictionary<Guid, WorkerReply>();
        var commands = new ConcurrentDictionary<Guid, (InspectDispatch Command, long Fence)>();
        var cancelled = new ConcurrentDictionary<Guid, bool>();
        Task execution = Task.CompletedTask;
        async Task Heartbeat()
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(2000, stop.Token).ConfigureAwait(false);
                host.Refresh(); if (host.HasExited) { pipe.Dispose(); stop.Cancel(); return; }
                await framed.SendAsync(MessageKind.Heartbeat, null, 0, Protocol.Body(new HeartbeatBody(false)), stop.Token).ConfigureAwait(false);
            }
        }
        Task heartbeat = Heartbeat();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var message = await framed.ReadAsync(stop.Token).ConfigureAwait(false);
                if (message.Kind == MessageKind.Stop) break;
                if (message.Kind == MessageKind.Cancel && message.OperationId is Guid cancel) { cancelled[cancel] = true; continue; }
                if (message.Kind != MessageKind.Dispatch || message.OperationId is not Guid id) throw new ProtocolException();
                var command = Protocol.Read<InspectDispatch>(message.Body);
                if (commands.TryGetValue(id, out var prior))
                {
                    if (prior.Command != command || prior.Fence != message.Fence) throw new ProtocolException();
                    if (completed.TryGetValue(id, out var previous)) await framed.SendAsync(MessageKind.Receipt, id, message.Fence, Protocol.Body(previous), stop.Token).ConfigureAwait(false);
                    continue;
                }
                if (commands.Count >= 1000 || Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new ProtocolException();
                commands[id] = (command, message.Fence);
                var done = new TaskCompletionSource<Receipt>(TaskCreationOptions.RunContinuationsAsynchronously);
                work.Add(() =>
                {
                    Receipt receipt;
                    string auditPath = Path.Combine(bootstrap.ArtifactRoot, "v19-audit-" + bootstrap.WorkerEpoch.ToString("N") + ".jsonl");
                    void Audit(string call)
                    {
                        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = id, worker_epoch = bootstrap.WorkerEpoch, at = DateTimeOffset.UtcNow, call, thread_id = Thread.CurrentThread.ManagedThreadId }) + "\n");
                        using var audit = new FileStream(auditPath, FileMode.Append, FileAccess.Write, FileShare.Read); audit.Write(line, 0, line.Length); audit.Flush(true);
                    }
                    try
                    {
                        byte[] request = Protocol.ReadArtifact(bootstrap.ArtifactRoot, command.Request, 1024 * 1024); Protocol.CheckJson(request);
                        using var doc = JsonDocument.Parse(request);
                        if (doc.RootElement.GetProperty("schema_version").GetString() != "1.0" || doc.RootElement.GetProperty("method").GetString() != "project.inspect") throw new InvalidOperationException("UNSUPPORTED");
                        string key = doc.RootElement.GetProperty("project_key").GetString()!;
                        var target = setup.Settings.Targets.SingleOrDefault(t => Guid.Parse(t.ProjectKey) == Guid.Parse(key)) ?? throw new InvalidOperationException("IDENTITY_MISMATCH");
                        if (cancelled.ContainsKey(id)) receipt = new Receipt("Cancelled", null, null, 0, true);
                        else
                        {
                            framed.SendAsync(MessageKind.Started, id, message.Fence, Protocol.Body(new StartedBody(1)), stop.Token).GetAwaiter().GetResult();
                            using var gate = VendorGate.Enter();
                            var observation = VendorReader.Inspect(target, Audit);
                            string report = JsonSerializer.Serialize(observation, Protocol.Json);
                            if (System.Text.Encoding.UTF8.GetByteCount(report) > Protocol.MaxReceiptBytes) throw new InvalidOperationException("LIMIT_EXCEEDED");
                            receipt = new Receipt("Succeeded", report, null, 0, true);
                        }
                    }
                    catch (Exception e)
                    {
                        var cause = e.InnerException ?? e;
                        string code = cause is Siemens.Engineering.EngineeringSecurityException ? "ATTACH_DENIED" : cause.Message is "IDENTITY_MISMATCH" or "UNSUPPORTED" or "LIMIT_EXCEEDED" ? cause.Message : "TIA_UNAVAILABLE";
                        Audit("ReadFailed:" + cause.GetType().Name + ":" + code);
                        receipt = new Receipt("Failed", null, code, 0, e is not VendorReadFailure failure || failure.Quiescent);
                    }
                    done.TrySetResult(receipt);
                });
                execution = Task.Run(async () =>
                {
                    var receipt = await done.Task.ConfigureAwait(false);
                    var reply = new WorkerReply(1, command.InputHash, null, receipt); completed[id] = reply; Interlocked.Exchange(ref busy, 0);
                    try { await framed.SendAsync(MessageKind.Receipt, id, message.Fence, Protocol.Body(reply), stop.Token).ConfigureAwait(false); }
                    catch (Exception e) when (e is IOException or OperationCanceledException) { stop.Cancel(); pipe.Dispose(); }
                });
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            stop.Cancel(); work.CompleteAdding();
            // Parent loss does not abort a vendor call. This owned process stays alive until the owner returns/disposes.
            owner.Join();
            try { await execution.ConfigureAwait(false); await heartbeat.ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            work.Dispose();
        }
        return 0;
    }
    internal static void ValidateSettings(V19Settings settings)
    {
        if (settings is null || settings.Targets is null || settings.Targets.Length is < 1 or > 2 || settings.Apartment is not ("STA" or "MTA")
            || settings.Targets.Select(t => t.ProjectKey).Distinct(StringComparer.Ordinal).Count() != settings.Targets.Length) throw new ProtocolException();
        string root = Path.GetFullPath(settings.ProjectRoot).TrimEnd('\\') + "\\";
        if (!Path.IsPathRooted(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) throw new ProtocolException();
        foreach (var t in settings.Targets)
        {
            if (!Guid.TryParseExact(t.ProjectKey, "D", out var key) || key == Guid.Empty || t.Pid <= 0 || t.StartTicks <= 0
                || !Path.GetFullPath(t.ProjectPath).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !t.ProjectPath.EndsWith(".ap19", StringComparison.OrdinalIgnoreCase)) throw new ProtocolException();
            for (string? path = t.ProjectPath; path is not null; path = Path.GetDirectoryName(path))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ProtocolException();
        }
    }
}

internal sealed class VendorGate : IDisposable
{
    private readonly Mutex mutex;
    private VendorGate(Mutex mutex) { this.mutex = mutex; }
    internal static VendorGate Enter()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var mutex = new Mutex(false, "Local\\TIA_Bridge2_V19_" + identity.User!.Value);
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { mutex.ReleaseMutex(); mutex.Dispose(); throw new InvalidOperationException("NOT_OWNER"); }
        if (!acquired) { mutex.Dispose(); throw new InvalidOperationException("NOT_OWNER"); }
        return new VendorGate(mutex);
    }
    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}
