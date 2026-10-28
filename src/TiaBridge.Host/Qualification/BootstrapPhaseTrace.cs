using System.Text;
using System.Text.Json;
using TiaBridge.Host.Operations;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Qualification;

/// <summary>Diagnostic evidence validation only; never permission or a qualification verdict.</summary>
internal static class BootstrapPhaseTrace
{
    internal static void Verify(byte[] bytes, Guid epoch, IReadOnlyList<BootstrapCycleRecord> cycles)
    {
        if (bytes.Length is 0 or > 1024 * 1024) throw new IOException("PHASE_TRACE_SIZE");
        var entries = new List<JsonDocument>();
        try
        {
            foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entries.Count >= 4096) throw new IOException("PHASE_TRACE_COUNT");
                Protocol.CheckJson(Encoding.UTF8.GetBytes(line)); entries.Add(JsonDocument.Parse(line));
            }
            if (entries.Count == 0 || entries[0].RootElement.GetProperty("phase").GetString() != "worker.start") throw new IOException("PHASE_TRACE_START");
            long previousTicks = -1, frequency = 0; int[] gc = [0, 0, 0];
            var binding = cycles.First();
            for (int n = 0; n < entries.Count; n++)
            {
                var e = entries[n].RootElement;
                if (e.GetProperty("schema_version").GetString() != "v19-resource-trace/1" || e.GetProperty("sequence").GetInt32() != n + 1
                    || e.GetProperty("worker_epoch").GetGuid() != epoch || e.GetProperty("worker_pid").GetInt32() != binding.WorkerPid
                    || e.GetProperty("worker_start_ticks").GetInt64() != binding.WorkerStartTicks) throw new IOException("PHASE_TRACE_BINDING");
                long ticks = e.GetProperty("elapsed_ticks").GetInt64(), currentFrequency = e.GetProperty("stopwatch_frequency").GetInt64();
                if (ticks < previousTicks || currentFrequency <= 0 || frequency != 0 && frequency != currentFrequency) throw new IOException("PHASE_TRACE_CLOCK");
                previousTicks = ticks; frequency = currentFrequency; _ = e.GetProperty("utc").GetDateTimeOffset();
                foreach (string field in new[] { "handles", "private_bytes", "working_set_bytes", "managed_bytes", "threads" })
                    if (e.GetProperty(field).GetInt64() < 0) throw new IOException("PHASE_TRACE_RESOURCE");
                for (int g = 0; g < 3; g++) { int value = e.GetProperty("gc" + g).GetInt32(); if (value < gc[g]) throw new IOException("PHASE_TRACE_GC"); gc[g] = value; }
                var id = e.GetProperty("operation_id");
                if (id.ValueKind != JsonValueKind.Null && !cycles.Any(c => c.OperationId == id.GetString())) throw new IOException("PHASE_TRACE_OPERATION");
                if (id.ValueKind == JsonValueKind.Null && e.GetProperty("phase").GetString() is not ("worker.start" or "worker.idle")) throw new IOException("PHASE_TRACE_OPERATION");
            }
            string[] ordered = ["cycle.enter", "enumeration.returned", "attach.enter", "attach.returned", "project.checked", "devices.enter", "devices.returned", "inventory.returned", "dispose.enter", "dispose.returned", "cycle.returned"];
            foreach (var cycle in cycles)
            {
                var phases = entries.Select(e => e.RootElement).Where(e => e.GetProperty("operation_id").GetString() == cycle.OperationId)
                    .Select(e => e.GetProperty("phase").GetString()).ToArray();
                int previous = -1;
                foreach (string phase in ordered)
                {
                    int index = Array.IndexOf(phases, phase);
                    if (index <= previous || phases.Count(p => p == phase) != 1) throw new IOException("PHASE_TRACE_ORDER");
                    previous = index;
                }
                int begin = Array.IndexOf(phases, "devices.returned"), end = Array.IndexOf(phases, "inventory.returned");
                for (int n = 0; n < phases.Length; n++)
                    if ((phases[n] is "device_items.traversed" or "device.classified" or "software.returned.null" or "software.returned.present") && (n <= begin || n >= end))
                        throw new IOException("PHASE_TRACE_ORDER");
                if (Array.IndexOf(phases, "worker.idle") <= Array.IndexOf(phases, "cycle.returned")) throw new IOException("PHASE_TRACE_IDLE");
                if (!phases.Contains("device_items.traversed") || !phases.Contains("device.classified")
                    || !phases.Any(p => p is "software.returned.null" or "software.returned.present") || !phases.Contains("worker.idle"))
                    throw new IOException("PHASE_TRACE_COVERAGE");
            }
        }
        finally { foreach (var entry in entries) entry.Dispose(); }
    }
}
