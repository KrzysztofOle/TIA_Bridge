# Decision 0003: HMI canonical model and native Siemens artifact boundaries

- Status: **Accepted**
- Date: **2026-10-26**
- Authority: six explicit user-approved architectural decisions (D-12).
- Scope: WinCC Unified, incomplete Openness V19 coverage and future V21 adapters.
- Nature: architecture only. **Acceptance is not implementation evidence, API qualification, or proof of native WinCC Unified screen export/import or lossless round-trip.**

## Context

TIA_Bridge 2 edits a curated file Workspace through CODEX while TIA integration belongs to isolated, version-specific workers. Openness may expose only a subset of an HMI object's configuration. API availability, a public class or an export method alone do not establish complete serialization, write safety, round-trip fidelity or cross-version support. Native Siemens exchange formats are vendor/version-specific. This decision develops [ADR 0001](0001-file-workspace-and-adapter-boundary.md) and preserves the execution, authorization and recovery boundaries of [ADR 0002](0002-async-host-and-versioned-worker.md).

## Considered alternatives

- **A — mirror native Siemens V21 XML/YAML:** closer superficial syntax, but unproven schema coverage, unstable version-specific dependency, ambiguous semantics for missing fields, and high accidental overwrite risk. Rejected as the canonical contract.
- **B — independent versioned Bridge canonical model with V19/V21 adapters:** semantic alignment with documented Unified object terminology while decoupling vendor serializations. **Accepted**.
- **C — native-only editable Workspace:** rejected for inability to establish reliable readable coverage, fine-grained ownership and guarded changes across unsupported fields.

## Six accepted decisions

1. **Independent canonical HMI model.** TIA_Bridge owns a versioned schema/semantic contract independent of Siemens XML/YAML. Match confirmed Unified object families, hierarchy and terminology where supported by evidence; never reconstruct a hypothetical V21 Siemens schema. Siemens exchange formats belong exclusively to qualified version-specific adapters.
2. **Preserve original native artifacts.** Retain byte-identical Siemens-exported XML, YAML, JavaScript and associated files (when actually available), linked to their complete `ExchangeUnit`, with raw SHA-256 and provenance: TIA version/build, adapter version/build, project and unit identity, export revision and artifact role/path. Distinguish **CANONICAL** (Bridge semantic projection), **SOURCE** (user/CODEX-editable representation), and **NATIVE** (immutable vendor export). Canonical is a logical projection, not a requirement for a second independently edited value; exact paths/layout require later specification. Do not fabricate missing native exports.
3. **Partial read cannot rebuild an entire object.** Missing/unread/unsupported values are not null or empty and must never be cleared, removed or overwritten by inference. `vendor_fields` retains only actual observed data with trustworthy provenance, not invisible vendor properties. Prefer explicitly qualified property-level patch/update over whole-object regeneration if API coverage is incomplete; deny replacement where unknown fields could be destroyed.
4. **Fine-grained capabilities.** Qualify independently by TIA version/build, adapter version/build, device family (Unified versus Classic), object kind, operation (enumerate/read/export/import/create/update/delete), representation and coverage/scope. READ ≠ WRITE; EXPORT ≠ IMPORT; success ≠ lossless round-trip. Unknown remains deny-by-default. Reuse existing [Core/Capabilities](../12_WORKSPACE_CAPABILITIES.md), `CapabilityRegistry` and coverage CONTENT/CONFIG/API/STRUCTURE/DEPENDENCIES/INVENTORY without prescribing a new implementation contract. Existing exact registry key and OperationMethod remain normative; finer distinctions require later compatible design/qualification, not an unapproved schema change.
5. **Separately qualify potentially destructive imports.** Distinguish single-property edit, partial-structure edit, ExchangeUnit replacement, delete/recreate and unknown-impact import. Method existence is insufficient. Require bounded impact/dependency analysis, preservation of unsupported fields, relevant plan/authorization, backup, ExclusiveAccess, quarantine/reconciliation on uncertainty, compilation when applicable, Save and independent fresh readback under the existing [synchronization saga](../04_SYNCHRONIZATION.md). Layout import in Unified warrants specific investigation; destructive outcomes are **not confirmed** absent version-specific evidence and conditions.
6. **Evidence-led evolution.** Add HMI fields and readers only from documented properties, observed API results and qualified experiments. Use explicit type/version-specific property allowlists, not default unbounded reflection sweeps. V21 external experiments inform risks but never prove identical V19 behavior. Do not invent Siemens XML elements, XSD namespaces, YAML keys, IDs or full-screen shape from API class names.

## Workspace consequences

