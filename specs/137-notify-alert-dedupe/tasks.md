# Tasks: Notify alert dedupe window

**Input**: Design documents in `specs/137-notify-alert-dedupe/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notify-step.md, quickstart.md

Format: `- [ ] T### [P?] [Story?] Description with file path`. [P] means the task can run in parallel (different file, no open dependency). Tasks that edit the same file are never marked [P].

## Phase 1: Setup

- [ ] T001 Read `src/GameBot.Domain/Commands/Notify/NotifyPayload.cs`, `src/GameBot.Domain/Queues/FileSequenceRunStatisticsStore.cs`, `src/GameBot.Domain/Services/LastRunWindow.cs` (SinceStart) and `DispatchNotifyAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Note the patterns to copy. No file change.

## Phase 2: Foundational (blocks all user stories)

- [ ] T002 Add `DedupeKey`, `Since`, `Within` read-only properties, the constant `MaxDedupeKeyLength = 100`, and parse and validation rules (FR-001, FR-002, FR-011) in `src/GameBot.Domain/Commands/Notify/NotifyPayload.cs`. Error messages name the field and give a fix.
- [ ] T003 [P] Create the interface `ISentAlertStore` (TryReserveAsync, CommitAsync, ReleaseAsync, WasSentSinceAsync, HasLiveReservationAsync, DeleteQueueAsync) with doc comments in `src/GameBot.Domain/Queues/ISentAlertStore.cs`.
- [ ] T004 Create `FileSentAlertStore` in `src/GameBot.Domain/Queues/FileSentAlertStore.cs`: file `<dataRoot>/notify-alert-dedupe/sent-alerts.json` (schemaVersion 1), one gate, in-memory reservations that die after 5 minutes, atomic write only on commit, prune of records older than 48 hours on commit. Include read-only `WasSentSinceAsync` and `HasLiveReservationAsync` (true when a live, not expired reservation exists for the scope), and `DeleteQueueAsync` (removes records and reservations of the queue) (FR-007, FR-008, FR-009, FR-013, FR-014). Depends on T003.
- [ ] T005 Register `ISentAlertStore` as a singleton in `src/GameBot.Service/GameBotServiceSetup.cs` (one DI line; keep `Program.cs` thin). Depends on T004.
- [ ] T006 Add an optional `ISentAlertStore` parameter and a `TimeProvider` to the `SequenceExecutionService` constructor in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, so hand-built test instances still compile. When the store is null, a step with dedupe fields always sends. Depends on T003.

**Checkpoint**: Payload parses the new fields and the store exists. User stories can start.

## Phase 3: User Story 1 - One alert per window across retries (P1) MVP

**Goal**: Only the first run in a window sends the alert. Later runs report `suppressed`.
**Independent Test**: Run one `notify` step with `dedupeKey` and `since` 10 times in one window. The notifier gets 1 alert. Outcomes are 1 `sent` and 9 `suppressed`. Two or more parallel runs give 1 delivery.

- [ ] T007 [P] [US1] Unit tests for `FileSentAlertStore` (reserve, commit, release, dead reservation after 5 minutes, prune at 48 hours, restart reload, 20 concurrent reserves give 1 winner, `HasLiveReservationAsync` true while reserved and false after release or expiry) in `tests/unit/Queues/FileSentAlertStoreTests.cs`.
- [ ] T008 [P] [US1] Unit tests for window start math (`since` equals `LastRunWindow.SinceStart`, including daylight-saving days; `within` ends now) in `tests/unit/Queues/NotifyDedupeWindowTests.cs`.
- [ ] T009 [US1] Implement the base dedupe flow in `DispatchNotifyAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Build the scope input first: the queue identity (not the name), or `adhoc` for a run outside a queue (FR-006). Then compute window start, `TryReserveAsync`, skip with outcome `suppressed` when null, deliver, `CommitAsync` on success, `ReleaseAsync` on failure, outcome `sent`. Step status stays `success`. A step without the new fields keeps its old path and outcome text (FR-003, FR-004, FR-007, FR-010, FR-013). Depends on T002, T004, T006.
- [ ] T010 [US1] Integration tests in `tests/integration/Sequences/NotifyStepRunTests.cs` (SC-001, SC-005): the same step runs 10 times in one window (1 `sent`, 9 `suppressed`, notifier gets 1 alert); the new-window scenario; the `within` scenario; failed delivery then retry; restart inside the window. Depends on T009.
- [ ] T011 [US1] Concurrent-dispatch test through the notify step in `tests/integration/Sequences/NotifyStepRunTests.cs`: start 2 or more runs of the same step in parallel (use a notifier that blocks until all runs reach the step); the notifier receives exactly 1 alert (FR-013). Depends on T010 (same file).
- [ ] T012 [US1] Reservation tests in `tests/integration/Sequences/NotifyStepRunTests.cs`: a run that starts while a reservation is live reports `suppressed` and does not send; after the first delivery fails and releases the slot, a later run sends. Depends on T011 (same file).
- [ ] T013 [US1] Null-store test in `tests/integration/Sequences/NotifyStepRunTests.cs`: a `SequenceExecutionService` built with a null `ISentAlertStore` sends every time for a step with dedupe fields. Depends on T012 (same file).

