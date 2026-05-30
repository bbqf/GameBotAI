# Data Model: Installer Semantic Version Upgrade Flow

## Entity: VersionOverride
- Description: Checked-in manual override values used for semantic version components.
- Fields:
  - `major` (int, optional, >=0)
  - `minor` (int, optional, >=0)
  - `patch` (int, optional, >=0)
  - `updatedBy` (string, optional)
  - `updatedAtUtc` (datetime, optional)
- Validation:
  - Any provided component must be an integer >=0.
  - When absent, automated policy computes the component.

## Entity: ReleaseLineMarker
- Description: Dedicated checked-in file indicating intentional transition to a new release line.
- Fields:
  - `releaseLineId` (string, required, unique per release line)
  - `sequence` (int, required, monotonic)
  - `updatedAtUtc` (datetime, required)
  - `updatedBy` (string, optional)
- Validation:
  - `sequence` must increase for each intentional release-line transition.
  - Marker file is the sole authoritative source for automatic minor transitions.

## Entity: CiBuildCounter
- Description: Repository-tracked authoritative build counter, written only by CI.
- Fields:
  - `lastBuild` (int, required, >=0)
  - `updatedAtUtc` (datetime, required)
  - `updatedBy` (string, required; CI identity)
- Validation:
  - Persisted value must increase by exactly 1 per CI run.
  - Local workflows must not modify this record.

## Entity: ComputedVersion
- Description: Fully resolved semantic version used for artifact generation and install decisions.
- Fields:
  - `major` (int, required)
  - `minor` (int, required)
  - `patch` (int, required)
  - `build` (int, required)
  - `source` (enum: `ci`, `local`, required)
  - `resolvedFrom` (object, required: override/marker/counter provenance)
- Validation:
  - Comparison semantics are numeric lexicographic across all four components.
  - For local source, `build = persistedCiBuild + 1` and result is non-authoritative.

## Entity: InstallComparison
- Description: Result of comparing candidate installer version against installed version.
- Fields:
  - `installedVersion` (ComputedVersion, required)
  - `candidateVersion` (ComputedVersion, required)
  - `outcome` (enum: `downgrade`, `upgrade`, `sameBuild`, required)
  - `comparisonDetail` (string, required)
- Validation:
  - `outcome` derives strictly from full four-component comparison.

## Entity: SameBuildPolicy
- Description: Behavior policy when `outcome = sameBuild`.
- Fields:
  - `interactiveOptions` (enum[], required: `reinstall`, `cancel`)
  - `unattendedDefault` (enum, required: `skip`)
  - `unattendedStatusCode` (int, required; dedicated same-build code)
- Validation:
  - Unattended same-build must never mutate installation state.

## Entity: UpgradePropertySnapshot
- Description: Persisted installer/runtime properties retained across successful upgrades.
- Fields:
  - `snapshotId` (string, required)
  - `properties` (object map, required)
  - `capturedAtUtc` (datetime, required)
  - `appliedAtUtc` (datetime, optional)
- Validation:
  - On upgrade outcome, all supported persisted properties are retained unless explicit override is supplied.

## Relationships
- `VersionOverride`, `ReleaseLineMarker`, and `CiBuildCounter` are inputs to `ComputedVersion` resolution.
- `ComputedVersion` pairs produce one `InstallComparison` outcome.
- `InstallComparison` with `sameBuild` references `SameBuildPolicy` for execution behavior.
- `InstallComparison` with `upgrade` applies `UpgradePropertySnapshot` retention.

## State Transitions
- Versioning flow:
  - `inputsLoaded` -> `versionComputed` -> `comparisonComputed` -> (`downgradeBlocked` | `upgradeApplied` | `sameBuildHandled`)
- Same-build flow:
  - `sameBuildDetected` -> (`interactivePrompted` -> `reinstall` | `cancelled`) OR (`unattendedSkipped`)
- Build counter flow:
  - `counterRead` -> (`ciIncrementPersisted` | `localDerivedNoPersist`)
