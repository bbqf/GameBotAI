---

description: "Task list for feature 189: Smart scheduling"
---

# Tasks: Smart Scheduling

**Input**: Design documents from `/specs/189-smart-scheduling/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/rest-api.md, contracts/scheduling-rule.md, quickstart.md

**Tests**: The constitution (Principle II) requires tests. Write the tests of each story first, and make sure that they fail before the implementation.

**Organization**: The tasks are in groups by user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependencies).
- **[Story]**: The user story of the task (US1, US2, US3, US4).

## Path Conventions

- Domain: `src/GameBot.Domain/`. Service: `src/GameBot.Service/`. Web UI: `src/web-ui/src/`.
- Tests: `tests/unit/`, `tests/integration/`, `tests/contract/`, and `__tests__` directories in the web UI.
- Check the test project names with `Get-ChildItem C:\src\GameBot\tests -Recurse -Filter *.csproj` before the first test run.
- All new text (comments, messages, descriptions, docs) obeys STE. Method names have no underscores.

---

## Phase 1: Setup

**Purpose**: Make sure that the start point is green.

- [X] T001 Build the solution with `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` and run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Queues"`. Record that all tests pass before a change.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The stored members and the pure rule that all user stories use.

- [X] T002 [P] Create `src/GameBot.Domain/Queues/SmartSchedulingLimits.cs`: a public static class with the constants `DefaultWindowMinutes = 15`, `MinWindowMinutes = 1`, `MaxWindowMinutes = 120`, and the method `IsValidWindow(int minutes)`. Add XML comments (data-model.md).
- [X] T003 In `src/GameBot.Domain/Queues/ExecutionQueue.cs`, add `public bool SmartScheduling { get; set; }` and `public int SmartSchedulingWindowMinutes { get; set; } = SmartSchedulingLimits.DefaultWindowMinutes;` with XML comments. The comments say: default off; an old queue file reads as off and 15; the window stays when the switch is off; each Timer booking reads the two values from the stored queue, so a saved change applies immediately (research R-004).
- [X] T004 [P] In `src/GameBot.Domain/Commands/CommandSequence.cs`, add `IgnoreSmartScheduling` (bool) with `[JsonPropertyName("ignoreSmartScheduling")]` and `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`, on the pattern of `ExcludeFromSuccessNotifications`. The XML comment says: when true, the bookings of this sequence always keep the requested time.
- [X] T005 [P] Create `tests/unit/Queues/SmartSchedulingRuleTests.cs` with one test for each row of the decision table in `contracts/scheduling-rule.md` (rows 1 to 10), one test for the tie rule (two candidates with the same due time: the first in list order is the target), and one test of the invariant `requested <= booked <= requested + window` over a set of random candidate lists with a fixed seed. Use `DateTimeOffset` values with the offset `+01:00`. Row 10 uses a candidate on the next day.
- [X] T006 Create `src/GameBot.Domain/Queues/SmartSchedulingRule.cs` with the public enum `SmartTargetKind` (`SelfReschedule`, `TemplateTimer`, `DailyRetry`, `LiveSchedule`), the record `SmartCandidate(DateTimeOffset DueAt, string SequenceId, SmartTargetKind Kind)`, the record `SmartSchedulingDecision(DateTimeOffset BookedAt, SmartCandidate? Target)`, and the static method `SmartSchedulingRule.Resolve(DateTimeOffset requestedAt, TimeSpan window, IReadOnlyList<SmartCandidate> candidates)`. The method keeps each candidate with `requestedAt < DueAt <= requestedAt + window` and returns the earliest (first in list order for equal times). With no such candidate it returns `(requestedAt, null)` (data-model.md). Run the tests of T005 and make sure that they pass.
- [X] T007 In `tests/unit/Queues/FileQueueRepositoryTests.cs`, add two tests: a queue with `SmartScheduling = true` and `SmartSchedulingWindowMinutes = 20` keeps the two values after a save and a load; a queue file with no such members loads as `false` and 15. Use the existing idle-pause tests of the file as the pattern. Make sure that the tests pass.

**Checkpoint**: The rule and the stored members exist. No behaviour changed.

---

## Phase 3: User Story 1 - Merge near wake-ups of a queue (Priority: P1) 🎯 MVP

**Goal**: A Timer booking of a sequence moves to the earliest pending firing in the window, when the stored queue has smart scheduling on (spec FR-010 to FR-019).

