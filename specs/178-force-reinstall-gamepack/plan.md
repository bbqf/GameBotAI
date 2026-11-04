# Implementation Plan: Install the Same Gamepack Version Again (Force Install)

**Branch**: `178-force-reinstall-gamepack` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/178-force-reinstall-gamepack/spec.md`

## Summary

Issue #374 (FR-041). A caller can set the option `force` when the caller installs a pack name and version that is already installed. The install then rewrites every object of the pack from the file.

The plain install of the same version already rewrites the objects today. It makes no rollback point. It does not report the overwritten objects. The managed update does nothing for the same version.

The feature adds these parts:

1. The option `force` on four routes: direct install, direct update, catalog install, and catalog update (the managed update). See FR-004 and FR-005.
2. A reinstall point. It is a rollback point that is separate from the kept previous version. It holds the live content of the pack before the forced install.
3. The response members `reinstalled` and `overwritten`. The list holds only the objects whose stored content differs from the file.
4. Dry-run support. A dry run with `force` reports the result and the list. It changes no object and makes no reinstall point.
5. The list member `hasReinstallPoint` (FR-012).
6. A timed test for SC-007. The result goes into `quickstart.md`.

## Technical Context

**Language/Version**: C# 13 on .NET 9  
**Primary Dependencies**: System.Text.Json (already in use). No new dependency.  
**Storage**: Gamepack registry file and one new file for each reinstall point: `<dataRoot>\gamepacks\reinstall\<name>.json`. The new registry member `reinstall` is optional.  
**Testing**: xUnit. Unit tests (`tests\unit\Gamepacks`), contract tests (`tests\contract\Gamepacks`), integration tests (`tests\integration\Gamepacks`). One timed test for SC-007.  
**Target Platform**: Windows service, Linux CI  
**Project Type**: Web service with Domain library  
**Performance Goals**: A forced install of a pack with 200 objects takes 1.5 times the time of a normal install, or less (SC-007).  
**Constraints**: No change to a call without `force` (FR-003, SC-006). Atomic install (FR-006). No web UI change.  
**Scale/Scope**: Changes in `GamepackService`, `GamepackApplier`, `GamepackValidator`, the registry store, the managed update orchestrator, four endpoints, the DTOs, tests, and docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Build and tests: no known red state. Implementation runs build and tests before each commit. PASS.
- STE (Principle VI): all text in this plan and in the artifacts follows STE. Each sentence has 20 words or fewer and gives one instruction. PASS.
- Code quality (I): methods stay under 50 lines. Method names use CamelCase with no underscores. The design reuses the take-over and rollback system. PASS.
- Testing (II): unit, contract, and integration tests cover each requirement. Coverage is 80% line and 70% branch for touched files. PASS.
- UX (III): no web UI change. Phone support level: not applicable. PASS.
- Performance (IV): install is not a hot path. SC-007 has a timed test with a limit of 1.5 times. PASS.
- Living documentation (V): update `specs\STATUS.md` and the spec `Status` line. Review `docs\architecture.md` and the API docs. PASS (planned in tasks).
- Workflow: the shard balance rule applies to new tests. Bump the version in `installer\versioning\version.override.json` before the PR push. PASS (planned).

Post-design re-check: no new violation. The design adds no project, no endpoint, and no dependency. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/178-force-reinstall-gamepack/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (also holds the SC-007 result)
├── contracts/
│   └── force-reinstall-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackInstallOptions.cs        # CHANGED: Force
├── GamepackPlan.cs                  # CHANGED: OverwriteEntry, Overwrites
├── GamepackRegistry.cs              # CHANGED: InstalledGamepack.Reinstall
├── IGamepackRegistryStore.cs        # CHANGED: four reinstall members (default implementations)
├── GamepackRegistryStore.cs         # CHANGED: reinstall file read, write, delete
├── GamepackApplier.cs               # CHANGED: ReinstallPointHandling (Keep, Capture, Clear)
├── GamepackValidator.cs             # CHANGED: reinstall flag, CompareOverwritesAsync
├── GamepackService.cs               # CHANGED: forced reinstall detection, dry run, rollback order
├── GamepackService.Snapshot.cs      # CHANGED: ReinstallFileText
└── Managed/GamepackUpdateOrchestrator.cs   # CHANGED: Force skips IsSameVersion
src/GameBot.Service/Endpoints/       # CHANGED: four install routes, rollback route, list DTO, OpenAPI text
tests/unit/Gamepacks/                # NEW: store, validator, applier tests
tests/contract/Gamepacks/            # NEW: route and response tests
tests/integration/Gamepacks/         # NEW: hand-deleted object test, timed test (SC-007)
docs/                                # REVIEW: architecture and API docs
specs/STATUS.md                      # CHANGED: add row 178
```

**Structure Decision**: Keep the existing Domain and Service projects. Add no new project.

## Design Notes

Edit points, by file. The research file has the reasons.

- `GamepackService.InstallAsync`: read the registry record. The call is a forced reinstall when `Force` is true and the record version equals the file version (R2).
- `GamepackService.InstallAsync` dry run: return `Overwritten` from `plan.Overwrites`. Do not call the applier (R6).
- `GamepackService.RollbackAsync`: use the reinstall point first. Then use the previous version (R4).
- `GamepackApplier.ApplyAsync`: write order is objects, previous file, reinstall file, registry. The failure restore also restores the reinstall file (R4).
- `GamepackUpdateOrchestrator.ExecuteAsync`: `Force` makes `IsSameVersion` return false. Pass `Force` to the dry-run check and to the install (R5).
- Managed rollback check: accept a reinstall point as a rollback source (R5).
- Normal install of a different version: clear the reinstall point (R4).
- `RemoveAsync` and `RemoveInstalledAsync`: delete the reinstall file (R4).

- Routes (FR-004): exactly four routes. Managed update (`managed=true`) is a mode of the two update routes. It is not a fifth route.
- Term: "reinstall point" names the rollback point of a forced install (FR-007, FR-012).
- Failure test (US1 scenario 4, FR-006): inject a store write fault through `IGamepackWriteGate`. Check that no object changes.
- Hand-deleted object test (US1 scenario 5): delete one object, run a forced install, check that the object exists again.
- Timed test (SC-007): do one warm-up run. Do at least 5 runs each of normal and forced install. Compare the medians. The ratio limit is 1.5.
  - Put the ratio and both medians in `quickstart.md`.
  - The test has `[Trait("Category","Performance")]`.
  - It is a local measure. It runs in CI only if it is stable.
- Last task: set the `Status` line of `spec.md` to "Implemented".

## Complexity Tracking

No violation. This section is empty.
