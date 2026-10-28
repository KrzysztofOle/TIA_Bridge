# QA-01 — Universal QA Runner v1

Status: implemented offline CLI, Python 3.12+ and standard library. Configuration
and repository test code are trusted inputs reviewed through Git. Scenarios are
untrusted selectors of approved tasks. The engine has no Siemens dependency.

## Quick start

From the repository root, without activating an environment:

```powershell
.\qa.ps1 doctor
.\qa.ps1 list
.\qa.ps1 run fast
.\qa.ps1 run standard
.\qa.ps1 run qualification
.\qa.ps1 run --scenario tests/qa/scenarios/worker-handles.json
.\qa.ps1 report --latest --failures-only
.\qa.ps1 report --run-id <run-id>
.\qa.ps1 run qualification --performance-profile STRICT
```

The wrapper locates this checkout's configuration even from another directory.
Python CLI: `python scripts/qa.py run fast`; Unix: `python3 qa run fast` (or make
`qa` executable and put it on PATH). CLI searches upwards for `qa.project.json`.
Explicit configuration: `python scripts/qa.py --config path/to/qa.project.json run fast`.
Missing configuration produces a short instruction. No installation, server, DB
or package auto-download is performed. The wrapper needs the Windows Python launcher
with 3.12, or `python3`. `doctor` checks available executables, Python modules,
scenario configuration and the runner lock; it grants no environment qualification.

TIA_Bridge additionally needs the SDK selected by `global.json`, Git, populated
locked NuGet cache and the existing external QA Python with
[requirements-qa.txt](../scripts/requirements-qa.txt). Its configured first candidate
is under `${LOCALAPPDATA}/TIA_Bridge2/dev/qa-b01a-20261025`; `{python}` is the fallback
only if the required modules are installed. Missing dependencies produce BLOCKED
with details. One-time preparation remains in [tests README](../tests/README.md).
Restore uses an empty local feed, locked mode and disabled NuGet audit; there is no
public-feed fallback. Test and application state policies remain unchanged.

## Components and portability

The small [package](../scripts/qa_runner/__init__.py) separates loader/project config,
adapters, sequential runner, diagnostics, evaluator and reports. `qa.py` is the CLI
entry point. Copy those two items to any trusted project's `tools/` directory,
create its configuration and scenarios, and execute `python tools/qa.py run fast`.
There are no imports from TIA_Bridge, absolute checkout paths or third-party packages
in the engine. The [independent example](../examples/qa-project/README.md) and tests
exercise a separate temporary project with a copied engine. Windows and POSIX
process paths are implemented; QA-01 execution evidence records the tested OS only.
Portability does not claim adapters for technologies not listed below.

## Profiles in TIA_Bridge

| Profile | Executed scope | Excluded or separate evidence |
|---|---|---|
| FAST | Locked restore, Release solution build, Core and runner regression | No long NF measurements or Siemens; module scenario can narrow test selection |
| STANDARD | Release build, Core, Host including IPC/E1/B0/basic resilience, contracts Python, runner tests, generators, quality audit, whitespace and diff checks | ResiliencePerformanceTests excluded; no formal D-11 or Siemens |
| QUALIFICATION | Prepare empty QA workspace roots, then unchanged canonical validate_offline.py, including preflight, complete RT/NF suite, original manifests and receipt | Full qualification may be BLOCKED; NF-11 and Engineering stay separate |

The integration explicitly selects `SHARED_WORKSTATION 1.0.0` by default; its
definition SHA-256 is retained. `--performance-profile STRICT` remains available.
The existing canonical runner's default STRICT, definitions and safety timeouts
are unchanged. This tool's profile name is not the qualification verdict.
NF-11 is never launched implicitly. No QA command starts Siemens Attach, B0
activation, enrollment, Save/Compile/Download, PLC/HMI control or Runtime.
Building a worker binary is separate from executing it.

## JSON v1 scenario contract

