# TIA Bridge

TIA Bridge is a planned Windows application for synchronizing selected Siemens TIA Portal project objects with a Git-friendly file workspace.

The workspace is intended for VS Code and Codex. Codex edits files in that workspace; TIA Bridge owns communication with TIA Portal, export and import operations, comparison, validation, conflict handling, and safety checks. A human reviews and starts operations that affect a live project.

## Goals

- Represent selected project objects as files in a hierarchy that is understandable beside the TIA Portal project tree.
- Export supported objects from a TIA Portal project into a workspace.
- Compare project and workspace state and explain differences.
- Validate proposed changes before applying them.
- Import approved changes into a project with explicit conflict and backup handling.
- Keep work reviewable and versioned in Git.
- Start with a version-specific Siemens Openness V19 adapter and leave room for later adapters.

## Status

This repository contains architecture and planning documentation only. It does not yet contain a working application or a verified list of objects that Openness V19 can export or import.

## Initial repository layout

\`\`\`text
TIA_Bridge/
├── AGENTS.md
├── README.md
├── .editorconfig
├── .gitignore
├── docs/
│   ├── architecture.md
│   ├── sync-model.md
│   ├── formats.md
│   ├── safety.md
│   ├── roadmap.md
│   └── decisions/
│       └── 0001-file-workspace-and-adapter-boundary.md
├── src/
│   ├── TiaBridge.Core/
│   ├── TiaBridge.Adapters/
│   └── TiaBridge.Cli/
├── tests/
├── examples/
└── scripts/
\`\`\`

Folders intended for future code and examples include a short README or .gitkeep so the planned structure is visible in Git.

## MVP proposal

1. Detect and report the installed TIA Portal and Openness environment without modifying a project.
2. Connect to a user-selected project through an explicitly supported V19 workflow.
3. Export a documented, bounded set of object types into a workspace, preserving stable identity and source metadata where available.
4. Compare project and workspace state and produce a deterministic, reviewable report.
5. Validate an import plan and present its exact scope.
6. Apply only supported changes after explicit user confirmation, with a recoverable project backup or other documented recovery path.
7. Record provenance and operation results without storing credentials or unnecessary project data in logs.

The first release should prefer read-only discovery, export, and comparison. Import stays disabled for object types until round-trip behavior, validation, and recovery have been demonstrated for that exact type and TIA Portal version.

## Important boundaries

- TIA Bridge is the sole component that talks to TIA Portal. Editors and coding agents work on workspace files only.
- The workspace is not assumed to be a complete or lossless copy of a TIA Portal project.
- Export/import support is object-type and version specific. It must be established from Siemens documentation and tested against representative projects.
- An exported file being editable does not prove that importing it preserves all semantics, references, security settings, or project behavior.
- Unsupported objects and ambiguous changes must be reported and left untouched.
- Never import automatically just because Git reports a change. Require an explicit plan, validation result, and user action.
- A Git commit is useful provenance, but it is not a substitute for a project backup or tested recovery procedure.

## Documentation

- [Architecture](docs/architecture.md)
- [Synchronization model](docs/sync-model.md)
- [Formats and object mapping](docs/formats.md)
- [Safety](docs/safety.md)
- [Roadmap](docs/roadmap.md)
- [Decision 0001: file workspace and adapter boundary](docs/decisions/0001-file-workspace-and-adapter-boundary.md)
- [Contributor and agent guidance](AGENTS.md)

## Development status

There is no implementation, build, or test suite yet. Planned source areas are placeholders. When implementation begins, keep version-specific Siemens integration behind adapters and keep core comparison and planning logic testable without a TIA Portal installation.