**Independent Test**: With smart scheduling on and a pending firing of A at 12:10, a booking of B for 12:00 has the time 12:10. With smart scheduling off, it has the time 12:00.

### Tests for User Story 1

- [X] T008 [P] [US1] Create `tests/unit/Queues/QueueRunScheduleDueTimesTests.cs` for the new method `QueueRunSchedule.SnapshotTimedEntryDueTimes(now)`. Tests: a time-of-day entry ahead today gives today at that time with the kind `TemplateTimer`; a time-of-day entry that fired today gives tomorrow; a relative entry that did not fire gives the run start plus the offset; a relative entry that fired gives no candidate; an armed daily retry gives its due time with the kind `DailyRetry`; entries that are not Timer entries give no candidate. Build the schedule with the constructor `QueueRunSchedule(entries, runStartedAt, cycling)`.
- [X] T009 [P] [US1] Create `tests/unit/Queues/SelfRescheduleCoordinatorSmartTests.cs`, with the fixture pattern of `tests/unit/Queues/SelfRescheduleCoordinatorKeepTests.cs` (registry, handle, `FakeTimeProvider`). Tests, each with `new SmartSchedulingRequest(15)` unless the text says a different value: (a) a pending Timer booking of A in the window moves the booking of B to the time of A, and the result has `RequestedAt` and `SmartTarget`; (b) a pending booking of A out of the window does not move B; (c) a pending booking of A earlier than T does not move B; (d) the pending booking of B itself is not a target, and the new booking replaces it; (e) a pending live schedule of a different sequence is a target; (f) a live schedule of B itself is not a target; (g) a template Timer entry on `handle.Schedule` is a target, and a template Timer entry of B itself is not; (h) a null request books the requested time and the entry has no `Postponement`; (i) the options `OncePerRun`, `AtQueueStart`, and `EveryStep` with a request behave as with no request; (j) `keep: earliest` with the same run ID compares the booked time: a pending booking of B at 12:05 stays when the new booking is requested for 12:00 and postponed to 12:10; (k) `RearmTimerFiring` keeps `FireAt` and `Postponement` of a postponed entry; (l) the target booking keeps its time; (m) a handle with a null `Schedule` gives no template candidate and does not throw; (n) after B moved to the booking of A, `CancelSelf` for A removes the booking of A, and the booking of B keeps its `FireAt` and its `Postponement`; the same is true when A books a new, later time (spec Edge Cases, FR-015); (o) a booking with `timerTimeOfDay` and no relative offset moves to a target in the window after its resolved time of day, and the result has the resolved time of day as `RequestedAt`.
- [X] T010 [P] [US1] Create `tests/integration/Queues/SmartSchedulingRunIntegrationTests.cs`, with the pattern of `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`. Tests: (a) a queue with smart scheduling on and two sequences that book Timer offsets of 10 minutes (A) and 2 minutes (B): after the two run, the monitor shows the same `expectedAt` for the two, and the two fire in the same wake-up after the clock moves 10 minutes; (b) the same template with smart scheduling off: B has the expected time start plus 2 minutes, and the execution log message of the step of B is the same text as before this feature (SC-004); (c) a queue starts with smart scheduling off, the test saves the queue with smart scheduling on while it runs, and the next booking moves (FR-007); (d) a queue starts with a window of 15, the test saves a window of 5 while it runs, a booking that is pending keeps its time, and a later booking with a target 8 minutes away does not move; (g) the queue repository throws an exception on the read at the time of a booking (write a private class in the test file that implements `IQueueRepository`, sends each call to the real repository, and throws from `GetAsync` while a flag is set; register it in the test host with `ConfigureTestServices` in place of the real registration, as `FakeQueues` in `tests/integration/Updates/UpdateFlowIntegrationTests.cs` shows for a replaced repository; set the flag after the queue started): the booking has the requested time, the step has the outcome `scheduled`, and the run continues (plan, Design Note 4). Add the same case for a read that gives no queue.
- [X] T011 [US1] Run the tests of T008 to T010 and make sure that they fail (compile errors for the new members count as a failure).

### Implementation for User Story 1

