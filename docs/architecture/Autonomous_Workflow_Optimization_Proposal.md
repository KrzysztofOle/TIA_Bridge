# Autonomous Workflow Optimization Proposal

**Status:** analysis and proposal only; no feature implementation authorized
**Audit date:** 2026-11-02
**Repositories inspected:** `X:\Projects\TIA_Bridge`, `X:\TESTY_TIA_Bridge`, `C:\RMA\t05_plc`

## 1. Executive summary

The current TIA_Bridge already provides strong controls around durable operations, workspace-manifest integrity, qualification preflights, `ModificationSession`, backups, readback, and reviewed recovery. These mechanisms protect a TIA operation or an exchange workspace. They do not currently form a generic offline validator for PLC/HMI source packages, a CODEX task-progress checkpoint, or a compact cross-tool E2E report.

The most useful next step is a **read-only, offline source preflight**, first for exact file bytes, manifest membership and SHA-256, line endings, and JS/Events contract links. It can reject known-bad input before Attach/import/compile and should not require TIA. The implementation should extend the existing validation entry points only where their contracts fit; `WorkspaceValidation` validates the current exchange-manifest model and should not be misrepresented as a PLC/HMI source validator.

For task resumption, prefer a small versioned JSON file written by the orchestrating task script, with paths/hashes to existing session receipts and evidence. Do not add Task Checkpoint state to `ModificationSession`, SQLite operation history, or the TIA project. A task checkpoint is a progress index, never a TIA state assertion or permission to repeat a mutation.

For E2E and diagnostics, extend `C:\RMA\t05_plc\tools\RecipeTransferLabV19.ps1` and its existing artifact directory to emit one concise summary and filtered diagnostic index. T05 owns recipe/schema/PLC semantics; TIA_Bridge can define generic operation-result and diagnostic-reference envelopes; LabExporter should continue owning reports for its own import/export/readback work.

These changes do not require a TIA change. The stage order recommended is **1) offline preflight, 2) task checkpoint, 3) E2E summary/diagnostic index**. The expected largest reduction in TIA time and repeat work comes from preflight, while checkpointing most directly avoids re-reading context and rerunning already accepted independent steps. No credible token or Computer Use savings percentage can be derived from the evidence reviewed.

## 2. Scope, evidence and instruction hierarchy

This is a read-only audit of source, documentation and existing reports. The supplied T05 audit and E2E report were used rather than replaying the recipe-transfer history. No TIA Portal, Runtime, PLCSIM, E2E, import, synchronization, or Computer Use operation was performed. No tests were run for this proposal.

Instruction hierarchy in TIA_Bridge is explicitly: current user decision subject to safety, `AGENTS.md`, accepted ADR/contracts, detailed specifications, then backlog/notes. `Instrukcja_ChatGPT.md` is complementary: it governs analysis/design/review; `AGENTS.md` governs execution. Architecture or contract conflicts must be reported, not silently chosen by document precedence.

T05 has a separate root `AGENTS.md` and `siemens/AGENTS.md`; narrower HMI instructions apply below them. These instructions govern the T05 workspace and do not inherit from TIA_Bridge. The T05 root permits certain already-supported, explicitly scoped test-project operations, but the HMI_RT_1 local rule retains manual JS-to-TIA synchronization. Therefore no general statement that “T05 automation may import JS” is safe; the applicable local rule and exact task scope govern.

No direct normative conflict was found that blocks this proposal. The repositories do differ in implementation and operational boundaries: TIA_Bridge is a new generic architecture with read-only Worker V19/bounded Discovery status; T05's Python/PowerShell Bridge has project-specific modification workflows; LabExporter is a separate laboratory executable with its own HMI script contract. These are separate capabilities and grants, not a shared authorization chain.

## 3. Current architecture and tool map

### 3.1 TIA_Bridge repository

The current repository describes Core, Host/API/CLI, durable Fake execution, and an experimental V19 read-only worker for X-01. The architecture explicitly says the Host receives versioned DTOs and Siemens proxy/vendor objects stay in `TiaBridge.Worker.V19`. The current production boundary is bounded Discovery/Inventory; the repository instructions state that general object export, synchronization, and mutation adapters are not implemented.

