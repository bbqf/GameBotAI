---

description: "Task list for 199: device recovery option in the queue form"
---

# Tasks: Device Recovery Option in the Queue Form

**Input**: Design documents from `specs/199-device-recovery-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-recovery-ui-api.md, quickstart.md

**Tests**: Required. FR-005 is a bug fix (Constitution Principle II): write the contract test of a `PUT` with no `deviceRecovery` first, and make sure that it fails before the fix.

**Organization**: Tasks are grouped by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story of the task (US1 to US4)

## Order of the stories

Spec priority: US1 (P1), US2 (P1), US3 (P2), US4 (P3). US2 comes before US1 in this list: the service fix of US2 stops the data loss, and the form of US1 then uses the new update rule. The setting change on a running queue (FR-012a) is part of US1.

---

## Phase 1: Setup

**Purpose**: Confirm a green start.

- [ ] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`. Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~DeviceRecovery|FullyQualifiedName~QueueLivenessWatch"`, `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~DeviceRecovery|FullyQualifiedName~SessionMonitoring"`, and `npm --prefix "C:\src\GameBot\src\web-ui" test -- QueueForm PhoneQueuesView MonitoringPage`. Record the result. Stop if the start is not green.

---

## Phase 2: Foundational

**Purpose**: The web UI types that US1, US3, and US4 use (data-model.md, "Web UI form state" and the types under it).

- [ ] T002 [P] In `src/web-ui/src/services/queues.ts`, add `export type QueueDeviceRecoveryDto = { action: 'none' | 'reboot-instance'; afterMs: number; maxAttempts: number; cooldownMs: number }` and `export type QueueDeviceLivenessDto = { state: 'live' | 'not_live'; reason?: string | null; notLiveSince?: string | null; gatedFirings?: number; alertSent?: boolean; recoveryAttempts?: number; recoveryState?: 'idle' | 'running' | 'exhausted' }`. Add `deviceRecovery?: QueueDeviceRecoveryDto | null` and `deviceLiveness?: 'live' | 'not_live'` to `QueueDto`. Add `deviceRecovery?: QueueDeviceRecoveryDto | null` to `QueueCreate` and `QueueUpdate`. Add `deviceLiveness?: QueueDeviceLivenessDto | null` to `QueueHealthDto`. Add a short STE doc comment to each new member that names feature 121 or 199.
- [ ] T003 [P] In `src/web-ui/src/services/monitoringApi.ts`, add `ownerQueueDeviceLiveness?: 'live' | 'not_live' | null` to `MonitoredSessionDto`, with the STE doc comment "The device state of the owner queue (feature 199). Null when the session has no running owner queue." Add `ownerQueueDeviceLiveness: null` to the default object in `src/web-ui/src/testUtils/monitoring.ts`.

**Checkpoint**: `npm --prefix "C:\src\GameBot\src\web-ui" run build` passes (types only).

---

## Phase 3: User Story 2 - A save keeps the recovery settings (Priority: P1)

**Goal**: An update with no `deviceRecovery` member keeps the stored value. Only an explicit `null` clears it (FR-005a, research R-001).

**Independent Test**: Set `deviceRecovery` through the API. Send `PUT /api/queues/{id}` with no `deviceRecovery`. Read the queue: the values did not change.

### Tests for User Story 2

- [ ] T004 [US2] In `tests/contract/Queues/QueueDeviceRecoveryContractTests.cs`, add three tests: (a) create a queue with `emulatorInstanceName` and `deviceRecovery` `{ action: "reboot-instance", afterMs: 120000 }`, then `PUT` a body with the name and no `deviceRecovery` member: the response and the next `GET` keep `action` `reboot-instance` and `afterMs` 120000; (b) `PUT` with `"deviceRecovery": null`: the response has `deviceRecovery` null; (c) `PUT` with `"deviceRecovery": { "action": "none" }`: the response has `action` `none`. Send the bodies as raw JSON strings, so that test (a) has no member at all. Run the tests. Test (a) MUST fail before T005. Find the current test that expects "an absent member clears" (if one exists) and change it to the new rule, with a comment that names spec 199.

### Implementation for User Story 2

- [ ] T005 [US2] In `src/GameBot.Service/Contracts/Queues/UpdateQueueRequest.cs`, change `DeviceRecovery` to a property with a private field. The setter sets the field and sets `DeviceRecoverySpecified = true`. Add `[System.Text.Json.Serialization.JsonIgnore] public bool DeviceRecoverySpecified { get; private set; }`. Change the doc comment of `DeviceRecovery` to STE: "Optional device recovery settings (feature 121). An absent member keeps the stored settings (feature 199). An explicit null clears them." Doc comment of the flag: "True when the request JSON has the member deviceRecovery, also with the value null."
- [ ] T006 [US2] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (the `MapPut("{id}"` handler, near line 116), map and validate `req.DeviceRecovery` only when `req.DeviceRecoverySpecified` is true. When it is false, keep `queue.DeviceRecovery`, and validate the kept value with `QueueDeviceRecoveryValidator.Validate(queue.DeviceRecovery, updateInstanceName)`, so that a request that clears the instance name of a queue with `reboot-instance` still gives 400 with the text of feature 121. Change the comment at line 116 to: "Feature 199: an absent deviceRecovery keeps the stored settings. An explicit null clears them." Change the comment at lines 109-110 to say that device recovery applies to a running queue at its next liveness check.
- [ ] T007 [P] [US2] Add a unit test in `tests/unit/Queues/UpdateQueueRequestDeviceRecoveryTests.cs` (new file): deserialize three JSON bodies with the same `JsonSerializerOptions` as the web host (`JsonSerializerDefaults.Web`): no member gives `DeviceRecoverySpecified == false`; `null` gives `true` and a null value; an object gives `true` and the object. Add a contract test in `tests/contract/Queues/QueueDeviceRecoveryContractTests.cs`: a queue with `reboot-instance`, then a `PUT` with no `deviceRecovery` and an empty `emulatorInstanceName`, gives 400 with the text "deviceRecovery.action 'reboot-instance' needs emulatorInstanceName. Set emulatorInstanceName on the queue."
- [ ] T008 [US2] In `src/GameBot.Service/Swagger/QueueDeviceRecoverySchemaFilter.cs`, replace the sentence "On PUT, an absent member clears the stored settings." in `FieldDescription` with "On PUT, an absent member keeps the stored settings and null clears them. A saved change applies to a running queue at its next liveness check." Update the test in `tests/contract/DeviceLivenessOpenApiTests.cs` (or the OpenAPI test that checks `FieldDescription`) to check the new text.

**Checkpoint**: T004 to T008 tests pass. The web UI save no longer clears the settings (it sends no member).

---

## Phase 4: User Story 1 - Turn on device recovery in the queue form (Priority: P1) MVP

**Goal**: The option and its three fields in the create and edit forms. The save rule of research R-006. A change on a running queue applies within 1 minute (FR-012a, research R-002).

**Independent Test**: Open the edit form of a queue with an instance name. Turn on the option, set the wait to 3 minutes, and save. `GET /api/queues/{id}` gives `action` `reboot-instance` and `afterMs` 180000.

### Service: a change applies to a running queue (FR-012a)

- [ ] T009 [US1] Write unit tests in `tests/unit/Queues/QueueLivenessWatchTests.cs` (use the fakes that the file uses now, and a `FakeTimeProvider`). Give the watch a read delegate that returns a queue object that the test changes. Tests: (a) the run starts with no recovery; the device is not live for longer than 1 minute; the test sets `DeviceRecovery` to `reboot-instance` with `AfterMs` 60000 on the read result; the next `CheckOnceAsync` starts one attempt; (b) recovery is on and one attempt ended; the test sets `Action` to `none`; the next checks start no attempt; (c) recovery is on with `MaxAttempts` 3 and 1 attempt ended; the test sets `MaxAttempts` to 1; the next check starts no attempt; (d) the read delegate throws or returns null; the watch uses the last known settings and does not throw; (e) the attempt count of the episode stays the same after a change; (f) recovery is on and an attempt runs (the fake coordinator holds the reboot open); the test sets `Action` to `none` and runs one check; the attempt that runs ends normally when the test releases the reboot; the next checks start no attempt. Tests (a) and (c) MUST fail before T010.
- [ ] T010 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueLivenessWatch.cs`, add the constructor parameter `Func<CancellationToken, Task<ExecutionQueue?>>? readQueue = null` (after `rebind`). Add the fields `_recoverySettings` (`QueueDeviceRecovery?`, start value `queue.DeviceRecovery`) and `_instanceName` (`string?`, start value `queue.EmulatorInstanceName`). At the start of `CheckOnceAsync`, when `readQueue` is not null, await it with `_stopToken`. When the result is not null, set the two fields from it. When the read throws (not `OperationCanceledException`) or gives null, keep the fields and log one debug line with a new `LoggerMessage` (EventId 1144, Level Debug, Message "Queue {QueueId}: the read of the stored queue failed. The watch uses the last known recovery settings."). In `StartRecoveryIfDue`, use `_recoverySettings` in place of `_queue.DeviceRecovery`. In `RunRecoveryAttemptAsync`, give the runner the current settings (T011). Update the class doc comment in STE: the watch reads the stored queue at each check, so a saved change of `deviceRecovery` applies to the run (feature 199).
- [ ] T011 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueDeviceRecoveryRunner.cs`, add the parameters `string? instanceName` and `int maxAttempts` to `RunAttemptAsync` and pass them down. `ExecuteStepsAsync` uses `instanceName` in place of `queue.EmulatorInstanceName`. `SendRecoveryFailedIfUsedUp` uses `maxAttempts` in place of `queue.DeviceRecovery?.MaxAttempts`. In `QueueLivenessWatch.RunRecoveryAttemptAsync`, pass `_instanceName` and `_recoverySettings?.MaxAttempts ?? QueueDeviceRecovery.DefaultMaxAttempts`. Update each caller in `tests/unit/Queues/QueueDeviceRecoveryRunnerTests.cs` and `tests/integration/DeviceRecovery*.cs` to pass the instance name and the max attempts of the test queue.
- [ ] T012 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (`StartLivenessWatch`, near line 1103), pass `ct => _queues.GetAsync(queue.Id)` as `readQueue` (wrap the call so that the token is not used by the repository, which has no token parameter). Add an integration test in `tests/integration/DeviceRecoveryFlowTests.cs`: start a queue with recovery off and a blocked device; after the episode is old enough, save `deviceRecovery` `reboot-instance` with `afterMs` 60000 through `PUT`; advance the fake time by one check interval; expect one reboot call on the fake coordinator, with no stop and start of the queue. Use the helpers in `tests/integration/DeviceRecoveryTestSupport.cs`.

### Web UI: the form rules

- [ ] T013 [P] [US1] Create `src/web-ui/src/components/queues/deviceRecoveryForm.ts` with pure functions and constants (STE doc comments):
  - `RECOVERY_DEFAULTS = { on: false, afterMinutes: 5, maxAttempts: 2, cooldownMinutes: 3 }`.
  - `msToMinutes(ms: number): number` → `Math.round(ms / 6000) / 10` (one decimal place).
  - `recoveryFormFromDto(dto: QueueDeviceRecoveryDto | null | undefined)` → `{ deviceRecoveryOn, recoveryAfterMinutes, recoveryMaxAttempts, recoveryCooldownMinutes, storedDeviceRecovery }` (defaults when the dto is null or undefined; `deviceRecoveryOn` is true only for `reboot-instance`).
  - `recoveryFieldErrors(value, emulatorInstanceName)` → `{ instanceName?, afterMinutes?, maxAttempts?, cooldownMinutes? }` with the texts of the contract ("Device recovery needs an emulator instance name.", "Enter 1 minute or more.", "Enter a whole number from 1 to 5.", "Enter 0 minutes or more."). Check only when the option is on. A value whose milliseconds are above 2147483647 or not finite gives the same text as below the minimum.
  - `recoveryToSend(value)` → `QueueDeviceRecoveryDto | undefined` by the table of research R-006. For each minute field: when the field equals `msToMinutes` of the stored milliseconds, send the stored milliseconds; else send `Math.round(minutes * 60000)`.
- [ ] T014 [P] [US1] Create `src/web-ui/src/components/queues/__tests__/deviceRecoveryForm.test.ts`: defaults; `recoveryFormFromDto` for null, `none`, and `reboot-instance`; `msToMinutes(90500)` is 1.5; `recoveryToSend` for each row of the R-006 table; an unchanged field that holds 90500 ms sends 90500; a changed field sends `Math.round(minutes * 60000)`; each field error and its text; no errors when the option is off.

### Web UI: the form

- [ ] T015 [US1] In `src/web-ui/src/components/queues/QueueForm.tsx`, add the members of data-model.md ("QueueFormValue (new members)") to `QueueFormValue`. After the row "Emulator instance name | Emulator instance index", add the checkbox, the help text, and (when on) the three number inputs, with the labels, accessible names, `min`, `max`, and `step` values of the contract. Follow the smart scheduling block of the same file (checkbox label, `form-hint`, `aria-describedby`, `aria-invalid`, `field-error` with role `alert`). Show the errors of `recoveryFieldErrors` at their fields; show the instance name error under the checkbox. Change the running queue note to "Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute."
- [ ] T016 [US1] In `src/web-ui/src/pages/QueuesPage.tsx`: add the recovery defaults to `emptyForm` (spread `recoveryFormFromDto(null)`); in the edit load (near line 167) spread `recoveryFormFromDto(q.deviceRecovery)`; in the create and update calls (near lines 224 and 227) add `deviceRecovery: recoveryToSend(form)` only when it is not undefined (do not send the key when it is undefined). Before the save, stop when `recoveryFieldErrors` gives an error (as the page does for `isValidSmartWindow`). When the service gives 400, the form error area shows the `message` of the error (check that the current error path does this; change it only if it does not).
- [ ] T017 [US1] Add tests in `src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx`: the checkbox shows and is off for a new queue; the three fields show only when it is on, with the defaults 5, 2, and 3; the running queue note has the new text. Add tests in `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx` (mock `updateQueue` and `createQueue` as the file does now): the edit form shows the stored values of a queue with `reboot-instance`; turn on and save sends `deviceRecovery` with `action` `reboot-instance` and `afterMs` 300000; turn off and save sends `action` `none`; a queue with no recovery and no change sends no `deviceRecovery` key; a 400 response shows its message.

**Checkpoint**: US1 works with the quickstart manual check 1 (steps 1 to 6) and check 3.

---

## Phase 5: User Story 3 - Clear validation in the form (Priority: P2)

**Goal**: The form stops the save and shows a message at the field for each value that is not permitted (FR-008, FR-009).

**Independent Test**: Turn on the option with an empty instance name. Press Save. The message shows, and `updateQueue` is not called.

- [ ] T018 [US3] Add tests in `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`: option on and empty instance name → the text "Device recovery needs an emulator instance name." shows and `updateQueue` is not called; wait 0.5 → "Enter 1 minute or more."; attempts 6 and attempts 1.5 → "Enter a whole number from 1 to 5."; time between attempts -1 → "Enter 0 minutes or more."; the field has `aria-invalid="true"`. Fix `QueueForm.tsx` or `QueuesPage.tsx` if a test fails.
- [ ] T019 [P] [US3] Add a test in `src/web-ui/src/components/queues/__tests__/DuplicateQueueModal.test.tsx` or `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`: after a duplicate of a queue with `reboot-instance`, the edit form of the copy shows the option on with the values of the source (FR-010; the service copies the settings, research R-007).

**Checkpoint**: US3 works with the quickstart manual check 2.

---

## Phase 6: User Story 4 - See the recovery state of a running queue (Priority: P3)

**Goal**: The device state line in the edit view of a running queue (FR-011). The "Not live" mark on the Monitoring list and header, the phone queue card, and the phone queue detail (FR-011a, FR-012).

**Independent Test**: With a running queue whose device is not live, the edit view shows "Device: not live (...)", and the Monitoring row and the phone card show "Not live".

### Service: data for the mark

- [ ] T020 [P] [US4] In `src/GameBot.Service/Contracts/Queues/QueueResponse.cs`, add `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DeviceLiveness { get; set; }` with the STE doc comment "The device state of a running queue: live or not_live (feature 199). Only the list gives it. It can be up to one liveness check old. Absent when the queue does not run or has no report." In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (the list handler near line 64), when `TryGetLiveRun` is true, take `handle.Liveness.Snapshot()`, and set `item.DeviceLiveness = snapshot.State` when `snapshot.LastReport` is not null.
- [ ] T021 [P] [US4] In `src/GameBot.Service/Endpoints/Dto/SessionMonitoringDtos.cs`, add `public string? OwnerQueueDeviceLiveness { get; set; }` to `MonitoredSessionDto` with the STE doc comment "The device state of the owner queue: live or not_live (feature 199). Null when the session has no running owner queue or the queue has no report." In `src/GameBot.Service/Endpoints/SessionMonitoringEndpoints.cs` (`ListAsync`), in the loop that sets `CurrentSequenceName`, set the member from `runs.TryGet(item.OwnerQueueId, out var handle)` and `handle.Liveness.Snapshot()` with the same rule as T020.
- [ ] T022 [US4] Add contract tests: in `tests/contract/Queues/QueueDeviceLivenessContractTests.cs`, the list gives `deviceLiveness` `not_live` for a running queue with a not-live report, and no `deviceLiveness` member for a stopped queue; in `tests/contract/Sessions/SessionMonitoringListContractTests.cs`, a session of a running queue with a not-live report gives `ownerQueueDeviceLiveness` `not_live`, and a session with no owner queue gives null. Use the fakes and the setup of each file. Also add a unit test in `tests/unit/Queues/DeviceLivenessReaderRecoveryStateTests.cs`: an episode that is not live with 2 attempts, and stored settings `reboot-instance` with `MaxAttempts` lowered from 3 to 1, gives `recoveryState` `exhausted` (spec edge case "lower the maximum attempts").

### Web UI: state line and marks

- [ ] T023 [P] [US4] Create `src/web-ui/src/components/queues/NotLiveBadge.tsx`: `export const NotLiveBadge: React.FC = () => <span className="not-live-badge">Not live</span>`. Add the class `.not-live-badge` to the stylesheet that holds the other badges of the queues area (search for a current badge class, for example in `src/web-ui/src/styles` or `src/web-ui/src/index.css`): a warning color that passes the WCAG AA contrast rule, `white-space: nowrap`, small padding, and no fixed width.
- [ ] T024 [P] [US4] Create `src/web-ui/src/components/queues/QueueDeviceState.tsx` with the props `liveness: QueueDeviceLivenessDto | null | undefined` and `recoveryOn: boolean`. Render a `div` with role `status` and the texts of the contract (section "Device state"). Format `notLiveSince` with the time format that the queues area uses now (search for a current time helper, for example in `src/web-ui/src/lib`). Render nothing when `liveness` is null or undefined.
- [ ] T025 [US4] In `src/web-ui/src/components/queues/RunningQueueControls.tsx`, add the prop `recoveryOn: boolean` and render `<QueueDeviceState liveness={health?.deviceLiveness} recoveryOn={recoveryOn} />` above `QueuePauseControls`. In `src/web-ui/src/pages/QueuesPage.tsx`, pass `recoveryOn={detail.deviceRecovery?.action === 'reboot-instance'}` where the page renders `RunningQueueControls`.
- [ ] T026 [US4] In `src/web-ui/src/components/queues/PhoneQueuesView.tsx`, render `<NotLiveBadge />` on a queue card when `queue.deviceLiveness === 'not_live'`, next to the summary line, and in the detail header when `detail.health?.deviceLiveness?.state === 'not_live'`. In `src/web-ui/src/components/monitoring/SessionList.tsx`, render `<NotLiveBadge />` in a session row when `session.ownerQueueDeviceLiveness === 'not_live'`. In `src/web-ui/src/pages/MonitoringPage.tsx` (the header of the selected session, near line 223), render the same badge with the same rule.
- [ ] T027 [US4] Add tests: `src/web-ui/src/components/queues/__tests__/QueueDeviceState.test.tsx` (each row of the contract table, and no output for null); `src/web-ui/src/components/queues/__tests__/PhoneQueuesView.test.tsx` (the card mark for `not_live`, no mark for `live` and for a stopped queue, the detail mark); `src/web-ui/src/components/monitoring/__tests__/SessionList.test.tsx` (new file: the row mark for `not_live`, no mark for null); one test in `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx` or a new `src/web-ui/src/components/queues/__tests__/RunningQueueControls.test.tsx` that the edit view of a running queue shows "Device: not live", and that the edit view of a stopped queue has no text that starts with "Device:" (US4 scenario 3).

**Checkpoint**: US4 works with the quickstart manual check 4.

---

## Phase 7: Polish and Cross-Cutting Concerns

- [ ] T028 [P] Update `docs/architecture.md`: in the feature 121 API paragraph (near line 1936), replace "An absent member on `PUT` clears it." with the rule of the contract section 1, and add the running queue rule. Add one paragraph "Feature 199 added" with `deviceLiveness` on the queue list and `ownerQueueDeviceLiveness` on the monitoring session list, and the web UI option. Change the "Device liveness" section (near line 1354) if it says that the run keeps the settings of its start. Refresh the "Last reviewed" date (line 13) to 2026-11-24.
- [ ] T029 [P] Update `docs/mobile-ui-principles.md`, section "Queue activity on a phone": add one bullet: the "Not live" mark (feature 199, phone support level `full`) on the queue card and in the queue detail; the device recovery option is in the queue settings form, which is not on the phone (`desktop-only`).
- [ ] T030 [P] Add an entry to `CHANGELOG.md` under `## [Unreleased]`, `### Added`, in the style of the entries there: "Add the device recovery option to the queue form (199-device-recovery-ui)", with the bullets **Change** (the option and its fields), **API** (the update rule of `deviceRecovery`, the two new list members), **Running queue** (a change applies within 1 minute), **Web UI** (the device state line and the "Not live" mark), **Fix** (a save from the web UI no longer clears `deviceRecovery`), **Not changed** (the stored format, the create rule, the validation texts), **Perf note** (one queue file read each minute for each running queue; no new web UI request), and the version of T035.
- [ ] T031 [P] In `specs/STATUS.md`, add the row `| 199 | Device Recovery Option in the Queue Form | Implemented |` after row 198. Change the row of 121 to `Implemented (iterated by 199)`. In `specs/121-device-not-live-alert-recovery/spec.md`, change the Status line to `**Status**: Implemented (iterated by 199)`. In `specs/199-device-recovery-ui/spec.md`, change the Status line to `Implemented`.
- [ ] T032 Run `npm --prefix "C:\src\GameBot\src\web-ui" run lint` and `npm --prefix "C:\src\GameBot\src\web-ui" test`. Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with zero new warnings, and `dotnet test` for the unit, contract, and integration projects (quickstart "Automated checks"). Fix each failure, also in old code.
- [ ] T033 Check all new and changed text (code comments, UI texts, log messages, docs, CHANGELOG) against STE (Constitution Principle VI). Correct each violation.
- [ ] T034 Do the quickstart manual checks 1 to 4 when a device is available. Record the result in the PR description. If no device is available, write that in the PR description.
- [ ] T035 Before the push of the branch for the PR, bump the version (Constitution, "Version bump"). Run `git fetch origin master`. Compare `installer/versioning/version.override.json` with the file on `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Name the new version in the CHANGELOG entry of T030.

---

## Dependencies and Execution Order

### Phase dependencies

- Phase 1 (Setup) → Phase 2 (Foundational) → the story phases.
- US2 (Phase 3) has no dependency on Phase 2 (service only), so it can start after Phase 1.
- US1 (Phase 4): the web UI tasks (T013 to T017) need Phase 2. T016 needs the update rule of US2 to keep a queue with no recovery unchanged (FR-006), so do US2 first. The service tasks T009 to T012 need only Phase 1.
- US3 (Phase 5) needs T013, T015, and T016.
- US4 (Phase 6): the service tasks T020 to T022 need only Phase 1. The web UI tasks need Phase 2.
- Phase 7 needs all stories.

### Within a story

- Write the test tasks that are marked "MUST fail" (T004, T009) before their implementation tasks, and see them fail.
- T010 before T011 (the watch passes the new parameters). T011 before T012.
- T013 before T015 and T016. T023 and T024 before T025 and T026.

### Parallel opportunities

- T002 and T003.
- After Phase 1: the service work of US2 (T004 to T008), of US1 (T009 to T012), and of US4 (T020 to T022) are in different files, except `QueuesEndpoints.cs` (T006 and T020). Do T006 before T020.
- T013 and T014 with the service tasks.
- T020, T021, T023, and T024.
- T028 to T031.

## Parallel Example: User Story 4

```text
Task: "T020 [US4] QueueResponse.DeviceLiveness and the list handler"
Task: "T021 [US4] MonitoredSessionDto.OwnerQueueDeviceLiveness"
Task: "T023 [US4] NotLiveBadge component and style"
Task: "T024 [US4] QueueDeviceState component"
```

## Implementation Strategy

### MVP first

1. Phase 1 and Phase 2.
2. Phase 3 (US2): this stops the data loss at once, also with the current web UI.
3. Phase 4 (US1): the option in the form, and the live apply on a running queue.
4. Stop and check with the quickstart checks 1 and 3. This is the MVP.

### Then

5. Phase 5 (US3): the field checks.
6. Phase 6 (US4): the state line and the marks.
7. Phase 7: docs, CHANGELOG, status lines, full test run, STE check, version bump (T035).
