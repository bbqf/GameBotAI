# Tasks: Reschedule-Self Cancel

**Input**: Design documents in `C:\src\GameBot\specs\123-reschedule-self-cancel\` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)
**Prerequisites**: plan.md, spec.md
**Tests**: Tests are required (spec FR-008, FR-010, FR-015, constitution Principle II). Write each test first and see it fail.
**Language**: All new text, messages and comments MUST be in Simplified Technical English (STE).

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: The task can run in parallel (other file, no dependency on an open task).
- **[Story]**: US1 = remove the retry booking (P1), US2 = safe when nothing to remove (P1), US3 = readable result, 400s and docs (P2).
- Task order inside a phase is the execution order. Where a task names a step in the same file, do the tasks in order.

---

## Phase 1: Setup

- [X] T001 Run the existing tests for the area as a baseline: `dotnet test` with filter `FullyQualifiedName~SelfReschedule|FullyQualifiedName~QueueRunHandle|FullyQualifiedName~QueueExecutionService` for `C:\src\GameBot\GameBot.sln`. Record the result. Do not continue if a test fails (constitution gate).

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The enum value, the result members and the validator rules. All stories use them.

- [X] T002 Add the value `Cancel` (with an XML comment in STE) to the enum in `C:\src\GameBot\src\GameBot.Domain\Commands\SelfReschedule\SelfRescheduleOption.cs`. Do not change the other values (FR-011).
- [X] T003 In `C:\src\GameBot\src\GameBot.Domain\Commands\SelfReschedule\SelfReschedulePayload.cs`, change the text of the unknown-option error in `TryRead` so that it lists `Cancel` with the other options (FR-009). Make sure `TryRead` parses `"Cancel"` case-insensitively and keeps the unknown-field rule of issue #228.
- [X] T004 In `C:\src\GameBot\src\GameBot.Domain\Services\SequenceStepValidationService.cs`, change `ValidateRescheduleSelfPayload` so that it checks `timerTimeOfDay`, `timerRelativeOffset` and `ocrOffset` one by one (no `else if` chain). Each error names its field and says it is only valid when the option is Timer. This applies to Cancel (FR-008, R-005).
- [X] T005 Add the optional member `bool? Removed { get; init; }` (default null, XML comment in STE) to the record in `C:\src\GameBot\src\GameBot.Domain\Services\ActionDispatchResult.cs` (R-004).
- [X] T006 Add `bool? Removed { get; set; }` to the class `StepResult` in `C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs` (about L2482). Add an optional parameter `bool? removed = null` to `AddStep` (about L2380) and copy it into the new `StepResult`. In the same file, at the reschedule dispatch site (about L624, where `actionOutcome: dispatch.Outcome` is passed), pass `removed: dispatch.Removed`. Depends on T005. Same file as T018 area: do T006 before any other edit to `SequenceRunner.cs`.
- [X] T007 In `C:\src\GameBot\src\GameBot.Domain\Commands\FileSequenceRepository.cs`, change `ValidateActionPayloads` (backstop, R-006): for a `reschedule-self` step, call `SelfReschedulePayload.TryRead`. Reject a Cancel option that carries `timerTimeOfDay`, `timerRelativeOffset` or `ocrOffset` with `InvalidOperationException` and a message that names the field, as the other backstop checks do.

**Checkpoint**: The domain compiles (`dotnet build C:\src\GameBot\GameBot.sln`).

---

## Phase 3: User Story 1 - Remove the retry booking when the work succeeds (P1)

**Goal**: A Cancel step removes the pending booking of the owner sequence in the current queue run, also from the OncePerRun drain copy.

**Independent Test**: Run a sequence with Timer at step 0 and Cancel as the last step in a queue. The queue monitor shows no pending booking and no wake follows. A run that fails by name before Cancel keeps the booking.

### Tests for User Story 1 (write first, they MUST fail)

- [X] T008 [P] [US1] Unit tests in `C:\src\GameBot\tests\unit\Queues\QueueRunHandleCancelTests.cs` (new) for `RemovePendingBookings(sequenceId)`: removes the Timer entry, `PendingOncePerRun` entries and `PendingNextCycleStart` entries of the sequence; keeps entries of another sequence; keeps template hold entries with the prefix `at-queue-start:`; keeps `EveryStepInjections` and `PendingLiveSchedules`; returns the removed count; keeps the order of the entries that stay (R-001, R-002, FR-001, FR-007).
- [X] T009 [P] [US1] Unit tests in `C:\src\GameBot\tests\unit\Queues\QueueRunHandleDrainCancelTests.cs` (new) for the in-flight drain copy: after `BeginOncePerRunDrain(list)`, `RemovePendingBookings` marks the in-flight entries of the sequence; `TryConsumeCancelled(id)` returns true for a marked id and false for another id; a booking added after the Cancel gets a new id and is not marked (last booking wins); `EndOncePerRunDrain` clears the set; the count includes the marked in-flight entries (FR-005, FR-015, R-002).
- [X] T010 [P] [US1] Unit tests in `C:\src\GameBot\tests\unit\Queues\SelfRescheduleCoordinatorCancelTests.cs` (new) for `CancelSelf(queueId, sequenceId)`: result `Cancelled` with count when removed; `NothingPending` when no entry; `NotRunning` when the queue run is not active. Also a test that `ScheduleSelf` still throws `ArgumentOutOfRangeException` for Cancel (R-007, R-008).
- [X] T011 [P] [US1] Unit tests in `C:\src\GameBot\tests\unit\Queues\SelfRescheduleDispatchCancelTests.cs` (new) for `DispatchSelfReschedule` in `SequenceExecutionService`: a Cancel with a removed booking gives outcome `cancelled` and `Removed = true`; the dispatch never calls `ScheduleSelf` for Cancel (R-004, FR-004).
- [X] T012 [P] [US1] Unit test in the same new file `C:\src\GameBot\tests\unit\Queues\SelfRescheduleDispatchOwnerIdTests.cs` (new): call the dispatch with an owner sequence id that differs from the id of the firing sequence. Prove that the coordinator receives the owner id for Cancel and for Timer (R-012, spec edge case "nested or called sequence"). If the dispatch passes the top-level id, fix it for both options in one place in `C:\src\GameBot\src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs`, and keep the Timer tests green.
- [X] T013 [P] [US1] Queue-level test in `C:\src\GameBot\tests\unit\Queues\QueueExecutionServiceCancelDrainTests.cs` (new, FR-015): two OncePerRun bookings of one sequence are in one drain copy. The first firing runs a Cancel step. Assert that the second booking does not fire (one firing in total) and is not counted in `executed`. Also assert that the Timer drain needs no set (one Timer entry for each sequence).
- [X] T014 [P] [US1] Integration tests in `C:\src\GameBot\tests\integration\Queues\SelfRescheduleCancelIntegrationTests.cs` (new): (a) Timer at step 0 plus final Cancel gives no pending booking and no retry wake (SC-001); (b) a run that fails by name before Cancel keeps the booking and the wake occurs (SC-002, FR-006); (c) Cancel, then a later step books a new Timer: the new booking stays (last booking wins). Remove any file or data that a test creates.

### Implementation for User Story 1

- [X] T015 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs`, add `RemovePendingBookings(string sequenceId)`: `RemoveAll` under `_timerLock` on the Timer list; rebuild `PendingOncePerRun` and `PendingNextCycleStart` (dequeue the current count, enqueue back the entries that stay, skip the `at-queue-start:` prefix entries); keep `EveryStepInjections` and `PendingLiveSchedules`. Add the in-flight list and the cancelled-id set with `BeginOncePerRunDrain(IReadOnlyList<SelfRescheduleEntry>)`, `EndOncePerRunDrain()` and `TryConsumeCancelled(string entryId)`. Under a lock, mark each in-flight entry id of the sequence. Return the total count (live removed plus marked). Public members get STE XML comments (R-002).
- [X] T016 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs` (OncePerRun drain, about L737-752): call `handle.BeginOncePerRunDrain(list)` after the copy is filled; wrap the firing loop in `try`/`finally` with `handle.EndOncePerRunDrain()` in the `finally`; before each firing, `if (handle.TryConsumeCancelled(entry.Id)) continue;` so that no firing, no `executed` count and no hold re-queue occur (FR-015). Depends on T015.
- [X] T017 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\ISelfRescheduleCoordinator.cs`, add `CancelSelf(string queueId, string sequenceId)` and the result type with `Outcome` (`Cancelled`, `NothingPending`, `NotRunning`) and `RemovedCount` (R-008). Depends on T015.
- [X] T018 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\SelfRescheduleCoordinator.cs`, implement `CancelSelf`: find the running handle for the queue (`NotRunning` if none), call `RemovePendingBookings`, return `Cancelled` or `NothingPending`. Do not change the `ScheduleSelf` default branch. Depends on T017.
- [X] T019 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs`, in `DispatchSelfReschedule`, when the option is Cancel and a queue started the run (after the existing "no originating queue" check), call `CancelSelf(queueId, sequenceId)` with the owner sequence id. Return outcome `cancelled` with `Removed = true` and a short STE message when `Cancelled`; do not call `ScheduleSelf` (R-004, R-012). Depends on T018, T005.
- [X] T020 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs` (about L477-482), add the outcome `cancelled` to the check that treats `scheduled` and `noop` as reschedule outcomes. Add a `removed` field to the execution log item of a reschedule step (about L493-500) from `step.Removed`. Other options leave the field out. Same file as T019: do after T019 (R-004).

**Checkpoint**: T008-T014 for the removal cases pass. User Story 1 works alone.

---

## Phase 4: User Story 2 - Cancel is safe when there is nothing to remove (P1)

**Goal**: Cancel never fails the run. Each no-op case gives outcome `noop` with `removed` false.

**Independent Test**: Run a sequence with only a Cancel step, in a queue with no booking and outside a queue. Both runs succeed.

### Tests for User Story 2 (write first, they MUST fail)

- [X] T021 [P] [US2] Unit test in `C:\src\GameBot\tests\unit\Queues\SelfRescheduleDispatchNoopTests.cs` (new): the case "queue run has no pending booking of the sequence" gives step success, outcome `noop`, `Removed = false` and a short message (FR-002, SC-003).
- [X] T022 [P] [US2] Unit test in the same file `C:\src\GameBot\tests\unit\Queues\SelfRescheduleDispatchNoopTests.cs`, separate test method: the case "no originating queue" (sequence run outside a queue) gives success, `noop`, `Removed = false` and the coordinator is not called (FR-003).
- [X] T023 [P] [US2] Unit test in the same file, separate test method: the case "queue run no longer active" (coordinator returns `NotRunning`) gives success, `noop`, `Removed = false`.
- [X] T024 [P] [US2] Unit test in the same file, separate test method: two Cancel steps in one run. The first removes the booking (`cancelled`). The second succeeds with `noop` and `Removed = false`.
- [X] T025 [P] [US2] Integration test in `C:\src\GameBot\tests\integration\Sequences\SelfRescheduleCancelStandaloneIntegrationTests.cs` (new, pattern of `SelfRescheduleStandaloneIntegrationTests.cs`): a sequence with only a Cancel step runs (a) in a queue with no booking and (b) outside a queue. Both runs end in Succeeded (SC-003).

### Implementation for User Story 2

- [X] T026 [US2] In `C:\src\GameBot\src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs`, in the Cancel branch of `DispatchSelfReschedule`: return `noop` with `Removed = false` and a short STE message when there is no originating queue (before any coordinator call), when `CancelSelf` returns `NothingPending`, and when it returns `NotRunning`. The step MUST never return `failed` for Cancel. Do after T019 and T020 (same file).

**Checkpoint**: T021-T025 pass. User Stories 1 and 2 work together.

---

## Phase 5: User Story 3 - The author can read the result, the 400s and the documentation (P2)

**Goal**: Invalid payloads return 400 (never 500) on each save path. The OpenAPI text, the docs and the UI show Cancel.

**Independent Test**: Send invalid reschedule-self payloads to create, update and PATCH. Each returns 400 that names the problem. Read the OpenAPI text and find Cancel.

### Tests for User Story 3 (write first, they MUST fail)

- [X] T027 [P] [US3] Unit tests in `C:\src\GameBot\tests\unit\Sequences\RescheduleSelfCancelValidationTests.cs` (new): Cancel with `timerTimeOfDay` is rejected and the message names it; the same for `timerRelativeOffset`; the same for `ocrOffset` (three tests, FR-008); Cancel with two timer fields reports both; a valid Cancel payload passes; an unknown option message lists `Cancel` (FR-009). Also extend `C:\src\GameBot\tests\unit\Sequences\SelfReschedulePayloadTests.cs` with a Cancel parse test.
- [X] T028 [P] [US3] Repository backstop test in `C:\src\GameBot\tests\unit\Sequences\FileSequenceRepositoryCancelPayloadTests.cs` (new, pattern of `FileSequenceRepositoryEnsureEmulatorTests.cs`): `CreateAsync` and `UpdateAsync` reject a Cancel payload with a timer field or `ocrOffset` by `InvalidOperationException`; a valid Cancel payload saves (FR-010, R-006). Use a temp data directory and delete it after the test.
- [X] T029 [P] [US3] Contract tests in `C:\src\GameBot\tests\contract\Sequences\RescheduleSelfCancelContractTests.cs` (new, pattern of `SelfRescheduleActionContractTests.cs`): POST `/api/sequences` with Cancel plus each of `timerTimeOfDay`, `timerRelativeOffset`, `ocrOffset` returns 400 and the message names the field; POST with an unknown option returns 400 and the message lists `Cancel`. Each test removes the sequence it creates (the contract tests share the bin data dir) (FR-010, SC-004).
- [X] T030 [P] [US3] Contract tests in the same file, separate test methods: PUT `/api/sequences/{id}` with the same invalid payloads returns 400 (never 500). First create a valid sequence, then update it. Remove it at the end.
- [X] T031 [P] [US3] Contract tests in the same file, separate test methods: PATCH `/api/sequences/{id}` with the same invalid payloads returns 400 (never 500). First create a valid sequence, then patch it. Remove it at the end.
- [X] T032 [P] [US3] Contract test in the same file, separate test method: save a sequence with a valid Cancel step, then call POST `/api/sequences/{id}/validate`. The call returns `valid: true`. The validate call cannot receive a new payload, so the test saves first (FR-010, R-006). Remove the sequence at the end.
- [X] T033 [P] [US3] OpenAPI test in `C:\src\GameBot\tests\contract\Sequences\PrimitiveActionTypesOpenApiTests.cs`: assert that the `reschedule-self` payload text lists `Cancel`, states the no-op rules, the `removed` field and the drain-copy rule (FR-013, SC-005).
- [X] T034 [P] [US3] Jest test in `C:\src\GameBot\src\web-ui\src\pages\__tests__\SequencesPage.reschedule.spec.tsx`: the option list has "Cancel pending booking"; selecting it hides the timer fields; the saved payload has `option: "Cancel"` and no timer field; a loaded step with Cancel shows the option (FR-014).

### Implementation for User Story 3

- [X] T035 [US3] In `C:\src\GameBot\src\web-ui\src\pages\SequencesPage.tsx`, add `'Cancel'` to the `RescheduleOption` type and to `RESCHEDULE_OPTIONS` with the label "Cancel pending booking". Make sure that the payload builder adds no timer field for Cancel and that the timer fields stay hidden (R-010).
- [X] T036 [US3] In `C:\src\GameBot\src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs`, update the `reschedule-self` payload text: the option list with `Cancel`, what Cancel removes and keeps, the timer-field rule, the no-op cases, the `cancelled` and `noop` outcomes, the `removed` field, and the drain-copy rule. Check the example in `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs` and add a Cancel note only if the example lists the options. Use STE (R-011, FR-013).

**Checkpoint**: T027-T034 pass. All three stories work.

---

## Phase 6: Polish and cross-cutting

- [X] T037 [P] Update the self-reschedule paragraph (about L231) in `C:\src\GameBot\docs\architecture.md` with the Cancel option, its scope, the no-op rules, the `removed` field and the drain-copy rule. Refresh the "Last reviewed" line (about L13) to 2026-10-01 with a short note for feature 123. Use STE (FR-013, constitution Principle V).
- [X] T038 [P] Add an entry under `## [Unreleased]` (Added) in `C:\src\GameBot\CHANGELOG.md`: the option `Cancel` for `reschedule-self`, issue #264, FR-015. Use STE.
- [X] T039 Check the four items of FR-013 one by one and record the result in the PR text: OpenAPI text (T036), `docs/architecture.md` (T037), changelog (T038), and the tracker row FR-015 in `C:\src\GameBot\docs\api-feature-requests.md`. The file does not exist, so skip the row (R-011). Do not create the file.
- [X] T040 Add the row `| 123 | Reschedule-Self Cancel | Implemented |` after row 122 in `C:\src\GameBot\specs\STATUS.md`. Set `**Status**:` in `C:\src\GameBot\specs\123-reschedule-self-cancel\spec.md` to `Implemented`. Do this only after T041 passes.
- [X] T041 Run the full gate: `dotnet build C:\src\GameBot\GameBot.sln`, `dotnet test C:\src\GameBot\GameBot.sln`, and in `C:\src\GameBot\src\web-ui` run `npx vite build` and `npx jest`. All must pass. Check that no test left a sequence file in the shared data dir.
- [X] T042 Walk through `C:\src\GameBot\specs\123-reschedule-self-cancel\quickstart.md` and confirm each scenario against the tests. Record the line and branch coverage of the new code against the constitution targets (80% line, 70% branch).

