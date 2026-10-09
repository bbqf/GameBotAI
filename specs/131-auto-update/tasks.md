---

description: "Task list for feature 131 auto-update"
---

# Tasks: Auto-Update

**Input**: Design documents from `/specs/131-auto-update/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required. Constitution Principle II applies to all executable logic.

**Organization**: Tasks are in groups by user story. You can implement and test each story alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel. It uses other files and has no open dependency.
- **[Story]**: User story label (US1 to US4).
- Method names use CamelCase with no underscores.
- All comments, messages, and docs use STE (Constitution Principle VI).

## Phase 1: Setup

**Purpose**: New project and build changes

- [X] T001 Create the console project `src/GameBot.Updater/GameBot.Updater.csproj`. Use .NET 9 and a framework-dependent build. Add an empty `Program.cs`. Add the project to `GameBot.sln`.
- [X] T002 Publish `GameBot.Updater` framework-dependent into the installer payload. Edit `scripts/package-installer-payload.ps1`. Use the path `payload/service/updater/`. After a payload build, check that `PayloadFiles.Generated.wxs` lists the updater files.
- [X] T003 [P] Create the test folders `tests/unit/Updates/`, `tests/contract/Update/`, and `tests/integration/Updates/`. Git keeps a folder only when it has a file. T010, T016, and T033 add the first files.
- [X] T004 [P] Add `GameBot.Updater` to the coverage settings. The coverage gate in `scripts/check-coverage-thresholds.ps1` must include it.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared types, settings, and interfaces

- [X] T005 [P] Create the domain types `ReleaseInfo`, `UpdateCheckResult`, `UpdateCheckStatus`, `UpdateAttempt`, and `UpdateState` in `src/GameBot.Domain/Updates/`. Follow `data-model.md`.
- [X] T006 [P] Create `UpdateVersionSelector` in `src/GameBot.Domain/Updates/UpdateVersionSelector.cs`. Parse the tag `v<major>.<minor>.<patch>.<build>`. Ignore a tag that does not parse. Return `updateAvailable` only when the release version is greater. Use `SemanticVersionComparer`. Covers FR-002, FR-008.
- [X] T007 [P] Create `ChecksumVerifier` in `src/GameBot.Domain/Updates/ChecksumVerifier.cs`. Compute the SHA-256 of a stream. Compare it with a 64-character hex string. Ignore case. Covers FR-004.
- [X] T008 [P] Create the `UpdateManifest` parser in `src/GameBot.Domain/Updates/UpdateManifest.cs`. Follow `contracts/release-manifest.md`. Accept `schemaVersion` 1 only. The `version` must match the tag. The `sha256` must have 64 hex characters. The `sizeBytes` must be greater than 0. Covers FR-004, FR-010.
- [X] T009 [P] Create `UpdateOptions` in `src/GameBot.Service/Services/Updates/UpdateOptions.cs`. Add `Repository` (default `bbqf/GameBotAI`) and `ApiBaseUrl` (default `https://api.github.com`). Bind both from configuration. Covers FR-012.
- [X] T010 [P] Write unit tests for T006, T007, and T008. Use the files `tests/unit/Updates/UpdateVersionSelectorTests.cs`, `ChecksumVerifierTests.cs`, and `UpdateManifestTests.cs`. Cover a bad tag, an equal version, a lower version, a bad hash length, and a wrong schema. Covers FR-002, FR-004, FR-008.
- [X] T011 [P] Create the interfaces `IReleaseClient`, `IUpdateDownloader`, `IUpdaterLauncher`, `IUpdateResultStore`, `IInstalledVersionProvider`, and `IInstallLocationGuard` in `src/GameBot.Service/Services/Updates/`.
- [X] T012 Implement `InstalledVersionProvider` in `src/GameBot.Service/Services/Updates/InstalledVersionProvider.cs`. It reads `InformationalVersion` of the service assembly. It removes everything from the first `+` before the parse (research R-005). Add unit tests in `tests/unit/Updates/InstalledVersionProviderTests.cs`. Cover `1.7.0.412` and `1.7.0.412+abc123`. Covers FR-001, FR-002.
- [X] T013 Implement `UpdateResultStore` in `src/GameBot.Service/Services/Updates/UpdateResultStore.cs`. It reads and writes the result file. It renames the file to `update-result.reported.json`. Use the paths in `data-model.md`. Add unit tests in `tests/unit/Updates/UpdateResultStoreTests.cs`. Covers FR-006.

