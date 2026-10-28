# HMI-JS-02 — Unified V19 JavaScript module export

## Current status and acceptance

Base: verified clean HMI-JS-01 `05944ae`; isolated worktree `feat/hmi-js-02`.
Core splitting, offline CLI packaging, typed net48/x64 Worker export and the
existing asynchronous Host `project.export` route are implemented and tested offline.
**REAL TIA EXPORT: NOT_RUN.** No Attach, Import, Save, Compile, Archive, Runtime
operation or real enrollment occurred. The real ten-file success criterion is
**not achieved**. The target native format and editor-area fidelity remain UNKNOWN.
The historical HMI-JS-01 section below records the earlier state, superseded here.

## V19 API verification

Reflection-only inspection on 2026-10-28 in Windows PowerShell 5.1 x64 verified:

```text
HmiUnified.HmiSoftware.Scripts : HmiScriptModuleComposition
HmiScriptModuleComposition.GetEnumerator() : IEnumerator<HmiScriptModule>
HmiScriptModuleComposition.Export(DirectoryInfo) : IEnumerable<FileInfo>
HmiScriptModule.Export(DirectoryInfo) : IEnumerable<FileInfo>
HmiScriptModule.Export(DirectoryInfo, String) : IEnumerable<FileInfo>
HmiScriptModule.Name : String
HmiScriptModule.Parent : IEngineeringObject
```

Scripts types are in `Siemens.Engineering.HmiUnified.Scripts`. AssemblyVersion is
`19.0.0.0`. Installed file SHA-256, relative to Portal V19:

| File | SHA-256 |
|---|---|
| `PublicAPI/V19/Siemens.Engineering.dll` | `2d60b4bb34f810e67e78b8018e59a3b738b0a2393ef273164b4858f6c2196626` |
| `PublicAPI/V19/Siemens.Engineering.Hmi.dll` | `7352c9f88091a9ff0a1b502d6db1ddf3930630c887a8321ee75c84fc84d2aefc` |
| `Bin/PublicAPI/Siemens.Engineering.Contract.dll` | `63c1a3fefa9b14c96f86e68790c33ccd05495c6b9e11bfa0bd96bac575c8ebf9` |
| `Bin/PublicAPI/Siemens.Engineering.ClientAdapter.Interfaces.dll` | `29307b75572df22eebd565609040f9879fa24dfa3ed2e8cd170a79046bb7be46` |

The [probe](../scripts/inspect_v19_hmi_api.ps1) performs no process enumeration or
Attach. The Worker also compiles against these exact installed references. Typed
access is `DeviceItem.GetService<SoftwareContainer>().Software is HmiSoftware`,
then `software.Scripts`, bounded enumeration and single-module `Export(DirectoryInfo)`.
There is no alternate Siemens client, reflection dispatch or vendor DLL in Core/Host.
No public module-function inventory/source accessor or persistent module/function
ID was verified. Names/path components are observed locators, never stable IDs.

## Native format and splitting limits

**Exact target native format, encoding, YAML schema, markers and editor-area
mapping: UNKNOWN / NOT_RUN.** Earlier project documentation records `.hmi.js` +
`.hmi.yml`; this task does not claim a new native observation or invent YAML keys.
The complete actual export directory is retained, including sidecars omitted by
the returned `FileInfo` list. Returned paths must remain inside it. Subdirectories,
unsafe paths, case collisions, more than 32 files or more than 16 MiB block handling;
partial native files remain after failure. No automatic retry or overwrite.

