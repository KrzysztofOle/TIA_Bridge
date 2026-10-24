# TIA Bridge

TIA Bridge is a planned Windows application for synchronizing selected Siemens TIA Portal project objects with a Git-friendly file workspace.

VS Code and Codex work on workspace files. TIA Bridge owns communication with TIA Portal, export, comparison, validation, import, conflict handling, and safety checks. A human reviews and starts operations that affect a live project.

## Goals

- Represent selected project objects as files in a hierarchy understandable beside the TIA Portal project tree.
- Export supported objects into a workspace and compare project state with workspace state.
- Validate proposed changes and import approved changes with explicit conflict and recovery handling.
- Keep work reviewable and versioned in Git.
- Start with a version-specific Siemens Openness V19 adapter and add later releases only with compatibility evidence.

## Status

This repository contains architecture and planning documentation only. It has no working application and no verified list of object types that Openness V19 can export or import.

## Repository layout

The initial scaffold includes `docs/` (architecture, synchronization model, formats, safety, roadmap, and decisions), `src/TiaBridge.Core/`, `src/TiaBridge.Adapters/`, `src/TiaBridge.Cli/`, `tests/`, `examples/`, and `scripts/`.

## MVP proposal

1. Inspect the installed TIA Portal and Openness environment without modifying a project.
2. Connect to a user-selected project through a documented, supported V19 workflow.
3. Export a bounded set of evidenced object types, preserving identity and source metadata where available.
4. Compare project and workspace state and produce a deterministic report.
5. Validate an import plan and show its exact scope.
6. Apply only supported changes after explicit confirmation and with a documented recovery path.
7. Record operation results without storing credentials or unnecessary project data in logs.

The first release should focus on read-only discovery, export, and comparison. Import remains unavailable for an object kind until round-trip behavior, validation, and recovery are demonstrated for that kind and TIA Portal version.

## Boundaries and limitations

- TIA Bridge is the only component that communicates with TIA Portal. Editors and coding agents work on workspace files only.
- The workspace is a selected representation, not a complete or necessarily lossless copy of a TIA Portal project.
- Export and import support is object-kind and version specific. Establish it from Siemens documentation and representative tests.
- An editable export does not prove that import preserves semantics, references, security settings, or project behavior.
- Report unsupported objects and ambiguous changes without changing them.
- Never import automatically because Git reports a change. Require a reviewed plan, validation, and explicit user action.
- Git history helps review and provenance; it does not replace project backup or a tested recovery procedure.

## Documentation

- [Architecture](docs/architecture.md)
- [Synchronization model](docs/sync-model.md)
- [Formats and object mapping](docs/formats.md)
- [Safety](docs/safety.md)
- [Roadmap](docs/roadmap.md)
- [Decision 0001: file workspace and adapter boundary](docs/decisions/0001-file-workspace-and-adapter-boundary.md)
- [Contributor and agent guidance](AGENTS.md)

## Development status

There is no implementation, build, or test suite yet. Planned source areas are documentation placeholders. Keep version-specific Siemens integration behind adapters and keep core comparison and planning logic testable without a TIA Portal installation.