| Concern | Existing location | Verified responsibility and limit |
|---|---|---|
| Core workspace model | `src/TiaBridge.Core/Workspace/WorkspaceModels.cs`, `WorkspaceValidation.cs`, `WorkspacePaths.cs` | Validates manifest graph, identity, coverage, paths, exchange-unit membership and unit hashes. This is a workspace contract validator, not PLC source/JS project validation. |
| Artifact JSON | `src/TiaBridge.Core/Workspace/ArtifactJson.cs` | Workspace artifact JSON handling; inspect its versioned contract before proposing reuse. It does not establish recipe JSON 0.5 support. |
| Operations and lifecycle | `src/TiaBridge.Core/Operations/OperationModels.cs`, `OperationLifecycle.cs`, `src/TiaBridge.Host/Operations/SqliteOperationStore.cs` | Durable operation identity, lifecycle and history. This is operation execution state, not CODEX task progress. |
| Host scheduling/supervision | `src/TiaBridge.Host/Scheduling/OperationScheduler.cs`, `CircuitBreaker.cs`, `src/TiaBridge.Host/Supervision/WorkerSupervisor.cs` | Asynchronous scheduling and worker supervision; accepted operations survive host restart. Timeout/hang does not establish that Siemens stopped. |
| API and CLI | `src/TiaBridge.Host/Api/HostApi.cs`, `src/TiaBridge.Cli/Program.cs`, `src/TiaBridge.Client/BridgeClient.cs` | Versioned local API and nonblocking CLI (`submit`, `operation status/result/events/cancel`, `health`). No generic `preflight source` or task checkpoint command was found. |
| V19 implementation | `src/TiaBridge.Worker.V19/Program.cs`, `VendorReader.cs`, `VendorResolver.cs`, `InventoryFamily.cs`, `HmiEventDiagnosticReader.cs` | Experimental bounded read-only discovery/inventory and diagnostics. It is not an object exporter or mutation engine. |
| Qualification preflights | `scripts/d11_preflight.ps1`, `scripts/e2_gate.py`, `scripts/e2_diagnostic_session.py`, `scripts/validate_offline.py`, `tests/engineering/test_worker_preflight.py` | Qualification/build/evidence gates and offline contract checks. Their purpose is not general source payload validation. No TIA is needed for offline checks. |
| Contracts | `contracts/v1/*`, `contracts/v2/capability-registry.schema.json`, `contracts/ipc/*` | Versioned API, operation, workspace and IPC schemas. A new source-package report schema should be explicit and separately versioned. |
| Resilience evidence | `src/TiaBridge.Host/Operations`, `src/TiaBridge.Host/Storage`, `tests/integration/TiaBridge.Host.Tests/FailureAndRestartTests.cs`, `Resilience/*` | Host operation durability/restart and failure behavior. No autonomous CODEX task checkpoint store. |
| Reporting | CLI JSON responses, operation events/results, qualification dossier/evidence reports, `scripts/report_resilience.py`, `scripts/performance_evidence.py` | Existing structured reporting is domain-specific. It does not aggregate T05 Runtime logs and PLC readback. |
| Lab integration | `scripts/build_lab_events.ps1`, `scripts/test_lab_events.ps1`, related `scripts/lab*_tests.cs`, `docs/LAB_03K_HMI_EVENT_EXPORT.md` | Builds/tests lab event-export code from `X:\TESTY_TIA_Bridge\src`; it does not make LabExporter a TIA_Bridge assembly or make event export a Host capability. |

The technical recovery boundary already includes durable operation history, failure/UNKNOWN semantics, project quarantine/reconciliation requirements, and versioned evidence. TIA_Bridge's current implemented side should be described as operation durability and qualification/recovery—not as general TIA `ModificationSession` support. A full source scan did not identify a TIA_Bridge `ModificationSession` class. The `ModificationSession` implementation discussed below belongs to the T05 tool.

### 3.2 T05 TIA Bridge

`C:\RMA\t05_plc\tools\T05_TIA_Bridge.py` dispatches commands into the modules under `tools/t05_tia_bridge/`; `adapter.ps1` is the PowerShell/Openness boundary. The Python launcher reports static/live preflight statuses and command exit codes. The project-specific `preflight.py` handles static and live Bridge preflight. `modification_session.py` owns the T05 session manifest, operation journal, backup receipt binding, per-operation checkpoints, final compile/Save/readback, and reviewed recovery modes. `session_workflow.md`, `preflight.md`, `modification_session.md`, and `backups.md` document the operational contract.

This is not the same code as the generic TIA_Bridge repository or LabExporter. A direct SHA-256 observation of `T05_TIA_Bridge.py` and `X:\TESTY_TIA_Bridge\src\LabExporter.cs` produced different hashes (as expected for different files); names alone establish no code identity. The T05 source is in a Git checkout rooted at `C:\RMA\t05_plc`; the LabExporter root did not report as a Git worktree. No shared package, submodule, or generated-source relationship between these two implementations was found in the inspected paths.

### 3.3 LabExporter

The executable is `X:\TESTY_TIA_Bridge\bin\LabExporter.exe`; the implementation sources are under `X:\TESTY_TIA_Bridge\src`, including `LabExporter.cs`, `LabEventExport.cs`, `LabEventVendor.cs`, `LabEventView.cs`, `LabEventConsole.cs`, `LabEventSync.cs`, `LabForceSync.cs`, and `LabNewModules.cs`. `X:\Projects\TIA_Bridge\scripts\build_lab_events.ps1` compiles the lab source set against the installed V19 PublicAPI and retains the prior binary/build evidence. This script is a build workflow, not source ownership by the TIA_Bridge Core/Host.

The current EXE's Windows file metadata says FileVersion/ProductVersion `0.0.0.0`; it has no useful product/company metadata. The observed SHA-256 is `E37EBB431BEDDF5BCAE3B6F81DF23849AED3DB739C12C47CE3324CEF690869A3`. The lab keeps build evidence/manifests under `evidence`, but the inspected root is not a Git worktree and EXE version metadata does not identify a formal semantic release. Consequently the exact source-to-current-binary relationship is **not established by a current version field**; per-build manifests/source hashes are the available evidence and should be the release identity going forward. This audit did not modify or run the EXE.

README/source document bounded inventory, native export/readback, FORCE EXPORT/IMPORT, and FORCE SYNC variants, with exact commands and safeguards. The source also has `export-events`, including overwrite/snapshot publication, stable object mapping, exact-byte/UTF-8 handling, SHA-256 manifests, bounded enumeration, PARTIAL outcomes, and incomplete-publication recovery. It uses the exact TIA process/project/HMI identity and read-only conditions for event export. Source paths should be checked against the specific operation's README section before use because command semantics differ: some are read-only export, while import/force-sync performs project mutation.

