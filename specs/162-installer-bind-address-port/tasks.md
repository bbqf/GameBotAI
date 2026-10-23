---

description: "Task list for feature 162: Installer Bind Address and Port Control"
---

# Tasks: Installer Bind Address and Port Control

**Input**: Design documents from `/specs/162-installer-bind-address-port/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required. Constitution Principle II says a bug fix needs a test that fails first. Write each test task before the task that makes it pass.

**Organization**: Tasks are grouped by user story. The resolver (Phase 2) is shared by all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1 to US6).
- All paths are relative to `C:\src\GameBot`.

## Safety rule for this feature

- Do NOT run the real MSI over the production install. The per-user MSI shares `UpgradeCode`, registry key, and Start-Menu folder with the production install.
- Use only Level 1 (no install) and Level 2 (isolated test variant, temporary folder, temporary port) from `quickstart.md`.
- Any task that replaces the active production binaries is a stop. Ask the owner and wait for a clear yes.

## Phase 1: Setup

**Purpose**: Baseline and test isolation

- [X] T001 Run the test projects for installer, config, and updates. Record the result as the baseline: `dotnet test GameBot.sln --filter "FullyQualifiedName~Installer|FullyQualifiedName~Config|FullyQualifiedName~Updates"`. Fix nothing yet. Report red tests before you continue.
- [X] T002 Check how the web UI shows `Source` of a configuration parameter (search `src/web-ui/src` for `Source`) and how `IConfigApplier` uses `GAMEBOT_PORT` and `GAMEBOT_BIND_HOST` in `src/GameBot.Service/Services/`. Write the result as a note in `specs/162-installer-bind-address-port/research.md` under R11 (open check closed).
- [X] T003 Add an isolated test variant to the installer build. Add a build property `InstallerInstanceName` (empty by default) in `installer/wix/Installer.Build.props`. When it is set, change these values: `UpgradeCode` in `installer/wix/Product.wxs` and `installer/wix/Bundle.wxs`, the Start-Menu folder name in `installer/wix/Fragments/Directories.wxs`, the autostart value name in `installer/wix/Fragments/StartupRegistration.wxs`, and the registry root `Software\GameBot` in `installer/wix/Fragments/ConfigTemplates.wxs` and `Directories.wxs`. Add the property `NETWORK_REGISTRY_ROOT`. Its default is `Software\GameBot\Network`. When the instance name is set, the property gets a root that holds the instance name. Also derive every component GUID from the instance name. The installer has 38 fixed `Guid=` entries in `Components.wxs`, `ConfigTemplates.wxs`, `DataDirectories.wxs`, `Directories.wxs`, `StartupRegistration.wxs`, and the generator of `PayloadFiles.Generated.wxs`. With the property empty, the MSI must be the same as before. Add table checks to `tests/integration/Installer/InstallerAuthoringScenarioTests.cs`. The default MSI keeps the production `UpgradeCode`. The test builds the MSI from `origin/master` and compares its component GUIDs with the GUIDs of the branch build. A test MSI and the default MSI share no component GUID. The test MSI never uses the default value of `NETWORK_REGISTRY_ROOT`. **Done in another way**: the build has no instance switch. `scripts/installer/new-test-variant.ps1` changes the identities in a copy of the built MSI, so the default MSI stays as it was. `TestVariantTests` checks that the copy shares no GUID. See research R12.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The one resolver, wired in the UI and execute sequences. All stories need it.

**CRITICAL**: No story work starts before this phase is done.

### Tests first

- [X] T004 [P] Write `cscript` tests that fail first in `tests/integration/Installer/PersistedNetworkScriptTests.cs`. Test these new core functions. `ResolveBindingCore`: the priority is explicit value, file, legacy registry, default, and each field has its own chain. `ShortcutHostCore`: `127.0.0.1` gives `localhost`, and `0.0.0.0` and a specific address give the computer name. `BuildShortcutUrlCore`. `CheckPortCore`: a valid number, the range, and in use. The pre-fill rule: a value that equals the pre-filled value counts as not explicit. A UI run followed by the execute run: a value that the user typed stays. Every core function that reads the registry takes the registry root as an argument. The tests use a temporary key under `HKCU\Software\GameBotTest` and delete it after the run. A test must never read or delete `HKCU\Software\GameBot`.
- [X] T005 [P] Write unit tests that fail first in `tests/unit/Installer/PortSelectionRulesTests.cs`. Use sample `netstat -ano -p tcp` text. Test the first-install port choice: the first free port of `8080,8088,8888,80`, with the first port in use. Test the port check. A `LISTENING` line on the port counts. The same port in a foreign address does not count. A `TIME_WAIT` or `ESTABLISHED` line does not count. A listener on `0.0.0.0` counts for every interface. A listener on a specific address counts only for that address. A listener `GameBot.Service.exe` inside `APPLICATIONFOLDER` does not count. The same process name in another folder counts. An unreadable process counts.

### Implementation

- [X] T006 Implement the core functions from T004 in `installer/wix/Scripts/PortResolver.jscript`. Reuse `ReadNetworkFileCore`, `parsePortText`, `isValidHostText`, and the port list code of `DetectAvailablePorts`. Add `ReadLegacyRegistryCore`. It reads `BindHost` and `Port` under the registry root that the caller gives. The installer passes `NETWORK_REGISTRY_ROOT` under `HKCU`. It never throws. Replace `isPortAvailable` with the port check from `contracts/installer-properties.md`. Parse `netstat -ano -p tcp` for `LISTENING` lines and the local address. Then ask WMI (`Win32_Process`) for the name and path of each owner. Run `netstat` once for each resolve and reuse the text. Keep the parser a pure function, so that T005 can test it with sample text.
- [X] T007 Add the entry points `ResolveNetworkBinding` and `ValidateNetworkBinding` in `installer/wix/Scripts/PortResolver.jscript`. `ResolveNetworkBinding` sets `BIND_HOST`, `PORT`, `SHORTCUT_URL`, `BINDING_PREFILL_HOST`, and `BINDING_PREFILL_PORT`. It also writes the log lines from `contracts/installer-properties.md`. `ValidateNetworkBinding` sets `NETWORK_BINDING_ERROR`. The value is empty when the binding is valid.
- [X] T008 Declare the custom actions `ResolveNetworkBinding` and `ValidateNetworkBinding` in `installer/wix/Fragments/PortDetection.wxs`. Both are immediate. The resolver uses `Return="ignore"`. Schedule `ResolveNetworkBinding` in `InstallUISequence` after `ReadPersistedNetworkFile`. Schedule it once in `InstallExecuteSequence` after `ReadPersistedNetworkFile`, with the condition `NOT REMOVE~="ALL"`. Move the three `Set*PersistedNetworkFileData` rows in `ConfigTemplates.wxs` so that they run after it. One execute run is enough, because `CreateShortcuts` runs later. Remove the "no persisted value" conditions from `DetectAvailablePorts` and `DiscoverBindInterfaces`. The resolver calls their core code. **Done**: the actions `DetectAvailablePorts` and `ReadPersistedNetworkFile` are removed. The resolver reads the file itself.
- [X] T009 Remove the `SetProperty` chain (`SetBindHostFromFile` to `SetShortcutUrlDefault`) and the registry searches `PERSISTED_BIND_HOST` and `PERSISTED_PORT` from `installer/wix/Fragments/InstallerProperties.wxs`. Keep `PERSISTED_PROTOCOL`, `PROTOCOL`, `PREFERRED_PORT_ORDER`, and `ALLOW_ONLINE_PREREQ_FALLBACK`. T013 removes the `PERSISTED_*` terms in `installer/wix/Product.wxs`. Until then, they read as empty.
- [X] T010 Make T004 and T005 pass. Build the MSI and run the Level 1 checks from `quickstart.md`.

**Checkpoint**: The resolver sets the binding in both sequences. The old chain is gone.

---

## Phase 3: User Story 1 - Choose interface and port on every local install or upgrade (Priority: P1) 🎯 MVP

**Goal**: Every interactive install and upgrade shows the choice, with validation.

**Independent Test**: Read the built MSI tables. The `WelcomeDlg` Next event has no upgrade branch around `NetworkConfigDlg`. Run the validator tests.

### Tests for User Story 1

- [X] T011 [P] [US1] Add MSI table tests that fail first in `tests/integration/Installer/InstallerAuthoringScenarioTests.cs`: no `ControlEvent` row sends `WelcomeDlg` Next to `VerifyReadyDlg` for an upgrade; `InstallDirDlg` Next runs `ResolveNetworkBinding` before `NetworkConfigDlg`; `NetworkConfigDlg` Next runs `ValidateNetworkBinding`. **Done** as source tests on the `.wxs` files (XML), and a manual check of the built MSI tables.
- [X] T012 [P] [US1] Add tests that fail first for the `ValidateNetworkBinding` messages in `tests/integration/Installer/PersistedNetworkScriptTests.cs`. Cases: invalid port text. A port out of range. A port that another program holds gives an error. A port that the installed GameBot service holds gives no message, also when it equals the saved port.

### Implementation for User Story 1

- [X] T013 [US1] Remove the `WelcomeDlg` order 1001 branch to `VerifyReadyDlg` in `installer/wix/Product.wxs`. Remove the order 1000 condition that goes with it. Every UI run goes `WelcomeDlg`, `InstallDirDlg`, `NetworkConfigDlg`, `VerifyReadyDlg`.
- [X] T014 [US1] Update `installer/wix/Fragments/NetworkConfigUi.wxs`. Run `ResolveNetworkBinding` on `InstallDirDlg` Next, before the dialog change. Run `ValidateNetworkBinding` on `NetworkConfigDlg` Next. Add one text control bound to `NETWORK_BINDING_ERROR`. Stay on the page when `NETWORK_BINDING_ERROR` is not empty. Remove the `SHORTCUT_HOST` publish rows (order 1 and 2). Update the hint text. The same-build text stays.
- [X] T015 [US1] In `installer/wix/Scripts/PortResolver.jscript`, make the interface list include a saved value that is not in the discovered list, so the combo box can show it.
- [X] T016 [US1] Make T011 and T012 pass. Run the Level 1 checks.

**Checkpoint**: The dialog shows on every interactive run and rejects bad values.

---

## Phase 4: User Story 2 - Sensible defaults for a new install (Priority: P1)

**Goal**: A new install pre-fills `127.0.0.1` and the first free port.

**Independent Test**: Run the resolver with no file and no registry value. The result is `127.0.0.1` and the first free port.

### Tests for User Story 2

- [X] T017 [P] [US2] Add tests in `tests/integration/Installer/PersistedNetworkScriptTests.cs`: no saved value gives `127.0.0.1` and `8080`; with `8080` in use it gives `8088`; the pre-filled values show in `BINDING_PREFILL_*`.
- [X] T018 [P] [US2] Add a silent first-install test (explicit `PORT` only, then explicit `BIND_HOST` only) in `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs`. The absent field follows the new-install rule.

### Implementation for User Story 2

- [X] T019 [US2] Fix any gap that T017 and T018 show, in `installer/wix/Scripts/PortResolver.jscript`. Update `scripts/installer/silent-install-examples.ps1` if the example text is out of date.

**Checkpoint**: A first install with defaults binds to localhost on the first free port.

---

## Phase 5: User Story 3 - Upgrade keeps interface and port (Priority: P1)

**Goal**: An upgrade keeps the saved binding. A bad or absent saved value falls back with a message. A silent upgrade with the saved port in use keeps the port.

**Independent Test**: Resolver tests with a saved file and with no argument give the saved values.

### Tests for User Story 3

- [X] T020 [P] [US3] Add tests in `tests/integration/Installer/UpgradePropertyRetentionTests.cs`. A saved `0.0.0.0` and `9090` stay with no argument. An explicit `PORT` replaces only the port. A bad saved port falls back to the default and writes a log line. The same-build reinstall keeps the binding.
- [X] T021 [P] [US3] Add tests for FR-022 in `tests/integration/Installer/PersistedNetworkScriptTests.cs`. A silent run with the saved port held by another program keeps the port. It writes `WARNING: port <n> is in use. The saved port is kept.` It sets no error. A silent run with the saved port held by the installed GameBot service writes no warning.

### Implementation for User Story 3

- [X] T022 [US3] In `installer/wix/Scripts/PortResolver.jscript`, make `ResolveNetworkBinding` skip the port gate in silent mode (`UILevel < 5`) and write the warning from T021. Make sure the saved port is never replaced by a "next free port" step when a saved value exists.
- [X] T023 [US3] Make T020 and T021 pass. Run the Level 1 checks.

**Checkpoint**: Upgrade keeps the binding in interactive and silent runs.

---

## Phase 6: User Story 4 - Start-Menu shortcuts show the real address (Priority: P1)

**Goal**: The `GameBot` shortcut opens `SHORTCUT_URL`. One rule decides the host.

**Independent Test**: The `Shortcut` table row uses `[SHORTCUT_URL]`. The host tests pass.

### Tests for User Story 4

- [X] T024 [P] [US4] Add MSI table tests that fail first in `tests/integration/Installer/InstallerAuthoringScenarioTests.cs`: the `GameBot` shortcut arguments are `url.dll,FileProtocolHandler [SHORTCUT_URL]`; no custom action named `NormalizeStartMenuShortcut` exists; the `GameBot Background` shortcut still exists.
- [X] T025 [P] [US4] Add table tests for `SHORTCUT_URL` cases in `tests/integration/Installer/PersistedNetworkScriptTests.cs`: `127.0.0.1` with `8081`, `0.0.0.0` with `8081`, a specific address with `8081`, and an empty `PROTOCOL` (defaults to `http`).

### Implementation for User Story 4

- [X] T026 [US4] Change the `Arguments` of `GameBotStartMenuShortcut` in `installer/wix/Fragments/Directories.wxs` to `url.dll,FileProtocolHandler [SHORTCUT_URL]`.
- [X] T027 [US4] Remove the `NormalizeStartMenuShortcut` custom action and its sequence row from `installer/wix/Fragments/PortDetection.wxs`. Remove `NormalizeStartMenuShortcut`, `updateShortcutHost`, and `readPersistedPortFromRegistry` from `installer/wix/Scripts/PortResolver.jscript`. Remove `SHORTCUT_HOST` and its default from `installer/wix/Fragments/InstallerProperties.wxs`.
- [X] T028 [US4] Make T024 and T025 pass. Run the Level 1 checks.

**Checkpoint**: One rule decides the shortcut address. The shortcut is made from the real binding.

---

## Phase 7: User Story 5 - Upgrade from the web UI or API keeps binding and shortcuts (Priority: P1)

**Goal**: The updater command line has no binding. The MSI reads it from the file. Both shortcuts exist after the upgrade.

**Independent Test**: The updater tests pass, and the Level 2 run of the quickstart (steps 4 and 5) keeps the binding and the shortcuts.

### Tests for User Story 5

- [X] T029 [P] [US5] Update the tests in `tests/unit/Updates/UpdaterProgramTests.cs` and `tests/unit/Updates/UpdaterLauncherTests.cs`: the msiexec command line has no `PORT=` and no `BIND_HOST=`; an old `--port` argument does not fail the parser.
- [X] T030 [P] [US5] Add a test in `tests/integration/Installer/UpgradePropertyRetentionTests.cs`: a `msiexec`-style run with only `APPLICATIONFOLDER` and `MSIINSTALLPERUSER` resolves the saved binding and `SHORTCUT_URL` (the UI-started upgrade case).

### Implementation for User Story 5

- [X] T031 [US5] Remove `Port` and `BindHost` from `src/GameBot.Updater/UpdaterArguments.cs` and the `PORT` and `BIND_HOST` parts from `src/GameBot.Updater/MsiexecArguments.cs`. Update the comments to say that the MSI keeps the saved binding. Keep the parser tolerant of unknown pairs. Update `src/GameBot.Service/Services/Updates/UpdaterLauncher.cs` comment text if needed.
- [X] T032 [US5] Make T029 and T030 pass. Build the solution.
- [X] T033 [US5] Level 2 check (isolated test variant, temporary folder, temporary port). Follow `quickstart.md` steps 4 and 5: silent upgrade, then an upgrade started from the API of the test instance. Check the binding, both shortcuts, and the file. Do not touch the production install. If the check needs the production install, stop and ask the owner. **Done** with `msiexec` and the command line of the updater (only `APPLICATIONFOLDER` and `MSIINSTALLPERUSER`). An upgrade that the bot API starts was not run: it needs a release source.

**Checkpoint**: A UI-started or API-started upgrade keeps the binding and the shortcuts.

---

## Phase 8: User Story 6 - One source for the saved host and port (Priority: P2)

**Goal**: One saved location (`network.json`) and one main name per component.

**Independent Test**: No code or installer file writes the registry `BindHost` or `Port`. The snapshot has one row per value. Migration tests pass.

### Tests for User Story 6

- [X] T034 [P] [US6] Add tests that fail first in `tests/unit/Config/ConfigureWebHostUrlsPersistedTests.cs`: `ResolveListenAddress` returns `HostSource` and `PortSource`; the bot ignores the registry; priority is configuration, environment, file, default.
- [X] T035 [P] [US6] Add tests that fail first in `tests/unit/Config/ConfigSnapshotPersistedNetworkTests.cs`: one row each for `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT` with the right `Source`; no `Service__Network__*` rows; an update of the two rows is rejected with the message from `contracts/runtime-network-config.md`.
- [X] T036 [P] [US6] Add tests that fail first in `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs`. The MSI registry component has no `BindHost` and no `Port` value. The commit action deletes the legacy values after the file is written. The rollback action keeps them. A pre-132 state (registry only) migrates to the file. Run the registry cases against a temporary key under `HKCU\Software\GameBotTest`. Never use `HKCU\Software\GameBot`.

### Implementation for User Story 6

- [X] T037 [US6] Remove the `BindHost` and `Port` values from `PersistNetworkConfigComponent` in `installer/wix/Fragments/ConfigTemplates.wxs`. Keep `Protocol`. Extend the data of `CommitPersistedNetworkFile` and `CommitNetworkFileCore` in `installer/wix/Scripts/PortResolver.jscript`. The commit action deletes `BindHost` and `Port` under `NETWORK_REGISTRY_ROOT`. Pass the value of the property in the action data.
- [X] T038 [US6] Update `src/GameBot.Service/Services/PersistedNetworkSettings.cs`: read the file only. Remove `RegistrySubKey`, `ReadRegistryValue`, and the `registryReader` parameter. Update all callers in `src/GameBot.Service/GameBotServiceSetup.cs` and `src/GameBot.Service/Services/ConfigSnapshotService.cs`, and the constructors used by tests.
- [X] T039 [US6] Update `ResolveListenAddress` in `src/GameBot.Service/GameBotServiceSetup.cs` to return `HostSource` and `PortSource`. Add code where the web host starts. When the bind fails, the code logs an error that names the host, the port, and the source of each. Then it exits with a non-zero code. Add a unit test with a port that another listener holds.
- [X] T040 [US6] Update `src/GameBot.Service/Services/ConfigSnapshotService.cs`. Remove the default rows `Service__Network__BindHost` and `Service__Network__Port`. Give `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT` the active value and the `Source` from T039. Reject updates to the two keys in `UpdateParametersAsync` with the message from the contract. Apply the UI map change from T002 in `src/web-ui` if T002 shows that one is needed.
- [X] T041 [US6] Remove dead code and old names: any unused function in `installer/wix/Scripts/PortResolver.jscript`, the `PERSISTED_FILE_*` names (use plain local values), and the checks for removed names in `scripts/installer/scope-smoke.ps1`, `scripts/installer/install-smoke.ps1`, and `tests/contract/Installer/InstallerPropertyContractTests.cs`. Update `tests/unit/Installer/InstallerPropertyValidationTests.cs` for the removed properties.
- [X] T042 [US6] Make T034 to T036 pass. Build the solution. Run the Level 1 checks.
- [X] T043 [US6] Level 2 check: follow `quickstart.md` steps 9 and 10 (pre-132 migration and a forced failed upgrade) on the isolated test variant. Do not touch the production install. **Done** for step 10 (forced failed upgrade, see T053). Step 9 (older registry values) ran only in the `cscript` tests. The test shell does not see the registry view of the installer service, so the install could not see a seeded key.

**Checkpoint**: The host and port have one saved location and one main name per component.

---

## Phase 9: Polish and cross-cutting concerns

**Purpose**: Documentation, version, and final checks

- [X] T044 [P] Update `INSTALL.md`: the dialog on every run, the defaults, the silent arguments, the one saved location, the names and alias, and the removal of the registry copy. Write in STE.
- [X] T045 [P] Update `docs/architecture.md` (network settings section near line 1211, and the feature list near line 33). Refresh the "Last reviewed" date.
- [X] T046 [P] Update `docs/phone-access.md` and `installer/README.md` and `INSTALLER.md` if they name removed properties or show the old flow.
- [X] T047 [P] Add a `CHANGELOG.md` entry. Mention the version bump.
- [X] T048 Update spec status lines: set `**Status**:` in `specs/162-installer-bind-address-port/spec.md`; set the feature 132 spec to "Implemented (iterated by 162)"; update `specs/STATUS.md`.
- [X] T049 Bump `patch` in `installer/versioning/version.override.json` as the constitution says: run `git fetch origin master`, compare with `origin/master`, set `patch` to the master `patch` plus 1 (or 0 if `minor` or `major` changed), and set `updatedBy` and `updatedAtUtc`.
- [X] T053 [US5] Make a failed upgrade roll back (FR-012, research R13). Found in the Level 2 run: the default `MajorUpgrade` schedule removed the old product before the transaction. Test first: `InstallerAuthoringScenarioTests.MajorUpgradeRemovesTheOldProductInsideTheTransactionSoAFailedUpgradeRollsBack`. Then `Schedule="afterInstallInitialize"` in `installer/wix/Product.wxs`. Level 2 check with `new-test-variant.ps1 -FailAfterInstallFiles`.
- [X] T050 Run the full build, all tests, and the static analysis scripts (`scripts/installer/run-static-analysis.ps1`, `scripts/installer/run-security-scans.ps1`). All results must be green. Check coverage of touched code (80% line, 70% branch). Time the dialog pre-fill and the port check on the test variant. The pre-fill must take 3 s or less. The port check must take 2 s or less. **Result**: build with `-warnaserror` is clean. Unit 3985, integration 891 (1 skipped before this change), contract 1103, web UI 1038: all passed. Coverage of touched classes: 95% to 100% lines, except `ConfigSnapshotService` branches at 75% (above the 70% goal). Pre-fill 1.9 s (interface list 1.8 s, resolve 0.04 s). Port check 0.3 s.
- [X] T051 Review all new and changed text against Principle VI (STE): spec, plan, tasks, docs, comments, messages, and commit text.
- [ ] T052 Run the full Level 2 quickstart on the isolated test variant. Ask the owner to run the manual dialog checklist in `quickstart.md` and to report the result. Record both results. Tell the owner that a Level 3 step is still open. A Level 3 step is a real upgrade of the production install. Wait for approval before any such step. **Status**: the Level 2 run is done (see T033, T043, T053). The owner checklist and Level 3 are open.

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1 (Setup)**: No dependency. T003 blocks every Level 2 check.
- **Phase 2 (Foundational)**: Depends on Phase 1 (T001 only). Blocks all stories.
- **Phases 3 to 8 (Stories)**: Depend on Phase 2.
- **Phase 9 (Polish)**: Depends on the stories you ship.

### User story dependencies

- **US1** (dialog): after Phase 2. No dependency on other stories.
- **US2** (defaults): after Phase 2. Can run with US1.
- **US3** (upgrade keeps): after Phase 2. Touches the same script file as US1 and US2, so do the script tasks one after the other.
- **US4** (shortcut): after Phase 2. T027 removes functions from the script, so do it after US1 to US3 script tasks.
- **US5** (UI/API upgrade): after US4 (the shortcut must use `SHORTCUT_URL`). The updater edits (T031) are independent and can start earlier.
- **US6** (cleanup): the bot side (T038 to T040) is independent of the installer side. The installer side (T037, T041) comes after US1 to US4.

### Within each story

- Write the tests first and see them fail.
- Then change the files.
- Run the Level 1 checks at each checkpoint.

### Parallel opportunities

- T004 and T005 (different test files).
- T011 and T012; T017 and T018; T020 and T021; T024 and T025; T029 and T030; T034, T035, and T036.
- T031 (updater) with T034 to T040 (bot side), because they touch different projects.
- T044 to T047 (different documents).
- Do NOT run two tasks in parallel when both edit `installer/wix/Scripts/PortResolver.jscript`, `installer/wix/Product.wxs`, or `installer/wix/Fragments/NetworkConfigUi.wxs`.

## Parallel example: User Story 6

```text
# Tests together:
Task: "Tests that fail first, for ResolveListenAddress sources in tests/unit/Config/ConfigureWebHostUrlsPersistedTests.cs"
Task: "Tests that fail first, for snapshot rows in tests/unit/Config/ConfigSnapshotPersistedNetworkTests.cs"
Task: "Tests that fail first, for registry cleanup in tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs"

# Bot side and installer side in parallel:
Task: "Update PersistedNetworkSettings, ResolveListenAddress, ConfigSnapshotService (T038 to T040)"
Task: "Remove registry values and add commit cleanup (T037)"
```

## Implementation strategy

### MVP first (User Story 1 and 3)

1. Phase 1 and Phase 2.
2. Phase 3 (US1) and Phase 5 (US3): the dialog shows on upgrade and the binding is kept.
3. Stop and check with the Level 1 tests and one Level 2 run.

### Incremental delivery

1. Add US4 (shortcut). Check the table tests and one Level 2 run.
2. Add US5 (UI/API upgrade). Check the Level 2 API upgrade.
3. Add US2 checks (defaults).
4. Add US6 (cleanup). Check the migration and the rollback.
5. Polish.

## Notes

- Mark a task done only when its tests pass and the build is green (Constitution release blocker).
- Commit after each task or logical group. Write commit text in STE. Pass the commit message by file (`git commit -F`).
- Use CamelCase method names, no underscores (Constitution quality gate).
- Before the push for the PR, do T049 on your own, with no prompt.
