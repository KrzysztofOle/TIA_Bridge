using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaBridge.Ipc;

namespace TiaBridge.Worker.V19;

internal static class VendorReader
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static JsonElement Enumerate()
    {
        _ = VendorResolver.Validate(typeof(TiaPortal).Assembly.Location, "Siemens.Engineering");
        var entries = new List<object>();
        foreach (var portal in TiaPortal.GetProcesses())
        {
            // TiaPortalProcess.Dispose closes TIA itself. Never dispose a diagnostic process handle!
            using var process = Process.GetProcessById(portal.Id);
            entries.Add(new
            {
                pid = portal.Id,
                start_ticks = process.StartTime.ToUniversalTime().Ticks,
                executable = portal.Path?.FullName,
                project_path = portal.ProjectPath?.FullName,
                products = portal.InstalledSoftware.Select(p => new { name = p.Name, version = p.Version }).ToArray()
            });
        }
        return Protocol.Body(entries);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static V19Observation Inspect(V19Target target, Action<string> audit)
    {
        int owner = Thread.CurrentThread.ManagedThreadId;
        var callbacks = new ConcurrentQueue<int>();
        var timer = Stopwatch.StartNew();
        using var process = Process.GetProcessById(target.Pid);
        bool quiescent = true;
        try
        {
            CheckProcess(process, target);
            audit("TiaPortal.GetProcesses");
            var matches = TiaPortal.GetProcesses().Where(p => p.Id == target.Pid).ToArray();
            if (matches.Length != 1 || !SamePath(matches[0].ProjectPath?.FullName, target.ProjectPath)) throw new InvalidOperationException("IDENTITY_MISMATCH");
            double enumeration = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); audit("TiaPortalProcess.Attach");
            TiaPortal? attached = null;
            V19Observation? observation = null;
            double disposeMs = 0;
            try
            {
                attached = matches[0].Attach(); quiescent = false; double attach = timer.Elapsed.TotalMilliseconds;
                attached.Disposed += (_, _) => { if (callbacks.Count < 64) callbacks.Enqueue(Thread.CurrentThread.ManagedThreadId); };
                timer.Restart(); audit("TiaPortal.Projects.Read");
                CheckProcess(process, target);
                if (attached.Projects.Count != 1) throw new InvalidOperationException("IDENTITY_MISMATCH");
                var project = attached.Projects[0];
                if (!SamePath(project.Path?.FullName, target.ProjectPath) || !SamePath(attached.GetCurrentProcess().ProjectPath?.FullName, target.ProjectPath)) throw new InvalidOperationException("IDENTITY_MISMATCH");
                bool modified = project.IsModified;
                var devices = new List<V19Device>(); var nodes = new List<V19Node>();
                audit("Project.Devices.ReadBounded");
                foreach (var device in project.Devices)
                {
                    if (devices.Count >= 64) throw new InvalidOperationException("LIMIT_EXCEEDED");
                    var softwareTypes = new List<string>();
                    void Walk(DeviceItem item, string path, int depth)
                    {
                        if (depth > 8 || nodes.Count >= 256) throw new InvalidOperationException("LIMIT_EXCEEDED");
                        string locator = path + "/" + item.Name;
                        var software = item.GetService<SoftwareContainer>()?.Software;
                        string type = software?.GetType().FullName ?? "hardware";
                        softwareTypes.Add(type);
                        nodes.Add(new V19Node(locator, type));
                        foreach (var child in item.DeviceItems) Walk(child, locator, depth + 1);
                    }
                    foreach (var item in device.DeviceItems) Walk(item, device.Name, 0);
                    devices.Add(new V19Device(device.Name, InventoryFamily.Classify(softwareTypes)));
                }
                audit("Project.IdentityAndIsModified.ReadAfter");
                if (!SamePath(project.Path?.FullName, target.ProjectPath)) throw new InvalidOperationException("IDENTITY_MISMATCH");
                observation = new V19Observation(target.ProjectKey, Guid.NewGuid(), target.Pid, target.StartTicks, project.Path!.FullName, project.Name,
                    modified, project.IsModified, FileVersionInfo.GetVersionInfo(typeof(TiaPortal).Assembly.Location).FileVersion ?? "unknown",
                    devices.ToArray(), nodes.ToArray(), enumeration, attach, timer.Elapsed.TotalMilliseconds, 0, owner,
                    Thread.CurrentThread.GetApartmentState().ToString(), [], false);
            }
            finally
            {
                if (attached is not null)
                {
                    timer.Restart();
                    try { audit("TiaPortal.DisposeAttachedSession"); }
                    finally { attached.Dispose(); quiescent = true; disposeMs = timer.Elapsed.TotalMilliseconds; }
                    audit("TiaPortal.DisposeAttachedSession.Returned");
                }
            }
            CheckProcess(process, target);
            return observation! with { DisposeMs = disposeMs, CallbackThreads = callbacks.ToArray(), PortalAliveAfterDispose = true };
        }
        catch (Exception e) { throw new VendorReadFailure(e, quiescent); }
    }
    private static bool SamePath(string? actual, string expected) => actual is not null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
    private static void CheckProcess(Process process, V19Target target)
    {
        process.Refresh();
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != target.StartTicks
            || !string.Equals(process.MainModule?.FileName, Path.Combine(VendorResolver.Root, "Bin", "Siemens.Automation.Portal.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("IDENTITY_MISMATCH");
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new InvalidOperationException("IDENTITY_MISMATCH");
        try { using var identity = new WindowsIdentity(token); using var current = WindowsIdentity.GetCurrent(); if (identity.User != current.User) throw new InvalidOperationException("IDENTITY_MISMATCH"); }
        finally { CloseHandle(token); }
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class VendorReadFailure(Exception inner, bool quiescent) : Exception("Read-only vendor failure.", inner)
{
    internal bool Quiescent => quiescent;
}
