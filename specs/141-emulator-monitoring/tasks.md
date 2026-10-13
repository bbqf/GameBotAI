# Tasks: Emulator Monitoring

**Input**: Design documents in `C:\src\GameBot\specs\141-emulator-monitoring\` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)
**Tests**: Tests are required. Each user story passes on automated tests alone (FR-023). One final manual step is separate (FR-025).

**Format**: `- [ ] T### [P?] [Story?] Description with file path`
- [P] means the task edits a file that no other open task edits, and it has no open dependency.
- Tasks that edit the same file are NEVER marked [P].
- Paths: backend `src/GameBot.Service`, `src/GameBot.Emulator`; tests `tests/unit`, `tests/contract`; web UI `src/web-ui/src`.

## Phase 1: Setup

- [X] T001 Create the folder `src/GameBot.Service/Services/Monitoring/` and the folder `src/web-ui/src/components/monitoring/` (add a `.gitkeep` only if the folder stays empty).
- [X] T002 Read `docs/mobile-ui-principles.md`, `specs/STATUS.md` (spec 140 row), and the newest `CHANGELOG.md` entries. Note the rules for later tasks. No file change.

---

## Phase 2: Foundational (blocks all user stories)

- [X] T003 Add `SessionMonitoringOptions` (`RestartTimeoutMs` 120000, `RestartPollIntervalMs` 3000, validation: poll greater than 0 and less than timeout) in `src/GameBot.Service/Services/Monitoring/SessionMonitoringOptions.cs`. Bind it to config section `Service:Monitoring` in the service registration, and add the section to `src/GameBot.Service/appsettings.json` (FR-022). (Done without the file: the repo has no `appsettings.json`; the defaults apply and `docs/architecture.md` lists the section.)
- [X] T004 Add `LdInstanceMapper` (device serial such as `emulator-5558` to LDPlayer instance index; returns "unsupported" for other serials) in `src/GameBot.Emulator/Adb/LdInstanceMapper.cs`. Use `docs`/research.md decisions.
- [X] T005 [P] Add unit tests for `LdInstanceMapper` (valid serial, no serial, unsupported serial, never reads another serial) in `tests/unit/LdInstanceMapperTests.cs`.
- [X] T006 [P] Add `MonitoringLog` with `LoggerMessage` methods for restart request, refusal, start, success, failure, and failed screenshot. Each entry shows the session id and the result (FR-017). Put it in `src/GameBot.Service/Services/Monitoring/MonitoringLog.cs`.
- [X] T007 [P] Add the DTOs and enums (`MonitoredSessionDto`, list response, `SessionRestartRecordDto`, state, step, reason values) from data-model.md in `src/GameBot.Service/Endpoints/Dto/SessionMonitoringDtos.cs`.
- [X] T008 [P] Add `ISessionReconnector` and its implementation. `ReconnectAsync(sessionId, serial)` calls `StopCapture` then `StartCapture` for the same session id (FR-015). Put it in `src/GameBot.Service/Services/Monitoring/SessionReconnector.cs`.
- [X] T009 Create the thin endpoint file with an empty `MapSessionMonitoringEndpoints` method in `src/GameBot.Service/Endpoints/SessionMonitoringEndpoints.cs`. Register the services (options, reconnector, log) and call the map method from the existing endpoint registration. Do not add logic to `Program.cs`.
- [X] T010 Create `src/GameBot.Service/Swagger/SessionMonitoringSchemaFilter.cs` (`ISchemaFilter`) with an empty body, and register it in the existing Swagger setup (FR-018).

**Checkpoint**: Backend builds. Mapper tests pass.

---

## Phase 3: User Story 1 - See a live picture of each emulator (P1)

**Goal**: List all sessions with a state. Show a live screenshot of the selected session. Update by itself every 2 s.
**Independent test**: The contract tests in `SessionMonitoringListContractTests.cs` and the Jest tests for the hook and page pass.

### Tests for User Story 1

- [X] T011 [US1] Add contract tests in `tests/contract/SessionMonitoringListContractTests.cs`: empty list gives 200 `{ "sessions": [] }`; all sessions are listed with name and state; a session with no answer shows `no_answer`; a stopping session shows `stopping`; route clash (FR-021): `GET /api/sessions/monitoring` returns 200 with the list and never `not_found`, and a real session id still reaches `GET /api/sessions/{id}`, `/snapshot`, and `/health`. Use the harness notes in memory (shared bin data dir).
- [X] T012 [P] [US1] Add a test in `tests/contract/Sessions/ScreenshotFailureLoggingTests.cs` (moved to the contract project, because the unit project has no web host): a failed snapshot request writes one log entry with the session id and result, and a successful snapshot request writes none (FR-017). Use a capturing logger and open the logging gate for the component first.
- [X] T013 [P] [US1] Add Jest tests for `useSequentialPolling` with fake timers in `src/web-ui/src/hooks/__tests__/useSequentialPolling.test.tsx`: the next request starts 2 s after the last start; no request starts while one is open; abort on session change and unmount; pause when hidden and one request at resume (FR-024, M-21, M-22).
- [X] T014 [P] [US1] Add Jest tests in `src/web-ui/src/pages/__tests__/MonitoringPage.screenshot.test.tsx`: list of all sessions with state chips; "No running sessions."; "Loading" then picture with age; "No live picture" after a failed request while the last picture stays; "Session ended" when the selected session leaves the list; only the selected session is requested and no snapshot request goes to other sessions (SC-005).

