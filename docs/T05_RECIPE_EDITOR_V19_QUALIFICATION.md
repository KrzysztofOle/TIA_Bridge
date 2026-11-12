# T05 Recipe Editor — Openness V19 qualification, 2026-11-11

Update 2026-11-12: authorized finalization stopped at read-only preflight with
`EXACT_OPEN_PROJECT_NOT_UNIQUE:0`. The exact lab AP19 still exists on disk, but
the former lab process/owner is absent; only the base test project is advertised.
No Attach to that other project, Compile, Save or intent closure was performed.
Current IsModified and preservation of the previously unsaved form are UNKNOWN.
Engineering remains BLOCKED; Runtime NOT_RUN; original INTENT remains active.
New evidence is below the original evidence root in
`engineering-finalization-20261112T060307/report.md` and `result.json`.

**Result: BLOCKED / RECONCILIATION_REQUIRED.** JavaScript synchronization passed
Engineering on the isolated copy. The form operation created thirty controls and
configured eleven required handlers, then stopped before Compile/Save because
its readback comparison rejected Siemens serialization of an empty paragraph.
The original project was not modified by CODEX.

The optional candidate-build13 read-only reconciliation did not return from Attach
during the observation window and yielded no new snapshot. Only that owned
read-only diagnostic was terminated after checking its exact path/command line.
The dirty lab Portal was retained. No further mutation was attempted.

After the operator subsequently confirmed build13 access, a new read-only Attach
completed successfully. Receipt `build13-readonly-reconciliation.json` in the
evidence root records `configurationMatches=True`, 77 unique items and all 19
IO fields with `AcceptOnDeactivated=True`. The public screen tree, handlers and
all eight native module pairs exactly match the post-failure state. The original
INTENT is unchanged; IsModified remains True. This verifies the corrected
comparison against live Engineering state without Compile, Save or configure
replay. Overall qualification remains BLOCKED / RECONCILIATION_REQUIRED.

The dedicated operation is [recipe_editor_v19.cs](../scripts/recipe_editor_v19.cs),
with [candidate builder](../scripts/build_recipe_editor_v19.ps1),
[offline checks](../scripts/test_recipe_editor_v19.py) and
[usage/safety contract](../scripts/recipe_editor_v19.md). Production Host/Worker,
capability registry and DTOs are unchanged. This is a separate, explicitly
requested laboratory qualification, not a general screen importer or Events
import through LabExporter.

| Check | Actual result |
|---|---|
| Original live identity | Exact AP19, T05_PLC_SIM, HMI_JSON_TEST/HMI_RT_1; clean |
| Source archive | PASS: complete ZIP read, CRC, AP19 identity, SHA-256 |
| Official Retrieve | PASS: new root below X:/T05_EditorLab, separate PID 27476 |
| Source versus retrieved public screen | MATCH; both initially clean, 47 items |
| JS package validation on fresh export | PASS_INPUT_READY |
| Catalog and Editor synchronization | PASS: import, HMI Compile, Save, exact readback |
| JS independent reattach/readback | MATCH for both prepared native pairs |
| Six protected JS module pairs | 6/6 unchanged |
| Controls created | 30: 19 IO fields, 5 texts, 6 buttons; total screen items 77 |
| Required handlers configured/read | 11: 2 screen, 9 Tapped; including Apply/Discard |
| Existing screen items/navigation | 47 retained; NAV_0–NAV_3 public trees unchanged |
| Existing property changes | Only 17 planned: 13 visibility, 4 geometry values |
| Form configuration readback | Build11 rejected empty EDIT_Context serialization |
| Form HMI Compile / Save | NOT_RUN; stopped before both |
| Lab final state after form attempt | IsModified=True, persistent INTENT, quarantine retained |
| Saved form independent readback / NO_OP rerun | NOT_RUN |
| Original final readback | Public snapshot byte-identical, all 8 JS pairs identical, IsModified=False |
| PLC / Runtime / download / commit / push | NOT_RUN |

## Provenance and isolation

Original: C:/RMA/TIA_TEST/T05_RecipeSim_V19/T05_RecipeSim_V19.ap19, PID 19244.
Lab: X:/T05_EditorLab/qualification-20261111/T05_RecipeSim_V19/T05_RecipeSim_V19.ap19,
PID 27476, owned headless Engineering instance. The copy was created only through
public ProjectComposition.Retrieve from the verified source archive. Public
identity and initial screen/native module contents were checked independently.
No online, PLC compilation, Runtime, download or machine operation was used.
The original was used only for read-only discovery, inspection and Archive.

Source archive SHA-256:
`c4b565470f8f040ba17ad40335d531a2d87cadb93901ea742430a0787a836eb6`.
Pre-form lab archive SHA-256:
`b7061be0fa554ab36cacfe04159bc9340ab702bec21ac4545536b20b3d8e374e`.
Both contain exactly the expected AP19 and passed CRC. The separate JS session
also retained its verified LabExporter pre-import backup. These are separate
bounded modification sessions, not repeated backups before each edit.