Existing reusable LabExporter behavior for later work:

- export/import/readback of supported HMI JavaScript modules, with module/native manifests and SHA-256 checks;
- byte-exact export/event projection and bounded enumeration, including explicit PARTIAL/UNKNOWN outcomes;
- `force-sync --validate-only` / input validation before the modifying route, plus exact source-set and native baseline checks;
- selective module/source selection where the command contract allows it;
- native backup, import intent/stage reports, Save and independent native readback in the relevant FORCE workflow;
- failure reports that distinguish not-started from `RECONCILIATION_REQUIRED` after import begins;
- atomic-ish staged publication/recovery for filesystem output, with explicit caveat that multi-file publication is not fully transactional.

These functions are owned by LabExporter. A future TIA_Bridge proposal should consume its reports or define an interoperable envelope; it should not reimplement its Siemens native import/export/readback. No project-changing LabExporter command was run in this audit. The supplied user constraints remain in force for future use.

## 4. Existing capabilities versus gaps

| Area | Existing capability | Actual gap | Recommended direction |
|---|---|---|---|
| Offline preflight | TIA_Bridge qualification checks; Core workspace manifest validation; T05 static plan/payload preflight; LabExporter import validation | No one offline command validates a proposed PLC/HMI package's bytes, source-format rules, manifest, event routes and project-specific map before TIA | Stage 1: add file-only validators and machine JSON; preserve each project's specific rules in its repository. |
| JS/Events | LabExporter bounded event export/index, handler coverage and event sync manifests; T05 offline tests and generated Events evidence | No generic pre-import cross-check for source exports → function index → event route → named module/handler/callback in one report | Add a format/contract validator at the LabExporter/T05 boundary; Bridge may define generic report semantics. Do not infer hidden CWC internals. |
| Manifest/hash/EOL | Workspace unit SHA validation; LabExporter source/native/export manifests; T05 report records CRLF and `-text` byte preservation | No shared offline entry point that validates a selected source package before Apply/import; EOL requirements vary by payload | Define manifest/hash/EOL policy per package type, validate bytes before decoding, and never normalize unknown native/vendor artifacts. |
| Checkpoint | TIA_Bridge durable operation ID/events/history; T05 ModificationSession checkpoints and receipts; runner evidence artifacts | No small checkpoint for CODEX goal, accepted criteria, already-read evidence and next action | A standalone task JSON written by orchestration; references existing operation/session IDs and evidence without copying their state. |
| Recovery | TIA_Bridge FAILED/UNKNOWN/quarantine policy; T05 reviewed resume/recovery and fresh readback; LabExporter partial failure receipts | A task-level resume can be mistaken for project-state confirmation or retry consent | On resume revalidate current repo, checkpoint hash, operation/session status and live state before writes; UNKNOWN is manual reconciliation. |
| E2E reporting | T05 report and runner logs include scenario evidence, request/ack and readbacks; per-run artifacts retained | No consistent single compact report across all scenarios and tool logs; aggregate run identity/status/elapsed/path lookup is incomplete | Extend the runner to emit one versioned summary and retain detailed logs. Use `UNKNOWN` for unavailable metrics. |
| Diagnostics | T05Logger CSV, Runtime logs, Bridge receipts, compiler JSON, PLC readback; existing analyzers and correlation chain | No single selector/correlation/index that filters by run/operation/session/RequestId/AckId and reports bounded findings | Produce references and filtered summaries from sources; do not concatenate/copy full logs. |
| Computer Use | Instructions prefer CLI/API/Playwright and restrict GUI-only evidence | No reliable complete CU call telemetry was found in the supplied audit; count/time/token cost is `UNKNOWN` | Keep Computer Use only for genuinely GUI-only assertions; record source telemetry if available, without inferring tokens from time or screenshots. |
| Selective synchronization | T05 Bridge has bounded workflows; LabExporter supports selected modules/Events under its own contract; T05 report documents selective sync | Generic TIA_Bridge currently has no general synchronization/import feature | Continue selective execution in the owning tool. Do not create a second synchronizer in TIA_Bridge. |

## 5. Proposed TIA Preflight Validator

### 5.1 Ownership and location

The proposed **generic** validator should live with file-contract validation in TIA_Bridge, initially as an offline command/module under `scripts/` and/or a small public CLI command only after its input contract is approved. The eventual domain boundary is `src/TiaBridge.Core/Workspace` for versioned workspace/manifest invariants; do not place project-specific recipe validation in Core. Existing `WorkspaceValidation` may be reused only for the exact `WorkspaceManifest` type it already validates.

T05 owns recipe JSON 0.5, JSON-to-`UDT_Product` mapping, PLC field limits/types, dictionary/revision rules, and acceptance expectations. T05 also owns project-specific Events-to-handler mapping for its RecipeTransfer workflow. LabExporter owns Siemens event/module extraction, native representation and supported import/readback semantics. A generic, declared JS/Events syntax/index contract could be shared only after it is made a stable versioned contract and verified against all consumers; the current LabExporter event inventory is not proof of a complete public CWC object model.

### 5.2 Suggested interface

Proposed offline CLI (new, not currently implemented):

```text
tia-bridge validate source-package --manifest <path> [--root <dir>] [--checks manifest,hash,eol,js-index,event-links] --json [--output <report.json>]
```

