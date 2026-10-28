using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TiaBridge.Diagnostics;

/// <summary>Test-only Windows PSS metadata of self only, never a TIA PID.
/// No VA clone, thread capture, object names, foreign CloseHandle, injection or privilege change.</summary>
internal static class PssHandleSnapshot
{
    [DllImport("kernel32.dll")] private static extern uint PssCaptureSnapshot(IntPtr process, uint flags, uint context, out IntPtr snapshot);
    [DllImport("kernel32.dll")] private static extern uint PssFreeSnapshot(IntPtr process, IntPtr snapshot);
    [DllImport("kernel32.dll")] private static extern uint PssWalkMarkerCreate(IntPtr allocator, out IntPtr marker);
    [DllImport("kernel32.dll")] private static extern uint PssWalkMarkerFree(IntPtr marker);
    [DllImport("kernel32.dll")] private static extern uint PssWalkSnapshot(IntPtr snapshot, int information, IntPtr marker, IntPtr buffer, uint length);

    // SDK processsnapshot.h x64: common prefix 88 bytes, largest union (Thread) 48 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 136)]
    private struct Entry
    {
        [FieldOffset(0)] internal IntPtr Handle;
        [FieldOffset(8)] internal uint Flags;
        [FieldOffset(12)] internal int ObjectType;
        [FieldOffset(56)] internal ushort TypeNameBytes;
        [FieldOffset(64)] internal IntPtr TypeName;
    }
    internal static object Capture(Process owned)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("x64 required");
        using var current = Process.GetCurrentProcess();
        if (owned.Id != current.Id) throw new InvalidOperationException("Only self snapshots are permitted; remote PSS unavailable in this environment");
        // HANDLES + BASIC + TYPE_SPECIFIC; no object names, memory snapshot or thread context.
        uint error = PssCaptureSnapshot(owned.Handle, 0x34, 0, out var snapshot);
        if (error != 0) return new { status = "NOT_AVAILABLE", error };
        IntPtr marker = IntPtr.Zero, buffer = IntPtr.Zero;
        try
        {
            Check(PssWalkMarkerCreate(IntPtr.Zero, out marker));
            buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Entry)));
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var handles = new List<object>();
            while (true)
            {
                error = PssWalkSnapshot(snapshot, 2, marker, buffer, 136); // PSS_WALK_HANDLES
                if (error == 259) break; // ERROR_NO_MORE_ITEMS
                Check(error);
                var e = Marshal.PtrToStructure<Entry>(buffer);
                string type = "UNAVAILABLE";
                if ((e.Flags & 1) != 0 && e.TypeName != IntPtr.Zero && e.TypeNameBytes is > 0 and <= 512 && e.TypeNameBytes % 2 == 0)
                    type = Marshal.PtrToStringUni(e.TypeName, e.TypeNameBytes / 2)!.TrimEnd('\0');
                counts[type] = counts.TryGetValue(type, out int count) ? count + 1 : 1;
                object? detail = null;
                if ((e.Flags & 8) != 0 && e.ObjectType == 2) // PSS_OBJECT_TYPE_THREAD
                    detail = new { exit_status = unchecked((uint)Marshal.ReadInt32(buffer, 88)), process_id = Marshal.ReadInt32(buffer, 104), thread_id = Marshal.ReadInt32(buffer, 108) };
                if ((e.Flags & 8) != 0 && e.ObjectType == 4) // PSS_OBJECT_TYPE_EVENT
                    detail = new { manual_reset = Marshal.ReadInt32(buffer, 88) != 0, signaled = Marshal.ReadInt32(buffer, 92) != 0 };
                handles.Add(new { value = e.Handle.ToInt64(), type, flags = e.Flags, object_type = e.ObjectType, detail });
                if (handles.Count > 32768) throw new IOException("PSS handle limit");
            }
            return new { status = counts.ContainsKey("UNAVAILABLE") ? "PARTIAL" : "PASS", counts, handles };
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            try { if (marker != IntPtr.Zero) Check(PssWalkMarkerFree(marker)); }
            finally { Check(PssFreeSnapshot(new IntPtr(-1), snapshot)); } // snapshot resides in this process
        }
    }
    private static void Check(uint error) { if (error != 0) throw new IOException("PSS error " + error); }
}