### Implementation for User Story 1

- [X] T015 [US1] Implement `GET /api/sessions/monitoring` in `src/GameBot.Service/Endpoints/SessionMonitoringEndpoints.cs`: list from `ISessionManager.ListSessions()` with no status filter; state rules from data-model.md; no device probe; name, owner queue name, `restartAvailable` using `LdInstanceMapper`. Keep the method small.
- [X] T016 [US1] Log each failed screenshot request (and no successful one) with `MonitoringLog` in the existing snapshot endpoint (find it with a search for `/snapshot` in `src/GameBot.Service/Endpoints/`). Do not change its response (FR-017).
- [X] T017 [US1] Add the list DTO descriptions and enum values to `src/GameBot.Service/Swagger/SessionMonitoringSchemaFilter.cs` (FR-018).
- [X] T018 [P] [US1] Implement `useSequentialPolling` (2 s interval, no overlap, `AbortController`, `visibilitychange` pause) in `src/web-ui/src/hooks/useSequentialPolling.ts`.
- [X] T019 [US1] Create `src/web-ui/src/services/monitoringApi.ts` with `listMonitoredSessions` and `fetchSnapshot(sessionId)` (adds `?t=<ms>`).
- [X] T020 [P] [US1] Create `ScreenshotView` (loading state, picture, age counter, "No live picture" marker, full width) in `src/web-ui/src/components/monitoring/ScreenshotView.tsx`.
- [X] T021 [P] [US1] Create `SessionList` (state chips; `PhoneCard` on a phone) in `src/web-ui/src/components/monitoring/SessionList.tsx`.
- [X] T022 [US1] Create `src/web-ui/src/pages/MonitoringPage.tsx`: list through `usePolling` every 5 s; selection in page state and the URL query `session`; screenshot polling only for the selected session; empty and ended messages. Use the STE text list in `contracts/monitoring-ui-contract.md`.

**Checkpoint**: US1 tests pass without any manual step.

---

## Phase 4: User Story 2 - Restart the whole emulator (P1)

**Goal**: Confirm, restart, show progress and result. One restart at a time for each session. Always reconnect.
**Independent test**: Unit tests for the restart service, `SessionMonitoringRestartContractTests.cs`, and the Jest dialog and page restart tests pass.

### Tests for User Story 2

- [X] T023 [US2] Add unit tests in `tests/unit/SessionRestartServiceTests.cs` with a fake `IEmulatorControl`, a fake `ISessionReconnector`, and a fake clock: success calls the reconnector once with the session id and serial (FR-015, FR-023); timeout gives `failure` with reason `timeout`; reboot error gives `reboot_failed`; `RestartPollIntervalMs` set small controls the check wait (FR-022); a second request while `restarting` is refused (FR-013); a restart for session A never touches the instance of session B (SC-007); each step is logged (FR-017).
- [X] T024 [P] [US2] Add contract tests in `tests/contract/SessionMonitoringRestartContractTests.cs`: 404 unknown session; 202 then `GET` shows `success` with a fake control; 409 on two POST calls with a slow fake control; 422 for a session without a serial and the emulator is not changed (FR-014); `GET` gives 404 `no_restart` before any restart; no change of a second session.
- [X] T025 [P] [US2] Add Jest tests in `src/web-ui/src/components/monitoring/__tests__/RestartEmulatorDialog.test.tsx`: the dialog names the emulator; the queue warning shows when `ownerQueueId` is set; "Cancel" and Escape send nothing.
- [X] T026 [P] [US2] Add Jest tests in `src/web-ui/src/pages/__tests__/MonitoringPage.restart.test.tsx`: one POST on "Restart"; no second POST after a connection loss; step text while `restarting`; "Emulator restarted"; "Restart failed" with reason; 409 message; control off when `restartAvailable` is false.

### Implementation for User Story 2

- [X] T027 [US2] Add `SessionRestartRecord` and the in-memory store with an atomic check-and-set in `src/GameBot.Service/Services/Monitoring/SessionRestartRecord.cs`.
- [X] T028 [US2] Implement `ISessionRestartService` and `SessionRestartService` in `src/GameBot.Service/Services/Monitoring/SessionRestartService.cs`: `ldconsole reboot` through `IEmulatorControl`, instance from `LdInstanceMapper`, wait for device with `RestartPollIntervalMs`, call `ISessionReconnector.ReconnectAsync`, check capture, limit `RestartTimeoutMs`, log each step with `MonitoringLog`. Register it in the service registration.
- [X] T029 [US2] Add `POST /api/sessions/{id}/restart` (202, 404, 409, 422) and `GET /api/sessions/{id}/restart` (404 `not_found`, 404 `no_restart`) in `src/GameBot.Service/Endpoints/SessionMonitoringEndpoints.cs`.
- [X] T030 [US2] Add the restart DTO descriptions and enum values to `src/GameBot.Service/Swagger/SessionMonitoringSchemaFilter.cs` (FR-018).
- [X] T031 [US2] Add `startRestart` and `getRestart` to `src/web-ui/src/services/monitoringApi.ts`.
- [X] T032 [P] [US2] Create `RestartEmulatorDialog` (focus trap, Escape closes, buttons "Cancel" and "Restart", queue warning) in `src/web-ui/src/components/monitoring/RestartEmulatorDialog.tsx`.
- [X] T033 [US2] Wire the restart in `src/web-ui/src/pages/MonitoringPage.tsx`: 44 px "Restart emulator" control, reason text when off, dialog, one POST, `GET /restart` poll every 2 s while `restarting`, result messages, `ConnectionBanner` off state.

