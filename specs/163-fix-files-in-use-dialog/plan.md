# Implementation Plan: Fix the Second "Files in use" Dialog in an Interactive Upgrade

**Branch**: `163-fix-files-in-use-dialog` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/163-fix-files-in-use-dialog/spec.md`

## Summary

An interactive upgrade with the bot running shows the "Files in use" dialog two times. The log of the production upgrade shows the cause: the new product shows the dialog at its `InstallValidate`, and the uninstall of the old product (started by `RemoveExistingProducts`) shows it again in its own Restart Manager (RM) session (research R1, R2). The bundle starts the MSI one time, and the removal of the old bundle shows no dialog.

The plan has two parts. Part 1 proves which hypothesis is true with isolated tests (research R3, R5). Part 2 makes the smallest change that gives one dialog and keeps the rollback of feature 162. The first candidate is a change that lets the first RM session close the bot fully, so the nested check finds no lock (research R4, C1). The schedule stays `afterInstallInitialize`.

## Technical Context

**Language/Version**: WiX Toolset 6 (MSI package and Burn bundle), PowerShell 5.1 test scripts. The bot is .NET (existing).
**Primary Dependencies**: Windows Installer Restart Manager, WiX `Util` extension (only if C1 uses `util:CloseApplication`).
**Storage**: `network.json` and the registry values of feature 162 (not changed).
**Testing**: Existing installer tests and scripts under `scripts/installer`. New check: a script step that starts a no-window locking process and counts dialogs by log (`MsiRMFilesInUse` lines) in a `/l*v` log. Interactive click by the owner.
**Target Platform**: Windows 11, per-user install.
**Project Type**: Windows installer (MSI and bundle) in an existing .NET solution.
**Performance Goals**: None. The upgrade time must not grow by more than the 30 s RM wait that the fix removes.
**Constraints**: Keep rollback (FR-002). Keep the dialog pages, binding, and shortcut rule of feature 162 (FR-005, FR-009). No code in the Web UI update path (FR-008). Test only on isolated copies (FR-010).
**Scale/Scope**: One installer definition (`installer/wix/Product.wxs`, maybe `Bundle.wxs`) and its test scripts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Not applicable.

| Principle | Status |
|-----------|--------|
| I. Code quality | Pass. A small change in one installer file. |
| II. Testing | Pass. A repeatable log-based dialog count and the forced-failure check cover the change. |
| III. UX consistency | Pass. No web UI change. The installer dialog count goes from 2 to 1. |
| IV. Performance | Pass. No runtime path changes. |
| V. Living documentation | Plan to update `INSTALL.md`, `installer/README.md`, `CHANGELOG.md`, `specs/STATUS.md` (research R7). |
| VI. STE | Pass. Applied to all new text. |
| Version bump | Before the push of the PR, bump `installer/versioning/version.override.json` (CLAUDE.md rule). |

Post-design re-check: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/163-fix-files-in-use-dialog/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── installer-upgrade-dialog.md
├── checklists/requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
installer/
├── wix/
│   ├── Product.wxs          # MajorUpgrade, candidate C1 (CloseApplication) or C3
│   ├── Bundle.wxs           # Read only. Change only if E1 shows a bundle cause
│   └── Fragments/           # New fragment only if C1/C3 needs one
└── README.md                # Test steps
scripts/installer/
├── new-test-variant.ps1     # Existing. Reused for isolated copies
└── (new) test-files-in-use-dialog.ps1   # Locking process and dialog count from the log
INSTALL.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Keep the change inside the installer definition and its scripts. Add one test script beside the existing installer scripts. No change to `src/`.

## Phases

1. **Phase 0 (research)**: Done. [research.md](research.md) names the cause and the experiments.
2. **Phase 1 (design)**: [data-model.md](data-model.md), [contracts/installer-upgrade-dialog.md](contracts/installer-upgrade-dialog.md), [quickstart.md](quickstart.md).
3. **Phase 2 (tasks)**: `/speckit-tasks`. Order: E0 to E3 (diagnose) first, then the change (E4), then SC-002 (E5), then docs and version bump.

## Risks

- The owner must click the dialog for E1, E2, E4. Ask for approval before each interactive run.
- H2 may be false (the nested dialog shows with any bot state). Then C1 does not help and C3 or a nested-session setting is next. The tasks must stop and report after E1/E2 with the data.
- A change that ends the bot without a dialog (`CloseApplication` with terminate) can look like a changed "one choice" rule. The decision in the tasks must say if a dialog stays.

## Complexity Tracking

No violations.