It reads only the declared package and writes only the optional report. It performs no Attach, TIA lookup, project modification, network access, script execution, normalization or source rewrite. Input must be a versioned manifest listing relative paths, role, encoding/EOL policy, and expected SHA-256; package root traversal, reparse points, duplicate paths, unknown fields, oversized files and invalid encodings fail closed. Hash exact bytes before text parsing. A project-specific validator can be invoked separately with a recipe contract, but must identify its owner and schema explicitly.

Possible first result format:

```json
{
  "schemaVersion": "tia-bridge.preflight/1.0",
  "status": "PASS",
  "scope": {"root": "<canonical-root>", "manifestSha256": "<sha256>"},
  "checks": [
    {"id": "manifest.files", "status": "PASS", "count": 8},
    {"id": "files.sha256", "status": "PASS", "count": 8},
    {"id": "files.eol", "status": "PASS", "count": 8},
    {"id": "events.routes", "status": "PASS", "count": 213}
  ],
  "issues": [],
  "generatedAtUtc": "<ISO-8601>"
}
```

Each issue should have stable `code`, `severity`, `path`, optional `line`/`symbol`, expected/actual values, and a short remedy. Do not emit recipe payloads or secrets. `PASS_WITH_WARNINGS` may be useful for explicitly incomplete public coverage, but unknown handler resolution, hash mismatch or contract failure must not become PASS.

Suggested exit codes: `0` PASS; `1` validation errors; `2` invalid invocation/schema/unreadable package; `3` incomplete/unsupported coverage (when caller policy requires complete); `4` validator/internal error. The exact mapping should be fixed in the CLI contract before implementation. The process must not return zero on a skipped required check.

### 5.3 Checks and boundaries

| Check | Generic vs project-specific | Offline? | First-stage treatment |
|---|---|---:|---|
| Manifest schema, path membership, duplicate/unlisted files, SHA-256 | Generic package contract | Yes | Include in MVP |
| CRLF/LF/BOM/encoding | Package-role policy, generic engine | Yes | Include for declared roles; exact-byte preservation, no auto-conversion |
| PLC source parser/compile diagnostics | Vendor/project format and installed toolchain | Mostly not generic; TIA compile remains live | Defer; static syntax checks only when a qualified parser exists |
| JS exported-function index and module contract | Generic syntax rule only if the JS profile is versioned; Siemens markers remain exact | Yes | MVP may check declarations/manifest agreement using existing parser capability; don't execute JS |
| Events → module → handler/callback | Generic route graph shape plus project-specific expected route set | Yes from exported/manifests | Include only where explicit mapping data exists; hidden CWC children remain UNKNOWN/PARTIAL |
| Import/export contract | Generic package layout and native manifest; Siemens semantics remain LabExporter-owned | Yes for file package | Check declared schema/version/files; do not claim Siemens importability |
| Scope/dependency/synchronization plan | TIA_Bridge's future Apply contract; T05's own session/plan | Plan-only checks may be offline; live state must be fresh before mutation | No replacement for existing Apply preflight; validate plan fingerprint and declared scope only |
| TIA types/object dependencies | Project-specific type map plus fresh TIA capabilities/readback | File-based subset offline; live support is not | T05 owns recipe mapping; generic bridge validates stable object/dependency IDs only when represented in workspace |
| API request parameters | Existing versioned request schemas and `ContractJson` | Yes | Reuse current API contract validation; do not duplicate schemas |

### 5.4 Workflow integration and tests for a future implementation

Run validators in order: manifest/schema → exact hashes/EOL → JS/module index → Events route closure → project-specific JSON/type mapping → approved plan checks → only then the existing authorized tool workflow. Offline PASS only says declared files satisfy those checks. It does not prove TIA import, compile, Save, readback, synchronization, or Runtime.

Regression coverage should use synthetic fixtures for LF-vs-CRLF, BOM/encoding, missing/extra/duplicate/traversal/reparse paths, tampered hashes, stale manifest, invalid JS declaration, missing/duplicate event target, unresolved callback, unknown schema/field, excessive size/depth, malformed JSON, and unsupported/incomplete coverage. A negative fixture must prove no TIA process discovery or file mutation occurred. Run no live tests as part of the initial validator package.

### 5.5 Minimal first release

Limit the first delivery to versioned source manifest validation, exact SHA-256 and file-set check, declared EOL/encoding check, and a compact JSON report. Add JS function index and Event-route checks only against explicit machine-readable mapping already available in the chosen package. Keep recipe schema/UDT mapping in T05. This sequence is most likely to catch the supplied audit's late LF/manifest mismatch before a costly Openness operation.

## 6. Proposed Task Checkpoint JSON

### 6.1 Fit with existing state

TIA_Bridge operation persistence (`operation_id`, request, status/events/result) and T05 `ModificationSession` (session manifest, immutable plan/evidence, backup, operation log, checkpoints, readback and recovery) must remain authoritative for their respective domains. Do not duplicate them into a task document. Task Checkpoint stores the user's goal, stage, acceptance progress, safe next action, blocker, and references to their durable evidence.

Recommendation among the three choices: **C — agreed JSON format written by the existing orchestrating script**. It is the smallest option and does not require CODEX-specific state in TIA_Bridge. A reusable library/manager is premature; native Bridge support would conflate a CODEX goal with a TIA operation and create a second recovery authority.

### 6.2 Proposed schema

