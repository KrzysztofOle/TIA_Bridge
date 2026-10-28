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

#if !NETFRAMEWORK
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
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
        => Inspect(target, audit, CheckProcess, null);

    // Production supplies CheckProcess; offline tests compile this lifecycle against
    // synthetic API types. No test seam is exposed by IPC, Host or the public API.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static V19Observation Inspect(V19Target target, Action<string> audit,
        Action<Process, V19Target> checkProcess, Action<string>? resourcePhase, Action<Project>? readProject = null)
    {
        int owner = Thread.CurrentThread.ManagedThreadId;
        var callbacks = new ConcurrentQueue<int>();
        var timer = Stopwatch.StartNew();
        using var process = Process.GetProcessById(target.Pid);
        bool quiescent = true;
        try
        {
            resourcePhase?.Invoke("cycle.enter");
            checkProcess(process, target);
            audit("TiaPortal.GetProcesses");
            var matches = TiaPortal.GetProcesses().Where(p => p.Id == target.Pid).ToArray();
            if (matches.Length != 1 || !SamePath(matches[0].ProjectPath?.FullName, target.ProjectPath)) throw new InvalidOperationException("IDENTITY_MISMATCH");
            resourcePhase?.Invoke("enumeration.returned");
            double enumeration = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); audit("TiaPortalProcess.Attach");
            TiaPortal? attached = null;
            V19Observation? observation = null;
            double disposeMs = 0;
            try
            {
                resourcePhase?.Invoke("attach.enter");
                attached = matches[0].Attach(); quiescent = false; double attach = timer.Elapsed.TotalMilliseconds;
                resourcePhase?.Invoke("attach.returned");
                attached.Disposed += (_, _) => { if (callbacks.Count < 64) callbacks.Enqueue(Thread.CurrentThread.ManagedThreadId); };
                timer.Restart(); audit("TiaPortal.Projects.Read");
                checkProcess(process, target);
                if (attached.Projects.Count != 1) throw new InvalidOperationException("IDENTITY_MISMATCH");
                var project = attached.Projects[0];
                if (!SamePath(project.Path?.FullName, target.ProjectPath) || !SamePath(attached.GetCurrentProcess().ProjectPath?.FullName, target.ProjectPath)) throw new InvalidOperationException("IDENTITY_MISMATCH");
                bool modified = project.IsModified;
                resourcePhase?.Invoke("project.checked");
                var devices = new List<V19Device>(); var nodes = new List<V19Node>();
                audit("Project.Devices.ReadBounded");
                // Preserve a raw dirty-state observation and Dispose without continuing inventory.
                if (!modified)
                {
                    resourcePhase?.Invoke("devices.enter");
                    var projectDevices = project.Devices;
                    resourcePhase?.Invoke("devices.returned");
                    foreach (var device in projectDevices)
                    {
                        if (devices.Count >= V19DiscoveryProfile.MaxDevices) throw new InvalidOperationException("LIMIT_EXCEEDED");
                        if (nodes.Count >= V19DiscoveryProfile.MaxNodes) throw new InvalidOperationException("LIMIT_EXCEEDED");
                        int deviceIndex = devices.Count;
                        int rootIndex = nodes.Count;
                        var softwareTypes = new List<string?>();
                        nodes.Add(new V19Node(V19NodeLocatorCodec.Encode(new(rootIndex, null, deviceIndex, device.Name,
                            device.GetType().FullName ?? "UNKNOWN", "NOT_APPLICABLE", "UNKNOWN", "UNAVAILABLE")), "unknown"));
                        void Walk(DeviceItem item, int parentIndex, int depth)
                        {
                            if (depth > V19DiscoveryProfile.MaxDepth || nodes.Count >= V19DiscoveryProfile.MaxNodes) throw new InvalidOperationException("LIMIT_EXCEEDED");
                            var software = item.GetService<SoftwareContainer>()?.Software;
                            resourcePhase?.Invoke(software is null ? "software.returned.null" : "software.returned.present");
                            string? type = software?.GetType().FullName;
                            softwareTypes.Add(type);
                            int index = nodes.Count;
                            nodes.Add(new V19Node(V19NodeLocatorCodec.Encode(new(index, parentIndex, deviceIndex, item.Name,
                                item.GetType().FullName ?? "UNKNOWN", software is null ? "UNKNOWN" : "READ",
                                InventoryFamily.HmiKind(type), "UNAVAILABLE")), type ?? "unknown"));
                            foreach (var child in item.DeviceItems) Walk(child, index, depth + 1);
                        }
                        foreach (var item in device.DeviceItems) Walk(item, rootIndex, 0);
                        resourcePhase?.Invoke("device_items.traversed");
                        string detailedFamily = InventoryFamily.ClassifyDetailed(softwareTypes);
                        resourcePhase?.Invoke("device.classified");
                        string family = detailedFamily == "PLC" ? "PLC" : detailedFamily.StartsWith("HMI_", StringComparison.Ordinal) ? "HMI" : "UNKNOWN";
                        devices.Add(new V19Device(device.Name, family));
                    }
                }
                resourcePhase?.Invoke("inventory.returned");
                if (!modified) readProject?.Invoke(project);
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
                    try { audit("TiaPortal.DisposeAttachedSession"); resourcePhase?.Invoke("dispose.enter"); }
                    finally { attached.Dispose(); quiescent = true; disposeMs = timer.Elapsed.TotalMilliseconds; }
                    audit("TiaPortal.DisposeAttachedSession.Returned");
                    resourcePhase?.Invoke("dispose.returned");
                }
            }
            checkProcess(process, target);
            resourcePhase?.Invoke("cycle.returned");
            return observation! with { DisposeMs = disposeMs, CallbackThreads = callbacks.ToArray(), PortalAliveAfterDispose = true };
        }
        catch (Exception e) { throw new VendorReadFailure(e, quiescent); }
    }
    private static bool SamePath(string? actual, string expected) => actual is not null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
    internal static void CheckProcess(Process process, V19Target target)
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
