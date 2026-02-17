# Research: Standalone Windows Installer (EXE/MSI)

## Decision 1: Installer technology stack
- Decision: Use WiX v4 (`WixToolset.Sdk`) to produce MSI + bootstrapper EXE.
- Rationale: Native Windows installer semantics, enterprise deployment compatibility, explicit support for chaining prerequisites and silent properties.
- Alternatives considered:
  - Custom self-extracting EXE: weaker enterprise policy integration and uninstall/repair semantics.
  - In-app API installer flow: not a standalone installer artifact and does not satisfy EXE/MSI deployment expectations.

## Decision 2: Installation scope model
- Decision: Support per-user scope only in v1.
- Rationale: Avoids elevation-dependent machine installation behavior and keeps install/runtime ownership aligned to the current user.
- Alternatives considered:
  - Per-machine only: too restrictive for non-admin environments.
  - Mixed per-machine/per-user: adds scope branching and increases install-path regression risk.

## Decision 3: Mode/scope constraints
- Decision: Support `backgroundApp` mode in `perUser` scope only.
- Rationale: Removes machine-level service requirements and ensures deterministic non-elevated installation behavior.
- Alternatives considered:
  - Keep service mode in installer: rejected because it introduces elevated system-level installation behavior.

## Decision 4: Prerequisite distribution policy
- Decision: Hybrid strategy: bundle critical prerequisites, optional online fallback from allowlisted vendor URLs only.
- Rationale: Supports restricted networks while preserving recovery path for optional components; improves supply-chain control.
- Alternatives considered:
  - Full online download: fragile in offline/locked-down environments.
  - Full offline only: larger package and less flexibility for optional components.

## Decision 5: Endpoint protocol behavior
- Decision: Default to HTTP with optional HTTPS configuration path.
- Rationale: Keeps initial install flow simple while still supporting secure endpoint migration.
- Alternatives considered:
  - HTTPS mandatory at install time: higher certificate provisioning burden and slower first-time installs.

## Decision 6: Silent mode contract
- Decision: Single canonical property schema across EXE/MSI paths and standardized exit codes.
- Rationale: Reduces deployment automation errors and keeps SCCM/Intune scripting deterministic.
- Exit codes: `0`, `3010`, `1603`, `1618`, `2`.
- Alternatives considered:
  - Generic non-zero failures only: poor diagnosability.

## Decision 7: Logging policy
- Decision: Installer logs written to `%LocalAppData%\GameBot\Installer\logs` with rolling retention of last 10 files.
- Rationale: Keeps installer artifacts in the current-user scope and avoids machine-level directory dependencies.
- Alternatives considered:
  - `%TEMP%` only: poor persistence and supportability.

## Decision 8: Install roots
- Decision: Default install root `%LocalAppData%\GameBot`.
- Rationale: Aligns with per-user-only installation and avoids elevation/UAC behaviors.
- Alternatives considered:
  - System-level install root: rejected because it requires elevated installation semantics.

## Decision 9: Signing policy
- Decision: Release artifacts must be signed; development artifacts may be unsigned/test-signed.
- Rationale: Strong trust for production distribution with practical dev workflow.
- Alternatives considered:
  - Sign everything always: unnecessary local/dev friction.

## Decision 10: Performance target
- Decision: Install-time SLOs on clean machine (excluding reboot):
  - Interactive <=10 minutes
  - Silent <=8 minutes
- Rationale: Measurable operational expectation for acceptance and CI/VM smoke validation.
- Alternatives considered:
  - No numeric target: ambiguous acceptance and higher rework risk.

## Decision 11: Runtime data directory placement
- Decision: Keep runtime data outside install binaries at `%LocalAppData%\GameBot\data`.
- Decision addendum: Expose runtime data path as canonical silent property `DATA_ROOT` and allow interactive UI override with writeability validation.
- Rationale: System install directories are not appropriate writable runtime data locations; per-user data root avoids permission failures and cross-user coupling.
- Alternatives considered:
  - Store runtime data under system install directories: rejected due to write restrictions and UAC implications.
  - `%ProgramData%` root for per-user installs: rejected due to unnecessary cross-user coupling.