- [X] T012 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`, add the public method `SnapshotTimedEntryDueTimes(DateTimeOffset now)` that returns `IReadOnlyList<SmartCandidate>`. Under `_gate`, it adds one candidate for each Timer entry of the plan (time of day: `NextTimeOfDayDueLocked`; relative offset: `RunStartedAt + offset` only when the key is not in `_relativeFired`) with the kind `TemplateTimer`, and one for each daily retry whose key is in the plan, with the sequence ID of that entry and the kind `DailyRetry`. Do not change `ComputeNextDue` (research R-003). Add an XML comment.
- [X] T013 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, add the record `SmartPostponement(DateTimeOffset RequestedAt, string TargetSequenceId, SmartTargetKind TargetKind)` and the optional member `SmartPostponement? Postponement = null` at the end of `SelfRescheduleEntry`, with XML comments. `RearmTimerFiring` already uses `with`, so it keeps the member.
- [X] T014 [US1] In `src/GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs`, add the record `SmartSchedulingRequest(int WindowMinutes)`, add the optional last parameter `SmartSchedulingRequest? smartScheduling = null` to `ScheduleSelf` with a `<param>` comment, and add the optional members `DateTimeOffset? RequestedAt = null` and `SmartCandidate? SmartTarget = null` to `SelfRescheduleResult` with `<param>` comments. Update each other implementation of the interface in `src/` and `tests/` (search for `ISelfRescheduleCoordinator`) so that the solution builds.
- [X] T015 [US1] In `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`, change the `Timer` case. After `ResolveTimerFireAt`, when the request is not null: collect the candidates in this order: `handle.SnapshotPendingTimerFirings()` (kind `SelfReschedule`, only entries with a `FireAt`), `handle.Schedule?.SnapshotTimedEntryDueTimes(now)`, and `handle.PendingLiveSchedules` (kind `LiveSchedule`); remove each candidate with the sequence ID of the booking; call `SmartSchedulingRule.Resolve` with `TimeSpan.FromMinutes(request.WindowMinutes)`. Put the collection in one private method. Make the entry with `FireAt` equal to the booked time and with a `Postponement` when the decision has a target. Give `RequestedAt` and `SmartTarget` in the result for a postponed booking, also in the `KeptPending` branch. `ResolvedTiming` is the booked time. Add the log message `SelfRescheduleLog.Postponed` (event ID 1162, Information): `Sequence {SequenceId} booking moved from {RequestedAt:u} to {BookedAt:u}, the firing of {TargetSequenceId} (smart scheduling, window {WindowMinutes} min)`. With a null request the code path stays the same as today.
- [X] T016 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, make the smart scheduling request for a `reschedule-self` step. In `DispatchActionAsync` (the asynchronous caller of `DispatchSelfReschedule`), when the action type is `reschedule-self` and the queue ID is not empty: read the queue with `_queueRepository` (null repository, null queue, or an exception give no request) and read the sequence with `_sequenceRepository`. Make `new SmartSchedulingRequest(queue.SmartSchedulingWindowMinutes)` only when `queue.SmartScheduling` is true, the window is valid (`SmartSchedulingLimits.IsValidWindow`), and the sequence is found. The check of `IgnoreSmartScheduling` comes in T031. Give the request to `DispatchSelfReschedule` as a new parameter, and give it to `ScheduleSelf` only for the option `Timer`. Do not change the message text in this task.
- [X] T017 [US1] Run the tests of T008 to T010 and the filter `FullyQualifiedName~SelfReschedule` in the unit and integration test projects. All tests must pass. The existing self-reschedule tests must pass with no change.

**Checkpoint**: A queue with `smartScheduling: true` in its stored file merges near bookings. A queue with the default behaves as before.

---

## Phase 4: User Story 2 - Configure smart scheduling on the queue (Priority: P1)

**Goal**: The operator reads and changes the two queue settings in the API and in the web UI (spec FR-001 to FR-007).

**Independent Test**: Turn on smart scheduling in the queue form, see 15 minutes, set 20, save, load the queue again, and see the two values.

### Tests for User Story 2

- [X] T018 [P] [US2] Create `tests/contract/Queues/SmartSchedulingQueueContractTests.cs`, with the pattern of the existing queue contract tests in `tests/contract/Queues/`. Tests (contracts/rest-api.md): create with no member gives `false` and 15; create with `true` and 20 gives the two values in the response and in `GET`; update with no member keeps the stored values; update with `smartScheduling: false` keeps the window (FR-004); a window of 0, 121, and -1 gives 400 `invalid_request` with the message `smartSchedulingWindowMinutes must be from 1 to 120`, on create and on update; a window of 1 and of 120 is accepted; duplicate copies the two values (FR-006); the OpenAPI document describes the two members on the request and response schemas.
- [X] T019 [P] [US2] Add tests for the settings document in the existing test file of the queue settings exporter and importer (search `tests/` for `QueueSettingsExporter` and `QueueSettingsImporter`): the export writes `smartScheduling` and `smartSchedulingWindowMinutes`; the import applies them; a document with no such members imports as `false` and 15; a window out of range gives the import error of a bad value and changes nothing.
- [X] T020 [P] [US2] In `src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx`, add tests: the switch "Smart scheduling" is off by default; the window field shows only when the switch is on; the field shows 15 for a new queue; a change of the field calls `onChange` with the number; a value out of 1 to 120 shows the message "Enter a value from 1 to 120." and the form is not valid; the note of a running queue is "Settings apply at the next start. Smart scheduling applies to the next booking." (FR-007a). Update the existing assertions for the old note text in this file, in `src/web-ui/src/pages/__tests__/QueuesPage.monitor.spec.tsx`, and in `src/web-ui/src/pages/__tests__/QueuesPage.runningEdit.spec.tsx`. In `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`, add one test: a save of an edited queue sends `smartScheduling` and `smartSchedulingWindowMinutes` in the update call.
- [X] T021 [US2] Run the tests of T018 to T020 and make sure that they fail.

### Implementation for User Story 2

- [X] T022 [P] [US2] In `src/GameBot.Service/Contracts/Queues/CreateQueueRequest.cs` and `src/GameBot.Service/Contracts/Queues/UpdateQueueRequest.cs`, add `public bool? SmartScheduling { get; set; }` and `public int? SmartSchedulingWindowMinutes { get; set; }` with XML comments that give the rules of `contracts/rest-api.md`. In `src/GameBot.Service/Contracts/Queues/QueueResponse.cs` (and the detail response type, if it does not derive from it), add `public bool SmartScheduling { get; set; }` and `public int SmartSchedulingWindowMinutes { get; set; }` with XML comments.
- [X] T023 [US2] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: add one private static check method that returns the error text `smartSchedulingWindowMinutes must be from 1 to 120` for a non-null window that `SmartSchedulingLimits.IsValidWindow` rejects; call it in the create route and in the update route and return `Error(400, "invalid_request", ...)`. Create: `SmartScheduling = req.SmartScheduling ?? false`, window `req.SmartSchedulingWindowMinutes ?? SmartSchedulingLimits.DefaultWindowMinutes`. Update: change each stored value only when the request value is not null. Duplicate: copy the two values from the source. Add the two members to each place that maps `ExecutionQueue` to a response (the two maps near `PauseWhenIdle = queue.PauseWhenIdle`).
- [X] T024 [P] [US2] In `src/GameBot.Domain/QueueSettings/SettingsDocument.cs`, add `SmartScheduling` (bool) and `SmartSchedulingWindowMinutes` (int, default 15) to `QueueSettingsDoc` with XML comments. In `src/GameBot.Domain/QueueSettings/QueueSettingsExporter.cs`, write them. In `src/GameBot.Domain/QueueSettings/QueueSettingsImporter.cs`, apply them next to `queue.PauseWhenIdle = doc.PauseWhenIdle;`, and reject a window out of range with the existing mechanism of the importer for a bad value. Do not change `CurrentFormatVersion` (research R-008).
- [X] T025 [P] [US2] Create `src/GameBot.Service/Swagger/SmartSchedulingSchemaFilter.cs`, an `ISchemaFilter` on the pattern of `src/GameBot.Service/Swagger/QueueHealthSchemaFilter.cs`. For the schemas of `CreateQueueRequest`, `UpdateQueueRequest`, and the queue response types, it sets the descriptions of `smartScheduling` ("When true, a Timer booking of a sequence moves to a pending firing that is not more than the window later. Default false. A change applies to each booking made after the save.") and `smartSchedulingWindowMinutes` ("The window in minutes, from 1 to 120. Default 15."). Register it in `src/GameBot.Service/GameBotServiceSetup.cs` next to `options.SchemaFilter<QueueRosterSchemaFilter>();`.
- [X] T026 [US2] In `src/web-ui/src/services/queues.ts`, add `smartScheduling: boolean` and `smartSchedulingWindowMinutes: number` to the queue types (next to `pauseWhenIdle`) and the optional members to the create and update request types. In `src/web-ui/src/components/queues/QueueForm.tsx`, add the two members to `QueueFormValue`, a switch with the label "Smart scheduling" and the help text "Moves a booking of a sequence to a pending firing that is not more than the window later. The queue wakes up less often.", and a number field "Window (minutes)" that shows only when the switch is on, on the pattern of the idle-pause block. The field has `min=1`, `max=120`, and the error text "Enter a value from 1 to 120." In `src/web-ui/src/pages/QueuesPage.tsx`, add the two members to `emptyForm` (`false`, 15), to the form load from a queue, and to the create and update calls. Block the save while the window is not valid. In `QueueForm.tsx`, change the note of a running queue (`id="queue-running-note"`) to "Settings apply at the next start. Smart scheduling applies to the next booking." (FR-007a).
- [X] T027 [US2] In `tests/unit/Queues/QueueSettingsWhileRunningTests.cs`, change the header comment: the smart scheduling settings are the one exception, because each Timer booking reads them from the stored queue (feature 189). Add the two members to the copy in `SaveSettingsAsync`. Add no test here: the integration tests T010 (c) and (d) prove the immediate effect.
- [X] T028 [US2] Run the tests of T018 to T020, the filter `FullyQualifiedName~Queues` in the unit and contract test projects, and `npm --prefix "C:\src\GameBot\src\web-ui" test -- QueueForm QueuesPage`. All tests must pass.

**Checkpoint**: The operator can turn smart scheduling on and set the window in the web UI and in the API. With User Story 1, the feature works end to end.

---

## Phase 5: User Story 3 - Keep a time-critical sequence on its exact time (Priority: P2)

**Goal**: A sequence with "ignore smart scheduling" always keeps the requested time (spec FR-008, FR-009, FR-017).

**Independent Test**: With smart scheduling on and a pending firing at 12:10, a sequence with the setting on books 12:00 and gets 12:00.

### Tests for User Story 3

- [X] T029 [P] [US3] Create `tests/contract/Sequences/IgnoreSmartSchedulingContractTests.cs` and `tests/integration/Sequences/IgnoreSmartSchedulingPersistenceTests.cs`, as copies of the patterns `tests/contract/Sequences/ExcludeFromSuccessNotificationsContractTests.cs`, `tests/contract/Sequences/ExcludeFromSuccessNotificationsOpenApiTests.cs`, and `tests/integration/Sequences/ExcludeFromSuccessNotificationsPersistenceTests.cs`. Cover the table of `contracts/rest-api.md` ("Sequences"): create, PUT, PATCH, list, detail, a PUT with no member keeps a stored `true` (this is different from the pattern), the 400 message `ignoreSmartScheduling must be true or false.`, the OpenAPI description, and a stored file with no member when the value is false.
- [X] T030 [P] [US3] In `tests/integration/Queues/SmartSchedulingRunIntegrationTests.cs`, add the tests: (e) a sequence S with `IgnoreSmartScheduling = true` books with a target in the window and keeps the requested time; (f) a sequence B with no setting moves to the pending booking of S (spec US3-AS2). Create `src/web-ui/src/pages/__tests__/SequencesPage.ignoreSmartScheduling.spec.tsx` on the pattern of `SequencesPage.excludeNotifications.spec.tsx`: the checkbox is off by default, a save sends `ignoreSmartScheduling: true` when it is on, the edit form loads the stored value, and the list shows the mark. Run the tests of T029 and T030 and make sure that they fail.

### Implementation for User Story 3

- [X] T031 [US3] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, in the code of T016, make no request when the sequence has `IgnoreSmartScheduling` true.
- [X] T032 [US3] In `src/GameBot.Service/Models/SequenceStepContracts.cs`, `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `src/GameBot.Service/Swagger/SwaggerConfig.cs`, and `src/GameBot.Service/Swagger/SequenceTimeLimitSchemaFilter.cs`, add `ignoreSmartScheduling` at each place that has `excludeFromSuccessNotifications`, with the same rules (create, PATCH, list, detail, the boolean check with the message `ignoreSmartScheduling must be true or false.`). One difference: in the PUT route, change the stored value only when the request has the member (`if (ignoreFlag is { } value) seqDomain.IgnoreSmartScheduling = value;`). Make sure that the PUT route starts from the stored sequence or copies the stored value, so that an absent member keeps it. The OpenAPI description is the text of `contracts/rest-api.md`.
- [X] T033 [US3] In `src/web-ui/src/services/sequences.ts`, add `ignoreSmartScheduling?: boolean` to the two sequence types. In `src/web-ui/src/pages/SequencesPage.tsx`, add `ignoreSmartScheduling` at each place that has `excludeFromSuccessNotifications`: the form state and its default, the load of the edit form, the two save payloads, a checkbox in the create form and in the edit form with the label "Ignore smart scheduling" and the help text "Use it for a time-critical sequence. The bookings of this sequence always keep the requested time.", and a mark "Ignores smart scheduling" in the list row. The edit form always sends the member, so a save from the web UI can set it to false.
- [X] T034 [US3] In `tests/unit/QueueSettings/QueueSettingsExporterTests.cs`, add one test: a sequence with `IgnoreSmartScheduling = true` has `ignoreSmartScheduling: true` in its JSON object of the exported document. In `tests/unit/QueueSettings/QueueSettingsImporterTests.cs`, add one test: a document with a sequence that has `ignoreSmartScheduling: true` gives a stored sequence with the value true. The document and a gamepack store a sequence as its JSON object, so no source change is necessary (research R-009). Then run the tests of T029 and T030, the filter `FullyQualifiedName~Sequences` in the contract and integration test projects, and `npm --prefix "C:\src\GameBot\src\web-ui" test -- SequencesPage`. All tests must pass.