Store one file per task under the owning tool's ignored user-data area (for T05, `UserFiles/<task-id>/task-checkpoint.json`; for generic Bridge, outside the repository under its configured local state root). Never commit project/customer evidence by default. Use atomic write: serialize to a unique same-directory temporary file, flush/close, validate by rereading, then replace/rename into place while retaining the last valid file. If the platform cannot guarantee replace semantics, preserve both candidates and mark the checkpoint `WRITE_UNCERTAIN`; never overwrite the only valid copy.

```json
{
  "schemaVersion": "task-checkpoint/1.0",
  "taskId": "<stable-uuid-or-task-key>",
  "goal": "<one-sentence acceptance goal>",
  "stage": "VERIFY",
  "status": "IN_PROGRESS",
  "updatedAtUtc": "<ISO-8601>",
  "projectIdentity": {
    "profileId": "<optional-profile>",
    "projectKey": "<optional-identity-key>",
    "projectPath": "<optional-path-reference>",
    "identityEvidence": {"path": "<evidence-file>", "sha256": "<sha256>"}
  },
  "modificationSessionId": "<optional-existing-session-id>",
  "bridgeOperationIds": ["<optional-existing-operation-id>"],
  "lastVerifiedCheckpoint": {
    "name": "<acceptance-stage>",
    "status": "PASS",
    "verifiedAtUtc": "<ISO-8601>",
    "evidence": [{"path": "<existing-receipt>", "sha256": "<sha256>"}]
  },
  "completedAcceptance": [{"id": "<criterion-id>", "status": "PASS", "evidenceRefs": ["<evidence-ref>"]}],
  "pendingActions": [{"id": "<action-id>", "description": "<next bounded action>", "requiresFreshState": true}],
  "blockedReason": null,
  "evidencePaths": [{"path": "<existing-evidence>", "sha256": "<sha256>", "kind": "<receipt|log|readback>"}],
  "nextStep": "<one concrete next step>",
  "workspaceFingerprint": "<optional-git-tree-or-explicit-file-list-hash>",
  "checkpointSha256": "<optional-envelope digest excluding this field>"
}
```

Use relative evidence paths only when their base is explicitly fixed and canonicalized; otherwise use absolute paths. Hash referenced immutable evidence and verify it at resume. The checkpoint digest detects file corruption, not truthfulness or current project state. Do not store credentials, full logs, copied PLC data, approval grants, mutable plan payloads, or duplicated `ModificationSession` journals.

### 6.3 Resume protocol and failure handling

On resume: (1) validate schema and integrity; (2) verify task goal/repository and workspace fingerprint or explain drift; (3) check that evidence exists and hashes match; (4) query the existing Bridge operation/session status using its own interface; (5) for any pending TIA mutation, perform the existing fresh identity, baseline, capability, plan, backup/session and precondition checks; (6) continue only with the next action that is independently safe. If a prior operation is RUNNING, UNKNOWN or `RECONCILIATION_REQUIRED`, stop and follow that tool's recovery workflow. Never infer completion from a task checkpoint or rerun the same mutating request automatically.

Completed acceptance entries are append/update progress assertions with evidence references, not new baseline claims. If a partial write yields invalid JSON, retain the previous valid file and both candidate files; set a separate recovery note or require manual review rather than trusting a truncated replacement. A task can be `BLOCKED_MANUAL_REVIEW`, `INTERRUPTED`, `COMPLETE`, or `IN_PROGRESS`; `COMPLETE` requires every declared acceptance item to reference sufficient current evidence.

Future tests: schema/version forward-compatibility; missing/corrupt/stale evidence; tree drift; atomic replacement interruption; duplicate task IDs; path escape/reparse point; secret-field rejection; stale session/operation status; and an assertion that checkpoint resume alone cannot call Apply/Save/Compile or retry a mutation.

## 7. E2E Reporter and Diagnostics Collector

### 7.1 Existing runner and data

The principal integration point is `C:\RMA\t05_plc\tools\RecipeTransferLabV19.ps1`. It already accepts a bounded `Action` set (Inspect, InspectPlc, connection inspection/configuration, backup, compile, import and test-PLCSIM download among others), writes structured JSON artifacts under its configured output, records backup hashes, emits started/save/compile/readback files, and returns `PASS <Action>` on success. It is an engineering lab runner with project-specific behavior; it is not a generic E2E harness and its mutating actions remain governed by current T05 authorization.

The supplied E2E report records scenarios E2E-01…08, PASS/FAIL/BLOCKED distinctions, request/ack values, recipe IDs/version/SHA-256, field counts, mismatches, compiler reports, readback files, logs, and a final 2/0/0 runner result. T05Logger uses `GfxRTS(*).ScriptFW.csv`; the existing analyzer is `tools/analyze_t05_logs.py` with matching `.md`. Correlation for key scenarios is the chain WSS SessionId → Runtime GUID → Siemens session → `instanceId` → `operationId`; time proximity alone is not adequate. The report identifies full tool/CU invocation counts, elapsed aggregation and token usage as UNKNOWN.

### 7.2 Compact report proposal

Extend the T05 runner/orchestrator that owns the scenario loop (confirm the exact runner source from `RecipeTransferLabV19.md` and current invocation before implementation) to write one `e2e-summary.json` in the existing run output. Do not make the per-step TIA helper invent a test run or business result. Proposed envelope:

