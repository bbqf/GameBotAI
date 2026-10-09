# Tasks: Persist the Listen Host and Port

**Input**: Design documents in `specs/132-persist-listen-host-port/` (plan.md, spec.md, research.md, data-model.md, contracts/network-settings-file.md, quickstart.md)
**Tests**: Required (FR-014, FR-019, FR-022). For each fix, the failing test comes first. The manual MSI check (T034) is also required.

## Format: `- [ ] [ID] [P?] [Story] Description with file path`

- **[P]**: The task can start in parallel with other tasks of its group. It edits a different file than those tasks and it has no open dependency.
- **[Story]**: US1, US2, or US3 (see spec.md). Setup, Foundational, and Polish tasks have no story label.
- Tasks that edit the same file are never marked [P]. Do them one after the other, in ID order.
- **Test first (FR-022)**: A task named "Failing test" MUST be run before its fix task. Keep the failing output in the pull request notes. A task named "Guard test" covers behavior that earlier fix tasks already give. If a guard test fails, fix the cause.
- Read the design documents (plan.md, spec.md, contracts/network-settings-file.md, data-model.md) before T001. This is not a task.

## Phase 1: Setup

- [ ] T001 Run `dotnet build` and the unit and installer test projects (`tests/unit`, `tests/integration`) from `C:\src\GameBot` to prove the branch starts green (Constitution gate). If a test fails before any change, record it and get a maintainer waiver before you continue.

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The shared reader in the service, the tested installer script functions, and the installer changes that all three scenarios use.

### Service reader chain

- [ ] T002 [P] Create `src/GameBot.Service/Services/PersistedNetworkSettings.cs` as a compile stub only: the public types and method signatures from `data-model.md`, with every field returning `null` and an empty problem list.
- [ ] T003 Failing test: add `tests/unit/Config/PersistedNetworkSettingsTests.cs` (needs T002). Cover: file missing; file not valid JSON; port as string and as number; invalid port; invalid host; invalid port with valid host (host still used); file value wins over registry; registry used when file has no value; unknown fields ignored; no exception in any case. Run it and keep the failing output.
- [ ] T004 Implement `src/GameBot.Service/Services/PersistedNetworkSettings.cs` (needs T003). Read `<data root>\config\network.json`, then the HKCU `Software\GameBot\Network` values. Validate each field on its own (FR-018): port 1 to 65535; host not empty, no spaces, no `/` or `:`. Return a value or `null` per field plus a list of problems. Never throw. Run T003 again until it passes.
- [ ] T005 [P] Failing test: add `tests/unit/Config/ConfigureWebHostUrlsPersistedTests.cs` (needs T004). Assert the priority order: configuration, environment variable, `network.json`, registry, default (FR-006). Assert that host and port resolve on their own. Assert that problems go to the logger. Run it and keep the failing output.
- [ ] T006 Change `ConfigureWebHostUrls` in `src/GameBot.Service/GameBotServiceSetup.cs` (needs T005) to take the storage root and use `PersistedNetworkSettings`. Write the returned problems to the logger after the host is built (or to `Console.Error` if no logger exists). Update all callers. Run T005 again until it passes.
- [ ] T007 [P] Failing test: add `tests/unit/Config/ConfigSnapshotPersistedNetworkTests.cs` (needs T004). Assert that the default display host and port come from `network.json`, then the registry. Run it and keep the failing output.
- [ ] T008 Change `src/GameBot.Service/Services/ConfigSnapshotService.cs` (needs T007) to use `PersistedNetworkSettings`. Remove the duplicate registry reader `ReadInstallerNetworkValue`. Run T007 again until it passes.

### Installer script functions chain (executable `cscript` tests, FR-019)

All tasks in this chain edit `tests/integration/Installer/PersistedNetworkScriptTests.cs` or `installer/wix/Scripts/PortResolver.jscript`. Do them in ID order.

