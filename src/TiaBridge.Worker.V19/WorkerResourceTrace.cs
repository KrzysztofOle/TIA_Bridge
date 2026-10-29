using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TiaBridge.Worker.V19;

/// <summary>Opt-in Windows/CLR observations only. Never calls Siemens or forces GC.</summary>
internal sealed class WorkerResourceTrace
{
    internal const string EnvironmentSwitch = "TIA_BRIDGE_V19_RESOURCE_TRACE";
    internal const int MaximumSamples = 4096;
    internal const int MaximumBytes = 1024 * 1024;
    private static readonly HashSet<string> Phases = new(StringComparer.Ordinal)
    {
        "worker.start", "worker.idle", "cycle.enter", "enumeration.returned", "attach.enter", "attach.returned",
        "project.checked", "devices.enter", "devices.returned", "software.returned.null", "software.returned.present",
        "device_items.traversed", "device.classified", "inventory.returned", "dispose.enter",
        "dispose.returned", "cycle.returned"
    };
    private readonly object gate = new();
    private readonly string path;
    private readonly Guid epoch;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly int pid;
    private readonly long startTicks;
    private int sequence;

    private WorkerResourceTrace(string root, Guid epoch)
    {
        string full = Path.GetFullPath(root);
        string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA_Bridge2") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(app, StringComparison.OrdinalIgnoreCase) || epoch == Guid.Empty) throw new IOException("RESOURCE_TRACE_PATH_DENIED");
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS") throw new IOException("RESOURCE_TRACE_NTFS_REQUIRED");
        CheckChain(full); this.epoch = epoch;
        path = Path.Combine(full, "v19-resources-" + epoch.ToString("N") + ".jsonl");
        using (var own = Process.GetCurrentProcess()) { pid = own.Id; startTicks = own.StartTime.ToUniversalTime().Ticks; }
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) file.Flush(true);
        Record(null, "worker.start");
    }

    internal static WorkerResourceTrace? Open(string root, Guid epoch, string? setting)
    {
        if (string.IsNullOrEmpty(setting)) return null;
        if (setting != "1") throw new ArgumentException("RESOURCE_TRACE_SWITCH_DENIED");
        return new(root, epoch);
    }

    internal void Record(Guid? operation, string phase)
    {
        if (!Phases.Contains(phase) || operation == Guid.Empty) throw new ArgumentException("RESOURCE_TRACE_PHASE_DENIED");
        lock (gate)
        {
            if (sequence >= MaximumSamples) throw new IOException("RESOURCE_TRACE_SAMPLE_LIMIT");
            int handles, threads; long privateBytes, workingSet;
            using (var own = Process.GetCurrentProcess())
            {
                own.Refresh(); handles = own.HandleCount; privateBytes = own.PrivateMemorySize64; workingSet = own.WorkingSet64;
                var wrappers = own.Threads;
                try { threads = wrappers.Count; }
                finally { foreach (ProcessThread thread in wrappers) thread.Dispose(); }
            }
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                schema_version = "v19-resource-trace/1",
                worker_epoch = epoch,
                worker_pid = pid,
                worker_start_ticks = startTicks,
                operation_id = operation,
                sequence = sequence + 1,
                phase,
                utc = DateTimeOffset.UtcNow,
                elapsed_ticks = clock.ElapsedTicks,
                stopwatch_frequency = Stopwatch.Frequency,
                handles,
                private_bytes = privateBytes,
                working_set_bytes = workingSet,
                threads,
                managed_bytes = GC.GetTotalMemory(false),
                gc0 = GC.CollectionCount(0),
                gc1 = GC.CollectionCount(1),
                gc2 = GC.CollectionCount(2)
            }, TiaBridge.Ipc.Protocol.Json) + "\n");
            CheckChain(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (file.Length + bytes.Length > MaximumBytes) throw new IOException("RESOURCE_TRACE_BYTE_LIMIT");
            file.Position = file.Length; file.Write(bytes, 0, bytes.Length); file.Flush(true); sequence++;
        }
    }

    private static void CheckChain(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("RESOURCE_TRACE_REPARSE_DENIED");
    }
}
