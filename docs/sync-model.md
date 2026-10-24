# Synchronization model

> Materiał koncepcyjny zachowany z initial scaffold. Aktualna specyfikacja: [04_SYNCHRONIZATION.md](04_SYNCHRONIZATION.md) — S/W/P/B, baseline i formalne SYNCED. W zakresie autoryzacji stosuj [ADR 0002](decisions/0002-async-host-and-versioned-worker.md): zatwierdzony, ograniczony grant sesji może obejmować wiele kroków; nie ma import-on-save ani nieautoryzowanych zapisów. Opis poniżej nie deklaruje implementacji.

## Sources of state

For each tracked object, the system may observe:

- the current project representation obtained through a declared adapter operation;
- the last successfully exported baseline recorded in workspace metadata;
- the current workspace representation.

The baseline is needed to distinguish an edit on either side from a new object or a deletion. A Git commit alone does not identify the state last synchronized with a particular TIA project.

## Three-way comparison

Compare project state (P), baseline state (B), and workspace state (W):

| Project vs baseline | Workspace vs baseline | Result |
| --- | --- | --- |
| unchanged | changed | Workspace proposal |
| changed | unchanged | Project-side change |
| changed to same semantic value | changed to same semantic value | Converged |
| changed differently | changed | Conflict |
| missing | unchanged | Project deletion candidate |
| unchanged | missing | Workspace deletion candidate |
| missing | missing | Absent |
| no baseline | present on one side | New object candidate; identity must be resolved |

These are candidate classifications. Adapter and object-specific rules may add constraints. A missing object is not automatically safe to delete.

## Export

1. Identify the selected project and adapter.
2. Check declared read/export capability for requested object kinds.
3. Enumerate or select bounded scope.
4. Export supported objects and preserve identity metadata where available.
5. Write files through a staging area; validate output and path containment.
6. Atomically replace the workspace representation where the platform permits.
7. Update baseline metadata only for objects whose export completed successfully.
8. Emit an operation report.

The exact enumeration, serialization, and identity behavior is adapter-specific and must be evidenced.

## Compare and plan

Comparison should be read-only. It should produce stable object references, classifications, and concise explanations. A plan is an immutable proposal with source project, adapter version, selected object scope, expected preconditions, intended operations, and hashes of relevant workspace inputs.

A plan should become stale if its project or workspace preconditions change. Recompute and request review instead of applying a stale plan.

## Validate and apply

Validation checks support, identity, references, syntax or schema where documented, project state, path safety, and adapter preconditions. A validation PASS means only that declared checks passed; it does not guarantee runtime behavior.

Apply executes a reviewed plan in bounded units. Each operation records outcomes. If an operation fails, stop according to the documented transaction boundary; report completed and incomplete work. Do not claim atomic rollback unless the adapter and project workflow have demonstrated it.

## Conflict policy

- Never resolve a conflict by silently preferring the workspace or project.
- Present the object identity, affected paths, detected changes, and available resolution choices.
- Require a fresh comparison after resolution.
- If the adapter cannot safely identify or update an object, leave it unchanged and report why.

## Workspace identity and paths

Object IDs and paths are separate. A rename or path mapping change must not be interpreted as deletion plus creation when stable identity is available. If identity is unavailable or ambiguous, stop and ask for a human mapping decision.
