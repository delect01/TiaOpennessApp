---
name: tia-portal-openness
description: Siemens TIA Portal Openness, Windows PowerShell, Siemens.Engineering, project generation, import/export, and automated TIA Portal engineering. Use when writing, reviewing, debugging, or planning an Openness automation integration.
---

# TIA Portal Openness

For this workspace, interpret `TIA` as `TIA Portal V21`. Preserve explicit
version references when compatibility or installed API details matter.

Use this skill for controlled automation through Siemens TIA Portal Openness.
Favor supported APIs and version-matched references over UI automation or
guessed API members.

## PowerShell First

Use Windows PowerShell 5.1 as the default language for test tools and early
automation. It is a practical host for .NET assemblies and short engineering
workflows on systems running TIA Portal.

- Start scripts with `#Requires -Version 5.1` when they depend on Windows
  PowerShell behavior or TIA-installed .NET Framework assemblies.
- Use `Set-StrictMode -Version Latest` and `$ErrorActionPreference = 'Stop'`.
- Keep parameters, project paths, output locations, and dry-run behavior
  explicit. Use `param(...)` rather than interactive prompts for repeatable
  testing.
- Load only version-matched `Siemens.Engineering` assemblies. Resolve paths
  from explicit configuration or documented installation locations, not from a
  silently assumed TIA version.
- Use `try`/`finally` to close/dispose portal and project objects. Surface full
  exception details, including inner exceptions, in a test-friendly report.
- Structure scripts into small functions with defined inputs and outputs.
  Isolate TIA/Openness calls from file parsing and orchestration so they can be
  migrated later to C#, Python, or another production language.
- Do not use UI keystroke/click automation when a supported Openness API or
  import/export workflow exists.

Use C# only when the user asks for it or when a durable compiled tool, complex
domain model, performance, deployment, or testability requirement justifies
the migration. Preserve the PowerShell tool's input/output contract during
that migration.

## Compatibility First

Before implementing or diagnosing, establish:

- TIA Portal version and update level used to create and run the automation.
- Installed Openness component and the `Siemens.Engineering` assemblies being
  referenced.
- Target .NET Framework/runtime and process architecture required by that TIA
  release, including the PowerShell host architecture.
- Whether the executable is launched interactively, by a service, CI agent, or
  scheduled task, plus the Windows user and permissions involved.
- Whether a graphical TIA Portal instance is required or a headless workflow is
  supported for the specific release and action.

Do not mix assemblies from different TIA Portal versions. Use the XML API
documentation and examples installed with the matching TIA Portal release to
confirm namespaces, member availability, import/export formats, and behavior.

## Safe Automation Pattern

Use this sequence for code and design reviews:

1. Validate configuration, input paths, project identity, and target version.
2. Start or attach to the intended TIA Portal process according to the
   version-supported API pattern.
3. Open the project with the minimum required access and locate objects by
   validated names/types rather than positional assumptions.
4. Inspect and report the proposed changes before applying mutations when the
   workflow permits.
5. Apply narrow, deterministic changes. Avoid deleting/recreating objects when
   an update is supported and preservation of references matters.
6. Save deliberately, then close/dispose project and portal objects even when
   operations fail.
7. Report the project path, objects changed, generated artifacts, and complete
   exception details without exposing secrets.

Always use deterministic cleanup such as PowerShell `try`/`finally` (or C#'s
`using`/`try`-`finally` when applicable) appropriate to the actual object
interfaces in the referenced API. Automation must not leave orphaned TIA Portal
processes or locked project files.

## Implementation Guidance

- Keep project paths configurable and normalize them before use.
- Treat project archives, imports, and exports as versioned artifacts. Validate
  schema/version compatibility before importing into a production project.
- Use stable identifiers or validated hierarchical traversal where names can be
  duplicated. Provide diagnostics showing the resolved object path.
- Make bulk operations resumable or idempotent where feasible, and produce a
  per-item success/failure report.
- Handle engineering, licensing, access-rights, exclusive-access, compiler,
  and file-lock exceptions distinctly. Include actionable remediation in error
  messages.
- Do not hard-code credentials or commit licensed DLLs, project archives, or
  generated binaries unless repository policy explicitly requires them.

## Diagnostics Checklist

For a failing automation, obtain the exact exception type, message, inner
exceptions, stack trace, TIA/Openness version, process bitness, Windows user,
referenced assembly paths, project location, and whether TIA Portal was already
running. Check version mismatch, missing Openness installation, licensing,
Windows permissions, running-process access, project locks, and unsupported
project-object operations before changing code.

## Online Boundaries

Openness work that changes a project is engineering automation, not automatic
commissioning approval. Do not initiate downloads, online changes, CPU state
transitions, forcing, or safety operations without explicit authorization and a
documented validation/rollback plan.