```json
{
  "schemaVersion": "t05.e2e-summary/1.0",
  "runId": "<uuid> subject to stable runner assignment",
  "startedAtUtc": "<ISO-8601>",
  "endedAtUtc": "<ISO-8601>",
  "status": "PASS",
  "scenarioCounts": {"pass": 2, "fail": 0, "blocked": 0, "notRun": 0},
  "project": {"profile": "<test-profile>", "identityRef": "<identity receipt>"},
  "recipe": {"documentId": "<filename-or-id>", "schemaVersion": "0.5", "sha256": "<sha256>"},
  "transfer": {"requestId": "<id>", "ackId": "<id>", "status": 2, "activeValid": true},
  "comparison": {"fieldsCompared": 2499, "mismatchCount": 0, "mismatches": []},
  "scenarios": [{"id": "E2E-01", "status": "PASS", "elapsedMs": 0, "evidenceRefs": ["<readback>"], "logRefs": ["<log>"]}],
  "diagnosticRefs": ["<diagnostic-index.json>"],
  "limits": {"runtimeEvidence": "<scope>", "toolInvocationTelemetry": "UNKNOWN", "codexTokens": "UNKNOWN"}
}
```

Use actual runner IDs when available; add a stable UUID only if there is no existing run ID. RequestId/AckId are opaque strings or bounded integer representations, not guessed. Counts and recipe/business status are T05-owned. For multiple requests/recipes, make per-scenario transfer records rather than a misleading singleton top-level field. `BLOCKED`/`NOT_RUN` never count as PASS. A mismatch report should include bounded field paths and expected/actual type or safe scalar values according to project data policy, not dump the whole recipe.

The generic TIA_Bridge contribution, if later needed, is only a versioned operation receipt/result and evidence-reference envelope with operation ID, status, timing, method/scope, and paths/hashes. It must not interpret recipes, PLC status 2, ActiveValid, or business acceptance. LabExporter reports its own import/export/readback stage and reconciliation state.

### 7.3 Diagnostics Collector proposal

Build a T05 collector on the existing analyzer and artifact layout. Inputs are one or more correlation selectors: `runId`, Bridge `operationId`, T05 `sessionId`, `RequestId`, and `AckId`. The collector should return a small `diagnostics-index.json` containing source references, source hashes, matched record counts/time window, the verified correlation edges, per-source summary, first/last relevant error, compiler message counts/severities, PLC readback comparison result, and unresolved links. It should not copy full logs into the JSON.

Recommended source adapters:

- T05Logger CSV via `tools/analyze_t05_logs.py`, filtering `instanceId`, `operationId`, level and result; do not assume a fixed `GfxRTS` process number.
- Runtime/Playwright logs using existing WSS/Runtime/session correlation IDs. If a link is missing, report `UNRESOLVED`; do not join solely by timestamp.
- Bridge reports using exact `operationId` and receipt path/hash; preserve operation status including UNKNOWN and reconciliation-required.
- PLC readback files by RequestId/AckId, with field count, equality and source JSON hash; never include full readback values by default.
- Offline test logs and compile reports by run/build identity, reporting counts and first relevant failures; do not merge OFFLINE, TIA compile, Runtime and PLC evidence into one indistinguishable PASS.
- LabExporter stage reports by its run/evidence ID and operation, keeping its native import/readback and reconciliation statuses intact.

Prefer links/paths plus SHA-256 and line/row ranges where stable. A command such as `RecipeTransferLabV19.ps1 -Action CollectDiagnostics -RunId ...` is a **possible new T05 command**, not an existing action; an alternate is a separate documented T05 utility if the runner's scope should remain narrow. Avoid adding a collector to generic TIA_Bridge that knows T05Logger or recipe semantics.

Future tests: synthetic logs with exact and broken correlation chains, duplicate IDs, interleaved unrelated events, missing sources, changed source hashes, malformed/oversized CSV/JSON, timezone boundaries, redaction, unsupported statuses, and no whole-log copying. Verify stable report output and correct separation of OFFLINE/TIA/Runtime/PLC status.

## 8. Responsibility split and target flow

```text
CODEX goal / acceptance
  -> offline PREFLIGHT (generic file contract + T05 project validators)
  -> existing authorized EXECUTION tool
       TIA_Bridge Host/CLI: durable generic operations, bounded current capability
       T05 Bridge: project-specific plan/session/recipe orchestration
       LabExporter: supported HMI script/Event import-export-readback
  -> VERIFY using each owner's existing compile/readback/Runtime evidence
  -> Task Checkpoint JSON stores progress and evidence references
  -> T05 E2E summary + filtered diagnostics index; generic operation receipts remain generic
```

| Layer | Keep or add | Must not take over |
|---|---|---|
| TIA_Bridge Core/Host | Existing versioned DTOs, workspace validation, durable operation lifecycle and generic evidence references; future offline package contract only when it matches generic Bridge data | Recipe 0.5, `UDT_Product` mapping, T05Logger, Runtime scenario meaning, second ModificationSession manager, native HMI import/export |
| T05 repository | Recipe/schema/PLC mapping and semantics, project plan and `ModificationSession`, runner, T05Logger correlation, E2E summary and business diagnostics | Generic Siemens vendor proxy in Bridge Host; duplicate LabExporter native package logic |
| LabExporter | Its supported Siemens HMI module/Event extract/import, native backup/readback, manifests and per-stage failure/reconciliation reports | Recipe acceptance semantics or CODEX task state |
| CODEX task workflow | Goal, stage, acceptance checklist, next step and references to prior evidence in small task JSON | Claims of current TIA state, authorization, retry policy, operation history or backup/readback truth |

