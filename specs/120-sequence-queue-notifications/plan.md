# Implementation Plan: Queue Sequence Notifications

**Branch**: `120-sequence-queue-notifications` | **Date**: 2026-09-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/120-sequence-queue-notifications/spec.md`

## Summary

The operator sets a notification level for each queue (None, Failure, Success+Failure). When a
queue entry sequence ends, the engine hands a job to the notification dispatcher. The dispatcher
writes the job to one channel. One worker reads this channel. The worker is the only owner of the
failure streak state. It applies the level rules and the streak rules. Then it starts the sends for
the message. The sends go to all enabled targets in parallel, each with a limit of 30 s. Sends for
one target, queue and sequence keep the order of the jobs. The worker does not wait for the send. The message has the format
`<queue> : <sequence> : <circle> <status>`.

Level changes to "None" and queue deletes reach the worker as control messages in the same
channel. The entry point is `INotificationDispatcher.ResetStreaks(queueId)`. `QueuesEndpoints` gets
this dispatcher from DI (FR-023).

Telegram is the first target type. A channel interface keeps other types possible. Targets live in
one JSON store. The REST API, the UI and a hand edit all use it. The store reloads the file with no
restart (FR-018). The queue level is a field on the queue. Every other queue change keeps it
(FR-021). A hand edit of the queue file is out of scope.

The UI has a "Notifications" page. It has a Telegram guide, a target list with add, edit and
delete, a form with "Save and test", and a queue level table. The last task sets the Status of the
spec and of `specs/STATUS.md` to "Implemented" (FR-022).

## Technical Context

**Language/Version**: C# on .NET (current solution); TypeScript and React in `src/web-ui`
**Primary Dependencies**: ASP.NET minimal APIs, `System.Threading.Channels`, `HttpClient` through `AddHttpClient`. No new package.
**Storage**: `data/notifications/targets.json` (targets); `notificationLevel` field in the queue file
**Testing**: xUnit (unit, contract, integration) with a fake `HttpMessageHandler`; Jest for the UI; coverlet for coverage
**Target Platform**: Windows service host (local install); browser UI
**Project Type**: Web service with a web UI
**Performance Goals**: The job hand-off in the queue path takes under 1 ms and never blocks. The message reaches the target within 30 s of the failure (SC-002). Send budget: 2 attempts of 10 s each plus a 1 s pause is 21 s. This fits in 30 s.
**Constraints**: A send failure never changes a run result or run time (FR-010, SC-005). The token never appears in a response, a log line or an error text (FR-016, SC-006). One total limit of 30 s for each message, also with slow targets.
**Coverage baseline**: 80% line and 70% branch for the new modules (constitution Principle II). Item V-27 measures it.
**Scale/Scope**: Tens of queues, a few targets, one job for each finished queue entry

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | Small classes: message, channel, dispatcher, worker, store. No secret in a log line. Program.cs stays thin (endpoint map call only). |
| II. Testing | Pass | Unit, contract and integration tests for every requirement (see Verification Plan). Coverage baseline in V-27. A test that fails comes first for FR-021. |
| III. UX consistency | Pass | Errors use the `{ error: { code, message, hint } }` shape. A bad body gives 400, never 500. The UI guide names each field. |
| IV. Performance | Pass | Goals are in Technical Context. The time checks are V-12, V-13, V-23 and V-24. |
| V. Living docs | Pass | A task updates `docs/architecture.md` and its "Last reviewed" date. A task sets the spec Status and `specs/STATUS.md` (FR-022). |
| VI. STE | Pass | All artifacts are in STE. Item V-28 runs a check on each artifact. |

Post-design re-check: no violation. Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/120-sequence-queue-notifications/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── notification-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Notifications/
│   ├── NotificationLevel.cs
│   ├── NotificationTarget.cs
│   ├── INotificationTargetStore.cs
│   └── FileNotificationTargetStore.cs      # reloads on file change
└── Queues/ExecutionQueue.cs                # new field notificationLevel

src/GameBot.Service/
├── Contracts/Notifications/NotificationContracts.cs
├── Endpoints/NotificationsEndpoints.cs     # targets, test, types
├── Endpoints/QueuesEndpoints.cs            # level route, level in responses, ResetStreaks calls
├── Services/Notifications/                 # message, channel, Telegram, dispatcher, worker, streak state
├── Services/QueueExecution/QueueExecutionService.cs   # RecordRunAsync hook
└── GameBotServiceSetup.cs                  # DI

src/web-ui/src/
├── pages/NotificationsPage.tsx
├── components/notifications/               # TelegramSetupGuide, TargetList, TargetForm, QueueLevelTable
└── services/notifications.ts, queues.ts

tests/unit/Notifications/, tests/contract/, tests/integration/
docs/architecture.md, specs/STATUS.md, CHANGELOG.md
```

