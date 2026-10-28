// Compile-time API doubles only. This test assembly never references or loads Siemens DLLs.
using System.Collections;
using System.Reflection;

namespace TiaBridge.Host.Tests
{
    internal sealed class SyntheticVendorScenario
    {
        internal static readonly AsyncLocal<SyntheticVendorScenario?> Current = new();
        internal string Path { get; init; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "synthetic.ap19");
        internal string? FailAt { get; set; }
        internal bool Dirty { get; set; }
        internal Action? ReadSoftware { get; set; }
        internal object? Software { get; set; }
        internal int Disposes, ActiveEnumerators;
        internal List<WeakReference> References { get; } = [];
        internal void Check(string point) { if (FailAt == point) throw new InvalidOperationException("synthetic failure"); }
        internal Siemens.Engineering.TiaPortal Attach()
        {
            Check("attach"); var portal = new Siemens.Engineering.TiaPortal(this);
            References.Add(new WeakReference(portal)); return portal;
        }
    }
    internal sealed class SyntheticCollection<T>(IEnumerable<T> values, SyntheticVendorScenario scenario) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => new Enumerator(values.GetEnumerator(), scenario);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(IEnumerator<T> inner, SyntheticVendorScenario scenario) : IEnumerator<T>
        {
            private readonly int counted = ++scenario.ActiveEnumerators;
            public T Current => inner.Current;
            object IEnumerator.Current => Current!;
            public bool MoveNext() { _ = counted; return inner.MoveNext(); }
            public void Reset() => inner.Reset();
            public void Dispose() { scenario.ActiveEnumerators--; inner.Dispose(); }
        }
    }
}
namespace Siemens.Engineering
{
    internal sealed class TiaPortalProcess(TiaBridge.Host.Tests.SyntheticVendorScenario scenario) : IDisposable
    {
        public int Id => Environment.ProcessId;
        public FileInfo ProjectPath => new(scenario.Path);
        public DirectoryInfo Path => new(System.IO.Path.GetTempPath());
        public SyntheticProduct[] InstalledSoftware => [];
        public TiaPortal Attach() => scenario.Attach();
        public void Dispose() => throw new InvalidOperationException("Synthetic Portal process must never be closed by reader");
    }
    internal sealed class TiaPortal : IDisposable
    {
        private readonly TiaBridge.Host.Tests.SyntheticVendorScenario scenario;
        internal TiaPortal(TiaBridge.Host.Tests.SyntheticVendorScenario scenario) { this.scenario = scenario; }
        public event EventHandler? Disposed;
        public static TiaPortalProcess[] GetProcesses() => [new(TiaBridge.Host.Tests.SyntheticVendorScenario.Current.Value!)];
        public List<Project> Projects { get { scenario.Check("projects"); return [new(scenario)]; } }
        public TiaPortalProcess GetCurrentProcess() => new(scenario);
        public void Dispose() { scenario.Disposes++; scenario.Check("dispose"); Disposed?.Invoke(this, EventArgs.Empty); }
    }
    internal sealed class Project(TiaBridge.Host.Tests.SyntheticVendorScenario scenario)
    {
        public FileInfo Path => new(scenario.Path);
        public string Name => "synthetic";
        public bool IsModified => scenario.Dirty;
        public IEnumerable<HW.Device> Devices
        {
            get { scenario.Check("devices"); return new TiaBridge.Host.Tests.SyntheticCollection<HW.Device>([new(scenario)], scenario); }
        }
    }
    internal sealed record SyntheticProduct(string Name, string Version);
}
namespace Siemens.Engineering.HW
{
    internal sealed class Device(TiaBridge.Host.Tests.SyntheticVendorScenario scenario)
    {
        public string Name => "synthetic-device";
        public IEnumerable<DeviceItem> DeviceItems => new TiaBridge.Host.Tests.SyntheticCollection<DeviceItem>([new(scenario)], scenario);
    }
    internal sealed class DeviceItem(TiaBridge.Host.Tests.SyntheticVendorScenario scenario)
    {
        public string Name => "synthetic-item";
        public IEnumerable<DeviceItem> DeviceItems => new TiaBridge.Host.Tests.SyntheticCollection<DeviceItem>([], scenario);
        public T? GetService<T>() where T : class
        {
            scenario.Check("service"); scenario.ReadSoftware?.Invoke();
            if (scenario.FailAt == "null-service") return null;
            return new Features.SoftwareContainer() as T;
        }
    }
}
namespace Siemens.Engineering.HW.Features
{
    internal sealed class SoftwareContainer { public object Software => TiaBridge.Host.Tests.SyntheticVendorScenario.Current.Value?.Software ?? new object(); }
}
namespace Siemens.Engineering.HmiUnified
{
    internal sealed class HmiSoftware
    {
        public Scripts.HmiScriptModuleComposition Scripts { get; } = new();
    }
}
namespace Siemens.Engineering.HmiUnified.Scripts
{
    internal sealed class HmiScriptModule(string name, Func<DirectoryInfo, IEnumerable<FileInfo>> export)
    {
        public string Name => name;
        public IEnumerable<FileInfo> Export(DirectoryInfo directory) => export(directory);
    }
    internal sealed class HmiScriptModuleComposition : List<HmiScriptModule> { }
}
namespace TiaBridge.Worker.V19
{
    internal static class VendorResolver
    {
        internal static readonly string Root = System.IO.Path.GetTempPath();
        internal static AssemblyName Validate(string path, string name) => new(name);
        internal static string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }
}
