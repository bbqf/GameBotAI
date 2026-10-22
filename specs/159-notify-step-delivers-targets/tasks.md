---

description: "Task list for feature 159: Notify step delivers to the notification targets"
---

# Tasks: Notify Step Delivers to the Notification Targets

**Input**: Design documents from `/specs/159-notify-step-delivers-targets/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notify-step.md, quickstart.md

**Tests**: The plan lists four test files. Test tasks are included. This is a bug fix, so the regression test comes first and MUST fail before the fix (FR-013).

**Organization**: Tasks are grouped by user story. All paths are relative to `C:\src\GameBot`. In PowerShell commands, use absolute paths and no `cd dir;` prefix. Edit UTF-8 files with the Edit or Write tool. Write all text in STE.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task). A task that edits the same file as another task is NOT marked [P].
- **[Story]**: US1, US2, US3

**File-sharing notes** (these tasks edit the same file, so they run in order):

- `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`: T003, T012, T013, T015, T018
- `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`: T010, T011
- `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`: T008, T017
- `specs/STATUS.md`: T023, T024

---

## Phase 1: Setup

**Purpose**: Check the base before the change.

- [x] T001 Run `dotnet build C:\src\GameBot\GameBot.sln` and the existing notification tests (`tests/unit/Notifications`) to record a green base. No file change.
- [x] T002 [P] Read `src/GameBot.Service/Services/Notifications/NotificationTypes.cs`, `QueueNotificationDispatcher.cs`, `QueueNotificationWorker.cs`, and `DispatchNotifyAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` to find the exact names of `ChainSend`, `SendOneAsync`, `NotificationWork`, `NotificationDispatchLimits.SendTimeout`, and the DI registration in `src/GameBot.Service/GameBotServiceSetup.cs`. No file change.

---

## Phase 2: Foundational (red-first regression test, FR-013)

**Purpose**: Prove the defect before the fix. No source code change in this phase.

- [x] T003 [US1] Write the regression test in `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`: one enabled target (fake `INotificationChannel`), a `notify` step with a `message` and no `url`, run the step, assert that the channel receives the `message` text. Use only the types that exist today, so the test compiles on the current code.
- [x] T004 [US1] Run the regression test from T003 on the current code. It MUST fail. Save the failing output (test name and message) in `specs/159-notify-step-delivers-targets/checklists/red-evidence.md` as proof for FR-013. Do not start Phase 3 code changes before this step is done.

**Checkpoint**: The defect is shown by a red test.

---

## Phase 3: User Story 1 - Operator gets the notify message on the enabled targets (Priority: P1)

**Goal**: A `notify` step with a `message` and no `url` sends the message to each enabled target, one time each.

**Independent Test**: One enabled target, a `notify` step without `url`: the target receives the message text.

### Implementation for User Story 1

- [x] T005 [US1] In `src/GameBot.Service/Services/Notifications/NotificationTypes.cs`, add the internal records `TextSend` (Text, ScopeKey, SequenceId, Completion) and `TextSendOutcome` (NoTarget, Delivered, Failed(reason)), the work kind `Text` with factory `NotificationWork.ForText(TextSend)`, and the member `Task<TextSendOutcome> SendText(string text, string scopeKey, string sequenceId, CancellationToken ct)` on `INotificationDispatcher`. Follow `data-model.md`.
- [x] T006 [US1] In `src/GameBot.Service/Services/Notifications/QueueNotificationDispatcher.cs`, implement `SendText`: create a `TaskCompletionSource<TextSendOutcome>` with `RunContinuationsAsynchronously`, queue the work item (not counted in `MaxQueuedJobs`), return the task at once, never throw. If the work cannot be queued, return `Failed` with safe text. Add the constant `TextSendGuardMargin` (5 seconds) to this class for the guard time limit (FR-016).
- [x] T007 [US1] In `src/GameBot.Service/Services/Notifications/QueueNotificationWorker.cs`, add `HandleTextAsync`: read enabled targets with `INotificationTargetStore.List()`. If none or the read fails, set `NoTarget`. Otherwise send one `ChainSend` per enabled target with key `notify:<sequenceId>` and the scope key, no worker retry, plain `message` text, `_lastSent` unchanged. A fault on one target must not stop the others. Set `Delivered` if at least one target accepts, else `Failed(reason)`. When some targets fail but at least one accepts, write a log line that names how many targets failed (FR-017). Set `Completion` exactly one time, also on a fault. Log without secrets.
- [x] T008 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, change `DispatchNotifyAsync`: when `url` is empty, call `INotificationDispatcher.SendText` (scope key = queue id or `adhoc`) and await it with the guard time limit `NotificationDispatchLimits.SendTimeout` + `QueueNotificationDispatcher.TextSendGuardMargin` (FR-016). On expiry, use `Failed("time limit")`. Map `Delivered` to "notification delivered", `Failed` to "notification not delivered: reason", and `NoTarget` to the old `IFailureNotifier` path. Keep the dedupe, record, and step-message logic. The step outcome stays `success` (FR-012).
- [x] T009 [US1] Run the T003 test again. It MUST now pass. Add a line with the green result to `specs/159-notify-step-delivers-targets/checklists/red-evidence.md`.
- [x] T010 [US1] In `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`, add worker tests: two enabled targets each get one message (SC-002); a disabled target gets none (FR-003); one failing target does not stop the other (FR-007); no enabled target gives `NoTarget` (FR-006); a target-list read fault gives `NoTarget`; all targets fail gives `Failed`; the text has no prefix (FR-011); the queue notification level output is unchanged (FR-009).
- [x] T011 [US1] In `tests/unit/Notifications/QueueNotificationWorkerTextSendTests.cs`, add the partial-failure test (FR-017): two enabled targets, one accepts and one fails. Assert that the result is `Delivered` and that the log names the count of failed targets (1).
- [x] T012 [US1] In `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`, add step tests: two targets and a disabled target; the log does not contain "notification not delivered: no destination configured" when a target is enabled (FR-004); no target and no `url` keeps the old path and the step stays `success` (FR-006, FR-012); a delivery fault keeps `success`.
- [x] T013 [US1] In `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`, add the time-limit test (FR-016): a fake dispatcher or channel that never answers. Use a short test value for the send time limit if the code allows it, or a fake clock. The guard is `SendTimeout` + 5 s margin. Assert that the result is "not delivered" (time limit) and that the step outcome stays `success`.
- [x] T014 [US1] Confirm the step-through path for `notify` (FR-019): read `src/GameBot.Service/Services/StepThrough/StepThroughActionPreview.cs` (`DescribeNotify`) and write down in a code comment of the T015 test the method name and the outcome name (`preview`). No source change. If the name differs from the plan, use the real name in T015.
- [x] T015 [US1] In `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`, add the dry-run test (FR-014), based on the result of T014: run a `notify` step in step-through mode with an enabled target. Assert that the outcome is the confirmed outcome name and that no channel and no dispatcher `SendText` call happens.
- [x] T016 [P] [US1] In `tests/integration/Notifications/NotifyStepToTargetsTests.cs`, add an integration test with the real dispatcher and worker, a fake channel, and a sequence with a `notify` step without `url`. Assert that the fake channel gets the message text once per enabled target.

**Checkpoint**: User Story 1 works and is tested on its own.

---

## Phase 4: User Story 2 - The `url` field still overrides the default (Priority: P2)

**Goal**: A `notify` step with `url` sends only to that `url`. No target gets the message.

**Independent Test**: An enabled target exists and the step has a `url`: only the `url` receives the message.

- [x] T017 [US2] Check in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` that the `url` branch of `DispatchNotifyAsync` does not call `SendText`. Fix it only if it does.
- [x] T018 [US2] In `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`, add the ONE `url` override test (FR-005, SC-003): an enabled target exists, the step has a `url`; assert that the `url` notifier gets the message and the dispatcher `SendText` is not called.