Only UTF-8 JSON, optional BOM, `schema_version: "1.0"`. The authoritative validator
is [loader.py](../scripts/qa_runner/loader.py); unknown fields are rejected.
Input limit 1 MiB, nesting depth 20, no duplicate keys or NaN/Infinity, at most
100 steps per scenario. IDs are 1–80 ASCII letters/digits/dot/underscore/hyphen,
starting with a letter or digit; Windows reserved device names are rejected on
all platforms. Step IDs and scenario IDs in a profile are unique without regard to case.
Timeouts are finite numbers greater than zero and at most 86400 seconds.

```json
{
  "schema_version": "1.0",
  "id": "module-regression",
  "description": "Approved module regression",
  "tags": ["offline", "regression"],
  "mode": "offline",
  "timeout_seconds": 600,
  "requires": ["dotnet"],
  "on_failure": "stop",
  "steps": [
    {"id": "core", "type": "dotnet_test", "task": "core", "timeout_seconds": 600, "required": true}
  ]
}
```

All shown fields are required. `requires` lists configured runtime names.
`on_failure` is `stop` or `continue`; stop leaves subsequent steps/scenarios
NOT_RUN. Every scenario needs at least one required step. `mode` accepts only
`offline` in v1. A step references the exact task and adapter approved in project
configuration; it cannot supply argv, paths, environment, outputs or success codes.
Its timeout may shorten the approved task maximum; the enclosing scenario deadline
also applies. Task parameters, expected success, output artifacts and environment
requirements form part of the referenced configuration, whose snapshot/hash is
stored alongside the scenario. This indirection is intentional: adding a scenario
does not authorize new commands. Scenarios must reside within `scenario_root`.

## Project configuration contract

[qa.project.json](../qa.project.json) is the complete TIA_Bridge example. Required:
`schema_version`, `project_id`, `default_profile`, `scenario_root`, `results_root`,
`runtimes`, `tasks`, `profiles`. Optional: `performance_profiles` with `default`,
`allowed`, `definition`, and `environment` with `clear_prefixes`/`values` for
project-specific inherited-variable policy. Paths use forward slashes, are relative to the config
directory, and reject traversal, rooted paths, Windows ADS and escapes through
symlinks/junctions. Results use a dedicated ignored directory; TIA_Bridge uses
`.tia-bridge/qa-results/`, preserving its existing working-output convention.

Each runtime defines ordered `candidates`; optional `modules` selects a Python
3.12+ probe and verifies imports. `${ENVIRONMENT_VARIABLE}` expands per OS;
`{python}` means the runner interpreter. No runtime or command is selected by
the scenario. Each task requires `type`, `offline: true`, `runtime`,
`timeout_seconds`. Optional fields are:

| Field | Meaning |
|---|---|
| project, configuration, filter | .NET project/solution, default Release, optional VSTest filter |
| path, pattern | Python script or unittest directory, default test_*.py |
| args | Configuration-approved extra argv, never passed through a shell |
| expected_exit_code | If present, must be zero; negative tests assert internally |
| min_tests | Positive minimum count for test adapters, default 1 |
| env | Approved string values only; not copied into execution.json |
| collect | Approved relative file globs; collect newly created paths only |
| receipt | Pattern, status_field, layers, optional bundle/counts; described below |

Adapters: `dotnet_restore`, `dotnet_build`, `dotnet_test`, `python_test`,
`python_script`, `approved_command`. Restore/build/test have fixed CLI options;
tests use --no-build/--no-restore and new TRX directories. Python test uses unittest
discovery plus a stdlib harness writing structured counts/failures; no fragile
stdout-count inference. `python_script` and `approved_command` run reviewed
configuration argv. `{artifacts}` expands to this step's new artifact directory;
`{performance_profile}` expands only to an allowed configured performance profile.
An approved command is a trust boundary, not an OS sandbox: review configuration,
test code, runtime resolution and repository changes before executing any profile.

