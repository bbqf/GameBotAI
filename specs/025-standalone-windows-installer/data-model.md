# Data Model: Standalone Windows Installer

## Entity: InstallerPackage
- Description: Build-time artifact metadata for bootstrapper and MSI outputs.
- Fields:
  - `packageId` (string, required, unique)
  - `version` (string, required, SemVer/build version)
  - `bundlePath` (string, required)
  - `msiPath` (string, required)
  - `isReleaseSigned` (bool, required)
  - `createdAtUtc` (datetime, required)
- Validation:
  - `bundlePath` and `msiPath` must exist at packaging completion.
  - `isReleaseSigned=true` required for release pipeline.

## Entity: InstallRequest
- Description: Canonical install property set used by interactive defaults and silent mode.
- Fields:
  - `installMode` (enum: `service`, `backgroundApp`, required)
  - `installScope` (enum: `perMachine`, `perUser`, required)
  - `installRoot` (string, required, default by scope)
  - `dataRoot` (string, required, default by scope; canonical silent property `DATA_ROOT`; user-editable in interactive UI)
  - `backendPort` (int 1..65535, required)
  - `webUiPort` (int 1..65535 or `auto`, required)
  - `protocol` (enum: `http`, `https`, required)
  - `enableHttps` (bool, required)
  - `certificateRef` (string, optional, required when `enableHttps=true`)
  - `startOnLogin` (bool, optional; background mode only)
  - `allowOnlinePrereqFallback` (bool, required)
  - `unattended` (bool, required)
- Validation:
  - `service` mode requires `perMachine` scope.
  - `backgroundApp` mode supports `perMachine` and `perUser`.
  - `dataRoot` must be scope-aligned and outside `%ProgramFiles%`.
  - `DATA_ROOT` override must resolve to writable path for selected execution context.
  - `backendPort != webUiPort` when `webUiPort` resolved concrete value.
  - `enableHttps=true` requires `certificateRef`.

## Entity: PortResolution
- Description: Deterministic web port selection outcome.
- Fields:
  - `requestedWebUiPort` (int or null)
  - `selectedWebUiPort` (int, required)
  - `preferenceOrder` (int[], required: `[8080, 8088, 8888, 80]`)
  - `wasFallbackApplied` (bool, required)
  - `alternatives` (int[], required)
- Validation:
  - `selectedWebUiPort` must be available at validation time.
  - `alternatives` must not include selected/occupied ports.

## Entity: PrerequisitePolicy
- Description: Source policy and allowlist controls for prerequisite acquisition.
- Fields:
  - `criticalBundled` (string[], required)
  - `allowOnlineFallback` (bool, required)
  - `allowlistedSources` (string[], required when `allowOnlineFallback=true`)
  - `blockedSourceAttempts` (int, optional)
- Validation:
  - Every online URL must match allowlist host/path rules.

## Entity: InstallExecution
- Description: Runtime installation execution record.
- Fields:
  - `runId` (string, required, unique)
  - `status` (enum: `success`, `failed`, `aborted`, required)
  - `exitCode` (int, required: one of `0`, `3010`, `1603`, `1618`, `2`)
  - `warnings` (string[], required)
  - `errors` (string[], required)
  - `startedAtUtc` (datetime, required)
  - `completedAtUtc` (datetime, required)
  - `durationSeconds` (int, required)
  - `logFilePath` (string, required)
- Validation:
  - `durationSeconds` must satisfy SLO checks for scenario type during acceptance validation.

## Entity: InstalledRuntimeProfile
- Description: Persisted installed configuration for post-install startup/operation.
- Fields:
  - `profileId` (string, required, unique)
  - `installMode` (enum, required)
  - `installScope` (enum, required)
  - `installRoot` (string, required)
  - `dataRoot` (string, required)
  - `startupPolicy` (enum: `bootAutoStart`, `loginStartWhenEnabled`, `manual`, required)
  - `protocol` (enum, required)
  - `backendEndpoint` (string, required)
  - `webUiEndpoint` (string, required)
  - `createdAtUtc` (datetime, required)
  - `updatedAtUtc` (datetime, required)
- Validation:
  - Startup policy must match mode defaults unless explicitly overridden.

## Relationships
- `InstallRequest` -> produces one `PortResolution` and one `InstallExecution`.
- `InstallExecution` -> persists one `InstalledRuntimeProfile` on success.
- `PrerequisitePolicy` -> constrains prerequisite retrieval during `InstallExecution`.

## State Transitions
- InstallExecution lifecycle:
  - `started` -> `success`
  - `started` -> `failed`
  - `started` -> `aborted`

- InstalledRuntimeProfile lifecycle:
  - `created` -> `updated` -> `removed` (on uninstall)
