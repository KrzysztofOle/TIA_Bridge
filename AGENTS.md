# Guidance for contributors and coding agents

## Project boundary

- Work on files in the TIA Bridge workspace only. Do not use TIA Portal UI, Openness, project files, or vendor tools unless the task explicitly asks for an integration operation.
- TIA Bridge owns all communication with TIA Portal. Keep Siemens-specific behavior behind a versioned adapter.
- Do not claim support for an object type, import mode, round-trip property, or TIA Portal release without a source and reproducible evidence.
- Preserve unknown vendor data when a documented workflow permits it; otherwise block or report the object as unsupported.

## Change discipline

- Read the architecture, synchronization model, formats, and safety documents before changing synchronization behavior.
- Keep object identity separate from workspace paths. Paths are presentation and can change.
- Make planning, comparison, and serialization deterministic.
- Treat project files and exported content as untrusted input. Avoid executing content or resolving external references during parsing.
- Never log credentials, authorization material, or full customer project content by default.
- Import operations must show a bounded plan, validate preconditions, and require explicit user confirmation.
- Do not add an automatic import-on-save or import-on-Git-change workflow.
- Keep version-specific API calls and capability declarations in the matching adapter.
- Update documentation and tests with implementation changes. Tests must use synthetic projects unless a task explicitly provides a safe test project.

## Current state

This repository is a documentation scaffold. There is no application implementation yet. Do not infer functionality from planned folders.
