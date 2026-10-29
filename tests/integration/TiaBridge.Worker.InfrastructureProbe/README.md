# Offline InfrastructureProbe

Test-only net48/x64 executable with a separate filename and no Siemens reference,
DLL or API. Links the actual Worker Program.cs and resource trace. VendorSubstitute
replaces only the vendor boundary with synthetic DTOs. InfrastructureEntry invokes
Main with empty arguments and rejects loaded Siemens assemblies. The normal Host
cannot select this executable as a V19 worker.

The worker lane preserves the actual STA owner, VendorGate, heartbeat at 2 seconds,
IPC 1.1, queues, audit, dictionaries and cleanup. Heartbeat/no-heartbeat are isolated
test transport controls, not production switches. Baseline, sampler and audit modes
isolate the process and repeated wrapper/stream operations. Every lane includes
the same 1-second sampler; its overhead remains in the baseline and measurements.

PSS captures metadata of self only, without object names, VA clone, thread capture,
driver, debug attach or privilege changes. Type metadata can be partial. Remote
PSS is forbidden after the preserved native smoke fault. Never pass a TIA PID.

Build Release TiaBridge.slnx, then run
[investigate_worker_handles.py](../../../scripts/investigate_worker_handles.py).
Requires Python 3.12 standard library and installed .NET SDK/Framework. No packages
or tools are installed by the runner. Optional --smoke checks plumbing only.
Raw data remains outside Git under per-user TIA_Bridge2/dev/e2-b1-i. This probe
and its synthetic PASS do not qualify the actual Siemens Worker.

`--follow-up` runs fixed poll-only/send-only/timer-only controls (30 s / one
dispatch / 120 s), then a synthetic worker (30 s / 20 dispatches / 120 s) with
an explicitly separate controlled GC and 30 s observation afterward. Ordinary
A–E never force GC. `--flush-control` runs one isolated sender with inline pipe
flush to test inherited async flush scheduling; this is not a production change.
Each invocation creates a new immutable run; no failed scenario is retried.