**Checkpoint**: A time-critical sequence keeps its time on a queue with smart scheduling on.

---

## Phase 6: User Story 4 - See what the scheduler did (Priority: P3)

**Goal**: The execution log and the queue monitor show a postponement (spec FR-020 to FR-022).

**Independent Test**: After a postponed booking, the step log shows the requested time, the booked time, and the target, and the monitor shows the booked time with the postponed text.

### Tests for User Story 4

- [X] T035 [P] [US4] Create `tests/unit/Queues/QueueMonitorSmartSchedulingTests.cs`, with the pattern of `tests/unit/Queues/QueueMonitorServiceTests.cs`. Tests: a pending Timer firing with a `Postponement` gives an item with `ExpectedAt` equal to the booked time, `RequestedAt` equal to the requested time, and the reason `Rescheduled by a sequence, postponed from HH:mm (smart scheduling)`; a firing with no `Postponement` gives a null `RequestedAt` and the reason of today; the snapshot has `SmartScheduling` with the values of the stored queue, for a running queue and for a stopped queue. Add a contract test in `tests/contract/Queues/SmartSchedulingQueueContractTests.cs`: the monitor response has `smartScheduling.enabled` and `smartScheduling.windowMinutes`, and the OpenAPI document describes `requestedAt`.
- [X] T036 [P] [US4] In `tests/integration/Queues/SmartSchedulingRunIntegrationTests.cs`, add assertions to test (a): the detail item of the `reschedule-self` step of B has the attributes `requestedFireAt`, `bookedFireAt`, `smartSchedulingTarget` (the name of A), `smartSchedulingTargetKind` (`SelfReschedule`), and `smartSchedulingWindowMinutes` (15), and its message contains `postponed from` and `(smart scheduling, window 15 min)`. Add assertions to test (b): the detail item has none of these attributes. In `src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx`, add tests: the header shows "Smart scheduling: 15 min" when `smartScheduling.enabled` is true and shows no such line when it is false; a postponed item shows its reason text. In the test file of `buildScheduleRows` (search `src/web-ui/src` for `buildScheduleRows` in `__tests__`), add tests: a monitor item with `requestedAt` gives a row with the timing text `Scheduled · <booked time> · postponed from HH:mm`; an item with no `requestedAt` gives the text of today. In `src/web-ui/src/components/queues/__tests__/PhoneQueuesView.test.tsx`, add one test: the phone row of a postponed firing shows "postponed from" (FR-021, FR-024). Run the tests of T035 and T036 and make sure that they fail.