`hmi.es-module-prefix.v1` is an explicit Bridge **SOURCE projection**, not a Siemens
serializer or round-trip format. Esprima 3.0.6 ([parser documentation](https://github.com/sebastienros/esprima-dotnet))
parses strict UTF-8 ECMAScript modules without execution. Limits: 1 MiB JS,
100,000 AST nodes, assignment depth 128; parser stack exhaustion also fails closed.
Exactly the requested top-level named `export function` declarations (including
async) must exist. Nested functions remain in their parent. The prefix before the
first export is the candidate global area. Imports, variables and helpers are retained.
Only whitespace may separate the last global statement and first export, or separate
subsequent exported functions. Comment-only prefixes, ambiguous boundary comments,
interleaved globals, export lists/variables/defaults, unsupported or invalid JS block
the **entire** split. Comments inside declarations, Unicode, regex/string/template
literals and multiline functions are handled by syntax ranges, not brace regexes.

Each file is an original byte slice, including following whitespace; concatenation
in source order reconstructs every original byte. BOM, CRLF/LF, spelling and code
are never normalized, regenerated or wrapped. A blocked split emits only native
artifacts and a BLOCKED manifest, with no partial set of apparently correct JS files.
Unexpected native markers require a newly documented/tested format profile or an
operator-reviewed boundary map. Global-prefix correspondence to the TIA editor must
be independently qualified before claiming native global-definition fidelity.
YAML and unknown vendor fields remain opaque. No SYNCED/API/structure/import claim.

## Output and manifest

The Worker creates `StatePath/hmi-export-<operation_id N>/native/`. After receipt
identity/clean-state/size/hash verification, the Host creates `package/` containing
`native/`, `manifest.json`, and (only after source split PASS):

```text
T05RecipeCatalog/
  Global definition area.js
  createCatalog.js
  createRecipeTableModel.js
  directoryFailure.js
  getT05CatalogSchema.js
  HandleRecipeCatalog.js
  performCatalogAction.js
  render.js
  validateData.js
  validateDictionaries.js
```

The [Bridge manifest](../contracts/hmi-js/manifest.schema.json) records exact names,
roles, byte lengths, source offsets, raw SHA-256 and source-local comparison keys.
`object_id=null`, `identity_state=UNAVAILABLE`, `source_status=NOT_ENGINEERING_VERIFIED`.
Comparison keys are scoped source members, not Siemens identities. Comparison uses
the same explicitly reviewed scope/module/profile and compares individual byte hashes;
offset shifts do not mark other functions modified. Missing is observation-only;
blocked coverage gives UNKNOWN. The CLI verifies artifact paths/hashes before comparing.
The operation manifest wraps this source manifest and a path-to-immutable-artifact-ID
map. Raw receipt, native and source artifacts remain separate. Splitting failure is
FAILED/COVERAGE_GAP with retained references, never ten-file PASS. All sensitive files
stay outside Git on the validated local NTFS StatePath. Existing destinations/reparse
points/unsafe names/case collisions are rejected without escaping or renaming.

## Offline commands and tests

```powershell
dotnet src/TiaBridge.Cli/bin/Release/net10.0/TiaBridge.Cli.dll hmi-js split C:/private/native C:/private/new-package T05RecipeCatalog reviewed-project-module-scope
dotnet src/TiaBridge.Cli/bin/Release/net10.0/TiaBridge.Cli.dll hmi-js compare C:/private/previous/manifest.json C:/private/new-package/manifest.json
powershell.exe -NoProfile -File scripts/inspect_v19_hmi_api.ps1 -OutputPath C:/private/new-api-evidence.json
```

Use an absent destination for every run. The local CLI performs no Host/Worker start
or Siemens access; its first version fixes the nine catalog names. Exit 3 means
blocked splitting with native preservation. HMI-JS-01's Python packager stays unchanged.
Core tests cover all ten full files, repeat identity, Unicode/astral characters,
BOM/CRLF, comments, strings, regexes, nested templates/functions, multiline signatures,
collisions, incomplete exports, hashes/ranges, sidecars, tampering and one-function diff.
Host tests cover E1 kind/scope/build/window denial, typed API doubles, one owner,
Dispose/failure/dirty/missing/duplicate paths and preserved native-to-artifact projection.
Independent Python contract tests invoke the offline CLI and validate actual manifests.
All source is synthetic. FAST/STANDARD exclude NF-11 and all live Engineering/Runtime.

Final QA (2026-10-28): external QA Runner `2a286865bcffa1648ad81ab3b5c31689aa283746`,
with worktree-local scenarios/config retained under `.tia-bridge/hmi-js-02/`.
FAST `20261028T143708047689Z-c00ebad3f033427bb23b09e00aa257c7`: **40/40 PASS**
(19 Core HMI, 12 Host HMI, 9 Python HMI including the six HMI-JS-01 regressions).
STANDARD `20261028T143748881578Z-356d63bd1e0a486a912a5c6f30bd00e7`: **1079/1079 PASS**
(583 Core, 416 Host, 80 Python), no fail/skip. Both include offline locked restore,
Release builds (including Worker against installed V19), documentation/format/diff
checks; STANDARD also checks contract/vector generation. The existing STANDARD
filter excludes `ResiliencePerformanceTests` (five cases); this is functional QA,
not canonical D-11 requalification. No NF-11, Engineering or Runtime run.

## Live procedure and gates — not authorization

1. Review current D-11 and unresolved Worker resource qualification. NF-11 stays
   NOT_RUN, with no full qualification claim. Establish an authorized isolated copy,
   fresh AP19/PID/start/SID, exact Unified device-item path and module scope.
2. The first HMI qualification needs a **separately approved Engineering plan/lane**
   using the existing owner and durable engine. No HMI qualification-only bootstrap
   or E1-denial fallback is introduced. Without exact HMI Engineering E1 evidence,
   normal dispatch remains BLOCKED. Never self-enroll or substitute synthetic evidence.
3. An operator provisions a new protected Host configuration with `v19.hmi_export`
   matching the [binding schema](../contracts/hmi-js/export-binding.schema.json):
   new authorization UUID, exact project key (V19 target binds PID/start/AP19), device
   path/module, TIA/Worker builds, scope token, expected names and UTC window ≤30 min.
   Config/example files record an external approval; they do not grant it. E1 signer
   and operator/Host SID ACL checks remain mandatory. B1/bootstrap configuration is
   rejected for HMI export; its grant and qualification cannot be reused.
4. Require active signed **ENGINEERING_TEST/PASS** E1 for `project.export`, PROJECT
   scope, kind `hmi-script-module`, profile `v19.hmi.script-module.export.v1`, exact
   builds/getter-export read scope, CONTENT coverage and project key. `project_class`
   is `hmi-scope:` plus SHA-256 of compact UTF-8 snake_case JSON
   `{scope_token,device_path,module_name}` in that order; this is a qualification
   scope digest, not an object ID. Use `V19InspectionProfile.HmiExportScopeClass`;
   default .NET `Protocol.Json` string escaping is part of this digest (including
   escaped non-ASCII characters), so do not substitute a different JSON encoder.
   Qualification must verify the source/editor-area
   mapping of this native layout. Revalidate E1 before dispatch. OFFLINE_TEST denies.
5. Submit existing asynchronous `project.export` with a single exact scope token,
   `include_native=true` and fresh idempotency key; see the
   [synthetic request](../examples/hmi-js/request.project.export.synthetic.json).
   HTTP cannot choose a module/path/build/purpose/window. Existing IPC 1.1 framing,
   `InspectDispatch`, durable intent/status/result and one-owner scheduling are reused.
   The optional startup binding and opaque typed receipt are versioned by their
   explicit profile; the wire envelope and old 1.1 messages stay unchanged.
6. Worker checks binding/window/builds before Attach and again before Export,
   exports one module to create-new staging, checks identity/IsModified before/after
   and Disposes only its own attached session. Review actual files/encoding/markers/
   YAML and independently compare every function and global editor area to the slices.
7. After confirmed quiescence, a second deliberate authorized read uses a new key
   and directory. Compare native bundles and every function. Repeating the original
   key must return the same operation without another export. Timeout/fault requires
   preserved evidence and confirmed quiescence, never automatic retry.

TIA IMPORT remains **NOT_IMPLEMENTED**. No Save/Compile/Archive is added. Future
import requires independent qualification and the [04 synchronization saga](04_SYNCHRONIZATION.md)
and [ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md).

## Historical HMI-JS-01 — state at 05944ae

### Status and API evidence (historical)

The target Worker V19 integration is **not implemented** and no TIA project was attached. The local V19 installation was inspected read-only at `PublicAPI/V19/Siemens.Engineering.xml` and its corresponding `Siemens.Engineering.dll` / `Siemens.Engineering.Hmi.dll` references.

| Capability | Evidence | Status |
|---|---|---|
| Global script module type and module-level Export | Local V19 `PublicAPI/V19/Siemens.Engineering.xml` documents `HmiUnified.Scripts.HmiScriptModule`, its `Name`, and `Export(DirectoryInfo)` / `Export(DirectoryInfo,String)`, returning `IEnumerable<FileInfo>`. DLL metadata was not successfully reflected in this session. | DOCUMENTED; API_VERIFIED UNKNOWN; runtime behavior not proven |
| Module composition enumeration/export | Local V19 XML documents `HmiScriptModuleComposition` enumeration and `Export(DirectoryInfo)`. DLL metadata was not successfully reflected; entry point from target HMI software remains unproven. | DOCUMENTED; API_VERIFIED UNKNOWN |
| Complete function inventory and function text | No public function type/member giving complete source text was found in the V19 API XML. The module Export signature does not promise which files or source boundary are exported. | UNKNOWN |
| Screen-local and event scripts | No complete public screen/event script reader/export path was verified. | UNKNOWN |
| Native file format | Existing project docs record the expected pair `.hmi.js` + `.hmi.yml`; this MVP preserves any supplied files as opaque bytes, without assuming extensions/schema or reconstructing content. | PROJECT_DOCUMENTED; not newly verified from target project |
| Dependencies | No complete dependency enumeration was verified. Manifest accepts observations but does not infer dependencies from JavaScript text. | UNKNOWN |
| Actual project `T05_RecipeSim_V19_Testowy` | No attach, discovery, source read, or native export performed. | NOT_RUN |

The existing `DiscoveryV1` only identifies a Unified HMI software container. It does not enumerate script objects. A class or method signature is not evidence that it operates successfully against the target project's modules. No public online claim is used to fill that gap.

### Offline MVP (historical)

`scripts/hmi_js_sync.py` packages an operator-supplied inventory and already-exported native files. It does not invoke Siemens, parse/execute JS, normalize YAML, or claim source fidelity. The native bytes are copied unchanged under `Scripts/native/`; an editable `source.js` is emitted only when an inventory explicitly names `source_artifact`. Raw SHA-256, byte count, original filename, project/device/module identity, observed function/dependency arrays, and unsupported reasons are recorded in `Scripts/manifest.json`.

Names use NFC for path presentation, UTF-8 percent escapes for unsafe characters, and a SHA-256 identity suffix to avoid ordinary name collisions; original names remain in metadata. Existing non-empty destinations are rejected. The manifest and each packaged module are always marked incomplete/partial regardless of inventory claims, and project-level completeness is never inferred. The comparator classifies by object identity and represented content, functions, dependencies and observed locators. `MISSING` means absent from the compared observation and is explicitly `OBSERVATION_ONLY`; it is not a TIA deletion request. Incomplete unchanged coverage becomes `UNKNOWN`.

Input JSON shape:

```json
{
  "project_identity": "operator-bound project key",
  "hmi_device_identity": "operator-bound device key",
  "modules": [{
    "identity": "observed stable or session identity",
    "name": "module name",
    "native_artifacts": ["staging/module.hmi.js", "staging/module.hmi.yml"],
    "source_artifact": "curated-source/module.js",
    "functions": [],
    "dependencies": [],
    "coverage": "PARTIAL"
  }]
}
```

Do not place private scripts, project archives, or full TIA projects into a public Git repository automatically. Review the destination and artifacts as sensitive engineering source before staging. Keep unapproved customer data outside the repository.

### Limitations and qualification plan (historical)

Current source export, global discovery, function boundaries, local screen/event coverage, native export operation, dependency coverage, and project-specific behavior are not Engineering-verified. The inventory is an offline packaging contract, not a Siemens adapter or a qualified Workspace manifest. There is no import, setter, Compile, Save, Archive, or TIA write path in this package.

Future live qualification requires a **new purpose-specific read-only grant** bound to exact project/AP19, process/build, Worker build, operation (`HMI_SCRIPT_MODULE_ENUMERATE` and/or `HMI_SCRIPT_MODULE_EXPORT`), Unified family, module scope, read-only destination, window, and cycle/timeout budget. It must explicitly exclude import/write/compile/save/archive, screen/event export unless separately qualified, and grant inheritance from B1/E1. Use the approved project copy only after fresh PID/start/path/SID and project identity checks. First inventory composition names and module locators; then export one known module into a create-new isolated staging directory, record exact returned files and bytes, compare a second read, and confirm project `IsModified` unchanged. Preserve partial files on failure, report UNKNOWN/BLOCKED, and do not retry an uncertain call until quiescence is established. Only after operator review and renewed authorization may module-wide coverage be expanded. Never treat API presence, a clean compile, or identical text as round-trip proof.

### Controlled import plan (future, not implemented)

Import requires a separate ADR/specification and capability qualification for exact V19 build, object family, module scope, native bundle shape, replacement side effects, dependency impact, and preservation of unsupported vendor fields. It must use the existing immutable snapshot and synchronization saga, fresh identity and preconditions, authorized plan, verified backup, ExclusiveAccess drift check, post-import verification/readback and quarantine on uncertain outcome. No automatic replay, rollback, Save, or inference from this offline comparison is allowed.
