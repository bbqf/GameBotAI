# Data Model

## Overview
This feature introduces installer-oriented domain models to represent install intent, prerequisite evaluation, endpoint selection, and final deployment state for backend + web UI runtime.

## Entities

### InstallationProfile
- **Purpose**: Canonical set of operator-selected install settings.
- **Fields**:
  - `profileId` (string, required, unique)
  - `installMode` (enum: `service`, `backgroundApp`; required)
  - `installRootPath` (string, required)
  - `unattended` (bool, required)
  - `startupPolicy` (enum: `bootAutoStart`, `loginStartWhenEnabled`, `manual`; required)
  - `protocol` (enum: `http`, `https`; required)
  - `createdAtUtc` (datetime, required)
  - `updatedAtUtc` (datetime, required)
- **Validation Rules**:
  - `installMode=service` requires admin privilege context.
  - `protocol=https` requires valid certificate descriptor in `EndpointConfiguration`.
  - `startupPolicy` must match mode defaults unless explicitly overridden.

### PrerequisiteStatus
- **Purpose**: Captures detection/install result for each dependency.
- **Fields**:
  - `prerequisiteKey` (string, required, unique per run)
  - `displayName` (string, required)
  - `requiredVersion` (string, optional)
  - `detectedVersion` (string, optional)
  - `state` (enum: `detected`, `missing`, `installed`, `failed`, `skipped`; required)
  - `source` (enum: `system`, `bundled`, `online`; required)
  - `details` (string, optional)
- **Validation Rules**:
  - `state=failed` requires non-empty `details` remediation hint.
  - `source=online` is only valid when bundled payload unavailable or disallowed by policy.

### EndpointConfiguration
- **Purpose**: Declares resolved backend/web UI networking and announcement values.
- **Fields**:
  - `backendHostScope` (enum: `allInterfaces`, `selectedInterface`; required)
  - `backendPort` (int, required, range 1-65535)
  - `webUiPort` (int, required, range 1-65535)
  - `webUiPortSelectionOrder` (array<int>, required; default `[8080,8088,8888,80]`)
  - `firewallScope` (enum: `privateNetworkOnly`, `hostDefault`; required)
  - `protocol` (enum: `http`, `https`; required)
  - `certificateReference` (string, optional)
  - `announcedWebUiUrl` (string, required)
  - `announcedBackendUrl` (string, required)
- **Validation Rules**:
  - `webUiPort` and `backendPort` cannot be equal.
  - If configured preferred port unavailable, selected port must be from valid suggestion set.
  - `firewallScope=hostDefault` requires explicit confirmation flag in `InstallerExecutionResult` warnings.

### PortProbeResult
- **Purpose**: Snapshot of preflight port availability checks.
- **Fields**:
  - `requestedBackendPort` (int, required)
  - `requestedWebUiPort` (int, required)
  - `backendPortAvailable` (bool, required)
  - `webUiPortAvailable` (bool, required)
  - `backendAlternatives` (array<int>, required)
  - `webUiAlternatives` (array<int>, required)
  - `capturedAtUtc` (datetime, required)
- **Validation Rules**:
  - At least one suggested alternative is required for every unavailable requested port.

### InstallerExecutionResult
- **Purpose**: Final outcome emitted by installer run.
- **Fields**:
  - `runId` (string, required, unique)
  - `status` (enum: `success`, `failed`, `aborted`; required)
  - `selectedProfileId` (string, required)
  - `endpointConfiguration` (EndpointConfiguration, required on success)
  - `prerequisites` (array<PrerequisiteStatus>, required)
  - `warnings` (array<string>, optional)
  - `errors` (array<string>, optional)
  - `completedAtUtc` (datetime, required)
- **Validation Rules**:
  - `status=success` requires non-empty announced URLs and empty `errors`.
  - `status=failed` requires at least one `errors` entry.
  - Warning must be present when firewall policy falls back to host defaults.

## Relationships
- `InstallationProfile` 1..* `InstallerExecutionResult` (history per profile).
- `InstallerExecutionResult` 1..1 `EndpointConfiguration` (for successful runs).
- `InstallerExecutionResult` 1..* `PrerequisiteStatus`.
- `InstallerExecutionResult` 0..1 `PortProbeResult` (always present unless preflight skipped due to fatal early validation).

## State Transitions

### Installer Run State
`initialized -> preflightValidated -> prerequisitesResolved -> modeRegistered -> endpointsConfigured -> completed`

Failure branches:
- `preflightValidated -> failed` (invalid CLI args, privilege mismatch)
- `prerequisitesResolved -> failed` (dependency install failure)
- `modeRegistered -> failed` (service/background registration failure)
- `endpointsConfigured -> failed` (port conflicts unresolved or binding failure)

### Prerequisite Status State
`missing -> installed` or `missing -> failed`

`detected` and `skipped` are terminal states for already-satisfied or intentionally omitted components.