**Checkpoint**: Existing `url` behaviour is kept.

---

## Phase 5: User Story 3 - The API documentation names the default destination (Priority: P2)

**Goal**: The OpenAPI text of `notify` names the enabled notification targets as the default destination.

**Independent Test**: Read the published OpenAPI document and check the `notify` text.

- [x] T019 [US3] In `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs`, change the text of `notify` and of its `url` field (FR-008): without `url` the message goes to the enabled notification targets listed by `GET /api/notifications/targets`; with `url` the message goes only to that URL; `Service:Notifications:DefaultUrl` is used only when no target is enabled. Write the text in STE.
- [x] T020 [P] [US3] In `tests/contract/Sequences/NotifyActionOpenApiTextTests.cs`, add a contract test that reads the published OpenAPI document and checks that the `notify` text and the `url` text name the enabled notification targets as the default destination (SC-004).

**Checkpoint**: All three user stories work.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Documentation, status, version, and final checks. The order is fixed: docs (T021, T022), then status (T023, T024), then version bump (T025), then the final full build and test (T026), then the quickstart (T027). The final build and test run MUST come after all docs, status, and version tasks.

- [x] T021 [P] Update `docs/architecture.md`: describe the text send path (step to dispatcher to worker to targets) and update the "Last reviewed" date. Use STE.
- [x] T022 [P] Add an entry to `CHANGELOG.md` for the fix of issue #335. Use STE.
- [x] T023 Set the Status of `specs/137-*/spec.md` (notify alert dedupe window) to "Implemented (iterated by 159)" and update the row of spec 137 in `specs/STATUS.md` to match (FR-018). This is firm and not conditional.
- [x] T024 Set the Status of `specs/159-notify-step-delivers-targets/spec.md` to `Implemented` and update the row for spec 159 in `specs/STATUS.md` (FR-015). Do this after T023, because both tasks edit `specs/STATUS.md`.
- [x] T025 Run `git fetch origin master`, then update `installer/versioning/version.override.json` per the CLAUDE.md rule: if `minor` or `major` changed against `origin/master`, set `patch` to `"0"`; if not, set `patch` to the `origin/master` `patch` plus 1. Set `updatedBy` and `updatedAtUtc`. Do nothing if the value is already correct.
- [x] T026 Run `dotnet build C:\src\GameBot\GameBot.sln` and the full unit, contract, and integration tests. All must pass, including the existing queue notification tests (SC-005). Check coverage of the touched code (at least 80% line, 70% branch). Do this only after T021 to T025 are done.
- [x] T027 Run the steps in `specs/159-notify-step-delivers-targets/quickstart.md` to check the result.

---

## Dependencies and Execution Order

- Phase 1 first. Phase 2 (T003, T004) needs Phase 1 and MUST finish before T005.
- Phase 3: T005 -> T006 -> T007 -> T008 -> T009. After T008, the tests T010 to T015 run in order where they share a file (see the file-sharing notes). T014 comes before T015. T016 can run in parallel with them.
- Phase 4 needs T008. Phase 5 is independent of Phases 3 and 4 and can run after Phase 2.
- Phase 6 needs all earlier phases. Order: T021 and T022 -> T023 -> T024 -> T025 -> T026 -> T027.

## Parallel Example

After T008: run T016 (integration file) at the same time as the unit test chain T010 to T015. T020 (contract file) can run at the same time as T019 once T019 text is known, and Phase 5 can run at the same time as Phase 3. In Phase 6, T021 and T022 can run together.

## Implementation Strategy

- MVP: Phases 1 to 3 (User Story 1). It fixes the defect.
- Then add Phase 4 (guard for `url`) and Phase 5 (documentation text).
- Finish with Phase 6 in the fixed order.