---

## Dependencies and order

- Phase 1, then Phase 2 (T002 -> T003 -> T004; T005 -> T006; T007 after T003).
- US1 (Phase 3) needs Phase 2. US2 (Phase 4) needs T019 and T020 from US1 (same dispatch method). US3 (Phase 5) needs Phase 2 only: its tests and T035/T036 can run next to US1.
- Inside US1: T015 -> T016, T017 -> T018 -> T019 -> T020 -> T026. T016 and T017 both need T015 and can run in parallel with each other (other files).
- Polish (Phase 6) after all stories. T040 after T041.
- Tasks in the same file are never [P] with each other: `SequenceExecutionService.cs` (T012 fix, T019, T020, T026), `SequenceRunner.cs` (T006), the new test files with several test tasks (T021-T024, T029-T032).

## Parallel examples

- After Phase 2: T008, T009, T010, T011, T012, T013, T014, T027, T028, T029, T033, T034 (test files, all different) can be written together.
- US3 implementation T035 and T036 can run together with US1 implementation T015-T018.
- Polish T037 and T038 can run together.

## Implementation strategy

- **MVP**: Phases 1, 2 and US1 (remove the booking, including the drain copy) plus US2 (safe no-op). Stop and validate with the integration tests (SC-001, SC-002, SC-003).
- **Then**: US3 (400 paths, OpenAPI, UI), then Polish and the status updates.
- Do not commit until the gate (T041) is green.

## Counts

- Total: 42 tasks. Setup 1, Foundational 6, US1 13, US2 6, US3 10, Polish 6.
