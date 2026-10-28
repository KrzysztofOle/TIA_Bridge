using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TiaBridge.Ipc;

// Explicit test substitute, not a vendor assembly or production execution mode.
namespace Siemens.Engineering { internal sealed class EngineeringSecurityException : Exception { } }
namespace TiaBridge.Worker.V19
{
    internal static class HmiScriptExporter
    {
        internal static V19HmiExportObservation Export(V19Target target, V19HmiExportBinding binding, string root,
            Guid id, Action<string> audit, Action<string>? phase) => throw new InvalidOperationException("OFFLINE_EXPORT_FORBIDDEN");
    }
    internal static class VendorResolver
    {
        internal static string Validate(string _, string __) => throw new InvalidOperationException("OFFLINE_LOADER_FORBIDDEN");
        internal static void Install()
        {
            TiaBridge.Diagnostics.InfrastructureEntry.NoVendor();
            AppDomain.CurrentDomain.AssemblyLoad += (_, _) => TiaBridge.Diagnostics.InfrastructureEntry.NoVendor();
        }
        internal static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    }
    internal sealed class VendorReadFailure : Exception { internal bool Quiescent => true; }
    internal static class VendorReader
    {
        internal static JsonElement Enumerate() => throw new InvalidOperationException("OFFLINE_ENUMERATION_FORBIDDEN");
        internal static void CheckProcess(Process _, V19Target __) => TiaBridge.Diagnostics.InfrastructureEntry.NoVendor();
        internal static V19Observation Inspect(V19Target target, Action<string> audit, Action<Process, V19Target> _, Action<string>? phase)
        {
            TiaBridge.Diagnostics.InfrastructureEntry.NoVendor();
            audit("Synthetic.Read"); phase?.Invoke("cycle.enter");
            if (File.Exists(Path.Combine(Path.GetDirectoryName(target.ProjectPath)!, "fail-read"))) throw new IOException("Synthetic fault");
            if (File.Exists(Path.Combine(Path.GetDirectoryName(target.ProjectPath)!, "hang-read")))
                while (!File.Exists(Path.Combine(Path.GetDirectoryName(target.ProjectPath)!, "release-read"))) Thread.Sleep(10);
            phase?.Invoke("cycle.returned"); audit("Synthetic.Returned");
            return new(target.ProjectKey, Guid.NewGuid(), target.Pid, target.StartTicks, target.ProjectPath, "synthetic", false, false,
                "synthetic", [], [], 0, 0, 0, 0, Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.GetApartmentState().ToString(), [], true);
        }
    }
}
