# Tasks: Queue Sequence Notifications

**Input**: Design documents from `/specs/120-sequence-queue-notifications/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notification-api.md, quickstart.md

**Tests**: The spec and the plan require tests. Each plan item V-01 to V-29 has a task that names the check and the pass criterion.

**Organization**: Tasks are grouped by user story. A test task comes before the code task that it checks.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel. It writes a file that no other open task writes.
- **[Story]**: The user story of the task (US1 to US5).
- Test commands use the absolute paths from `quickstart.md`.
- All new text is ASD-STE100 Simplified Technical English (Principle VI).
- "Write a test that fails first" means: run the test before the code exists and see it fail.

## Path Conventions

- Unit tests: `tests/unit/Notifications/`. Queue engine unit tests: `tests/unit/Queues/`.
- Contract tests: `tests/contract/Notifications/` and `tests/contract/Queues/`.
- Integration tests: `tests/integration/Notifications/`.
- UI tests: `__tests__` folders next to the UI code, with the `.spec.ts` or `.spec.tsx` suffix.

---

## Phase 1: Setup

- [X] T001 Create the folders `src/GameBot.Domain/Notifications/`, `src/GameBot.Service/Contracts/Notifications/`, `src/web-ui/src/components/notifications/__tests__/`, `tests/unit/Notifications/`, `tests/contract/Notifications/` and `tests/integration/Notifications/`.
- [X] T002 Run `dotnet build`, `dotnet test`, `vite build` and `jest` before any change. Write the failed tests, if any, in the PR description as old failures (Principle II gate).

---

## Phase 2: Foundational (blocks all user stories)

- [X] T003 [P] Write a test that fails first in `tests/unit/Notifications/QueueLevelPersistenceTests.cs` (V-01). A queue file with no `notificationLevel` reads `none`. A saved level survives a store restart.
- [X] T004 [P] Write a test that fails first in `tests/unit/Notifications/FileNotificationTargetStoreTests.cs`. Cover create, update, delete, restart, atomic write, reload when write time or length changes, and a corrupt file that keeps the last good list.
- [X] T005 [P] Write a test that fails first in `tests/unit/Notifications/NotificationMessageFormatterTests.cs` (V-14). Names with `_`, `*`, `[` and an absent name give `<queue> : <sequence> : <circle> <status>`. The ID replaces an absent name. No parse mode.
- [X] T006 Add `NotificationLevel` with a string JSON converter in `src/GameBot.Domain/Notifications/NotificationLevel.cs`. Values are `none`, `failure` and `successAndFailure`.
- [X] T007 Add the `notificationLevel` field, default `none`, to `src/GameBot.Domain/Queues/ExecutionQueue.cs`. Keep the field in `FileQueueRepository.cs`. Run T003. Pass criterion: T003 passes (V-01).
- [X] T008 Add `NotificationTarget`, `INotificationTargetStore` and `FileNotificationTargetStore` in `src/GameBot.Domain/Notifications/`. The store reloads the file when write time or length changes (FR-018). Run T004. Pass criterion: T004 passes.
- [X] T009 Add `INotificationChannel`, `NotificationField`, `NotificationSendResult`, `QueueNotificationJob`, `NotificationWork`, `NotificationMessage` and the formatter in `src/GameBot.Service/Services/Notifications/`. Run T005. Pass criterion: T005 passes (V-14).
- [X] T010 [P] Add the request and response records, `NotificationTargetView` with `hasSecret` and `secretHint`, in `src/GameBot.Service/Contracts/Notifications/NotificationContracts.cs`.

**Checkpoint**: The domain types, the store, the channel interface and the message text work.

---

## Phase 3: User Story 1 - Get a message when a queue sequence fails (Priority: P1)

**Goal**: A failed queue entry sends one message. The send never slows the queue.

**Independent Test**: Set one queue to "Failure". Fail one entry. One message arrives with the queue, the sequence and the status "failure". A success sends none.

### Tests for User Story 1 (write first)

- [X] T011 [P] [US1] Write a test that fails first in `tests/unit/Notifications/TelegramChannelTests.cs`. Use a fake `HttpMessageHandler`. Check the URL, the body (`chat_id`, `text`, no `parse_mode`), no retry on 4xx, 2 attempts with a 1 s pause on 5xx. The reason has no token.
- [X] T012 [P] [US1] Write a test that fails first in `tests/unit/Notifications/QueueNotificationWorkerTests.cs`. Check the state table of research R-003 for 3 levels and 3 results (V-03). Check the flows of V-04, V-05, V-06 and V-08. Pass criterion: the message count equals the table in each case.
- [X] T013 [P] [US1] Write a test that fails first in `tests/unit/Notifications/QueueNotificationResetTests.cs` (V-07). Call `ResetStreaks` at level None with an open streak. Pass criterion: 0 open streaks and 0 messages at once. The next failure at level Failure sends one "failure" message. A reset is never dropped when 256 jobs wait. A delete reset removes all keys of the queue.
- [X] T014 [P] [US1] Write the concurrency stress test in `tests/unit/Notifications/QueueNotificationStressTests.cs` (V-16). Use 8 threads that run jobs, level changes to None and queue deletes for 2000 rounds. Repeat 50 times. Pass criterion: 0 exceptions, the state class runs on the worker thread only, a deleted queue has 0 streaks.
- [X] T015 [P] [US1] Write a test that fails first in `tests/integration/Notifications/NotificationParallelSendTests.cs` (V-12). Use 3 stuck targets, 1 fast target and 50 failure jobs from 50 queues. Pass criterion: the fast target gets 50 messages within 30 s. Each stuck target ends at 30 s with one log line. The worker handles 50 jobs in under 5 s.
- [X] T016 [P] [US1] Write a test that fails first in `tests/integration/Notifications/NotificationResilienceTests.cs` (V-13). Block the worker and enqueue 300 run jobs. Pass criterion: 256 jobs wait and 44 are dropped, each with one log line. A channel that throws or fails changes no run result or run time. The mean hand-off is under 1 ms.
- [X] T017 [P] [US1] Write a test that fails first in `tests/unit/Queues/QueueExecutionServiceNotificationTests.cs`. Test `QueueStop_WhileHostRuns_SendsCancelled` (V-09), `HostStop_SendsNothing` (V-10) and `Watchdog_TimeLimit_MapsToFailure` (V-11). Pass criterion for V-11: the job status is `failure` and the statistics record still says `Cancelled`.
- [X] T018 [P] [US1] Write a test that fails first in `tests/integration/Notifications/QueueNotificationFlowTests.cs` (V-02). Run one entry and one nested step at level Failure. Pass criterion: 1 job for the entry and 0 jobs for the nested step.

### Implementation for User Story 1

- [X] T019 [P] [US1] Add `TelegramChannel` in `src/GameBot.Service/Services/Notifications/TelegramChannel.cs`. Use 2 attempts of 10 s and a 1 s pause. Return a safe reason. Run T011. Pass criterion: T011 passes.
- [X] T020 [US1] Add `INotificationDispatcher` with `Enqueue` and `ResetStreaks`, `QueueNotificationDispatcher` and the streak state in `src/GameBot.Service/Services/Notifications/`. Use one channel and one worker. Set a 256 job cap and a 64 send cap. Send in parallel with one 30 s limit. Log each drop.
- [X] T021 [US1] Register the dispatcher, the worker, `TelegramChannel`, the targets store and a typed `HttpClient` in `src/GameBot.Service/GameBotServiceSetup.cs`. One dispatcher instance serves the engine and `QueuesEndpoints`.
- [X] T022 [US1] Add the run hook and `NotificationStatusFor` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, in `RecordRunAsync`. Check the host stop token. Map the watchdog stop to `failure`. Run T017 and T018.
- [X] T023 [US1] Run T011 to T018. Pass criterion: all 8 tasks pass. This closes V-02, V-03, V-04, V-05, V-06, V-08, V-09, V-10, V-11, V-12, V-13 and V-16. Write the result in the PR description.

**Checkpoint**: A failed queue entry sends a message. The send never blocks the queue.

---

## Phase 4: User Story 2 - Configure the target and the queue level in the UI (Priority: P1)

**Goal**: The operator adds, edits and deletes targets, sends a test message and sets the queue level.

**Independent Test**: Open the page with no target. Follow the guide. Use "Save and test". Set a queue level. Restart the service. The settings stay.

### Tests for User Story 2 (write first)

- [X] T024 [P] [US2] Write contract tests that fail first in `tests/contract/Notifications/NotificationTargetsContractTests.cs` for GET, POST, PUT, DELETE and `GET types` (V-18). Check 400, never 500, for a bad body, type, key, token shape and chat ID. A PUT with an empty token keeps the stored token.
- [X] T025 [P] [US2] Write a contract test that fails first in `tests/contract/Notifications/NotificationTestSendContractTests.cs` (V-19). Open a streak first. Pass criterion: the request text is `GameBot test message`, no `parse_mode`, the response has `ok` and `reason`, streaks and levels stay the same. Check 404 and the 15 s text.
- [X] T026 [P] [US2] Write a contract test that fails first in `tests/contract/Notifications/NotificationTokenMaskingTests.cs` (V-20). Create a target. Scan all responses, log output and error bodies for the token. Pass criterion: 0 matches.
- [X] T027 [P] [US2] Write contract tests that fail first in `tests/contract/Queues/QueueNotificationLevelContractTests.cs`. Check 200, 400 and 404 of the level route and the new field in both queue responses. Use a fake dispatcher that records each call. Pass criterion: `ResetStreaks` is called once after a save of `none` and once after a queue delete (FR-023).
- [X] T028 [P] [US2] Write contract tests that fail first in `tests/contract/Queues/QueueNotificationLevelKeptTests.cs` (V-23). Set level Failure. Run full update, template link, game link, entry replace, duplicate, create and backup restore. Pass criterion: the level stays Failure, except duplicate and create give `none`.
- [X] T029 [P] [US2] Write a test that fails first in `tests/integration/Notifications/NotificationStoreReloadTests.cs` (V-21). Write a new `targets.json` while the store runs. Pass criterion: the new list appears within 2 s with no restart. A corrupt file keeps the last good list.
- [X] T030 [P] [US2] Write a test that fails first in `tests/integration/Notifications/NotificationBackupExclusionTests.cs`. Pass criterion: the author-data backup archive has no `targets.json` (R-009).
- [X] T031 [P] [US2] Write a Jest test that fails first in `src/web-ui/src/components/notifications/__tests__/TelegramSetupGuide.spec.tsx` (V-17). Pass criterion: the guide shows 6 steps and a field name for each value.
- [X] T032 [P] [US2] Write a Jest test that fails first in `src/web-ui/src/components/notifications/__tests__/TargetList.spec.tsx` (V-18). Check list, edit and delete with a confirmation. Pass criterion: an empty token field on save sends no token, so the stored token stays.
- [X] T033 [P] [US2] Write a Jest test that fails first in `src/web-ui/src/components/notifications/__tests__/TargetForm.spec.tsx` (V-22). Press "Save and test". Pass criterion: POST or PUT runs first, then the test route. A save error skips the test and shows the error.
- [X] T034 [P] [US2] Write a Jest test that fails first in `src/web-ui/src/components/notifications/__tests__/QueueLevelTable.spec.tsx`. Pass criterion: each queue shows its level and a change calls the level route once.
- [X] T035 [P] [US2] Write a Jest test that fails first in `src/web-ui/src/services/__tests__/notifications.spec.ts`. Check each call for its URL, method and body. The token is never in a returned value.

### Implementation for User Story 2

- [X] T036 [P] [US2] Add `NotificationsEndpoints` in `src/GameBot.Service/Endpoints/NotificationsEndpoints.cs`: targets routes, the test route with a 15 s limit and the types route. Run T024 to T026.
- [X] T037 [P] [US2] Change `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`. Add the level route and the level field in responses. Add an optional level to create and update. Duplicate sets `none`. Take `INotificationDispatcher` from DI and call `ResetStreaks` after level `none` and after a delete (FR-023). Run T027 and T028.
- [X] T038 [P] [US2] Change `src/GameBot.Service/Services/BackupService.cs`. Exclude `targets.json` from the archive. Keep the level on a restore. A restore with no level gives `none`. Run T028 and T030.
- [X] T039 [US2] Add `app.MapNotificationsEndpoints()` in `src/GameBot.Service/Program.cs`. Add Swagger descriptions. Refresh the snapshots in `tests/contract/ApiContractSnapshots` and `docs/openapi.json` (R-012).
- [X] T040 [P] [US2] Add `src/web-ui/src/services/notifications.ts` and add the level field to `src/web-ui/src/services/queues.ts`. Run T035.
- [X] T041 [P] [US2] Add `TelegramSetupGuide.tsx` in `src/web-ui/src/components/notifications/`. Run T031.
- [X] T042 [P] [US2] Add `TargetList.tsx` in `src/web-ui/src/components/notifications/`. Run T032.
- [X] T043 [P] [US2] Add `TargetForm.tsx` with "Save and test" in `src/web-ui/src/components/notifications/`. Run T033.
- [X] T044 [P] [US2] Add `QueueLevelTable.tsx` in `src/web-ui/src/components/notifications/`. Run T034.
- [X] T045 [US2] Add `src/web-ui/src/pages/NotificationsPage.tsx`. Add the `/notifications` route and one navigation entry in the files where the other pages register (Grep for `BackupRestorePage`).
- [X] T046 [US2] Run T024 to T035 and `vite build`. Pass criterion: 0 failed tests and 0 build errors. This closes V-01, V-17, V-18, V-19, V-20, V-21, V-22 and V-23. Write the result in the PR description.

**Checkpoint**: The operator sets up a target and a level in the UI. Settings stay after a restart.

---

## Phase 5: User Story 3 - Get a message for every finished sequence (Priority: P2)

**Goal**: Level "Success+Failure" sends a green message for each success and a red message for each failure.

**Independent Test**: Set "Success+Failure". Run one success and one failure. Two messages arrive with the right circles.

- [X] T047 [P] [US3] Write a test in `tests/integration/Notifications/QueueNotificationSuccessFlowTests.cs` (V-03, SC-003). Run all 3 levels and 4 results. Pass criterion: the message count and circle match the table in 100% of cases.
- [X] T048 [P] [US3] Write a test in `tests/integration/Notifications/QueueNotificationStreakFlowTests.cs` (V-04, SC-007). Run 10 failures, then 1 success, at Failure and at Success+Failure. Pass criterion: exactly 2 messages each time, "failure" then "recovered".
- [X] T049 [US3] Run T047 and T048. If a test fails, change only the worker or hook code that the test names. Pass criterion: both tests pass.

---

## Phase 6: User Story 4 - Manual runs stay silent (Priority: P2)

**Goal**: A manual run sends no message.

**Independent Test**: Run a sequence with the REST route at level "Success+Failure". The target gets 0 messages.

- [X] T050 [P] [US4] Write a test in `tests/integration/Notifications/ManualRunNotificationTests.cs` (V-02, SC-004). Run one manual REST run and one nested step at level Success+Failure. Pass criterion: 0 jobs and 0 messages.
- [X] T051 [US4] Run T050. If it fails, change only the hook code that the test names. Pass criterion: T050 passes.

---

## Phase 7: User Story 5 - Add more target types later (Priority: P3)

**Goal**: A new target type needs one class and one DI registration.

**Independent Test**: Register a test-only second channel. Both targets get the message.

- [X] T052 [P] [US5] Write a test in `tests/unit/Notifications/MultiChannelDispatchTests.cs` (V-15). Register a test-only second channel type. Pass criterion: both targets get the message and the dispatcher has no `TelegramChannel` reference.
- [X] T053 [US5] Run T052. If it fails, remove the type reference that the test names. Pass criterion: T052 passes.

---

## Phase 8: Polish and Shared Checks

- [X] T054 [P] Write the time test in `tests/integration/Notifications/NotificationTimingTests.cs` (V-24, SC-002). Use a fake Telegram handler. Fail 20 entries. Pass criterion: at least 19 of 20 messages arrive within 30 s. The send budget is 21 s. This task does not block T060.
- [X] T055 Do the timed quickstart run (V-25, SC-001, SC-002) with the on-screen guide only. Pass criterion: the test message arrives in under 10 minutes and 5 failure messages arrive under 30 s each. With no real bot, run the same steps with a fake target and record this in the PR description. This task does not block T060.
- [X] T056 [P] Update `docs/architecture.md` with the notification design and set the "Last reviewed" date. Add an entry in `CHANGELOG.md`.
- [X] T057 Measure coverage (V-27). Run `dotnet test` with `--collect:"XPlat Code Coverage"` and `jest --coverage`. Pass criterion: 80% line and 70% branch or more for each new module. If a module is below, add tests for the named uncovered lines. Write the numbers in the PR description.
- [X] T058 Run `dotnet build`, `dotnet test`, `vite build` and `jest` (V-28). Pass criterion: 0 errors and 0 failed tests.
- [X] T059 Run the STE check (V-29). Run `C:\Users\anton\AppData\Local\Temp\claude\C--src-GameBot\04d6a640-7e89-4c2f-b33d-cec317187c13\scratchpad\ste.ps1` over `spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `contracts\notification-api.md`, `tasks.md`, and each doc or code comment that this feature wrote. Pass criterion: 0 output lines. Fix each line and run again.
- [X] T060 Set the Status line of `specs/120-sequence-queue-notifications/spec.md` to "Implemented". Add the row `| 120 | Queue Sequence Notifications | Implemented |` to `specs/STATUS.md`, or change the row that exists (V-26, FR-022). Pass criterion: both files say "Implemented". Depends on T057, T058 and T059 only.

---

## Dependencies and Execution Order

- Phase 1, then Phase 2, then Phase 3. Phase 2 blocks all user stories.
- US2 needs T020 and T021 from US1 (dispatcher and DI). US3, US4 and US5 need US1. US3 to US5 do not need US2.
- Test tasks come before the code tasks that they check. T022 runs after T017 and T018.
- T020, T021 and T022 run in this order. T039 runs after T036 and T037.
- T054 and T055 do not block T060.
- T060 is the last task.

## Parallel Examples

- Phase 2 tests: T003, T004, T005 and T010 together.
- US1 tests: T011 to T018 together (each writes its own file).
- US2 tests: T024 to T035 together. US2 code: T036, T037, T038, T040 to T044 together.

## Implementation Strategy

- MVP: Phase 1, Phase 2 and US1. A failed queue entry sends a Telegram message.
- Then US2, so that the operator can set up without the API.
- Then US3, US4 and US5 as checks of the same code.
- End with Phase 8. Set the Status last.
