# HMI-JS-01 — JavaScript export and synchronization MVP

## Status and API evidence

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

## Offline MVP

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

## Limitations and qualification plan

Current source export, global discovery, function boundaries, local screen/event coverage, native export operation, dependency coverage, and project-specific behavior are not Engineering-verified. The inventory is an offline packaging contract, not a Siemens adapter or a qualified Workspace manifest. There is no import, setter, Compile, Save, Archive, or TIA write path in this package.

Future live qualification requires a **new purpose-specific read-only grant** bound to exact project/AP19, process/build, Worker build, operation (`HMI_SCRIPT_MODULE_ENUMERATE` and/or `HMI_SCRIPT_MODULE_EXPORT`), Unified family, module scope, read-only destination, window, and cycle/timeout budget. It must explicitly exclude import/write/compile/save/archive, screen/event export unless separately qualified, and grant inheritance from B1/E1. Use the approved project copy only after fresh PID/start/path/SID and project identity checks. First inventory composition names and module locators; then export one known module into a create-new isolated staging directory, record exact returned files and bytes, compare a second read, and confirm project `IsModified` unchanged. Preserve partial files on failure, report UNKNOWN/BLOCKED, and do not retry an uncertain call until quiescence is established. Only after operator review and renewed authorization may module-wide coverage be expanded. Never treat API presence, a clean compile, or identical text as round-trip proof.

## Controlled import plan (future, not implemented)

Import requires a separate ADR/specification and capability qualification for exact V19 build, object family, module scope, native bundle shape, replacement side effects, dependency impact, and preservation of unsupported vendor fields. It must use the existing immutable snapshot and synchronization saga, fresh identity and preconditions, authorized plan, verified backup, ExclusiveAccess drift check, post-import verification/readback and quarantine on uncertain outcome. No automatic replay, rollback, Save, or inference from this offline comparison is allowed.