## 9. Efficiency assessment

The evidence supports qualitative, not percentage, benefit estimates. The supplied report records selected LabExporter build durations around 0.39–0.62 s, exports around 26.35–26.89 s and one `force-sync` at 212.86 s; these are measurements of those specific operations/run artifacts, not a general cost model. Full tool invocation counts, total elapsed E2E time, Computer Use calls, CODEX token consumption and financial cost remain UNKNOWN. Tool wall-clock time, CODEX context volume and token usage are distinct metrics.

| Extension | Expected benefit | Complexity / architecture / compatibility | Regression risk and future evidence | TIA change required? |
|---|---|---|---|---|
| Manifest + exact hash + EOL preflight | High for avoiding preventable live cycles; detects known byte/manifest errors cheaply | Low–medium; offline CLI/report; additive schema and command | Risk of false positives from wrong EOL policy. Test exact bytes, BOMs, stale/extra files, path attacks, schema compatibility. | No |
| JS/Event contract preflight | High where explicit module/event maps exist; reduces iterative import/readback and GUI investigation | Medium; generic graph checks plus T05 expected route profile and LabExporter export manifests | Risk of claiming complete coverage for hidden CWC internals or stale routes. Synthetic negative matrix plus actual exported fixture contract qualification. | No for file-only validation |
| Task Checkpoint JSON | High context/work repetition savings on interrupted long task; avoids rerunning accepted independent stages | Low; script-owned additive JSON, no Bridge state migration | Stale/corrupt checkpoint could mislead resume. Test atomic write interruption, evidence hashes, session UNKNOWN and no retry. | No |
| E2E Reporter | Reduces stdout and context consumed by CODEX; makes acceptance/status review faster | Low–medium in existing T05 scenario runner; additive summary | Incorrect aggregation or status flattening. Test scenario counts, IDs, FAIL/BLOCKED/NOT_RUN, multi-request runs and compatibility with existing reports. | No |
| Diagnostics Collector | Reduces repeated full-log reads and needless re-runs; faster root-cause isolation | Medium due to correlation adapters; report only references and excerpts | False joins, redaction leak, log format drift. Synthetic correlation and malformed-input tests; tool schema versioning. | No |
| Computer Use reduction policy | Fewer GUI interactions when CLI/API/Playwright/readback can prove criterion | Low policy, but benefit depends on actual GUI-only tasks | Cannot replace visual/GUI-only evidence. Track source telemetry; no inferred savings. | No |
| More selective sync/import | Less TIA runtime and smaller mutation scope | T05/LabExporter already support selected workflows; generic Bridge has no sync implementation | Reimplementing risks bypassing native baseline/capability/saga. Reuse owner tool and keep live verification. | Potentially affects project state; separate authorization/task rules apply |

The biggest likely reduction in **TIA runtime** is preflight because it can prevent a costly export/import/compile cycle from starting with invalid bytes or manifests. The biggest direct reduction in **context and repeated work** is checkpoint plus summary: CODEX can load the last confirmed state and inspect only referenced diagnostics. No evidence demonstrates a numeric token reduction. The largest reduction in **Computer Use** comes from replacing routine state reads with existing CLI/API/readback and Playwright, while preserving GUI use for truly visual or unsupported states.

## 10. Implementation plan (not authorized by this document)

### Stage 1 — Preflight Validator

**Initial acceptance slice:** offline manifest/schema, package file-set, SHA-256, declared encoding/EOL, concise JSON report and stable exit codes. Add JS function index and Event-route closure only when the package contains explicit route data. Keep recipe JSON and PLC mapping validation in T05. This stage must never Attach or write project files.

**Candidate TIA_Bridge files:** `src/TiaBridge.Core/Workspace/WorkspaceValidation.cs` only for the existing WorkspaceManifest contract; add a separate source-package validator/contract under `src/TiaBridge.Core` or a narrow `scripts/` prototype after choosing ownership; `src/TiaBridge.Cli/Program.cs` if a stable public command is approved; `contracts/` for versioned result schema; `tests/contracts/` and synthetic `tests/fixtures/`; `scripts/README.md`; `docs/architecture.md` and `Zmiany.md` only if architecture/workflow/acceptance contract changes. The project-specific mapping/plugin belongs under T05 tools and requires its own T05 report/instructions. Do not change `AGENTS.md` in this stage without a separate instruction review and approval.

**Acceptance:** byte-exact valid fixture PASS; tampered/missing/extra file and wrong EOL fail before any TIA lookup; machine report deterministic apart from timestamp/run ID; unknown required check cannot PASS; T05-only recipe errors are emitted by T05-owned validator; no import/normalization.

**Tests:** synthetic unit/contract fixtures for manifest, SHA, EOL, path/encoding/parser bounds, JS index/route closure as applicable; CLI exit code and JSON schema tests; negative assertion of no Siemens process/API dependency. No E2E/live TIA test.

**Limits/risks:** validators cannot prove native TIA structure, compile, or Runtime. EOL is role-specific. Unknown vendor fields must be preserved or cause a block, not be discarded. Avoid broad generic PLC source parsing without a qualified parser.

**Rollback:** remove the additive command/schema/module and its fixtures. Existing `WorkspaceValidation`, operation schema and T05/LabExporter workflows remain unchanged. Do not roll back or alter any TIA project because this stage is offline.

### Stage 2 — Task Checkpoint