**Checkpoint**: Domain logic and interfaces are ready.

---

## Phase 3: User Story 1 - Update from the UI in a few clicks (Priority: P1) MVP

**Goal**: A user checks for an update and confirms. The bot returns on the new version.

**Independent Test**: Install an older build. Publish a newer release. Click "Check for Update" and confirm. The UI shows the new version. All data is present.

### Tests for User Story 1

- [X] T014 [P] [US1] Write unit tests for `GitHubReleaseClient` in `tests/unit/Updates/GitHubReleaseClientTests.cs`. Use a fake `HttpMessageHandler`. Cover the latest release parse, the manifest fetch, the host list, and a redirect to a bad host. Covers FR-004, FR-012.
- [X] T015 [P] [US1] Write unit tests for `LoopbackGuard` in `tests/unit/Updates/LoopbackGuardTests.cs`. Cover IPv4 loopback, IPv6 loopback, IPv4-mapped IPv6, and a remote address. Check that `X-Forwarded-For` has no effect. Covers FR-014.
- [X] T016 [P] [US1] Write contract tests in `tests/contract/Update/UpdateEndpointsContractTests.cs`. Cover `GET /api/update/status`, `POST /api/update/check`, and `POST /api/update/install`. Cover these responses: 202, 400 `update_confirmation_required`, 403 `update_local_only`, and 409 `update_not_installed`. Use a loopback client and a fake launcher for 202. Use a fake remote address for 403. Use a fake guard for 409. Add a test for a request that is remote and not installed. It must return 403. Covers FR-003, FR-014, FR-015.
- [X] T017 [P] [US1] Write unit tests for the Updater in `tests/unit/Updates/UpdaterProgramTests.cs`. Cover the command line, the wait for exit, the `msiexec` argument builder, and the result file. Covers FR-005.
- [X] T018 [P] [US1] Write unit tests for `InstallLocationGuard` in `tests/unit/Updates/InstallLocationGuardTests.cs`. Cover these cases: same folder, different folder, registry value absent, quoted path, different letter case, and a slash at the end of the path. Covers FR-015.
- [X] T019 [P] [US1] Write unit tests for `UpdateCheckService` in `tests/unit/Updates/UpdateCheckServiceTests.cs`. Cover an update available, up to date, and check failed. Covers FR-002.
- [X] T020 [P] [US1] Write unit tests for the install flow of `UpdateCoordinator` in `tests/unit/Updates/UpdateCoordinatorTests.cs`. Use fake interfaces. Cover the confirm, download, verify, launch, and stop request. Covers FR-003, FR-004.
- [X] T021 [P] [US1] Write Jest tests for the update page in `src/web-ui/src/pages/__tests__/UpdatePage.test.tsx`. Cover the check and the available state. Check that notes show as text and that markup is not rendered. Check that the notes area is hidden when the notes are empty. Cover the confirm dialog with the queue-stop warning, the install, the reconnect, and the success state. Covers FR-001, FR-003, FR-006, FR-013, FR-016.

### Implementation for User Story 1

