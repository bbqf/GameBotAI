# Tasks: Reschedule-Self Keep Earliest

**Input**: Design documents from `/specs/125-reschedule-self-keep-earliest/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/reschedule-self-keep.md, quickstart.md

**Tests**: Required. FR-012 and Constitution Principle II list the tests. Write each test before the code it covers, and see it fail.

**Organization**: Tasks are grouped by user story. Each story can be tested alone after Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel. It shares no file with another task of its phase and has no open dependency.
- **[Story]**: The user story of the task (US1 to US5).

## Sequence rule (READ FIRST)

Several tasks change the same files. They are sequential, never parallel. Do them in the order of the task IDs.

- `src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs`: T004, T010, T016.
- `src\GameBot.Service\Services\QueueExecution\SelfRescheduleCoordinator.cs`: T005, T011.
- `tests\unit\Queues\QueueRunHandleTimerFiringTests.cs`: T006, T014.
- `src\GameBot.Domain\Commands\SelfReschedule\SelfReschedulePayload.cs`: T002, T024.

## Path Conventions

- Production code: `src\GameBot.Domain\` and `src\GameBot.Service\`.
- Tests: `tests\unit\`, `tests\integration\`, `tests\contract\`.
- Write all new text (code comments, log and error messages, docs) in ASD-STE100 Simplified Technical English.

---

## Phase 1: Setup

- [X] T001 Run the baseline build and tests with `dotnet build C:\src\GameBot\GameBot.sln` and the existing self-reschedule tests (`SelfRescheduleCoordinatorTests`, `QueueRunHandleTimerFiringTests`, `SelfReschedulePayloadTests`, `SelfRescheduleActionContractTests`, `SelfRescheduleRunIntegrationTests`). Record that all pass. Do not continue if a test fails (Constitution: build and test gate).

---

## Phase 2: Foundational (stubs that the red tests need)

**Purpose**: Add the new types and signatures with the OLD behavior, so the tests of Phase 3 and later compile and fail on behavior, not on a missing symbol. No task here changes behavior.

- [X] T002 Add the enum `SelfRescheduleKeep` (`None`, `Earliest`) in `src\GameBot.Domain\Commands\SelfReschedule\SelfRescheduleKeep.cs`. In `src\GameBot.Domain\Commands\SelfReschedule\SelfReschedulePayload.cs` add the members `Keep` and `HasKeep` (default `None` and false). Do not read the payload dictionary yet (see T024).
- [X] T003 Add `string? RunId = null` to the record `SelfRescheduleEntry` (find it with a search in `src\GameBot.Service\Services\QueueExecution\`). Add `bool KeptPending = false` and `DateTimeOffset? PendingFireAt = null` to `SelfRescheduleResult` in `src\GameBot.Service\Services\QueueExecution\ISelfRescheduleCoordinator.cs`. Add the optional arguments `keep` (`SelfRescheduleKeep`) and `runId` (`string?`) to `ISelfRescheduleCoordinator.ScheduleSelf` in the same file.
- [X] T004 In `src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs` add the type `TimerBookingResult` (`Added`, `Replaced`, `KeptPending`, plus the pending `FireAt`). Change `AddTimerFiring` to the signature `AddTimerFiring(entry, keepEarliest)` that returns `TimerBookingResult`. For now it keeps the old behavior: it always replaces the pending entry of the sequence (returns `Added` or `Replaced`). Fix the existing callers so the solution compiles.
- [X] T005 In `src\GameBot.Service\Services\QueueExecution\SelfRescheduleCoordinator.cs` accept the new `keep` and `runId` arguments of `ScheduleSelf`. Pass `runId` into the entry. Ignore `keep` for now. Build the solution and check that the existing tests still pass.

**Checkpoint**: The solution builds. All existing tests pass.

---

## Phase 3: User Story 1 - Keep the earliest booking of a run (Priority: P1) MVP

**Goal**: A run with `keep: earliest` leaves the earliest booking pending (15/50/30/40 gives 15).

**Independent Test**: Run four `reschedule-self` steps with `keep: earliest` that book 15, 50, 30, and 40 minutes. The one pending booking is 15 minutes ahead.

### Tests for User Story 1 (write first, see them fail)

- [X] T006 [US1] In `tests\unit\Queues\QueueRunHandleTimerFiringTests.cs` add the decision-table tests of `AddTimerFiring` from `data-model.md`: no `keepEarliest` replaces; `keepEarliest` with no pending entry adds; same run id and earlier replaces; same run id and later returns `KeptPending`; same run id and equal returns `KeptPending`; other run id replaces; null run id replaces; a past fire time beats a future fire time; a booking of one sequence never changes a booking of another sequence.
- [X] T007 [P] [US1] Create `tests\unit\Queues\SelfRescheduleCoordinatorKeepTests.cs` with coordinator-level tests: 15/50/30/40 with `keep: earliest` leaves 15; a later booking returns outcome `scheduled` with `KeptPending = true` and the pending fire time; an earlier booking replaces; an equal booking is kept back; a booking in the past replaces a pending future booking and a later future booking does not replace the past one (coordinator level, not only the store); a booking without `keep` records the run so a later `keep: earliest` booking of the same run compares with it; a losing booking writes one Information log message with the pending and the new fire time and the step result is a success (FR-006b).
- [X] T008 [P] [US1] Create `tests\integration\Queues\SelfRescheduleKeepRunIntegrationTests.cs`: run a real sequence with four `reschedule-self` steps with `keep: earliest` (15, 50, 30, 40) and check that the one pending booking is 15 minutes ahead (SC-001). Add one `ocrOffset` case: the compare uses the fire time after `min`, `max`, and the fallback. Add one case where the booking is made in a child sequence and uses the root run id.

### Implementation for User Story 1

- [X] T009 [US1] Run T006, T007, and T008 and record that they fail on behavior (not on a compile error).
- [X] T010 [US1] In `src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs` implement `AddTimerFiring(entry, keepEarliest)` under `_timerLock` as in the decision table: return `KeptPending` only when `keepEarliest` is true, the pending `RunId` is non-null and equal to `entry.RunId`, and the new `FireAt` is not earlier than the pending `FireAt`. In all other cases replace the pending entry.
- [X] T011 [US1] In `src\GameBot.Service\Services\QueueExecution\SelfRescheduleCoordinator.cs` pass `keep == Earliest` to `AddTimerFiring`. On `KeptPending` return outcome `Scheduled` with `KeptPending = true` and `PendingFireAt`, and write one Information log message with the pending fire time and the new fire time. Every Timer booking stores the `runId`, with or without `keep`.
- [X] T012 [US1] In `src\GameBot.Service\Services\SequenceExecution\SequenceExecutionService.cs` (the `reschedule-self` dispatch near the `ActionTypes.RescheduleSelf` check) read `keep` from the payload, pass the root execution id as `runId` (a child sequence uses the root id of its parent context), and pass the offset after `min`, `max`, and the fallback for an `ocrOffset` booking. A losing booking is a successful step with result `scheduled`.
- [X] T013 [US1] Run T006, T007, and T008 again. All pass.

**Checkpoint**: US1 works alone.

---

## Phase 4: User Story 2 - The retry booking of step 0 does not block the run (Priority: P1)

**Goal**: The first booking of a run replaces a booking that exists before the run, also a re-armed booking.

**Independent Test**: Make a pending engine retry booking that is earlier than the first booking of a run. The first booking of the run replaces it.

### Tests for User Story 2 (write first, see them fail)

- [X] T014 [US2] In `tests\unit\Queues\QueueRunHandleTimerFiringTests.cs` (after T006) add the `RearmTimerFiring` tests: the stored entry has `RunId = null` and the same `FireAt`; the entry is added only when the register has no Timer entry for the sequence; after a re-arm, a `keep: earliest` booking with a later fire time replaces the re-armed booking (RearmTimerFiring RunId=null rule).
- [X] T015 [P] [US2] Create `tests\integration\Queues\SelfRescheduleKeepRetryIntegrationTests.cs` with the REAL engine path: (a) the real engine retry path makes a pending booking with no run id that is 5 minutes ahead, then a run whose first booking has `keep: earliest` and is 20 minutes ahead replaces it, and a second booking of 10 minutes in the same run replaces the 20 minute booking; (b) the real hold and re-arm path puts a held booking back, and the first booking of the next run replaces it. Do not hand-build the booking entry.

### Implementation for User Story 2

- [X] T016 [US2] In `src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs` change `RearmTimerFiring` to store the held entry as `entry with { RunId = null }`. Keep the rule "add only if no entry exists" and keep the fire time.
- [X] T017 [US2] Run T014 and T015. All pass. If T015 fails, find the engine call site (`QueueExecutionService.cs`) that makes a booking and check that it sets no run id.

**Checkpoint**: US1 and US2 work.

---

## Phase 5: User Story 3 - Keep the default behavior unchanged (Priority: P1)

**Goal**: A booking without `keep` behaves as before: the last booking wins.

**Independent Test**: Book 15, 50, 30, and 40 minutes without `keep`. The pending booking is 40 minutes ahead.

- [X] T018 [P] [US3] In `tests\integration\Queues\SelfRescheduleRunIntegrationTests.cs` add a test: four steps without `keep` (15, 50, 30, 40) leave 40 minutes pending (SC-002). The expected values of the other tests in this file stay unchanged.
- [X] T019 [P] [US3] Create `tests\unit\Queues\SelfRescheduleDefaultBehaviorTests.cs`: a payload with no `keep` key and a payload with `keep` set to JSON null both book with the old rule; `AddTimerFiring` with `keepEarliest = false` replaces also when the pending entry has the same run id and an earlier time.
- [X] T020 [US3] Run the full existing booking and validation suites (`tests\unit\Queues`, `tests\unit\Sequences`, `tests\integration\Queues`, `tests\contract\Sequences`). All pass with no change to the expected values (SC-004).

**Checkpoint**: Default behavior is proven unchanged.

---

## Phase 6: User Story 4 - Reject an invalid `keep` value with 400 (Priority: P2)

**Goal**: A bad `keep` value or a non-Timer option gives a 400 response. A valid value is stored and returned unchanged.

**Independent Test**: Save a sequence with `keep: latest` and check for 400 that names `earliest`.

### Tests for User Story 4 (write first, see them fail)

- [X] T021 [P] [US4] In `tests\unit\Sequences\SelfReschedulePayloadTests.cs` add reader tests: `earliest` in any case parses; JSON null counts as absent; another value, an empty string, and a non-string value give a parse error that names `earliest`; `keep` inside `ocrOffset` is not read.
- [X] T022 [P] [US4] Create `tests\unit\Sequences\RescheduleSelfKeepValidationTests.cs`: the validator accepts `keep: earliest` with `Timer`; rejects an invalid value with a message that names `earliest`; rejects `keep` with any option other than `Timer` with the message "keep is only valid when option is Timer" (FR-006a).
- [X] T023 [P] [US4] Create `tests\contract\Sequences\SelfRescheduleKeepContractTests.cs`: save through the API (create, update, and import paths) returns 400 for `keep: latest` and for a non-Timer option with `keep`, never 500; `keep: earliest` and `Earliest` return success and the stored value comes back unchanged (round trip, FR-007). This guards against the allow-list sites that are missed.

### Implementation for User Story 4

- [X] T024 [US4] In `src\GameBot.Domain\Commands\SelfReschedule\SelfReschedulePayload.cs` (after T002) read `keep` from the payload dictionary, case-insensitive, and give the parse error that names `earliest`.
- [X] T025 [P] [US4] In `src\GameBot.Domain\Services\SequenceStepValidationService.cs` add `keep` to the allow-list and add the non-Timer rule with the message from the contract.
- [X] T026 [P] [US4] In `src\GameBot.Domain\Commands\FileSequenceRepository.cs` add `keep` to the backstop validation and the same non-Timer rule, so a bad value gives a 400 and not a 500.
- [X] T027 [US4] Run T021, T022, and T023. All pass. Check with a search for `ActionTypes.RescheduleSelf` that no other site lists the payload fields (FR-008).

**Checkpoint**: US4 works.

---

## Phase 7: User Story 5 - Find the option in the API documentation (Priority: P3)

**Goal**: The published schema text of `reschedule-self` names `keep`.

**Independent Test**: Read the published schema description and find `keep` and `earliest`.

- [X] T028 [P] [US5] Create `tests\contract\Sequences\SelfRescheduleKeepOpenApiContractTests.cs`: the published description of the `reschedule-self` payload names `keep`, `earliest`, the Timer-only rule, and the default behavior (FR-009).
- [X] T029 [US5] In `src\GameBot.Service\Swagger\PrimitiveActionSchemaFilter.cs` add `keep` to the `reschedule-self` payload description: the value `earliest`, the Timer-only rule, the same-run rule, and the default behavior. Run T028 and check that it passes.

**Checkpoint**: All stories work.

---

## Phase 8: Polish and cross-cutting concerns (FR-013)

- [X] T030 [P] Update `C:\src\GameBot\docs\architecture.md`: in the self-reschedule section describe the `keep: earliest` rule, the same-run rule (root execution id), and the RunId=null rule for the re-arm path. Update the "Last reviewed" date.
- [X] T031 [P] Add a line for feature 125 in `C:\src\GameBot\specs\STATUS.md`.
- [X] T032 [P] Add an entry in `C:\src\GameBot\CHANGELOG.md` for `reschedule-self` `keep: earliest` (closes #257).
- [X] T033 [P] Set the `Status` line in `C:\src\GameBot\specs\125-reschedule-self-keep-earliest\spec.md` to Implemented.
- [X] T034 Run `dotnet build C:\src\GameBot\GameBot.sln` and the full unit, integration, and contract test suites. Check that there is no new warning and that all tests pass.
- [X] T035 Run the steps in `C:\src\GameBot\specs\125-reschedule-self-keep-earliest\quickstart.md` and check the results.

---

## Dependencies and Order

- Phase 1, then Phase 2 (T002 to T005 in order), then the story phases.
- US1 (Phase 3) is the MVP. US2 depends on T010 (same file `QueueRunHandle.cs`). US3 needs only Phase 2 and US1 code. US4 and US5 need only Phase 2 and are independent of US1 to US3, except the shared files in the Sequence rule.
- Tests come before code in each phase. Foundational stubs (Phase 2) come before the red tests.
- Polish is last.

## Parallel Examples

- US1 tests: T007 and T008 run in parallel (T006 shares a file only with T014).
- US4 tests: T021, T022, and T023 run in parallel. Then T025 and T026 run in parallel.
- Polish: T030, T031, T032, and T033 run in parallel.

## Implementation Strategy

- MVP: Phase 1, Phase 2, and US1. Then US2 and US3 (all P1) before any release.
- Then US4 (P2) and US5 (P3), then Polish.