**Checkpoint**: US1 works alone.

## Phase 4: User Story 2 - Scope by queue and key (P2)

**Goal**: An alert in one (queue, key) scope never hides an alert in another scope.
**Independent Test**: Send key "a" in Q1. Key "a" in Q2 sends. Key "b" in Q1 sends.

- [ ] T014 [US2] Call `DeleteQueueAsync` when a queue is deleted in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (FR-009). Depends on T005.
- [ ] T015 [US2] Tests for the two scope scenarios and the ad hoc scope in `tests/integration/Sequences/NotifyStepRunTests.cs`, and a queue-delete clean-up test (records and reservations removed; a later run in a new queue with the same key sends) in `tests/integration/Sequences/NotifyStepRunTests.cs`. Depends on T009, T013 (same file), T014.
- [ ] T016 [US2] Store-level queue delete test (`DeleteQueueAsync` removes only that queue's records and reservations) in `tests/unit/Queues/FileSentAlertStoreTests.cs`. Depends on T007 (same file) and T004.

**Checkpoint**: US1 and US2 work.

## Phase 5: User Story 3 - Existing steps and invalid input (P2)

**Goal**: Old `notify` steps send every time. Invalid new fields give 400 at save.
**Independent Test**: Old step twice sends twice. Each invalid combination in FR-011 gives 400, never 500.

- [ ] T017 [US3] Check that the save path (`NotifyPayload.TryRead` through `src/GameBot.Domain/Services/SequenceStepValidationService.cs`) returns the new payload errors as 400. Edit `SequenceStepValidationService.cs` only if a message needs it; otherwise make no change (FR-011). Depends on T002.
- [ ] T018 [P] [US3] Validation unit tests for every invalid combination of FR-011 and the valid cases in `tests/unit/Sequences/NotifyStepValidationTests.cs`. Depends on T002.
- [ ] T019 [P] [US3] Contract tests: a representative subset of invalid combinations returns 400 with a message that names the field; old steps still save, in `tests/contract/Sequences/NotifyActionContractTests.cs`. Depends on T017.
- [ ] T020 [US3] Integration test: an old `notify` step runs twice and sends twice with the old outcome text (SC-002) in `tests/integration/Sequences/NotifyStepRunTests.cs`. Depends on T015 (same file).

**Checkpoint**: All stories work.

## Phase 6: Preview and OpenAPI (FR-012, FR-014)

The preview work needs the store registration (T005) and the base dispatch scope rule (T009).

- [ ] T021 Show "would send" or "would suppress" in `src/GameBot.Service/Services/StepThrough/StepThroughActionPreview.cs`. Use the same scope rule as a real run (queue identity of the stepped run, or `adhoc`). Show "would suppress" when `WasSentSinceAsync` returns a time or `HasLiveReservationAsync` returns true. With a null store, show "would send". No send, no record write. Depends on T005, T009.
- [ ] T022 [P] Document the new fields and the outcomes `sent` and `suppressed` in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs`.
- [ ] T023 Tests for preview in new file `tests/unit/StepThrough/NotifyDedupePreviewTests.cs`: no send, no write, scope rule (queue id and `adhoc`), "would suppress" on a record in the window, "would suppress" on a live reservation, "would send" with a null store. Depends on T021.
- [ ] T024 OpenAPI text test in `tests/contract/Sequences/NotifyActionContractTests.cs`. Depends on T019 (same file) and T022.

## Phase 7: Polish and cross-cutting

- [ ] T025 [P] Update `docs/architecture.md`: persistence layout (`notify-alert-dedupe/sent-alerts.json`), notify step dedupe flow, and set the "Last reviewed" date (now 2026-10-12) to the current review date.
- [ ] T026 [P] Update `specs/STATUS.md` with the status line for feature 137.
- [ ] T027 FR-015 check: run the existing `lastRun`, notify and alert channel tests without change. They must pass. Then run the quickstart steps in `specs/137-notify-alert-dedupe/quickstart.md`, run all new unit, integration and contract tests, and confirm the build has no new warning.

## Dependencies and order

- Phase 1 then Phase 2 (T002 and T003 parallel; T004 after T003; T005 after T004; T006 after T003).
- US1 (Phase 3) needs Phase 2. Tests in `NotifyStepRunTests.cs` run in order: T010, T011, T012, T013, T015, T020.
- US2 needs T009 (scope input is in T009) and T005. US3 needs T002 only and can run beside US1, except T020.
- Phase 6: T021 needs T005 and T009. T022 is free. Phase 7 comes last.

## Parallel examples

- After Phase 2: T007 and T008 together; T018 beside T007.
- T019 with T022 after their dependencies.
- T025 and T026 together.

## Implementation strategy

MVP is Phase 1 to Phase 3 (US1). Then add US2, then US3, then preview and docs. Validate after each phase.