**Structure Decision**: Extend the current web-service layout. Domain holds the entities and the
store. Service holds the channel, the dispatcher, the worker and the endpoints. The UI adds one page.

## Streak Ownership (FR-023)

One owner: the worker. No other thread reads or writes the streak state.

| Part | Rule |
|------|------|
| Entry point | `INotificationDispatcher` has two methods. `Enqueue(QueueNotificationJob job)` and `ResetStreaks(string queueId)`. Both write to the one channel and return at once. |
| Channel | One `Channel<NotificationWork>`, unbounded, one reader. The work item is a run job or a reset control message. The dispatcher counts queued run jobs. |
| Job cap | Above 256 queued run jobs, `Enqueue` drops the new job and writes one log line with the queue ID and sequence ID. A reset control message is never dropped. |
| Order | The channel is first in, first out. A reset that the endpoint writes after the level change comes before every later job. |
| Level None | The level route saves the level. Then it calls `ResetStreaks(queueId)`. The worker closes each open streak of that queue at once. No message is sent. |
| Queue delete | The delete route removes the queue. Then it calls `ResetStreaks(queueId)`. The worker removes all keys of that queue. |
| Why order does not matter for a stale job | The worker reads the level and the queue when it handles a job. A job for a queue with level None opens no streak. A job for a deleted queue is dropped. |
| DI | `QueuesEndpoints` takes `INotificationDispatcher` from DI. `QueueExecutionService` takes the same instance. |
| Send | The worker does not wait for a send. It starts one send task for each target. Each send has a 30 s limit that starts when the send starts. The tasks never touch the streak state. |
| Send order | A send waits for the earlier send of the same target, queue and sequence (FR-024). The worker chains the sends in the order of the jobs. Other targets and other pairs do not wait. |
| Send cap | At most 64 messages run at one time. Above this, the worker drops the message and writes one log line. |

## Design Decisions That Cover the Corrected Spec

| Requirement | Design | Where |
|-------------|--------|-------|
| FR-006, FR-012, FR-019 recovered at both levels | The worker sends "recovered" after an open streak at level Failure and at level Success+Failure. Later successes send "success" at Success+Failure only. | research R-003 |
| FR-012 level None closes the streak when the level is set | The level route calls `ResetStreaks`. The worker closes the streak at once. | research R-003, R-010 |
| FR-010, SC-002 parallel send, 30 s limit | One send task for each target. All targets in parallel. One 30 s limit for each send. Each drop is in the log. | research R-002, R-005 |
| FR-024 send order for one pair | Sends of the same target, queue and sequence run in a chain, in the order of the jobs. | research R-002 |
| FR-011 queue stop and host stop | The hook checks the host stop token. A queue stop while the host runs gives "cancelled". A host stop sends no message. | research R-001, R-008 |
| FR-013 target list | The UI has a target list with add, edit and delete. An empty token field on save keeps the stored token. | research R-011 |
| FR-018 reload | The targets store checks the file write time and file length on each read. It reloads when they change. A hand edit of the queue file is out of scope. | research R-006 |
| FR-020 Save and test | UI only. The form saves (POST or PUT) and then calls `POST .../{id}/test`. A save error stops the test. | research R-013 |
| FR-021 level kept | `PUT /api/queues/{id}` loads the stored queue and edits named fields, so the level stays. Duplicate and create set None. Only the level route changes it. | research R-014 |
| FR-022 status update | The final task edits the Status line of the spec and `specs/STATUS.md`. Real-target checks do not block it. | tasks |
| FR-023 one owner | Section "Streak Ownership". | research R-002, R-003, R-010 |
| SC-001, SC-002 time | The quickstart has a timed run. A fake-target fallback is in the PR when no real bot exists. | research R-015 |

## Verification Plan

Each item names the exact check and the pass criterion.