**Checkpoint**: US2 tests pass without any manual step.

---

## Phase 5: User Story 3 - Desktop and phone layouts (P2)

**Goal**: Same functions in both layouts. Selection stays on layout change.
**Independent test**: The three navigation tests and `MonitoringPage.layout.test.tsx` pass.

### Tests for User Story 3

- [X] T034 [US3] Update `src/web-ui/src/types/__tests__/navigation.test.ts` for the new area, the new count, the new order, and phone level `full`.
- [X] T035 [US3] Update `src/web-ui/src/components/__tests__/Navigation.test.tsx` for the Monitoring label in the desktop navigation and the phone tab bar or "More" list, and the moved order.
- [X] T036 [US3] Update `src/web-ui/src/__tests__/navigation.spec.tsx` for `/monitoring` and `?area=monitoring`, and the moved order.
- [X] T037 [P] [US3] Add Jest tests in `src/web-ui/src/pages/__tests__/MonitoringPage.layout.test.tsx`: the selected session stays after a switch between desktop and phone with no remount; on a phone the list is first, a tap opens the detail with a new history entry, and back calls `history.back()`.

### Implementation for User Story 3

- [X] T038 [US3] Add `{ id: 'monitoring', label: 'Monitoring', path: '/monitoring', order: 2, phone: 'full' }` and move later orders up by one in `src/web-ui/src/types/navigation.ts`.
- [X] T039 [US3] Wire the Monitoring area to `MonitoringPage` in `src/web-ui/src/App.tsx`.
- [X] T040 [P] [US3] Add phone styles (one column, picture `max-width: 100%`, 44 px targets, hover rules in `@media (hover: hover)`, no sideways scroll at 320 px) in `src/web-ui/src/pages/Monitoring.phone.css`, and import it in `src/web-ui/src/pages/MonitoringPage.tsx`.
- [X] T041 [P] [US3] List Monitoring as `full` in `docs/mobile-ui-principles.md` (M-03).

**Checkpoint**: All three stories pass on automated tests.

---

## Phase 6: Polish and cross-cutting

- [X] T042 Update `docs/architecture.md`: the three new routes, the monitoring capability, the emulator restart capability, the `Service:Monitoring` options, and a new "Last reviewed" date (FR-026).
- [X] T043 [P] Add an entry under `[Unreleased]` > `Added` in `CHANGELOG.md`: the Monitoring area, the three routes, the setting `RestartPollIntervalMs`, and `141-emulator-monitoring` (FR-028).
- [X] T044 [P] Add a row for feature 141 in `specs/STATUS.md` with the status words of that file (spec 140 is the example) (FR-027).
- [X] T045 Run the real green gates: `dotnet build`, the unit and contract tests, `jest`, and `vite build` in `src/web-ui` (lint and `tsc --noEmit` have known old failures). Fix new failures.
- [X] T046 Set the final `Status` line in `specs/141-emulator-monitoring/spec.md` (FR-027).
- [ ] T047 (SKIPPED: needs a real emulator and cannot run autonomously; left for the operator) Final manual verification on a real emulator (FR-025, one step only): check SC-001 (picture within 3 s), SC-002 (change within 5 s), SC-004 (restart ends in 2 min, picture returns), and SC-006 (360 px, no sideways scroll). Use `quickstart.md`. Record the result in the pull request text.

---

## Dependencies and order

- Phase 1 then Phase 2 then the stories then Phase 6.
- US1 and US2 share `SessionMonitoringEndpoints.cs`, `SessionMonitoringSchemaFilter.cs`, `monitoringApi.ts`, and `MonitoringPage.tsx`, so do US1 before US2. US3 needs the page from US1 and US2.
- T047 is last. No code story depends on it.

## Parallel examples

- Phase 2: T005, T006, T007, T008 together.
- US1 tests: T012, T013, T014 together (T011 first in its own file is also free to run beside them).
- US1 code: T018, T020, T021 together.
- US2 tests: T024, T025, T026 together.
- Polish: T043 and T044 together.

## Strategy

MVP is US1 (Phases 1, 2, 3). Then US2, then US3, then Polish and the final manual step.

## Totals

47 tasks. US1: 12 (T011-T022). US2: 11 (T023-T033). US3: 8 (T034-T041).
