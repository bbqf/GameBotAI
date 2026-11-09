# Tasks: Show the queue name in notify messages

**Input**: Design documents from `/specs/183-notify-queue-name/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notify-message.md, quickstart.md

**Tests**: Required. Constitution Principle II needs tests for all executable logic.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

## Phase 1: Setup

- [X] T001 Read `src/GameBot.Service/Services/Notifications/QueueNotificationWorker.cs` (`HandleAsync`, `HandleText`) and `tests/unit/Notifications/NotificationTestSupport.cs` (it defines `NotificationHarness`). The harness already has queue `q1` with the name `Farm-1`, in `MemoryQueueRepository`. Make no change.

---

## Phase 2: Foundational

No blocking work. The change uses existing classes only.

---

## Phase 3: User Story 1 - Know which queue sent a notify message (Priority: P1) MVP

**Goal**: A `notify` step that runs in a queue sends `Queue name : text` to each enabled target.

**Independent Test**: Run a `notify` step in a queue named "Farm A". The target gets `Farm A : <text>`. Two queues give two different labels.

### Tests for User Story 1

> Write these tests first. They MUST fail before T006 and T007.

- [X] T002 [P] [US1] Add formatter tests in `tests/unit/Notifications/NotificationMessageFormatterTests.cs`: a name gives `Farm A : text`; a blank or null name gives `q-7 : text`; a text with two lines keeps both lines with the label only on the first line; a name with special characters stays unchanged in the output.
- [X] T003 [US1] Add worker tests in `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`: a send for a queue with the name "Farm A" gives `Farm A : Hello` on each enabled target; two queues with the same text give two different labels; a queue with a blank name gives the queue ID as the label; a queue that you rename between two sends gives the new name in the second message (FR-006).
- [X] T004 [US1] Update the existing expected texts in `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs` (for example `Hello` and `Plain text 1`, send with queue ID `q1`). The harness queue `q1` has the name `Farm-1`, so the expected text becomes `Farm-1 : Hello`.
- [X] T005 [US1] Add one integration test in `tests/integration/Notifications/` (use `NotificationIntegrationHelpers.cs`): a sequence with a `notify` step runs in a queue, and the fake target gets the label and the author text. Also check that a step with `url` still sends the webhook payload with `queueId` and `queueName` and no label in `message`.

### Implementation for User Story 1

- [X] T006 [US1] Add `FormatNotifyText(string? queueName, string queueId, string text)` in `src/GameBot.Service/Services/Notifications/NotificationMessageFormatter.cs`. Return `{label} : {text}`. Use the ID when the name is blank. Add an XML comment with inputs, output, and error modes (Principle I).
- [X] T007 [US1] In `src/GameBot.Service/Services/Notifications/QueueNotificationWorker.cs`, change `HandleText` to an async method `HandleTextAsync`. Update the call in `HandleAsync`. When `send.ScopeKey` equals `NotifyPayload.AdhocScope`, do not read a queue and send `send.Text` as it is (FR-003). Otherwise read the queue with `_queues.GetAsync(send.ScopeKey)` and use the label from T006 for the send text. Keep the rest of the method as it is (order key, completion, logs).
- [X] T008 [US1] In the same method, handle a failed or empty queue read: use the queue ID as the label and do not drop the message. Log the failed read with the existing `Log.QueueReadFailed`. The method MUST still set the completion exactly one time.

**Checkpoint**: T002 to T005 pass. The queue case works from end to end.

---

## Phase 4: User Story 2 - Clear message when no queue applies (Priority: P2)

**Goal**: A step that runs outside a queue sends the plain author text with no label.

**Independent Test**: Run a sequence with a `notify` step by hand. The target gets only the author text.

### Tests for User Story 2

- [X] T009 [US2] Add worker tests in `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`: a send with scope key `adhoc` gives the plain text with no label and no queue read; a send for a queue that does not exist gives the ID as the label; a send where the queue read throws gives the ID as the label and the completion is `Delivered`. For the last test, add a queue repository that throws in `tests/unit/Notifications/NotificationTestSupport.cs`.
- [X] T010 [P] [US2] Add tests in `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`: (a) a step that runs with no originating queue calls `SendText` with the scope key `adhoc` and the unchanged message; (b) two queues use the same dedupe key and each queue sends its own message (the dedupe scope is the queue ID).

### Implementation for User Story 2

- [X] T011 [US2] Check that the ad-hoc rule in `HandleTextAsync` (made in T007) passes T009 and T010. Fix the code only if a test fails. This task makes no new code in the normal case.

**Checkpoint**: T009 and T010 pass. Both stories work.

---

## Phase 5: Polish and cross-cutting

- [X] T012 [P] Update the "Text send" paragraph in `docs/architecture.md`. Replace "The text has no prefix." with the label rule. Refresh the "Last reviewed" date.
- [X] T013 [P] Set the `Status` line of `specs/183-notify-queue-name/spec.md` to Implemented. Add feature 183 to `specs/STATUS.md`. Mark feature 159 as "Implemented (iterated by 183)" in its `spec.md` and in `specs/STATUS.md`.
- [X] T014 [P] (No changelog file exists in the repository. The note goes in the PR description: patch 13.) Add a changelog entry (STE) for the new label. Mention the new `patch` number from T018. Find the changelog file in the repository root or in `docs/`. If none exists, put the note in the PR description.
- [X] T015 Build the solution and run the notification tests. Run `dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Notif"`. Fix all failures. Check that new code has no analyzer warning and that method names use CamelCase only. Write a short perf note for the PR: one extra queue read for each text send, on the worker thread, and no new wait in the step (Principle IV).
- [ ] T016 Run the manual steps in `specs/183-notify-queue-name/quickstart.md` if a test target is available. Report if you skip them.
- [X] T017 Check the shard balance rule if you added a test class (Constitution, Workflow). Run `scripts/ci/measure-shards.ps1` only when a shard may be more than 20% above the average.
- [X] T018 Before the PR, bump `patch` in `installer/versioning/version.override.json` as the Constitution requires. Set `updatedBy` and `updatedAtUtc`.

---

## Dependencies and execution order

- Phase 1 first. Phase 2 has no tasks.
- User Story 1 (Phase 3) is the MVP. Do it first.
- User Story 2 (Phase 4) depends on T007, because T007 holds the ad-hoc rule that the Story 2 tests check. It does not need T008.
- Polish (Phase 5) starts after both stories pass.
- Inside a story: tests first, then T006, then T007, then T008.

## Parallel opportunities

- T002 is in a different file from T003 and T004. Run T002 in parallel with them. Do T003 and T004 one after the other, because they change the same file.
- T010 is in a different file from T009. Run them in parallel. T009 and T003 change files that you must not edit at the same time.
- T012, T013, and T014 are in different files.

## Implementation strategy

1. MVP: do Phase 1 and Phase 3. The queue label works for a queue run.
2. Add Phase 4. The ad-hoc case then has explicit tests and a clear rule.
3. Finish with Phase 5 and the version bump.
