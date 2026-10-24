# Decision 0001: file workspace and adapter boundary

- Status: accepted as an initial design direction
- Date: 2026-10-24

## Context

The project aims to make selected TIA Portal project elements accessible as ordinary files for VS Code, Codex, and Git, while keeping TIA Portal integration in a dedicated application. The initial integration target is Openness V19, with later product versions considered separately.

## Decision

1. Codex and editors operate only on workspace files.
2. TIA Bridge owns project discovery, export, comparison, validation, import, conflict handling, and project safety.
3. The workspace is a curated representation of selected objects, not a promise of full project serialization.
4. Siemens integration is behind a version-specific adapter with explicit capability declarations.
5. Project writes require a reviewed plan, fresh preconditions, and explicit user action.
6. Git provides review and provenance but does not replace project backup or recovery.

## Consequences

- Core comparison and plan logic can be tested without a TIA Portal installation.
- Each object kind and operation needs separate evidence and support status.
- Filesystem paths do not define TIA object identity.
- Import is delayed until exact behavior and recovery have been demonstrated.
- New TIA releases need compatibility evidence rather than an assumed version upgrade.

## Evidence still required

This decision does not establish what Openness V19 can export or import, what authorization steps apply, or whether any workflow is lossless. Those questions require current Siemens documentation and representative integration tests.