`receipt` imports exactly one newly created JSON result matching `pattern`.
`status_field` is its verdict; `layers` maps runner layer names to receipt fields.
`counts` maps labels to receipt objects with passed/failed/skipped counts. Optional
`diagnostics: {"path": "resilience.json", "fields": ["gate_blockers"]}` extracts
bounded explicit blocker messages from a sibling JSON report into the summary.
`bundle: true` requires an original complete SHA-256 manifest above that receipt,
matching receipt/manifest Run ID; the entire verified bundle is copied into the run.
Canonical preflight is performed by the canonical runner itself. Its original
report, RT/NF samples, applied thresholds, build/source bindings and manifests are
preserved, with no replacement of historical evidence. An exit code zero cannot
override BLOCKED/NOT_RUN in a required receipt layer.

The TIA_Bridge-only [layout helper](../scripts/qa_prepare_tia_bridge.py) creates
`.tia-bridge/resilience/workspaces` and `.tia-bridge/b02c/workspaces` when absent.
The existing preflight expects both roots even in a fresh worktree. The helper
rejects reparse/escape/collision, preserves existing contents, and creates no
state database or authorization. It is outside the reusable engine.

## Result, report and manifest contract

Every invocation creates a UTC-prefixed UUID Run ID with exclusive directory/file
creation. Runs are sequential within a project; `.runner.lock` blocks concurrent
ownership. A stale lock requires explicit owner review and manual removal after
confirming termination. It is not silently adopted or retried.

```text
<results_root>/<run-id>/
  summary.json
  execution.json
  failures.json
  report.md
  logs/runner.jsonl
  logs/<scenario>--<step>.log
  artifacts/<config and performance-profile snapshots>
  artifacts/scenarios/<scenario-id>.json
  artifacts/<scenario>--<step>/<TRX, unittest.json, collected logs, receipt-bundle>
  evidence-manifest.json
```

All reports carry schema_version 1.0 and Run ID. Summary contains project, UTC
start/end, Git SHA/CLEAN/DIRTY/UNAVAILABLE, source status, config/profile hashes,
per-step status/duration/exit/counts, aggregate counts, independent evidence layers,
grouped failures, previous compatible Run ID and new/resolved failures.
`execution.json` records scenario paths/hashes, resolved runtimes and redacted argv.
`failures.json` holds at most 100 groups, counts and step references; first relevant
stack is capped at 16000 characters and message at 2000. Full child output stays
in logs; the unittest harness limits structured failure entries to 200 per step.
Report Markdown includes stages, first failure, source/test/log, blockers, missing
steps, evidence location and next action without requiring full log inspection.

Step and run statuses are separate: PASS, FAIL, BLOCKED, NOT_RUN, SKIPPED,
REVIEW_REQUIRED. Confirmed failures dominate; required missing/skipped steps block
PASS. Optional omissions cannot hide a confirmed FAIL. Test counts never include
build/audit commands as tests. Functional execution, environment qualification,
performance, Engineering and Runtime are independent fields. Default Engineering
and Runtime are NOT_RUN. CLI exit: 0 PASS, 1 FAIL, 2 BLOCKED/NOT_RUN/REVIEW_REQUIRED
or invalid input. `report` exits zero on a valid verified report, regardless of its
test verdict. Every report read verifies its entire manifest; selected Run ID
does not read other runs. Automatic comparison selects the most recent sealed
run with the same project/profile/performance profile; input/source hashes remain
available to assess comparability. Corrupt previous runs are not used as baselines.

The final manifest lists every file except itself, with byte length and SHA-256.
It detects changes and missing/extra files, not malicious rewriting of both data
and manifest; this is not signed E1 evidence. Files are create-new and no retention
deletion is implemented. A final report appears after handled interruption;
logs stream throughout execution. Hard OS kill/power loss can leave only partial
logs and a lock: v1 does not promise transactional report recovery. Filesystem
failure can prevent sealing and is reported as CLI BLOCKED, never fabricated PASS.

## Diagnostic logging and failures