- [X] T022 [P] [US1] Implement `GitHubReleaseClient` in `src/GameBot.Service/Services/Updates/GitHubReleaseClient.cs`. Call `GET /repos/{repo}/releases/latest`. Read the `update-manifest.json` asset. Accept only `https` URLs on `github.com` and `*.githubusercontent.com`. Covers FR-004, FR-012.
- [X] T023 [P] [US1] Implement `LoopbackGuard` in `src/GameBot.Service/Services/Updates/LoopbackGuard.cs`. Use `Connection.RemoteIpAddress` (research R-006). Covers FR-014.
- [X] T024 [P] [US1] Implement `InstallLocationGuard` in `src/GameBot.Service/Services/Updates/InstallLocationGuard.cs` (research R-012). Covers FR-015.
- [X] T025 [P] [US1] Implement `UpdateDownloader` in `src/GameBot.Service/Services/Updates/UpdateDownloader.cs`. Stream the MSI to `<data root>\updates\GameBot-<version>.msi`. Report progress. Then call `ChecksumVerifier`. Covers FR-004.
- [X] T026 [P] [US1] Implement `GameBot.Updater` in `src/GameBot.Updater/Program.cs`. Parse these arguments: bot PID, MSI path, install folder, port, bind host, data root, and result path. Wait up to 60 s for the bot to exit. Run `msiexec /i ... /qn /norestart /l*v` (research R-002). Start `GameBot.Service.exe`. Write the result file. Covers FR-005, FR-007.
- [X] T027 [US1] Implement `UpdateCheckService` in `src/GameBot.Service/Services/Updates/UpdateCheckService.cs`. Use `IReleaseClient`, `UpdateVersionSelector`, and `IInstalledVersionProvider`. Keep the last check result. This task needs T022. Covers FR-002.
- [X] T028 [US1] Implement `UpdaterLauncher` in `src/GameBot.Service/Services/Updates/UpdaterLauncher.cs`. Copy the updater files to `<data root>\updates\updater\`. Start the copy detached with the arguments of T026. This task needs T026. Covers FR-005.
- [X] T029 [US1] Implement the install flow in `UpdateCoordinator` in `src/GameBot.Service/Services/Updates/UpdateCoordinator.cs`. Make it a singleton with one `SemaphoreSlim`. Return `409 update_in_progress` for a second request. Change the states in order. Call `IHostApplicationLifetime.StopApplication()` after the launch. This task needs T025, T027, and T028. Covers FR-003, FR-006.
- [X] T030 [US1] Implement `UpdateEndpoints` in `src/GameBot.Service/Endpoints/UpdateEndpoints.cs`. Use named handler methods, not inline lambdas. Follow `contracts/update-api.md`. Follow the check order of the install route. Add OpenAPI tags, names, request types, and response types. Compute `canInstallHere` and `installBlockedReason` from `LoopbackGuard` and `InstallLocationGuard`. Register the routes in `src/GameBot.Service/Program.cs` next to `MapVersioningEndpoints()`. This task needs T023, T024, and T029. Covers FR-003, FR-014, FR-015.
- [X] T031 [US1] Register all update services in DI in `src/GameBot.Service/GameBotServiceSetup.cs`. This task needs T009, T011, T012, and T013.
- [X] T032 [US1] Implement `UpdateResultReportingService` in `src/GameBot.Service/Hosted/UpdateResultReportingService.cs`. At start, it reads `update-result.json`. It exposes the result as `lastResult`. It renames the file. Register it as a hosted service that finishes before the first status request. Covers FR-006.
- [X] T033 [P] [US1] Write an integration test in `tests/integration/Updates/UpdateFlowIntegrationTests.cs`. Use a fake release server and a fake launcher. Run the check and the install. Check that the launcher gets the right arguments. Check that the bot reads the result file at start. Covers FR-004, FR-006.
- [X] T034 [P] [US1] Add `src/web-ui/src/services/update.ts`. It is the API client for the three routes. Add `src/web-ui/src/pages/UpdatePage.tsx`. The page has these parts: the "Check for Update" button, the versions, the release notes, and the "Install update" button. Show the notes as plain text only, with no HTML. Hide the notes area when the notes are empty. Add a confirm dialog that states that all active queues stop at once. Send `confirmStopQueues: true` only after the user accepts. Show progress. Request the status at intervals while the bot restarts. Show the last result. Show the messages for `remote` and `notInstalled`. Covers FR-001, FR-003, FR-006, FR-013, FR-015, FR-016.
- [X] T035 [US1] Add the page to the app navigation. Add a new tab "Update" in `src/web-ui/src/components/Nav.tsx`. Add the page to the page switch in the app root. Find the root with `Grep "Backup & Restore" src/web-ui/src`. Covers FR-001.
- [X] T036 [P] [US1] Change `.github/workflows/release-installer.yml`. Add the input `publish_release` to `workflow_dispatch`. Add a manifest step. Add a `publish_release` job. The job runs only on `master` and only when the input is true. The job has `contents: write`. It attaches `GameBot.msi`, `GameBotInstaller.exe`, and `update-manifest.json`. It uses the tag `v<version>`. The build job uploads the MSI, the EXE, and the manifest as artifacts. The publish job downloads them and does not build again. A push to master without the input must create no release. Add `scripts/new-update-manifest.ps1`. It writes the manifest from the MSI file. Covers FR-010, FR-011.
- [X] T037 [US1] Run the unit, contract, integration, and Jest tests for this story. Fix all failures before the next phase.

**Checkpoint**: The main path works, with the warning dialog. This is the MVP.

---

## Phase 4: User Story 2 - Already up to date and offline cases (Priority: P2)

**Goal**: The button gives a clear answer in every state.

**Independent Test**: Click the button on the newest version. Then click it again with no network.

- [X] T038 [P] [US2] Add unit tests to `tests/unit/Updates/UpdateCheckServiceTests.cs`. An equal version gives `upToDate`. A lower release version gives `upToDate` (no downgrade, FR-008). A network error gives `checkFailed` with a hint. HTTP 403, 404, and 5xx give `checkFailed` with a hint. Covers FR-008, FR-009.
- [X] T039 [P] [US2] Add contract tests to `tests/contract/Update/UpdateEndpointsContractTests.cs`. A failed check returns `200` with `status: "checkFailed"`. A second check during an install returns `409 update_in_progress`. Covers FR-009.
- [X] T040 [US2] Map every failure of `GitHubReleaseClient` to a `checkFailed` result. Add an error code and a hint. Edit `src/GameBot.Service/Services/Updates/UpdateCheckService.cs`. Use the codes in `contracts/update-api.md`: `update_network_error`, `update_rate_limited`, `update_no_release`, `update_manifest_invalid`, and `update_manifest_unsupported`. A rate limit gives a "try again later" hint. Test each code in T038. Covers FR-009.
- [X] T041 [P] [US2] Show the states "up to date" and "check failed" in `src/web-ui/src/pages/UpdatePage.tsx`. Show the hint. Disable the button while a check runs. Add Jest tests in `src/web-ui/src/pages/__tests__/UpdatePage.test.tsx`. Covers FR-009.

---

## Phase 5: User Story 3 - Safe update when queues run (Priority: P2)

**Goal**: Active queues stop in a clean way. They resume after the restart.

**Independent Test**: Start a queue and install an update. Check the queue state after the restart.

- [X] T042 [P] [US3] Add a unit test to `tests/unit/Updates/UpdateCoordinatorTests.cs`. The coordinator stops queue execution before it launches the updater. It does not wait for idle (clarification Q2). Covers FR-013.
- [X] T043 [US3] Call the current queue execution service in `src/GameBot.Service/Services/Updates/UpdateCoordinator.cs`. Stop all active queues at once before the updater starts. Find the stop method in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`. Covers FR-013.
- [X] T044 [US3] Add an integration test to `tests/integration/Updates/UpdateFlowIntegrationTests.cs`. A queue with `resumeOnServiceStart` starts again after a simulated restart. Reuse the pattern of the `QueueResumeOnStartupService` tests. Covers FR-013.

