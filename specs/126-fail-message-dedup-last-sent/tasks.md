# Tasks: Suppress a failure message only when the last message sent was the same

**Input**: Design documents from `/specs/126-fail-message-dedup-last-sent/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The plan and Constitution Principle II require unit tests for SC-001 to SC-005 and each edge case.

**Organization**: Tasks are in groups by user story. All code changes are in the notification worker.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story of the task (US1, US2)

## Phase 1: Setup

**Purpose**: Confirm a clean baseline before any change.

- [X] T001 Build the solution and run the tests in `C:\src\GameBot\tests\unit\Notifications\` to confirm that build and tests pass before changes (Constitution gate).
- [X] T002 Read `C:\src\GameBot\src\GameBot.Service\Services\Notifications\QueueNotificationWorker.cs` (`Decide`, `StartSend`, `HandleAlertAsync`) and `NotificationStreakState.cs` in the same folder. Note the guard pattern of the streak state for reuse in T004.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The last-sent record. Both user stories use it.

- [X] T003 [P] Write unit tests in `C:\src\GameBot\tests\unit\Notifications\NotificationLastSentStateTests.cs`: `IsSame` is false with no record; true after `Record` of an equal message; false when queue ID, sequence key, status, or text differs (ordinal compare); records of two targets are separate; `Record` replaces the earlier record.
- [X] T004 Create `C:\src\GameBot\src\GameBot.Service\Services\Notifications\NotificationLastSentState.cs`.
  - Add a `LastSentMessage` record with queue ID, sequence key, status, and text.
  - Add a class that has a dictionary from target ID to `LastSentMessage`.
  - Add the methods `IsSame(targetId, message)` and `Record(targetId, message)`.
  - Use the same guard pattern as `NotificationStreakState`.
  - Use CamelCase without underscores for method names.
  - Make T003 pass.

**Checkpoint**: The record class is ready and tested.

---

## Phase 3: User Story 1 - See a repeated failure again after other messages (Priority: P1) MVP

**Goal**: A failure is sent again when another message came after the last equal failure on the same target.

**Independent Test**: Feed failure (A), success (B), failure (A). The worker sends three messages. Feed failure (A), failure (A). The worker sends one message.

### Tests for User Story 1

> Write these tests first. Tests T006, T007, and T007A MUST fail before T008 to T010. T005 is a guard test and can pass before the fix.

- [X] T005 [US1] In `C:\src\GameBot\tests\unit\Notifications\QueueNotificationWorkerTests.cs`, add a test for SC-001 and acceptance scenario 1: failure (A), failure (A) with no other message sends exactly 1 message.
- [X] T006 [US1] In the same file, add tests for SC-002 (failure A, success B, failure A sends 3 messages) and SC-004 (two queues on one target: failure A queue 1, failure B queue 2, failure A queue 1 sends 3 messages).
- [X] T007 [US1] In the same file, add edge-case tests. Add one test for each case in this list.
  - Same sequence with different failure text: the service sends the new message.
  - Same sequence on a different queue: the service sends the message.
  - Two targets keep separate records. A failure that is equal on target 1 but new on target 2 goes only to target 2.
  - A message that the level filter, the exclude option, or the send cap drops does not update the record.
  - An alert between two equal failures makes the next failure new.
  - A "cancelled" message between two equal failures makes the next failure new.
  - A failed send still counts as the last message (FR-008).
  - A new `QueueNotificationWorker` (restart) sends the failure message.
- [X] T007A [US1] In the same file, add a test that replays the 2026-10-01 timeline from spec.md (SC-005). Expect exactly these messages in order: failure 19:24, success 20:24:35, failure 20:24:46, success 21:24:54, failure 21:25:05. Expect no message for the failure at 22:25:19. Confirm that the test fails before the fix.

### Implementation for User Story 1

- [X] T008 [US1] In `C:\src\GameBot\src\GameBot.Service\Services\Notifications\QueueNotificationWorker.cs`, create one `NotificationLastSentState` field that the worker thread owns. Change `Decide` for a failure. Open the streak if it is closed. Then always return `Failure`.
- [X] T009 [US1] In `StartSend` of the same file, do these steps after the worker lists the enabled targets and passes the send cap.
  - Remove each target for which the message is a `Failure` and `IsSame` is true.
  - If no target is left, release the cap slot and return.
  - For each target that is left, call `Record` before `ChainSend`.
  - Make sure that a message which the level filter, the exclude option, or the cap drops does not reach `Record`.
  - A "cancelled" message goes through `StartSend`. The worker records it and never suppresses it.
- [X] T010 [US1] In `HandleAlertAsync` of the same file, call `Record` for each target before `ChainSend`, with status `alert` and sequence key `alert`. An alert is never suppressed.
- [X] T011 [US1] Run the notification unit tests. Confirm that T005 to T007A pass.
  - Change the old test `V06_CancelledChangesNoStreak` in `QueueNotificationWorkerTests.cs`.
  - Its new expected message list is: failure, cancelled, failure, recovered.
  - Confirm that no other old test needs a change.
  - If another old test fails, stop and report it. Do not change it without a spec reason.

**Checkpoint**: User Story 1 works alone. Acceptance scenarios 1 to 3 and SC-001, SC-002, SC-004, SC-005 pass.

---

## Phase 4: User Story 2 - Keep the "recovered" message (Priority: P2)

**Goal**: The first success after a failure still sends "recovered".

**Independent Test**: Feed failure (A), failure (A), success (A). The worker sends "failure", then "recovered".

- [X] T012 [US2] In `C:\src\GameBot\tests\unit\Notifications\QueueNotificationWorkerTests.cs`, add a test for SC-003: failure (A), failure (A), success (A) sends "failure" then "recovered", in this order. Add a test that failure (A), success (B), failure (A), success (A) sends "recovered" once, because the streak of A stayed open.
- [X] T013 [US2] Run the tests. If T012 fails, fix the `Decide` branches for "recovered" and "success" in `C:\src\GameBot\src\GameBot.Service\Services\Notifications\QueueNotificationWorker.cs` so that they stay as before (FR-005). Make no other change to them.

**Checkpoint**: User Stories 1 and 2 both pass.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Living docs (FR-010, Constitution Principle V) and the final gate.

- [X] T014 [P] Update the Streaks paragraph and the "Last reviewed" date in `C:\src\GameBot\docs\architecture.md`. Use STE.
- [X] T015 [P] Change the Status line of `C:\src\GameBot\specs\126-fail-message-dedup-last-sent\spec.md` to "Implemented".
- [X] T016 [P] Change the Status line of the spec 120 file (`C:\src\GameBot\specs\120-*\spec.md`) to "Implemented (iterated by 126)".
- [X] T017 Update `C:\src\GameBot\specs\STATUS.md`: change the row of spec 120 to "Implemented (iterated by 126)" and add a row for spec 126 (FR-016, issue #269).
- [X] T018 Run `C:\src\GameBot\specs\126-fail-message-dedup-last-sent\quickstart.md` validation. Run the full build and the full unit test suite. Confirm no failure.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. It blocks both user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2.
- **User Story 2 (Phase 4)**: Depends on Phase 2. Its tests share the file of Phase 3, so do it after Phase 3 to avoid edit conflicts. It does not need the US1 behavior.
- **Polish (Phase 5)**: Depends on Phases 3 and 4.

### Within Phases

- T003 and T004: T004 follows T003 (tests first).
- T005 to T007A: same file, so run in order. They come before T008 to T010.
- T008 before T009 before T010 (same file).
- T014, T015, T016 touch different files and can run in parallel. T017 follows T015 and T016.

## Parallel Example

```text
Phase 2: T003 can start while T002 is in review.
Phase 5: T014, T015, T016 together.
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 and Phase 2.
2. Phase 3 (the defect fix).
3. Stop and check: the 2026-10-01 timeline test passes.

### Incremental Delivery

1. Add User Story 2 tests to prove that "recovered" did not change.
2. Finish the living docs and the final gate.

## Notes

- All new text follows STE (Constitution Principle VI).
- Do not add new settings or an API change (FR-007).
- New test method names use CamelCase without underscores.
- FR-006 (rule works on sent messages, not on the queue or sequence alone) is covered by T006.
- Do not commit in this step.
