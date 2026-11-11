# Recipe Editor V19 qualification executable

Status 2026-11-11: **OFFLINE TESTED / ENGINEERING BLOCKED / RUNTIME NOT_RUN**.
This is a dedicated, T05-specific qualification operation in the requested
tool repository. It is not a Host/Worker capability, a general screen importer,
or permission to run against the original project. See the
[qualification report](../docs/T05_RECIPE_EDITOR_V19_QUALIFICATION.md).

Dependencies: Windows PowerShell 5.1 x64, .NET Framework csc, installed official
TIA Openness V19 PublicAPI and Python 3 (`py.exe`, standard-library ZIP CRC check).
The builder records source, EXE and PublicAPI SHA-256; it requires a new output
directory and does not replace installed binaries. No vendor DLL is copied.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_recipe_editor_v19.ps1 -OutputDirectory .tia-bridge/editor-build-NEW
py -3 scripts/test_recipe_editor_v19.py .tia-bridge/editor-build-NEW/RecipeEditorV19.exe
```

Each command writes to a **new** evidence directory. Existing directories are
rejected without writing into them. Arguments below are positional; invoke the
EXE directly. There is no automatic retry, timeout replay, rollback or recovery Save.

| Command | Additional arguments after output directory | Effect |
|---|---|---|
| `plan` | none | Offline 30-control/11-event plan and bounds/overlap validation |
| `metadata` | none | Public installed CLR property metadata, no Attach |
| `compile-policy-tests` | none | Six offline acceptance/rejection cases, no TIA call |
| `processes` | none | Official public process discovery, no Attach |
| `inspect` | exact open AP19 path | Read-only snapshot and native module exports |
| `archive` | exact original AP19 path | Read-only Archive of clean original; no Save or Compile |
| `retrieve` | verified ZAP19 path, new lab root | Official Retrieve to a new root below `X:\T05_EditorLab\`; then Close |
| `open` | lab AP19 path | Open a lab project in an owned headless Portal; wait for stdin, then Close without Save |
| `configure` | lab AP19 path, expected `before.json`, prepared native directory | Guarded lab-only configuration, HMI Compile, Save |
| `verify` | lab AP19 path, successful configure evidence directory | Separate Attach, exact post-Save snapshot comparison and readback receipt |

The original AP19 is fixed to
`C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19` for read-only
archival. `configure` rejects it **before discovery/Attach**. Lab writes require
a path below `X:\T05_EditorLab\`, exactly one matching open project/process,
one project, `T05_PLC_SIM`, `HMI_JSON_TEST/HMI_RT_1`, Unified software and Editor
screen 9601. This path binding is a containment guard, not independent evidence
of isolation. Only a copy retrieved from the recorded source archive may be used
in this qualification. Archive identity, complete ZIP stream read, Python CRC
and SHA-256 are checked. No PLC compile, online, download or Runtime API exists.

The caller must keep an owned `open` session alive with interactive stdin while
running separate commands. Native Siemens access requests require operator
approval; the tool does not enroll itself or automate dialogs. Closing the owned
lab Portal after uncertainty is not automatic recovery and must not discard
unreconciled state. Do not close it until fresh read-only evidence and a recovery
decision have been recorded.

## JavaScript preparation

Use the current supported LabExporter workflow, not Events import. The observed
EXE SHA-256 was `711af7b95ed4c51d63d0bb1b4c0bbf71d2bd13bc0d29ea8a35a991a55314baff`.
Recheck its build, CLI, source/tests and recipient before any actual run.
`force-sync` requires the full module collection; its native import unit is not
an individual module. The current repository package fails baseline integrity.
Do not change its hashes or reconstruct `.native` by hand.

After Attach is available, export all modules from the **lab copy** into a new
workspace using LabExporter. Review current modules against canonical source.
Overlay only the direct canonical JS parts of `T05RecipeCatalog` and
`T05RecipeEditorReadonly`, preserving all other exported source/native bytes.
Freeze and hash this workspace; run `force-sync --validate-only`, then the
supported lab-only `force-sync` with its own backup, HMI Compile, Save and
readback. Keep the original nine modified T05 files unchanged. Do not export
into canonical `Scripts/` or reference `Events/`.

`configure` additionally requires the exact assembled native `.hmi.js`/`.hmi.yml`
pairs for both dependent modules from that qualified sync input/readback. It
independently exports them and requires byte equality before changing the screen.
This does not itself assemble source or prove it was correctly prepared; retain
the LabExporter input/source binding and receipt. Then run a new `inspect` and
pass its exact `before.json` to `configure`.

## Scope and failure behavior

The operation creates missing controls only; wrong types, case collisions,
duplicate names and existing dynamics/property handlers on the 30 controls block
the operation. Its allowlisted setters compare values first. The expected public
screen tree must match before Apply and after backup under ExclusiveAccess.
All previously existing screen items are retained. Navigation events are untouched.
Thirteen redundant preview/background items are hidden; SAVE_Draft and DraftHint
are compacted within their existing region. The six new buttons and status areas
are placed in the middle panel and nineteen string IO fields in the right panel.
Tooltips identify input fields. The hidden context is not a visible input.
Public readback is not full preservation evidence for unexposed vendor state.

The two screen handlers and nine button handlers use only an async call to
`RecipeEditor.HandleRecipeEditor({type:"formEvent",screen:Screen,event:...})`.
There is no field-level change event or second editor state model.
`ApplyChanges` and `DiscardChanges` map to their existing form actions.

Before the first mutation, an integral lab archive is required and a persistent
intent is created below `X:\T05_EditorLab\state`. An existing intent blocks a
new configure. Failure after mutation reports `RECONCILIATION_REQUIRED`; read
fresh state, preserve the open lab session, and prepare a separate recovery plan.
Do not delete intent files to retry. A successful Save still requires a separate
`verify`; it checks exact saved state and closes only its own successful intent.
The HMI compiler is resolved only through the exact HMI ancestry; traversal stops
at its single-software HMI device and never selects a Project compiler. Diagnostics
are flattened recursively with bounds. Zero errors are required, including nested
messages. The only permitted warning is exactly the known Anonymous Runtime-user
diagnostic already allowed by the qualified LabExporter workflow. This installation
reports it as a nested warning despite top-level Success/0 errors/0 warnings.
The message remains in the receipt. Any other warning, multiple warning leaves,
error, unknown state or incomplete traversal stops before Save. Six offline tests
exercise these acceptance/rejection cases. This policy was prepared before any
screen mutation. Build11 later stopped before form Compile/Save on its empty-text
comparison; candidate build13 corrects that comparison and adds typed InputBehavior
evidence, but still requires full Engineering qualification. See the incident
and separate recovery proposal in the qualification report.

Finally run `inspect`, `configure` again with fresh input and the same dependent
native pairs, and another `inspect`. Require `NO_OP`, zero creates/writes/event
changes, no Compile/Save, 77 unique screen items, eleven exact handlers, identical
protected navigation/public configuration and unchanged JS pairs. **This live
sequence has not passed yet.** Engineering does not qualify ProcessValue timing,
text overflow/scrolling, client rendering, file saving or native Runtime behavior.
