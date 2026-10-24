# Architecture

> Materiał koncepcyjny zachowany z initial scaffold. Aktualna specyfikacja: [03_ARCHITECTURE.md](03_ARCHITECTURE.md) — procesy, scheduler i runtime. W zakresie autoryzacji stosuj [ADR 0002](decisions/0002-async-host-and-versioned-worker.md): zatwierdzony, ograniczony grant sesji może obejmować wiele kroków; nie ma import-on-save ani nieautoryzowanych zapisów. Opis poniżej nie deklaruje implementacji.

## Purpose

TIA Bridge mediates between a TIA Portal project and a Git-friendly workspace. Editors and coding agents change workspace files. TIA Bridge is responsible for reading and changing a project through a version-specific Siemens integration.

The workspace is a selected, documented representation. It is not presumed to be a complete project archive.

## Proposed components

### Core

The core owns version-neutral concepts and policies:

- project and object identity references;
- capability declarations;
- workspace manifests and path mapping;
- export snapshots and comparison results;
- change plans, preconditions, validation findings, and operation reports;
- conflict classification and policy;
- audit event schemas.

Core code should be testable without TIA Portal installed.

### Adapter boundary

An adapter implements the integration contract for a specific TIA Portal and Openness version. It reports its exact product/version identity and capabilities, then performs only operations that it explicitly declares.

The initial adapter target is Openness V19. Later versions should use separately identified adapters or clearly versioned implementations. Do not infer compatibility because two releases expose similarly named APIs.

A capability should include at least object kind, supported operation, constraints, and evidence status. Unknown capability is unsupported.

### CLI

The CLI is an explicit user interface over core workflows. It should provide commands for environment inspection, export, compare, plan, validate, and apply as these operations become implemented.

Commands that modify a project must display the selected project, adapter, object scope, and plan summary before requiring confirmation. Exit codes and machine-readable reports should be stable and documented.

### Workspace

The workspace contains exported object files, a manifest, and reports as appropriate. It should be possible to review changes with ordinary Git tools. The exact serialization and path rules remain to be validated for supported object types.

## Dependency direction

- CLI depends on core and adapter contracts.
- Vendor adapters depend on core contracts and Siemens assemblies.
- Core does not depend on vendor assemblies, the CLI, or a particular workspace checkout.
- Tests for core do not require Windows or TIA Portal. Adapter integration tests require a separately prepared, non-production environment.

## Process and platform boundary

The initial target is Windows because TIA Portal and its Openness integration are Windows products. Keep the core boundary independent enough that file comparison and planning can run in other environments if implementation choices allow it. Actual support must be established by implementation and compatibility testing.

## Failure handling

Every operation should return a structured report with operation ID, selected project identity, adapter identity, object counts, skipped/unsupported items, warnings, and errors. Reports should avoid embedding full source content or secrets.

An adapter failure must not be converted into a successful partial result. Partial work must be clearly marked and must not silently proceed to import.