Final original readback compared the full advertised public Editor tree, its
properties/events and all eight native JS/YAML pairs. This is verified-scope
proof, not full serialization proof for unexposed vendor state or unrelated PLC
contents. A direct file hash attempt was denied by the open AP19 lock; the project
was not closed or unlocked to obtain it.

## JavaScript workflow

Canonical direct JS parts for T05RecipeCatalog and T05RecipeEditorReadonly were
copied to a fresh lab-exported package. All six other modules and all fresh native
baselines were preserved. LabExporter requires the complete collection; no
selective-module or Events import was assumed. The supported full-collection sync
modified only the two prepared module contents, and raw pairs for the other six
were independently checked unchanged.

LabExporter SHA-256:
`711af7b95ed4c51d63d0bb1b4c0bbf71d2bd13bc0d29ea8a35a991a55314baff`.
Current binary CLI/native-integrity tests: 17/17 PASS. Existing form tests:
13/13 PASS offline. JS Compile returned Success/0 errors/0 warnings at top level,
with the known nested Anonymous Runtime-user diagnostic retained in the report
and accepted by the existing explicit allowlist. Save returned IsModified=False.
A separate Attach/export confirmed both module pairs equal the frozen prepared
input. Canonical T05 source files, Events and manifests were not changed.

The original repository package initially failed offline validation because four
native JS hashes differ from their manifests (RecipeFileApiTest, Catalog, Editor,
Sync). Byte lengths match. This history remains in native-baseline-audit.json.
No hashes were repaired manually. The fresh export workflow resolved the lab
package precondition without altering the original repository baselines.

## Form failure and correction

The mutating test used build11. After verified backup and fresh expected-state
checks, it created thirty unique controls, set their bounded geometry, string
formats and labels, and installed the eleven async formEvent handlers. Public
readback confirms all required names/types, captured properties and handlers.
All original objects remain. The dedicated operation preserves navigation code;
its explicit layout patch hides thirteen preview/background objects and compacts
SAVE_Draft/DraftHint. Input labels use tooltips; client rendering, long-text
scrolling/overflow and ProcessValue timing remain outside Engineering proof.

Build11 expected empty rich text as `<body><p></p></body>` while Siemens returned
`<body><p/></body>`. Exact string comparison therefore raised
CONFIGURATION_READBACK_MISMATCH. No HMI Compile or Save followed. The durable
intent remains and blocks another configure. The original failure is preserved.

The candidate source now generates the observed canonical empty representation,
records typed InputBehavior fields in snapshots, and preserves an after-mismatch
snapshot for future failures. Candidate build13 compiles and passes offline plan,
geometry, event, target-denial, output-preservation, empty-text regression and
six diagnostic-policy checks. These changes do not retroactively qualify build11
or complete the interrupted Engineering session. Candidate configure was not
replayed. Read-only reconciliation is recorded separately when available.

Compiler selection is restricted to the verified HMI ancestry/single-software
HMI device, never Project. Nested diagnostics are bounded and recursively read;
only the exact known Anonymous warning is allowed, with no errors or additional
warning leaves. A complete repeat must still prove zero writes/creates/events,
no Compile/Save, exact dependency pairs and unchanged protected state.

## Evidence and next permitted work

Evidence root:
`X:/T05_UserFiles/UserFiles/T05_TIA_Bridge/editor-openness-qualification-20261111`.
Key receipts: js-phase-result.json, six-protected-modules-after-js.json,
configuration-mismatch-analysis.json, protected-screen-before-after.json,
target-final-comparison.json, nine-files-before.json, nine-files-after.json and
final-report.json. Builds, raw before/after snapshots, archives and failure are
under this repository's ignored `.tia-bridge/editor-*` directories.

JS receipt:
`X:/TESTY_TIA_Bridge/exports/LAB-03D-readback-4a574687a2424785b697b13ca6c0cce2/force-import-readback.json`.
JS report:
`X:/TESTY_TIA_Bridge/evidence/LAB-03P-force-sync-4a574687a2424785b697b13ca6c0cce2.json`.
Form failure:
`.tia-bridge/editor-lab-configure01/failure.json`.
Fresh post-failure read:
`.tia-bridge/editor-lab-after-failure01/before.json`.

Keep the dirty lab project open. Do not delete the intent, retry configure,
rollback, Save or close/discard it automatically. A separate recovery plan must
bind fresh exact state, the original failure/build and retained archive; review
whether only Compile/Save/readback are needed after validating the corrected
comparison, without reapplying controls/events. Then independently read back and
qualify a genuine NO_OP repeat. The current tool has no automatic recovery
handler; its successful-only verify does not release this failed intent.
The original target remains blocked from configure in the qualification tool.

Initial read-only native access attempts and one security timeout are retained;
they preceded screen mutation. Operator confirmation allowed subsequent reads.
No self-enrollment or GUI automation was used.

API evidence: installed Siemens PublicAPI/V19 XML and assemblies expose the exact
typed Create<T>, EventHandlers, IHmiScript, IO field/InputBehavior, Archive/Retrieve,
HMI ICompilable and Save calls used here. Reference:
[Siemens Openness V19 manual](https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf).
Observed creation/property/event support does not qualify completed Save,
idempotency, general screen import or Runtime.
