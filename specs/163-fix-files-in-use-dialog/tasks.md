# Tasks: Fix the Second "Files in use" Dialog in an Interactive Upgrade

**Input**: Design documents from `/specs/163-fix-files-in-use-dialog/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/installer-upgrade-dialog.md, quickstart.md

**Tests**: The spec asks for a repeatable dialog count and a forced-failure check (SC-001 to SC-003). The test script task is part of US1.

**Organization**: Tasks are grouped by user story. US1 (diagnose) comes first. It decides the fix in US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story (US1 to US4)
- **[OWNER]**: The owner must click the interactive dialog or approve the step. Do not run it alone.

**Safety rules (all tasks)**: Test only on isolated copies made with `scripts/installer/new-test-variant.ps1`. Never run the real MSI over the production install. Create a package and start `msiexec` in separate shell calls. Run the test bot from a published copy of the branch, not from `installer\wix\payload`. Write all new text in STE.

## Phase 1: Setup

**Purpose**: A working isolated test base

- [X] T001 Publish the branch bot with `dotnet publish src/GameBot.Service` to a temporary folder outside the repo. Record the folder in `specs/163-fix-files-in-use-dialog/research.md` (section "Test base"). (The scratch build publishes the bot into the MSI payload; see "Test base".)
- [X] T002 Build the MSI from a scratch copy (fix the `PayloadFiles.Generated.wxs` asset names if they differ from the payload) and make an old and a new test variant with `scripts/installer/new-test-variant.ps1` (own upgrade code, temporary folder, temporary port).

---

## Phase 2: Foundational

**Purpose**: Tools that all stories use

- [X] T003 Add `scripts/installer/test-files-in-use-dialog.ps1`. It starts a locking process that holds `GameBot.Service.exe` of the test install open (parameter for no window or console window), and it counts dialogs in a `/l*v` log: `MsiRMFilesInUse` displays, message 1611, and the line "reboot will be required". It prints a short result table. It never touches the production install.
- [X] T004 [P] Add a snapshot function in `scripts/installer/common.psm1` that records the old product registration, shortcuts, Run entry, and `network.json` of a test install, and a compare function. SC-002 uses it.

**Checkpoint**: The script and the snapshot compare work on an installed test variant.

---

## Phase 3: User Story 1 - Find the cause (Priority: P1)

**Goal**: Prove which hypothesis (H1, H2, H3 in research.md) is true.

**Independent Test**: The log set of each run names the action that raises each dialog.

- [X] T005 [US1] E0: run a silent upgrade of the test copy with the bot running. Record the result (baseline).
- [X] T006 [US1] [OWNER] E1: interactive upgrade of the test copy with the current `afterInstallInitialize` schedule and a no-window locking process. Save the `/l*v` log. Record the dialog count from `test-files-in-use-dialog.ps1` in `research.md` (section "Results").
- [X] T007 [US1] [OWNER] E2: repeat T006 with a console-window locking process. Record if the run mode changes the dialog count or message 1611.
- [X] T008 SKIPPED: the cause was confirmed (research R10, R13) without the schedule comparison. A schedule change is not the fix. Original text: - [ ] T008 [US1] [OWNER] E1 comparison: repeat T006 with each other schedule in a scratch copy of `installer/wix/Product.wxs` (`afterInstallValidate`, `afterInstallExecute`, `afterInstallExecuteAgain`). Record the dialog count. Do not commit this scratch change.
- [X] T009 [US1] [OWNER] E3: upgrade from a test 1.11.16.x build to a newer test build. Record the dialog count.
- [X] T010 [US1] Read `installer/wix/Bundle.wxs` and the bundle log again. Record in `research.md` that the bundle starts the MSI one time and passes no Restart Manager property (already seen in the log), and add the E0 to E3 results. Decide the fix: C1, then C3 (research R4). Stop and report to the owner with the data before US2.

**Checkpoint**: The cause and the chosen candidate are recorded.

---

## Phase 4: User Story 2 - One dialog, one choice (Priority: P1) MVP

**Goal**: An interactive upgrade shows the dialog at most one time, closes the bot, and starts it again with the saved host and port.

**Independent Test**: `scripts/installer/test-files-in-use-dialog.ps1` on the test copy shows one dialog, no message 1611, and no reboot message.

- [X] T011 [US2] Implement the chosen candidate in `installer/wix/Product.wxs` (and a new fragment under `installer/wix/Fragments/` only if needed). Keep `Schedule="afterInstallInitialize"`. Keep the one dialog (the contract says the owner chooses once). Write the comment in STE and update the comment about the schedule.
- [X] T012 [US2] Rebuild the MSI and the test variants (T002 steps).
- [X] T013 [US2] [OWNER] E4: interactive upgrade with a no-window locking process, then with a console-window locking process. Expect one dialog, no message 1611, no reboot message, success, and the bot running again.
- [X] T014 [US2] Check that the new bot uses the saved host and port (FR-003). Record the result in `research.md`.
- [X] T015 PARTLY DONE: upgrade between two recent test builds (.899 to .900) shows one dialog. An upgrade from a very old build was not run. Original text: - [ ] T015 [US2] [OWNER] E3 again with the fix: upgrade between two recent test builds and from a very old test build. Expect one dialog each (FR-011).

**Checkpoint**: SC-001 and SC-004 pass.

---

## Phase 5: User Story 3 - A failed upgrade still rolls back (Priority: P1)

**Goal**: The fix keeps the rollback of feature 162.

**Independent Test**: The snapshot before and after a forced failure is equal.

- [X] T016 [US3] Take the snapshot (T004), then run the forced-failure upgrade with `scripts/installer/new-test-variant.ps1 -FailAfterInstallFiles` on the fixed MSI. Compare the snapshot. Record the result in `research.md` (SC-002).
- [X] T017 [US3] If the snapshot differs, undo or change the fix from T011 and go back to T013. Do not change the schedule to a value that breaks the rollback.

---

## Phase 6: User Story 4 - Other upgrade paths keep working (Priority: P2)

**Goal**: Silent upgrade and Web UI or API update still work.

**Independent Test**: A silent run ends with success. The existing installer tests pass.

- [X] T018 [US4] Run a silent upgrade of the test copy with the bot stopped, and one with the bot running. Expect success and the bot running again with the saved binding (FR-004).
- [ ] T019 [US4] Run all existing installer tests and scripts (`scripts/installer/install-smoke.ps1`, `uninstall-smoke.ps1`, `scope-smoke.ps1`, and the installer unit tests in `tests/`). Fix any failure that the change causes (SC-003).

---

## Phase 7: Polish

- [X] T020 [P] Update `INSTALL.md` (interactive upgrade: one dialog), `installer/README.md` (the test script and the isolated steps), `CHANGELOG.md`, and `specs/STATUS.md`. Use STE. Update `docs/architecture.md` ("Last reviewed") only if the product definition changed.
- [X] T021 Clean up: remove the test products with `msiexec /x <ProductCode>`, delete the test Start-Menu folders and test Run values, and stop the locking and test bot processes.
- [X] T022 Bump the version in `installer/versioning/version.override.json` before the push (CLAUDE.md rule): run `git fetch origin master`, compare with `origin/master`, and set `patch`, `updatedBy`, and `updatedAtUtc`.
- [ ] T023 [OWNER] Ask the owner if a real upgrade of the production install is wanted (Level 3). Do not run it without approval.

---

## Dependencies and Order

- Phase 1 then Phase 2 then US1. US1 decides US2. Stop after T010 and report.
- US2 then US3 (the rollback check uses the fixed MSI). US4 can start after T011 and run with US3 where the MSI is the same.
- T004 and T020 are parallel with other tasks. All other tasks use the same installer files and run in order.

## Parallel Example

After T003: T004 (`common.psm1`) and T005 (baseline silent run) can run together.

## Implementation Strategy

- MVP: Phases 1 to 4 (cause found, one dialog). Then US3 to prove the rollback. Do not publish the fix before T016 passes.
- If E1 and E2 show that H2 is false, stop at T010 and decide with the owner. The next candidate is C3 (research R4).