Each runner JSONL event contains `timestamp_utc`, `level`, `application`, `component`,
`event_id`, `message`, `run_id`, `scenario_id`, `step_id`, `operation_id`, `worker_pid`,
`worker_epoch`, `duration_ms`, `exception_type`, `error_code`; unavailable identity
fields are null. Levels TRACE/DEBUG/INFO/WARN/ERROR, INFO default; use
`--log-level DEBUG` or TRACE for diagnostic selection. Stable v1 events:
QA_RUN_STARTED, QA_STEP_STARTED, QA_STEP_COMPLETED, QA_RUN_COMPLETED,
QA_INPUT_REJECTED, QA_APPLICATION_LOG, QA_LOG_LIMIT, QA_COMMAND_PREPARED (DEBUG),
QA_ARTIFACT_COLLECTED (TRACE). UTC timestamps correlate
observations; monotonic time measures durations/deadlines.

Existing application logging was reviewed: Host persists operation journal/events
and intentionally drains worker stdout/stderr; Worker emits error_type/hresult,
audit and opt-in resource JSONL with operation/epoch; CLI emits contract JSON.
The collector imports approved new log files and preserves their originals.
For JSONL it wraps known correlation fields into QA_APPLICATION_LOG, preserving
source timestamps separately and leaving unavailable identity null. Correlation
is capped at 4096 entries/file; full approved source logs remain. Collection limit
is 5000 matching files/pattern and 128 MiB/file. It neither queries production
state nor enables Worker tracing. IPC 1.1 and all application logging are unchanged.
Future work: compatible Host structured diagnostic sink and explicit journal export
with existing operation/epoch IDs; separate task and review, no new IPC route.

Classification uses explicit evidence: BUILD_FAILURE for build/restore exit,
TEST_ASSERTION for structured failed tests, TEST_TIMEOUT for deadline, ENVIRONMENT
for missing runtime/interruption, PROCESS_FAILURE for nonzero approved commands,
FILESYSTEM for I/O, INTEGRITY_FAILURE for malformed/missing test artifacts,
AUTHORIZATION for rejected live/unapproved input, otherwise UNKNOWN with evidence.
RESOURCE_GROWTH is reserved for a future adapter with explicit metric verdicts;
v1 never infers it or a root cause from a log phrase. No claim is made that a
canonical qualification BLOCKED is an assertion failure.

Child stdout/stderr are streamed to redacted log files. Common token/password/
authorization/Bearer forms and URL userinfo are redacted; no environment dump is
written. Authoritative receipt bundles and approved collected files retain exact
bytes to preserve hashes. Only synthetic reviewed QA logs may be collected in v1;
the collector is not a general secret/PII scrubber. Do not configure production
logs, secrets in argv/configuration, or private project exports. Evidence stays
ignored locally and must not be committed.

## Safety and acceptance

No scenario-provided shell, executable, argv, PID, grant, ACL change or live
adapter. Inherited TIA_BRIDGE_* switches are cleared before applying approved
offline configuration. Ctrl+C/SIGTERM preserves partial results and terminates
only the runner's own offline child subtree (Windows PID-scoped taskkill /T;
POSIX new process group). It never kills by image name, enumerates a termination
target, closes Portal, or interprets timeout as Siemens quiescence. A project
configuration must never label a command that touches external systems as offline.
Live execution is unsupported in v1 and needs a separate authorized adapter using
the existing Host → Scheduler → Worker route and all existing safety checks.

Acceptance suite: `python -B -m unittest discover -s tests/qa -v` (stdlib only).
It covers CLI, malformed/hostile scenarios, actual timeout/exit/missing runtime,
SIGINT and cancellation, partial reports, unique runs, manifest tampering, logs,
failure grouping/source, previous comparison, copied cross-project execution,
receipt-layer separation, stale evidence, lock and fixed adapter argv. STANDARD
runs existing Core/Host/contracts; QUALIFICATION delegates the full canonical
suite. Actual runs and limitations are recorded separately from implementation.