---

## Phase 6: User Story 4 - A failed update does not break the bot (Priority: P3)

**Goal**: Every failure leaves a bot that works and shows a clear error.

**Independent Test**: Serve an installer with a wrong checksum. The update stops. The old version works.

- [X] T045 [P] [US4] Add unit tests to `tests/unit/Updates/UpdateCoordinatorTests.cs`. A checksum mismatch deletes the file and sets `update_checksum_mismatch`. The updater does not start. Low disk space sets `update_disk_space`. Low means free space under 3 times the MSI size. A network stop in the middle of the download deletes the partial file and sets `update_download_failed`. Covers FR-009.
- [X] T046 [P] [US4] Add unit tests for the Updater to `tests/unit/Updates/UpdaterProgramTests.cs`. An `msiexec` exit code other than 0 and 3010 writes `failed` with the exit code. The updater starts the old bot. It records the path of the `msiexec` log. Covers FR-009.
- [X] T047 [US4] Add the disk space check, the checksum failure path, and the download failure path. Edit `src/GameBot.Service/Services/Updates/UpdateCoordinator.cs` and `UpdateDownloader.cs`. Delete the partial file after a download failure. Covers FR-009.
- [X] T048 [US4] Handle three cases in `src/GameBot.Updater/Program.cs`. These are an `msiexec` failure, a bot that does not start, and a time-out of the PID wait. Write `failed` with `errorCode` `update_install_failed` or `update_restart_failed`. Map the common `msiexec` exit codes 1603, 1618, and 1925 to a clear message with a hint. Covers FR-009.
- [X] T049 [US4] After a success, delete the old files in `<data root>\updates\`. Delete the `msiexec` logs that are older than 30 days. Edit `src/GameBot.Service/Services/Updates/UpdateResultStore.cs`. Covers FR-007.
- [X] T050 [P] [US4] Show the error code, message, hint, and log path of a failed result in `src/web-ui/src/pages/UpdatePage.tsx`. Add a Jest test. Covers FR-009.

---

## Phase 7: Polish and final checks

- [X] T051 Update `docs/architecture.md`. Add the API surface (three routes), the capability map (update flow), and the persistence layout (`updates` folder). Set the "Last reviewed" date (Constitution Principle V).
- [X] T052 [P] Set the `Status` line of `specs/131-auto-update/spec.md`. Update `specs/STATUS.md` with `scripts/set-spec-status.ps1`.
- [X] T053 [P] Add a changelog entry. Add a doc section to `installer/README.md`. The section states the SmartScreen step at the first install. It states how to update. It states how the owner publishes a release. Covers FR-010.
- [X] T054 [P] Check the analyzers. `UpdateEndpoints.cs` must have no new taint analyzer warnings. The workflow must have no secrets.
- [X] T055 Run the full test suite, the coverage gate (`scripts/check-coverage-thresholds.ps1`), and the web UI build. Report the results. A red result blocks the PR (Constitution release blocker).
- [ ] T056 Do the manual checks in `specs/131-auto-update/quickstart.md`, steps 1 to 10, on a real install. Record the click count (SC-001), the total update time (SC-004), and the time of one check. If `PORT` and `BIND_HOST` do not keep their values, pass the saved values as explicit `msiexec` properties in `src/GameBot.Updater/Program.cs`. Write the results in the PR description. Covers FR-005, FR-007, FR-011, FR-015, SC-001 to SC-005.
- [X] T057 Check all new text for STE. This covers specs, comments, UI text, error messages, and commit messages.

---

## Dependencies and Execution Order

- Do Phase 1 first. Phase 2 needs T001 for the build.
- Phase 2 blocks all stories.
- US1 (Phase 3) is the MVP. US2, US3, and US4 edit the same files: `UpdateCoordinator.cs`, `UpdateCheckService.cs`, and `UpdatePage.tsx`. Do them after US1, one after the other. Do not edit the same file in parallel.
- US2, US3, and US4 do not depend on each other. Do them in priority order: US2, US3, US4.
- Phase 7 runs last. T056 needs a built installer from the final code.

### Inside US1

- Write the tests T014 to T021 first. They must fail before the code exists.
- T022, T023, T024, T025, and T026 can run in parallel. Then do T027 to T032 in order.
- The UI (T034, T035) can run in parallel with the backend. The file `contracts/update-api.md` is final.
- T036 (workflow) does not depend on the code tasks.

## Parallel Example: User Story 1

```text
Task: T014 GitHubReleaseClientTests
Task: T015 LoopbackGuardTests
Task: T022 GitHubReleaseClient
Task: T023 LoopbackGuard
Task: T024 InstallLocationGuard
Task: T026 GameBot.Updater
Task: T034 UpdatePage and update.ts
Task: T036 release workflow
```

## Implementation Strategy

1. Finish Phase 1 and 2.
2. Finish US1. Run T056 on a real install. This is the MVP. It proves the goal of no Windows prompts.
3. Add US2, then US3, then US4. Run the full tests after each phase.
4. Finish with Phase 7. Open the PR.