| ID | Requirement | Check | Pass criterion |
|----|-------------|-------|----------------|
| V-01 | FR-001, FR-017 | Read a queue file with no `notificationLevel`. Then restart the store and read a saved level. | Old file reads `none`. Saved level survives. |
| V-02 | FR-005, FR-007 | Integration test: run one queue entry, one nested step and one manual REST run. Count jobs. | 1 job for the entry. 0 jobs for the nested step and for the manual run. |
| V-03 | FR-006, SC-003 | Worker unit test for the state table in research R-003. All 3 levels and 3 results. | Message count equals the table for each case. 100% match. |
| V-04 | FR-012, FR-019, SC-007 | Feed 10 failures then 1 success at level Failure. Repeat at Success+Failure. | Exactly 2 messages each time: "failure", then "recovered". |
| V-05 | FR-012 | Feed success, failure, success, success at Success+Failure. | Messages: "success", "failure", "recovered", "success". |
| V-06 | FR-012 | Feed failure, cancelled, failure, success at level Failure. | Messages: "failure", "cancelled", then "recovered". The second failure sends no message. The cancelled result changes no streak. |
| V-07 | FR-012 | Open a streak at level Failure. Call the level route with None. Read the open streak count at once, with no run. Then set Failure and feed one failure. | After the None call: 0 open streaks and 0 messages. The next failure sends one "failure" message. |
| V-08 | FR-012 | Change level between Failure and Success+Failure with an open streak. Feed a success. | One "recovered" message. |
| V-09 | FR-011 | Stop a queue while the host runs and an entry runs. | One "cancelled" message at levels Failure and Success+Failure. None at level None. The message is not dropped. |
| V-10 | FR-011 | Test `HostStop_SendsNothing`. Set the host stop token, then end an entry. | 0 jobs. The check separates a host stop from a queue stop. |
| V-11 | FR-011 | Watchdog test `Watchdog_TimeLimit_MapsToFailure`. Let the watchdog end an entry. | The job status is `failure`. The statistics record still says `Cancelled`. |
| V-12 | FR-010, SC-002 | Integration test: 3 stuck targets, 1 fast target and a backlog of 50 failure jobs from 50 different queues. | The fast target gets all 50 messages within 30 s of the job time. Each stuck target ends at 30 s with a log line. Worker handles 50 jobs in under 5 s. |
| V-13 | FR-010, SC-005 | Block the worker. Enqueue 300 run jobs. Also use a fake channel that throws or fails. | 256 jobs wait. 44 drops, each with one log line. Run result and run time equal the no-target case. The average hand-off is under 1 ms. |
| V-14 | FR-008, FR-009 | Message text test with names that hold `_`, `*`, `[`, and a absent name. | Text equals `<queue> : <sequence> : <circle> <status>`. No parse mode is sent. The ID replaces a absent name. |
| V-15 | FR-004 | Register a test-only second channel type. | Both targets get the message. The dispatcher has no Telegram type reference. |
| V-16 | FR-023 | Stress test: 8 threads run jobs, level changes to None and queue deletes for 2000 rounds. Repeat the test 50 times. | 0 exceptions. The state class is used by the worker thread only (thread ID check). A deleted queue has 0 streaks at the end. |
| V-17 | FR-013, FR-014 | Jest: render the page with no target. | The guide shows 6 steps and a field name for each value. |
| V-18 | FR-013 | Jest and contract test: list, edit and delete a target. Save an edit with an empty token field. | The list shows the target. Edit and delete work. The stored token does not change after an empty token save. |
| V-19 | FR-015, FR-008 | Contract test on `POST .../test` with a streak open. Read the request body at the fake handler. | The text is `GameBot test message`. No `parse_mode` key. Response has `ok` and `reason`. Streaks and levels stay the same. |
| V-20 | FR-016, SC-006 | Contract test: create a target, then scan all responses, logs and error bodies for the token. | 0 matches. |
| V-21 | FR-018 | Integration test: write a new `targets.json` while the store runs. Read the list again. Also write a corrupt file. | The new list appears with no restart within 2 s. The corrupt file keeps the last good list. |
| V-22 | FR-020 | Jest: press "Save and test" on a new form. | The page calls POST, then the test route, in this order. A POST error skips the test and shows the error. |
| V-23 | FR-021 | Contract tests for full update, template link, game link, entry replace, duplicate, and any import path. Set level Failure before each. | Level is still Failure after each, except duplicate and create, which give None. |
| V-24 | SC-002 | Integration test with a fake Telegram handler: fail an entry, measure to the fake receive time. Repeat 20 times. | 95% of the 20 messages arrive within 30 s. The worst case with 2 attempts is under 30 s. |
| V-25 | SC-001 | Timed quickstart run with a first-time reader and the on-screen guide only. With no real bot, run the same steps with a fake target and record this in the PR. | Test message arrives in under 10 minutes, or the PR records the fake-target run. This item does not block V-26. |
| V-26 | FR-022 | Read the Status line of the spec and the row in `specs/STATUS.md` at the end. | Both say "Implemented". Real-target checks do not block this update. |
| V-27 | Constitution | Run `dotnet test` with `--collect:"XPlat Code Coverage"`. Read the coverage of the notification namespaces. Run `jest --coverage` for the UI files. | 80% line and 70% branch or more for each new module. The task lists the result. |
| V-28 | Constitution | `dotnet build`, `dotnet test`, `vite build`, `jest`. | 0 errors and 0 failed tests. |
| V-29 | Constitution VI | Run the STE check script over each artifact that this feature writes. | 0 sentences over the limits. 0 words that end in "ing" outside names and quotations. |

## Complexity Tracking

No violations. This table stays empty.