- [ ] T009 [P] Failing test: create `tests/integration/Installer/PersistedNetworkScriptTests.cs` with the harness and the read cases. The harness makes a temporary folder, loads `installer/wix/Scripts/PortResolver.jscript` as text, runs it with `eval` in a small `.js` file, calls one core function, prints plain result lines, and starts `%SystemRoot%\System32\cscript.exe //nologo`. Skip the tests when the OS is not Windows or `cscript.exe` is missing. Read cases for `ReadNetworkFileCore(dataPath)`: no file; bad JSON; bad port with good host; port as number; unknown fields. Run it and keep the failing output.
- [ ] T010 Add `ReadNetworkFileCore(dataPath)` and the `Session` function `ReadPersistedNetworkFile` to `installer/wix/Scripts/PortResolver.jscript` (needs T009). The core function takes plain arguments (no `Session`), validates each field on its own, uses no `eval`, and returns host, port, and problems. The `Session` function sets `PERSISTED_FILE_BIND_HOST` and `PERSISTED_FILE_PORT` from `[DATAROOTFOLDER]config\network.json` and writes a debug line for each ignored value (FR-009). It never fails the install. Run T009 again until it passes.
- [ ] T011 Failing test: add write cases to `tests/integration/Installer/PersistedNetworkScriptTests.cs` (needs T010). Cases for `WriteNetworkFileCore(dataPath, host, port)`: the missing `config` folder is created (FR-021); write over an old file; write with one invalid field keeps the old value for that field (FR-018); no valid field and no old file writes nothing; a `.bak` file left by a hard stop is recovered first; output is UTF-8 with no byte order mark. Run it and keep the failing output.
- [ ] T012 Add `WriteNetworkFileCore` and the deferred `Session` function `WritePersistedNetworkFile` to `installer/wix/Scripts/PortResolver.jscript` (needs T011). Follow the six write steps in plan.md (create folder, validate, recover, make undo marker `.bak` or `.none`, write `.tmp`, replace). The `Session` function splits `CustomActionData` on `|`. A part count other than 3 does nothing and writes a debug line. Run T011 again until it passes.
- [ ] T013 Failing test: add rollback cases to `tests/integration/Installer/PersistedNetworkScriptTests.cs` (needs T012). Cases for `RollbackNetworkFileCore(dataPath)`: after an old file (restores it); after "no file" (removes the new file); after a partial write (`.tmp` and `.bak` present); no marker (file not changed); rollback twice (idempotent); all temporary files removed. Run it and keep the failing output.
- [ ] T014 Add `RollbackNetworkFileCore` and the `Session` function `RollbackPersistedNetworkFile` to `installer/wix/Scripts/PortResolver.jscript` (needs T013). An empty data path does nothing. It never fails the install. Run T013 again until it passes.
- [ ] T015 Failing test: add commit and data-split cases to `tests/integration/Installer/PersistedNetworkScriptTests.cs` (needs T014). Cases: `CommitNetworkFileCore(dataPath)` removes `.bak`, `.none`, and `.tmp` and keeps `network.json`; commit with an empty data path does nothing; a write `CustomActionData` with a wrong part count does nothing. Run it and keep the failing output.
- [ ] T016 Add `CommitNetworkFileCore` and the `Session` function `CommitPersistedNetworkFile` to `installer/wix/Scripts/PortResolver.jscript` (needs T015). Commit deletes the three temporary files only. Run T015 again until it passes.

### WiX chain (source tests first)

