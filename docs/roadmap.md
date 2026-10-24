# Roadmap

> Materiał koncepcyjny zachowany z initial scaffold. Aktualna specyfikacja: [08_IMPLEMENTATION_PLAN.md](08_IMPLEMENTATION_PLAN.md) — backlog etapów 0–11 i bramki. W zakresie autoryzacji stosuj [ADR 0002](decisions/0002-async-host-and-versioned-worker.md): zatwierdzony, ograniczony grant sesji może obejmować wiele kroków; nie ma import-on-save ani nieautoryzowanych zapisów. Opis poniżej nie deklaruje implementacji.

This roadmap is a proposal. A milestone is complete only when its acceptance evidence is recorded.

## Stage 0: evidence and design

- Inventory the installed and supported Openness V19 workflows from Siemens primary documentation.
- Build an object capability matrix with exact export, comparison, validation, and import claims.
- Decide project selection, connection, permissions, and backup workflow.
- Define a synthetic fixture policy and a safe integration test environment.

Exit criteria: cited evidence for each claimed capability; unsupported and unknown areas are explicit.

## Stage 1: workspace and read-only core

- Define manifest schema, stable object references, path rules, and baseline model.
- Implement deterministic path validation and file hashing.
- Implement three-way comparison and report generation on synthetic inputs.
- Implement environment inspection without project mutation.

Exit criteria: repeatable synthetic tests; no vendor dependency in core; clear reports for ambiguity and unsupported objects.

## Stage 2: V19 discovery and export

- Add a version-specific Openness V19 adapter.
- Verify authorization and project connection behavior from Siemens documentation.
- Export only the first bounded set of evidenced object kinds.
- Stage and validate workspace writes; update baseline only on successful export.

Exit criteria: integration evidence on representative, non-production projects; documented limitations and recovery guidance.

## Stage 3: plans and validation

- Produce immutable change plans with explicit preconditions and file hashes.
- Validate supported changes and identify stale plans.
- Present human-readable and machine-readable plan reports.

Exit criteria: invalid, ambiguous, unsupported, and stale plans are blocked in synthetic and adapter tests.

## Stage 4: limited import

- Enable import one object kind at a time after round-trip and failure behavior is demonstrated.
- Require explicit confirmation and verified recovery procedure.
- Report partial completion and stop conditions accurately.

Exit criteria: recorded integration evidence for each enabled operation; destructive cases require dedicated review.

## Stage 5: later TIA Portal releases

- Add a distinct adapter or versioned adapter implementation for each later release.
- Re-run capability and compatibility checks. Do not inherit V19 claims without evidence.

Exit criteria: per-release capability matrix and integration results.

## Deferred

- Automatic synchronization on file save or Git events.
- Unattended project writes.
- Broad claims of full-project fidelity.
- Multi-user merge semantics before identity and conflict handling are proven.
