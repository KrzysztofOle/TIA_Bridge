# Safety and data handling

> Materiał koncepcyjny zachowany z initial scaffold. Aktualna specyfikacja: [09_RISKS_AND_DECISIONS.md](09_RISKS_AND_DECISIONS.md) — grant sesji, ryzyka i eksperymenty. W zakresie autoryzacji stosuj [ADR 0002](decisions/0002-async-host-and-versioned-worker.md): zatwierdzony, ograniczony grant sesji może obejmować wiele kroków; nie ma import-on-save ani nieautoryzowanych zapisów. Opis poniżej nie deklaruje implementacji.

## Project protection

- Begin with inspection, export, and comparison workflows.
- Require an explicit, reviewable plan before project changes.
- Show the exact project, TIA version, adapter, object kinds, operation counts, and destructive actions.
- Require a separate confirmation for apply. A saved plan or successful validation is not authorization.
- Refuse stale plans when project or workspace preconditions differ.
- Treat deletion, rename, overwrite, and dependency changes as high impact.
- Define and verify a recovery path before enabling import for an object kind.
- Do not claim transactions or rollback unless demonstrated for the adapter workflow.

## Least privilege

Use the narrowest available process and filesystem permissions. Do not request or persist credentials unless an evidenced integration requires them. Store any required secrets using an operating-system protected mechanism and never place them in the workspace or Git.

## Untrusted inputs

Treat project exports, workspace files, paths, XML, archives, and metadata as untrusted. Limit file size and recursion, disable external entity and network resolution in parsers where relevant, validate archive entries before extraction, and prevent path traversal.

Do not execute scripts or code found in project content. Do not follow references that cause network access unless a separately reviewed feature explicitly requires it.

## Logging and audit

Logs should contain operation IDs, coarse counts, adapter/product identity, outcome, and safe error categories. Redact credentials, tokens, personal data, and source content by default. Provide an explicit diagnostic bundle policy before collecting detailed artifacts.

Audit records should identify the reviewed plan and actual outcomes. They should not imply that an operation was safe merely because it completed.

## Git and workspace hygiene

- Keep real project archives, backups, credentials, and customer data out of source control by default.
- Use synthetic examples.
- Review staged file names and content before committing exports.
- Document ignore patterns and provide a safe way to override them only when a user intentionally wants to version a particular artifact.
- A Git repository is not a project backup or disaster recovery plan.

## Operational safeguards

Any bulk operation should have bounded scope, cancellation behavior, progress reporting, and a clear partial-failure result. Do not retry a write automatically unless the adapter can prove the retry is idempotent for that operation.
