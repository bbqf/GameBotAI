# Quickstart: Installer Semantic Version Upgrade Flow

## 1. Preconditions

- Use Windows PowerShell from repository root.
- Ensure existing quality gates are available:
  - `dotnet build -c Debug`
  - `dotnet test -c Debug`
  - `powershell -NoProfile -File scripts/installer/run-security-scans.ps1`

## 2. Prepare version inputs

1. Set or update checked-in override file values for any manual `Major/Minor/Patch` overrides.
2. Update the dedicated checked-in release-line marker file only when intentionally creating a new release line.
3. Confirm current CI build counter value in the checked-in CI counter file.

Expected:
- Missing or malformed files fail fast with actionable remediation text.

## 3. Validate CI version progression

1. Trigger a CI build on this branch.
2. Confirm generated artifact version follows `Major.Minor.Patch.Build`.
3. Verify `Build` increments by exactly `+1` versus previous persisted CI value.
4. Verify CI counter file persisted update is committed by CI workflow policy.

## 4. Validate local build behavior

```powershell
dotnet build -c Debug
```

Expected:
- Local build computes `Build = persistedCiBuild + 1` for local artifact/version output.
- Local build does not persist repository counter changes.
- Locally computed build values are treated as non-publishing.

## 5. Validate install decision outcomes

### Downgrade block
1. Install a higher version.
2. Attempt install with lower `Major.Minor.Patch.Build`.
3. Confirm install is blocked with downgrade message.

### Upgrade path
1. Install lower version with non-default persisted properties.
2. Install higher version.
3. Confirm upgrade succeeds and all supported persisted installer/runtime properties remain unchanged.

### Same-build behavior
1. Install version `V`.
2. Re-run installer `V` interactively and confirm explicit choice (`reinstall` or `cancel`).
3. Run same-build in unattended mode and confirm exit without changes and dedicated same-build status code.

## 6. Contract verification

- Review [contracts/versioning-installer.openapi.yaml](contracts/versioning-installer.openapi.yaml) for:
  - `POST /versioning/resolve`
  - `POST /installer/compare`
  - `POST /installer/same-build/decision`
- Ensure implementation and tests align to schema fields and enum outcomes.

## 7. Evidence checklist

- CI build showing persisted build counter increment (`+1`).
- Local build evidence showing non-persisted derived build value.
- Downgrade blocked log/message capture.
- Upgrade retention verification for persisted properties.
- Same-build unattended dedicated status code capture.
- `dotnet test -c Debug` and security scan output attached to PR.