- [ ] T017 [P] Failing test: create `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` with source tests for the read side. Assert: `ReadPersistedNetworkFile` is scheduled `After="CostFinalize"` in `InstallUISequence` and `InstallExecuteSequence` and uses `DATAROOTFOLDER`; the `SetProperty` rows for `BIND_HOST`, `PORT`, `PROTOCOL`, and `SHORTCUT_*` have explicit action names and run after the read action in the fixed chain; host and port use separate rows (FR-018); each row applies explicit value, then file value, then registry value, only while the property is empty (FR-005, FR-007); `BIND_HOST` and `PORT` have empty defaults in `installer/wix/Bundle.wxs`; both `SetApplicationFolder` entries have the condition `APPLICATIONFOLDER = ""` (FR-004, FR-016). Run it and keep the failing output.
- [ ] T018 Remove the default values of `BIND_HOST` and `PORT` (empty means "not given") in `installer/wix/Bundle.wxs` and in the MSI `Property` rows (needs T017). Keep the last-step rows that set `127.0.0.1` and `8080` when the value is still empty (FR-010).
- [ ] T019 Change `installer/wix/Fragments/InstallerProperties.wxs` (needs T018 and T010). Declare the `PERSISTED_FILE_*` properties. Schedule `ReadPersistedNetworkFile` `After="CostFinalize"` in both sequences. Move the `SetProperty` rows from `Before="CostFinalize"` to the fixed chain after the read action, each with an explicit `Action` name, with separate host and port rows. Add the condition `APPLICATIONFOLDER = ""` to both `SetApplicationFolder` entries. Run the read-side tests of T017 until they pass.
- [ ] T020 Change `installer/wix/Fragments/PortDetection.wxs` and `installer/wix/Product.wxs` (needs T019). A non-empty `PERSISTED_FILE_*` value also skips port detection and the install-folder pages. Schedule `DiscoverBindInterfaces` and `DetectAvailablePorts` after `ReadPersistedNetworkFile` and before the `SetProperty` chain (replace `Before="CostFinalize"`). Run the tests of T017 until they pass.
- [ ] T021 Update `tests/integration/Installer/UpgradePropertyRetentionTests.cs` (needs T018) if the removed defaults break its checks. Run the file and fix it so it passes.
- [ ] T022 Failing test: add write-side source tests to `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` (needs T017; same file). Assert: three immediate `SetProperty` rows set `WritePersistedNetworkFile` (`[DATAROOTFOLDER]|[BIND_HOST]|[PORT]`), `RollbackPersistedNetworkFile` (`[DATAROOTFOLDER]`), and `CommitPersistedNetworkFile` (`[DATAROOTFOLDER]`) `Before="InstallInitialize"`; the write action is deferred, `After="InstallFiles"`, `Impersonate="yes"`; the rollback action has `Execute="rollback"` and runs before the write action; the commit action has `Execute="commit"` and runs after it; all have the condition `NOT REMOVE~="ALL"` (FR-017). Run it and keep the failing output.
- [ ] T023 Change `installer/wix/Fragments/ConfigTemplates.wxs` (needs T022, T016, and T019). Add the three `SetProperty` rows, the deferred write action, the rollback action, and the commit action as T022 describes. Do not track `network.json` in the MSI, so uninstall keeps it. Keep the registry component as is. Run T022 until it passes.

**Checkpoint**: The installer reads and writes the file. The service reads it. All foundational tests pass.

## Phase 3: User Story 1 - Keep host and port after an update from the Web UI (P1)

**Goal**: A Web UI update keeps the chosen host and port.
**Independent Test**: Quickstart V-1. Install with `0.0.0.0:9090`, run the Web UI update, check the service address and shortcut URL.

- [ ] T024 [US1] Failing test: update `tests/unit/Updates/UpdaterLauncherTests.cs` (and `UpdateTestSupport.cs` if needed). Assert that `BuildArguments` gives no `--port` and no `--bind-host` argument, even when `Service:Network:*`, `GAMEBOT_PORT`, or `GAMEBOT_BIND_HOST` exist. Run it and keep the failing output.
- [ ] T025 [US1] Change `BuildArguments` in `src/GameBot.Service/Services/Updates/UpdaterLauncher.cs` (needs T024). Stop adding `--port` and `--bind-host`. Keep `UpdaterArguments` and `MsiexecArguments` unchanged (FR-013). Run T024 again until it passes.
- [ ] T026 [US1] Guard test: add a source test to `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` (needs T022; same file). Assert that the shortcut URL in `installer/wix/Fragments/Directories.wxs` and `InstallerProperties.wxs` uses `[PORT]` and the host rule (`localhost` for `127.0.0.1` and `0.0.0.0`, other hosts as given) and that the shortcut rows run after `PORT` and `BIND_HOST` are final (FR-008).

**Checkpoint**: US1 works and can be tested alone.

## Phase 4: User Story 2 - Keep host and port after a manual update (P1)

**Goal**: A newer installer keeps the saved values, and an explicit option replaces them.
**Independent Test**: Quickstart V-2. Run a newer installer with no option, then with `PORT=9091`.

- [ ] T027 [US2] Guard test: add a case to `tests/integration/Installer/PersistedNetworkScriptTests.cs` (needs T016; same file). Write `9090`, then write `9191`: the file has `9191` (FR-007). Then write with an invalid port and a valid host: the file keeps `9191` and takes the host.
- [ ] T028 [US2] Guard test: add source tests to `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` (needs T026; same file). Assert that an explicit `PORT` or `BIND_HOST` option is not replaced by a saved value, that the write action receives the final values, and that a registry value with no file still gives the saved value (FR-005, SC-004).