### Implementation for User Story 4

- [X] T037 [US4] In `src/GameBot.Domain/Services/ActionDispatchResult.cs`, add the record `SmartSchedulingLogInfo(DateTimeOffset RequestedAt, DateTimeOffset BookedAt, string TargetSequenceId, string TargetKind, int WindowMinutes)` and the optional member `SmartSchedulingLogInfo? SmartScheduling = null` on `ActionDispatchResult`, with `<param>` comments. In `src/GameBot.Domain/Services/SequenceRunner.cs`, carry the member to the step record at each place that carries `Removed` (near lines 827, 2894, and 2996).
- [X] T038 [US4] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`: in `DispatchSelfReschedule`, when the result has a `SmartTarget`, resolve the name of the target sequence (the ID when the name is not known; resolve it in the asynchronous caller, or give a name lookup to the method), add `postponed from <requested, format "u"> to the firing of '<target>' (smart scheduling, window <N> min)` to the message before `applies to the current run only` (also in the `KeptPending` message), and return the `SmartSchedulingLogInfo`. In the code that makes `rescheduleDetail` (near line 506), add the five attributes of `contracts/rest-api.md` when the step has the info, on the pattern of `removed`. A booking that is not postponed keeps the message and the attributes of today.
- [X] T039 [US4] In `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs`, add the optional member `RequestedAt` to `QueueMonitorItem` and the member `SmartScheduling` (a record with `Enabled` and `WindowMinutes`) to `QueueMonitorSnapshot`, with comments. In `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`, set `RequestedAt` and the postponed reason for a self-reschedule firing with a `Postponement` (format the time with `HH:mm`), and set `SmartScheduling` from the stored queue in the two return paths of `BuildAsync` (a null queue gives off and 15). In `src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs`, add `SmartScheduling` (a response class with `Enabled` and `WindowMinutes`) and `RequestedAt` on the item, with XML comments. Map the new members where `QueuesEndpoints.cs` maps the snapshot to the response. In `src/GameBot.Service/Swagger/SmartSchedulingSchemaFilter.cs` (T025), add the descriptions of the monitor members `smartScheduling` and `requestedAt` with the text of `contracts/rest-api.md`.
- [X] T040 [US4] In `src/web-ui/src/services/queues.ts`, add `smartScheduling?: { enabled: boolean; windowMinutes: number }` to the monitor type and `requestedAt?: string | null` to the monitor item type. In `src/web-ui/src/components/queues/QueueMonitor.tsx`, show one line "Smart scheduling: N min" in the header when `smartScheduling.enabled` is true. The item reason already shows the server text. In `src/web-ui/src/components/queues/queueActivity.ts`, in `buildScheduleRows`, add the suffix ` · postponed from HH:mm` to the timing text when the monitor item has a valid `requestedAt` (format the time with the clock helper of the file). The phone view and the large screen use this function, so the two show the same text (FR-021, FR-024). The text is on the row, not in a tooltip, and the row does not scroll horizontally at 375 px (docs/mobile-ui-principles.md). Make sure that the existing test `src/web-ui/src/__tests__/QueueActivity.parity.test.tsx` passes.
- [X] T041 [US4] Run the tests of T035 and T036, the filter `FullyQualifiedName~QueueMonitor` in the unit test project, and `npm --prefix "C:\src\GameBot\src\web-ui" test -- QueueMonitor QueueActivity PhoneQueuesView`. All tests must pass.

**Checkpoint**: The operator can see each postponement in the execution log and in the monitor.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T042 [P] Create `docs/smart-scheduling.md` in STE (FR-023): what the feature does, the queue settings (default off, 15 minutes, 1 to 120), the rule with two examples from `contracts/scheduling-rule.md`, the maximum delay (one window), the sequence setting for a time-critical sequence, what does not move (template Timer entries, live schedules, run-next requests), where to see a postponement (execution log, monitor), and the result of the log analysis (85 wake-ups for each queue-day, the table of the spec).
- [X] T043 [P] In `docs/architecture.md`, add a paragraph "Smart scheduling (feature 189)" after the paragraph "Self-reschedule keep earliest": the rule, the candidates, `SmartSchedulingRule`, the place in `SelfRescheduleCoordinator`, the stored members of `ExecutionQueue` and `CommandSequence`, the read of the stored queue at each booking as the one exception to feature 173, `Postponement` on the booking, the monitor members, and the execution log attributes. Add the new queue and sequence members to the lists of the API surface and of the persistence layout, if these lists name the other members. Update the "Last reviewed" line with the date and feature 189.
- [X] T044 [P] In `specs/STATUS.md`, add the row for 189 after the row for 188, with the status `Implemented`. Set `**Status**: Implemented` in `specs/189-smart-scheduling/spec.md`. In `specs/173-queues-ui-cleanup/spec.md`, add one sentence in STE after FR-019: "Feature 189 makes one exception: the smart scheduling settings apply to the next booking of a running queue, and the note says so."
- [X] T045 Add a `CHANGELOG.md` entry in STE for feature 189: smart scheduling of a queue (default off, window 15 minutes), the sequence setting "Ignore smart scheduling", the new API members, and the monitor and execution log changes. Add a performance note: one Timer booking reads one queue file and scans the pending firings; the run loop does not change. Before the push, bump the version in `installer/versioning/version.override.json` as the constitution ("Version bump") and `CLAUDE.md` tell, set `updatedBy` and `updatedAtUtc`, and add a line about the version to this CHANGELOG entry. Put the same performance note in the PR description.
- [X] T046 Run `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with zero new warnings. Run the full unit, contract, and integration test projects and the full web UI test suite and lint (`npm --prefix "C:\src\GameBot\src\web-ui" test` and `npm --prefix "C:\src\GameBot\src\web-ui" run lint`). All must pass. Collect the coverage as the CI does (`.github/workflows/dotnet.yml`). Build with `dotnet build "C:\src\GameBot\GameBot.sln" -c Release`. Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" -c Release --no-build --collect "XPlat Code Coverage" --settings "C:\src\GameBot\scripts\ci\coverage.runsettings" --test-adapter-path "C:\src\GameBot\tests\unit\bin\Release\net9.0\coverlet-collector" --results-directory "<scratch>\coverage-unit"`. Run the same command for the integration test project, with `tests\integration` in the two paths and the results directory `<scratch>\coverage-integration`. `<scratch>` is a directory out of the repository. Then run `& "C:\src\GameBot\scripts\check-coverage-thresholds.ps1" -UnitCoverageDir "<scratch>\coverage-unit" -IntegrationCoverageDir "<scratch>\coverage-integration"`. The thresholds must pass (constitution, Principle II). Measure the shard balance with `scripts/ci/measure-shards.ps1` and split a shard that is more than 20% above the average of its project (`docs/ci-time.md`).
- [X] T047 Follow `specs/189-smart-scheduling/quickstart.md`, "Manual check", steps 1 to 9, on a development instance. If no emulator is available, record that the integration tests of T010, T030, and T036 cover the same steps.
  - Result (2026-11-13): no emulator was available in the implementation session. `SmartSchedulingRunIntegrationTests` covers the steps with the real queue engine and a test clock: steps 1 to 5 in test (a) (the same booked time for A and B, the monitor `expectedAt`, and one wake-up), step 6 in the log assertions of test (a), steps 7 and 8 in test (e), and step 9 in tests (c) and (d). `QueueMonitorSmartSchedulingTests` and the web UI tests cover the monitor and row text of step 5.