**Scope:** agree `task-checkpoint/1.0`; write/read/validate atomically from the existing orchestration layer; include acceptance items, current stage, next bounded step, blocked reason, optional project identity, optional `ModificationSession`/Bridge operation IDs and hashed evidence references. No daemon, manager, SQLite migration or TIA_Bridge operation extension.

**Candidate files:** first consumer's existing task script and matching `.md` usage note (if T05, an appropriate runner/orchestration script under `C:\RMA\t05_plc\tools`); ignored per-run data under `UserFiles/<task-id>/`; a schema in that owning repo only if its normal contract convention supports it. TIA_Bridge should host a generic schema only if there is a second real consumer; do not add CODEX lifecycle to `src/TiaBridge.Host/Operations`.

**Acceptance:** resume loads prior confirmed criterion/evidence; changed workspace or evidence hash becomes stale/review; corrupted partial write preserves last valid copy; pending UNKNOWN/reconciliation status blocks continuation; checkpoint alone cannot trigger or authorize Apply/retry.

**Tests:** valid/invalid schema, stale tree/evidence, failed write/replacement, path traversal, secret rejection, session/operation state mismatch, and no-mutation resume path.

**Limits/risks:** a task checkpoint can be outdated even when its JSON is valid. Always query authoritative tool status and fresh TIA identity before modifying operation. No checkpoint can stand in for backup, Save, compile, readback or Runtime evidence.

**Rollback:** stop writing the task file; preserve it as diagnostic evidence. Existing `ModificationSession` and Bridge operation histories remain untouched and authoritative.

### Stage 3 — E2E Reporter + Diagnostics

**Scope:** extend the T05 scenario orchestrator to produce one compact summary and a diagnostic index from existing logs/receipts/readbacks. Add selectors for existing run/operation/session/request/ack IDs only where the source contains them. Keep full logs in place. Define a generic result/reference envelope only if a second tool consumer exists; LabExporter retains its own stage reports.

**Candidate files:** `C:\RMA\t05_plc\tools\RecipeTransferLabV19.ps1` and `.md` if it owns the run aggregation; otherwise the specific runner identified by its usage documentation; `tools/analyze_t05_logs.py` and `.md`; tests/fixtures for synthetic logs; optional TIA_Bridge `contracts/` result/reference schema if truly cross-tool. The T05 E2E report and knowledge-base notes should be updated only when reporting conventions or confirmed evidence change.

**Acceptance:** one machine summary has every scenario and correct PASS/FAIL/BLOCKED/NOT_RUN counts, timestamps and evidence references; correlation requires verified IDs and reports missing edges; diagnostic output is bounded and contains no full logs/secrets; OFFLINE/TIA compile/Runtime/PLC statuses remain separate; rerunning aggregation does not rerun E2E or TIA.

**Tests:** synthetic T05Logger/Runtime/Bridge/compile/readback fixtures, exact and broken correlation, duplicate IDs, missing source, hash drift, malformed/oversized records, redaction, and summary stability. No E2E execution in the reporting test suite.

**Limits/risks:** runtime log formats and WSS correlation may drift; never correlate by time alone. Aggregated PASS is limited to explicit assertions and cannot broaden the source evidence. Invocation/token telemetry remains UNKNOWN unless measured directly by an approved platform source.

**Rollback:** disable summary/collector output and return to existing per-step reports; do not delete or rewrite existing raw logs/evidence. No TIA rollback is relevant.

## 11. Decisions and risks requiring review

1. **Preflight scope:** approve which manifest/package formats are first-class and whether the first CLI is a temporary offline script or stable `tia-bridge validate` command. Current Bridge source exchange is not implemented, so avoid promising future Apply integration.
2. **EOL policy:** define exact byte encoding and CRLF/LF per package role; do not globally enforce CRLF or normalize payloads.
3. **JS/Event contract:** decide whether a cross-project route schema is justified by multiple consumers. Keep T05 expected routes and business meanings local.
4. **Task checkpoint owner/location:** choose the first orchestration consumer and ignored local state root. Do not store project/customer evidence in Git or duplicate ModificationSession records.
5. **Reporter ownership:** verify the precise scenario runner entry point; the requested PowerShell helper emits per-action artifacts, while final scenario aggregation may be elsewhere. Do not add run-level semantics to a helper without confirming ownership.
6. **LabExporter release identity:** its EXE metadata is `0.0.0.0`, and its root is not a Git worktree. Establish source revision + build manifest + EXE hash as release identity before depending on stable cross-tool contracts.
7. **Telemetry:** determine whether actual Computer Use, tool invocation, wall time, or token counters are available from approved host telemetry. Otherwise preserve `UNKNOWN`; do not estimate.
8. **Instruction impacts:** if a future implementation changes architecture, workflow, synchronization, authorization, tests or safety, review both TIA_Bridge `AGENTS.md` and `Instrukcja_ChatGPT.md`, and the applicable T05 instructions/ADR/contracts. Record the result in `Zmiany.md` as required. This proposal itself does not amend those files.

## 12. Audit limits

The detailed implementation facts above derive from repository source/documentation and the two supplied completed T05 reports. The LabExporter binary was not invoked, so current command behavior was established from its source/README/build scripts rather than a runtime `--help`. No current formal version number could be established for the binary. The T05 run report explicitly leaves full tool-call, Computer Use, total duration, and token measurements unknown. No conclusions in this proposal claim a current TIA state or fresh live qualification.
