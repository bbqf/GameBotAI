# GameBot Versioning Guide (Planned)

This document describes the planned versioning behavior for installer builds and upgrades.

## Version format

GameBot installer versions follow:

`Major.Minor.Patch.Build`

All four components are numeric and compared lexicographically as numbers.

Examples:
- `1.4.0.22` > `1.4.0.21`
- `1.4.0.0` > `1.3.9.999`
- `2.0.0.1` > `1.999.999.999`

## Who this is for

- **Developers**: how version values are produced and persisted.
- **Users/Operators**: what happens during install, upgrade, downgrade, and reinstall.

---

## Developer view

### Source-of-truth files (planned)

The plan uses three checked-in versioning files:

- `installer/versioning/version.override.json`
  - Optional manual overrides for `Major`, `Minor`, `Patch`.
- `installer/versioning/release-line.marker.json`
  - Dedicated marker file that signals an intentional new release line.
- `installer/versioning/ci-build-counter.json`
  - Authoritative persisted counter for `Build` (CI updates this).

### Component rules

- **Major**
  - Set explicitly by maintainers.
  - Can be overridden via `version.override.json`.

- **Minor**
  - Auto-increments only when `release-line.marker.json` is created/updated.
  - Does **not** increment from branch naming or branch creation alone.
  - Can be overridden via `version.override.json`.

- **Patch**
  - Resets to `0` when Minor auto-increments.
  - Can be overridden via `version.override.json`.

- **Build**
  - CI is the only authoritative writer for the persisted build counter.
  - CI increments by exactly `+1` per CI build trigger.
  - Local builds do not persist counter updates.
  - Local builds derive `Build = persistedCiBuild + 1` for local artifacts only.

### Precedence (planned)

For `Major`, `Minor`, `Patch`:
1. Valid checked-in manual override (`version.override.json`)
2. Automatic policy (marker-driven minor increment, patch reset, etc.)

For `Build`:
- CI path: read persisted counter, increment by 1, persist.
- Local path: read persisted counter, use `+1` without persisting.

### Validation and failure behavior

- Missing/malformed versioning files fail fast with actionable errors.
- Invalid numeric values (negative/non-numeric) are rejected.
- Release marker is the single authoritative source for automatic release-line transitions.

### Publishing rule

- CI-generated build numbers are authoritative for released artifacts.
- Locally derived build numbers are non-authoritative and non-publishing.

---

## User/Operator view

### Upgrade decision behavior

Installer compares full `Major.Minor.Patch.Build`:

- **Candidate lower than installed** -> downgrade blocked.
- **Candidate higher than installed** -> upgrade allowed.
- **Candidate equals installed** -> same-build flow.

### Upgrade behavior

When upgrading to a higher version:
- Installer performs in-place upgrade.
- All previously persisted installer/runtime properties are preserved by default.
- Properties only change when explicitly overridden by the operator.

### Same-build behavior

- **Interactive install**: user is asked whether to proceed (reinstall/repair) or cancel.
- **Unattended/silent install**: installer exits without changes using a dedicated same-build status code.
  - Planned dedicated code: `4090`.

### Downgrade behavior

Attempted downgrade is blocked with a clear message and remediation guidance.

---

## Practical examples

### Example A: New release line

1. Update `release-line.marker.json` intentionally.
2. CI build runs.
3. Minor increments, Patch resets to `0`, Build increments by `+1`.

### Example B: Normal CI build on same release line

1. No release marker change.
2. CI build runs.
3. Major/Minor/Patch unchanged (unless override), Build increments by `+1`.

### Example C: Local developer build

1. Read current `ci-build-counter.json` value `N`.
2. Local build resolves `Build = N+1`.
3. No counter persistence to repository.

---

## Verification checklist

- Confirm version output uses four numeric components.
- Confirm CI increments and persists build counter by exactly `+1`.
- Confirm local builds do not persist counter changes.
- Confirm downgrade attempts are blocked.
- Confirm upgrade preserves persisted properties.
- Confirm unattended same-build exits without changes and returns dedicated status code.

---

## Status

This guide reflects the current planned implementation from:
- `specs/026-installer-semver-upgrade/spec.md`
- `specs/026-installer-semver-upgrade/plan.md`
- `specs/026-installer-semver-upgrade/research.md`

If implementation diverges, update this file together with the related spec/plan artifacts.