---

## Dependencies & Execution Order

- Phase 1 (T001) comes first. Phase 2 (T002 to T007) blocks all user stories. T003 needs T002. T006 needs T005.
- US1 (T008 to T017) needs Phase 2. Order: tests (T008 to T011), then T012, T013, T014, T015, T016, T017.
- US2 (T018 to T028) needs Phase 2 only. It can run in parallel with US1. T010 c and d need T023 only if the test saves through the route; they save through the repository, so they do not need US2.
- US3 (T029 to T034) needs T016 (US1). T031 edits the code of T016.
- US4 (T035 to T041) needs T015 and T016 (US1). T038 edits the same file as T016 and T031: do these tasks one after the other.
- Polish (T042 to T047) comes after the user stories. T046 and T047 come last.

### Parallel Opportunities

- T002, T004, and T005 edit different files.
- T008, T009, and T010 are different test files.
- T018, T019, and T020 are different test files. T022, T024, and T025 edit different files.
- US2 can run in parallel with US1 after Phase 2.
- T042, T043, and T044 edit different files.

## Parallel Example: User Story 1 tests

```text
Task: "T008 Create tests/unit/Queues/QueueRunScheduleDueTimesTests.cs"
Task: "T009 Create tests/unit/Queues/SelfRescheduleCoordinatorSmartTests.cs"
Task: "T010 Create tests/integration/Queues/SmartSchedulingRunIntegrationTests.cs"
```

## Implementation Strategy

### MVP First (User Stories 1 and 2)

1. Do Phase 1 and Phase 2.
2. Do US1 (T008 to T017). Stop and check: the integration tests show the merge, and a queue with the default has no change.
3. Do US2 (T018 to T028). The operator can now use the feature.

### Incremental Delivery

1. US1 gives the rule in the engine.
2. US2 gives the settings in the API and the web UI.
3. US3 adds the setting for a time-critical sequence.
4. US4 adds the execution log and monitor text.
5. Polish adds the docs, the changelog, and the version.
