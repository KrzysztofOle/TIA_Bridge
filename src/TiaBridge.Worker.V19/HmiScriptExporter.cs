using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Scripts;
using TiaBridge.Ipc;

namespace TiaBridge.Worker.V19;

/// <summary>Called only inside VendorReader's existing owner/Attach/identity/Dispose lifecycle.</summary>
#if !NETFRAMEWORK
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
internal static class HmiScriptExporter
{
    internal static V19HmiExportObservation Export(V19Target target, V19HmiExportBinding binding, string artifactRoot,
        Guid operationId, Action<string> audit, Action<string>? phase)
    {
        using var own = System.Diagnostics.Process.GetCurrentProcess();
        if (!V19HmiExportPolicy.Valid(binding, DateTimeOffset.UtcNow) || binding.ProjectKey != target.ProjectKey
            || binding.WorkerBuild != "1.0+" + VendorResolver.Hash(own.MainModule!.FileName)
            || binding.TiaBuild != System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(TiaPortal).Assembly.Location).FileVersion)
            throw new InvalidOperationException("UNSUPPORTED");
        V19HmiNativeFile[]? native = null;
        var observation = VendorReader.Inspect(target, audit, VendorReader.CheckProcess, phase, project =>
        {
            native = ExportAttached(project, binding, artifactRoot, operationId, audit);
        });
        if (observation.IsModifiedBefore || observation.IsModifiedAfter || native is null) throw new InvalidOperationException("IDENTITY_MISMATCH");
        return new(observation, binding.AuthorizationId, binding.ScopeToken, binding.ModuleName, "UNAVAILABLE", native);
    }

    internal static V19HmiNativeFile[] ExportAttached(Project project, V19HmiExportBinding binding, string artifactRoot,
        Guid operationId, Action<string> audit)
    {
        // Paths/names are exact observed locators; they are never promoted to stable Siemens identifiers.
        int devices = 0;
        var matches = new List<Device>();
        foreach (var device in project.Devices)
        {
            if (++devices > V19DiscoveryProfile.MaxDevices) throw new InvalidOperationException("LIMIT_EXCEEDED");
            if (device.Name == binding.DevicePath[0]) matches.Add(device);
        }
        if (matches.Count != 1) throw new InvalidOperationException("IDENTITY_MISMATCH");
        IEnumerable<DeviceItem> items = matches[0].DeviceItems;
        DeviceItem? selected = null;
        int nodes = 0;
        for (int depth = 1; depth < binding.DevicePath.Length; depth++)
        {
            var found = new List<DeviceItem>();
            foreach (var item in items)
            {
                if (++nodes > V19DiscoveryProfile.MaxNodes) throw new InvalidOperationException("LIMIT_EXCEEDED");
                if (item.Name == binding.DevicePath[depth]) found.Add(item);
            }
            if (found.Count != 1) throw new InvalidOperationException("IDENTITY_MISMATCH");
            selected = found[0];
            if (depth + 1 < binding.DevicePath.Length) items = selected.DeviceItems;
        }
        if (selected?.GetService<SoftwareContainer>()?.Software is not HmiSoftware software) throw new InvalidOperationException("UNSUPPORTED");
        audit("HmiSoftware.Scripts.ReadBounded");
        HmiScriptModuleComposition modules = software.Scripts;
        HmiScriptModule? module = null; int count = 0;
        foreach (var candidate in modules)
        {
            if (++count > 256) throw new InvalidOperationException("LIMIT_EXCEEDED");
            if (candidate.Name != binding.ModuleName) continue;
            if (module is not null) throw new InvalidOperationException("IDENTITY_MISMATCH");
            module = candidate;
        }
        if (module is null) throw new InvalidOperationException("UNSUPPORTED");
        if (!V19HmiExportPolicy.Valid(binding, DateTimeOffset.UtcNow) || project.IsModified) throw new InvalidOperationException("IDENTITY_MISMATCH");
        string directory = Path.Combine(artifactRoot, V19HmiExportPolicy.DirectoryName(operationId), "native");
        CheckChain(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("UNSUPPORTED");
        Directory.CreateDirectory(directory);
        using (var claim = new FileStream(Path.Combine(Path.GetDirectoryName(directory)!, ".claim"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) claim.Flush(true);
        audit("HmiScriptModule.Export.Enter");
        // The exact typed overload is verified against installed V19 DLLs at build time.
        // Preserve the entire actual directory, not just the returned list (which is not assumed exhaustive).
        var returned = module.Export(new DirectoryInfo(directory));
        int returnedCount = 0;
        foreach (var item in returned)
        {
            if (++returnedCount > 32 || item is null
                || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(item.FullName)), directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LIMIT_EXCEEDED");
        }
        audit("HmiScriptModule.Export.Returned");
        if (Directory.GetDirectories(directory).Length != 0) throw new InvalidOperationException("UNSUPPORTED");
        var files = Directory.GetFiles(directory).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > 32) throw new InvalidOperationException("LIMIT_EXCEEDED");
        long total = 0;
        var result = new List<V19HmiNativeFile>();
        foreach (var path in files)
        {
            CheckChain(path);
            long size = new FileInfo(path).Length; total += size;
            if (total > 16 * 1024 * 1024) throw new InvalidOperationException("LIMIT_EXCEEDED");
            result.Add(new(Path.GetFileName(path), VendorResolver.Hash(path), size));
        }
        return result.ToArray();
    }
    private static void CheckChain(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("UNSUPPORTED");
    }
}