Keep `WorkspaceObject`, stable identity/locator, coverage and `ExchangeUnit` ownership defined in [12](../12_WORKSPACE_CAPABILITIES.md) and the v1 contracts. SOURCE and NATIVE file roles remain as implemented; CANONICAL denotes the semantic/model projection already represented by model hash/metadata rather than a new v1 file role. Native bytes are immutable and hashed raw; generated source is separately editable. Unit boundaries, not individual source files, govern native Apply atomicity. Existing path/identity validation and no-overwrite-on-export rules remain unchanged.

## Core/Capabilities consequences

Retain existing `Assess`, exact version/build matching, required qualification evidence and deny defaults; no new enum, DTO, operation or registry schema is approved here. Coverage COMPLETE requires evidence for the specifically assessed dimensions and scope; PARTIAL/UNKNOWN/UNSUPPORTED retains its reason. An unobserved property must remain unknown and cannot silently become default. Qualifying a read, native exchange or property patch does not qualify the whole object, other operations, other families, or another TIA build.

## V19/V21 adapter consequences

V19 reader discovery after X-01 is read-only, bounded by approved property profiles and actual public API. Future V21 adapter is independent, with its own assemblies, capabilities and live qualifications; no inferred V21→V19 compatibility, no Classic→Unified inheritance. Store provenance with artifacts per version; preserve native bundles unchanged. This document neither widens X-01 nor adds new live tests to it.

## Synchronization and Apply consequences

Do not redefine `SYNCED`. Incomplete HMI API or structure evidence cannot prove equivalence for unobservable properties, even if text, model hashes, import, compilation or accessible readback appear successful. Existing scope-based comparison, baseline CAS, three-way plan, frozen snapshot, dependency validation and verification receipt criteria apply unmodified. Unsafe full-object replacement is blocked unless specifically qualified. Timeout/worker loss leaves `RECONCILIATION_REQUIRED`; no automatic replay or rollback.

## Limitations and open questions

- Actual native export/import availability and complete screen/item/event/animation/faceplate representation for **Unified V19 and V21**: UNKNOWN pending qualified tests.
- Native file extensions, field names, versioned schemas, round-trip default-value behavior and stable vendor identifiers: not assumed.
- Which properties admit safe in-place update; which imports replace/delete structure, including Unified screen layout: UNKNOWN.
- Appropriate physical file arrangement and semantic HMI schema version: future specification, not this ADR.
- How to evidence unobservable changes without claiming full fidelity: qualify explicitly bounded scope or deny Apply.

## Relations and qualification gates

[ADR 0001](0001-file-workspace-and-adapter-boundary.md) establishes curated files and versioned adapters; this ADR specializes the HMI canonical/vendor boundary. [ADR 0002](0002-async-host-and-versioned-worker.md) establishes asynchronous runtime, grants and recovery; unchanged here. D-11 X-01 limited read-only gate and PLC MVP sequence are unchanged.

Future qualification for each concrete tuple **TIA version/build + adapter build + device family + object kind + operation + representation + exact scope** requires: documented API or actual version-tagged observations, safe allowlisted read, provenance and raw hashes of any native files, inventory/dependency and unknown-field assessment; for mutating operations additionally impact/side-effect characterization, authorized isolated test copy, verified backup, preconditions/ExclusiveAccess, deliberate negative/fault experiments, independent readback (and compile/Save as applicable), and demonstrated non-loss of protected fields. Round-trip must be qualified separately and never inferred from success alone. X-04 should target Unified structure and supported public reader separately from import-risk trials.

## Sources and evidence levels

- **USER_ACCEPTED (normative):** six decisions supplied and accepted by the user on 2026-10-26; they define policy, not implementation.
- **PROJECT_DOCUMENTED:** [Workspace/Capabilities](../12_WORKSPACE_CAPABILITIES.md), [synchronization](../04_SYNCHRONIZATION.md), [architecture](../03_ARCHITECTURE.md), [integration](../06_TIA_INTEGRATION.md), and [risk register](../09_RISKS_AND_DECISIONS.md) describe local design and offline Core evidence, not vendor behavior.
- **SIEMENS_DOCUMENTED (V21):** [Openness What's New](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/what-s-new-in-tia-portal-openness) and [basic export/import principles](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/overview/basic-principles-of-importing/exporting) support version-specific caution. They are not proof of complete Unified native serialization.
- **INFERRED:** canonical isolation and strict safety gates reduce loss/compatibility risk; this is an architectural rationale.
- **UNKNOWN / NO VERIFIED_EXPORT:** no live V19/V21 export, import or full-screen round-trip is qualified by this ADR; any external V21 experiments are risk indicators, not proof of V19 behavior.
