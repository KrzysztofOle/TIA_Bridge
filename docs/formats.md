# Formats and object mapping

## Principle

The workspace format is defined per object kind and adapter version. File extensions and an exportable file do not establish that a representation is lossless, editable, or importable.

Use Siemens-supported export/import mechanisms where they are available and appropriate. Confirm exact object support, restrictions, and version behavior from Siemens documentation and representative integration tests before advertising a capability.

## Object capability registry

Maintain a capability table with one row per object kind and operation:

| Field | Meaning |
| --- | --- |
| TIA version | Exact product release and adapter build |
| Object kind | Stable internal name and vendor object category |
| Enumerate/read/export | Separately declared support and constraints |
| Compare | Semantic or byte-level method and known limits |
| Validate/import/delete | Separately declared support and constraints |
| Identity | Source of stable identity and uniqueness scope |
| File mapping | Workspace paths and file set |
| Evidence | Documentation reference and integration test status |

Until a row is evidenced, the operation is unsupported.

## Workspace metadata

A future manifest should record schema version, project identity fingerprint, adapter identity, export timestamp, object references, relative file paths, content hashes, and baseline references. Avoid storing credentials, machine-specific absolute paths, or unnecessary project data.

Metadata schema changes need explicit migration behavior. Unknown fields should not be discarded by older tooling without a clear compatibility policy.

## Serialization requirements

For each supported kind, document:

- encoding, newline, and deterministic ordering;
- whether files are vendor-native exports or TIA Bridge representations;
- which values are preserved, normalized, or omitted;
- reference and dependency handling;
- content hash rules;
- path escaping and collision behavior;
- round-trip evidence and known lossy transformations.

## Path safety

Workspace paths must be relative to the configured root. Reject rooted paths, parent traversal, device names that are unsafe on supported filesystems, ambiguous case-only collisions, and symlinks or reparse points that escape the workspace. Keep TIA object identity in metadata rather than deriving it solely from a filename.

## Initial status

No object kind is currently declared supported. The first implementation must establish a small object matrix using authoritative Siemens materials and tests before enabling import.