**Checkpoint**: US1 and US2 work.

## Phase 5: User Story 3 - Keep host and port after uninstall and install again (P2)

**Goal**: A reinstall into the same data directory keeps the values.
**Independent Test**: Quickstart V-3. Uninstall, then install again with no option.

- [ ] T029 [US3] Guard test: add source tests to `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` (needs T028; same file). Assert that no MSI element tracks `network.json` (uninstall keeps it), that the write step does not run when `REMOVE="ALL"`, and that the first-install defaults and port detection still apply when no file and no registry value exist (FR-010, FR-011, SC-005).
- [ ] T030 [P] [US3] Add an install smoke check in `scripts/installer/install-smoke.ps1` for the file: written after install, kept after uninstall.

**Checkpoint**: All three scenarios work.

## Phase 6: Polish and cross-cutting

- [ ] T031 [P] Update `INSTALL.md`. Say the installer keeps the host and port for update, manual update, and reinstall into the same data directory. Say an explicit `BIND_HOST` or `PORT` option replaces the saved value. Say a manual update under another account must pass `APPLICATIONFOLDER` of the existing install. Replace `BACKEND_PORT` and `WEB_PORT` with `BIND_HOST` and `PORT`. Use STE (FR-015).
- [ ] T032 [P] Update `docs/architecture.md` (FR-020, Principle V): describe the persistence layout (`config\network.json`, registry fallback), the address priority (configuration, environment variable, `network.json`, registry, default), and that the updater no longer forwards overrides. Set the `_Last reviewed:` line (line 13) to the date of this edit and add feature 132 to it. Use STE.
- [ ] T033 Run `dotnet build` and all unit and integration tests, plus `scripts/installer/install-smoke.ps1` if it can run (needs T001 to T032). Fix all failures. Build and tests MUST be green before T034 (Constitution gate).
- [ ] T034 REQUIRED before merge: run the manual checks V-1 to V-9 in `specs/132-persist-listen-host-port/quickstart.md` on a real MSI install (needs T033). This includes the update under a different account (V-8) and the rollback and cancel check V-9. If a check is not possible, record "not possible" and the reason in the pull request, as quickstart V-9 allows.
- [ ] T035 After T034: set `**Status**` in `specs/132-persist-listen-host-port/spec.md` to `Implemented`, and add a row for feature 132 to `specs/STATUS.md` in the same format as the existing rows (FR-020). Do this last, so Status says Implemented only when the work and the manual check are done.

## Dependencies and order

- Phase 1, then Phase 2. Phase 2 blocks all stories.
- Phase 2 has three chains that do not share files, so they can run in parallel:
  - Service: T002, T003, T004, then (T005, T006) and (T007, T008).
  - Script: T009 to T016, in order (same two files).
  - WiX: T017, T018, T019, T020, T021, T022, T023. T019 also needs T010. T023 also needs T016 and T019. T022 and every later task in `PersistedNetworkSettingsInstallerTests.cs` follow T017 in ID order.
- [P] marks the first task of each chain (T002, T009, T017) and the two service sub-chains (T005, T007). A task never runs in parallel with a task it needs.
- Phase 3: T024 then T025 (launcher chain, no shared file with other tasks). T026 needs T022.
- Phase 4: T027 needs T016. T028 needs T026.
- Phase 5: T029 needs T028. T030 has no dependency on tests; it needs T023.
- Phase 6: T031 and T032 can run together with each other and with Phase 3 to 5 tasks, because they edit different files. T033 needs all earlier tasks. T034 needs T033. T035 needs T034.

## Parallel examples

- Start together after T001: T002, T009, T017 (three chains, three different files).
- After T004: T005 with T007 (different files).
- After T023: T024 (launcher chain) with T027 and T030 (different files).
- Polish: T031 with T032.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1 (T001 to T026). This fixes the most-used path.
- Then add US2 and US3 guard tests, the smoke check, the documents, the full run, the manual MSI check, and last the Status change.

## Summary

- Total tasks: 35
- Per phase: Setup 1 (T001), Foundational 22 (T002 to T023), US1 3 (T024 to T026), US2 2 (T027 to T028), US3 2 (T029 to T030), Polish 5 (T031 to T035)
