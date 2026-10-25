using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;

namespace TiaBridge.Host.Api;

// Composition seam for the separately built test probe. Never selectable by HTTP, config or production argv.
internal sealed record HostRuntimeHooks(IStorageFaults Faults,
    Func<IExecutionProfile, IExecutionProfile>? Profile = null,
    Func<IWorkerSupervisor, IWorkerSupervisor>? Supervisor = null,
    Action? Ready = null);
