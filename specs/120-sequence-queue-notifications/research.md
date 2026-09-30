# Research: Queue Sequence Notifications

All items from Technical Context are resolved. No "NEEDS CLARIFICATION" remains.

## R-001: Where does the system detect a finished queue sequence?

- **Decision**: Call the notification hook from `QueueExecutionService.RecordRunAsync`.
- **Rationale**: Feature 105 made this method the single place where each queue entry run ends
  with a known status. Every run kind passes through it: at-start, timer, relative, live and
  self-reschedule. A nested sequence runs inside `SequenceExecutionService` and never reaches it. A
  manual run does not use the queue engine. This satisfies FR-005 and FR-007 with no new detection
  code.
- **Alternatives considered**:
  - Hook in `SequenceExecutionService`: it sees nested runs and manual runs, so it needs filters.
  - Poll the run statistics store: it adds delay and loses the failure and recovery edge.
- **Host stop and queue stop** (FR-011): `RecordRunAsync` returns early when the host stops. The
  hook keeps this rule, but only for the host. The check is the host stop token
  (`IHostApplicationLifetime.ApplicationStopping.IsCancellationRequested`). The task confirms the
  exact field that the early return uses today. A queue stop cancels only the run token. The host
  token stays clear. The hook then gives the status `cancelled` and never drops the job. Two tests
  cover this: `QueueStop_WhileHostRuns_SendsCancelled` and `HostStop_SendsNothing`.
- **Failure status**: it includes an error result, a fault and a absent sequence (statistics
  rule). The watchdog time limit is `Cancelled` in the feature 105 code. The spec says a time limit
  or a watchdog stop is a failure. The hook maps the watchdog case to `failure`. See R-008.

## R-002: How does the send stay off the queue run?

- **Decision**: One `Channel<NotificationWork>` (unbounded, one reader). The work item is a run job
  or a control message (`ResetStreaks`). The dispatcher keeps a count of queued run jobs. A
  `BackgroundService` (the worker) reads the channel.
- **Job cap**: Above 256 queued run jobs, `Enqueue` drops the new job. It writes one log line with
  the queue ID and the sequence ID (FR-010). A control message is never dropped.
- **Send**: The worker does the state logic. Then it starts one send task for the message and does
  not wait for it. The send task sends to all enabled targets in parallel. It has one total limit of
  30 s (`CancelAfter`). A slow or stuck target cannot delay the next job or the other targets. At
  most 64 send tasks run at one time. Above this, the worker drops the message and writes one log
  line.
- **Rationale**: `TryWrite` on an unbounded channel never blocks and never throws. A dead target
  cannot slow the queue (FR-010, SC-005). One reader gives ordered work. The worker is the only
  owner of the streak state, so no lock is needed (FR-023). The send task does not touch the state.
- **Alternatives considered**:
  - `Task.Run` for each event: no order, a race on the streak, and no bound on parallel sends.
  - Wait for the send in the worker: three stuck targets would delay each job by 30 s. This breaks
    SC-002 with a backlog.
  - A bounded channel with drop oldest: it could drop a control message. Rejected.
  - A lock around the state: it works, but two threads would then share the state. One owner is
    easier to test.
- **Entry point**: `INotificationDispatcher.Enqueue(QueueNotificationJob)` and
  `INotificationDispatcher.ResetStreaks(string queueId)`. See R-010.

## R-003: How does the failure streak work?

- **Decision**: In-memory `Dictionary<(queueId, sequenceId), bool>` (open or closed) in the streak
  state class. Only the worker thread touches it (FR-023).
- **Rules** (the worker reads the level when it handles the job):

| Level | Result | Streak open? | Action |
|-------|--------|--------------|--------|
| None | any | any | Send no message. Open no streak. |
| Failure | failure | no | Send "failure". Open the streak. |
| Failure | failure | yes | Send no message. |
| Failure | success | yes | Send "recovered". Close the streak. |
| Failure | success | no | Send no message. |
| Success+Failure | failure | no | Send "failure". Open the streak. |
| Success+Failure | failure | yes | Send no message. |
| Success+Failure | success | yes | Send "recovered". Close the streak. |
| Success+Failure | success | no | Send "success". |
| Failure or Success+Failure | cancelled | any | Send "cancelled". Do not change the streak. |

- **Level None closes the streak at the moment the level is set** (FR-012). The level route saves
  the level. Then it calls `ResetStreaks(queueId)`. The worker closes each open streak of that
  queue at once, with no message. The worker does not wait for the next run. A job for a level None
  queue that is still in the channel changes no state: the worker reads level None and takes no action.
- **Level change between Failure and Success+Failure**: the streak stays open.
- **Rationale**: This gives SC-007 (10 failures then a success give 2 messages). It matches FR-012
  and FR-019. The system sent the "failure" message, so it must send the paired "recovered"
  message at both levels.
- **Alternatives considered**: Persist streaks to disk. Rejected: the spec accepts a loss on
  restart, and a file write for each run adds cost and a failure mode. Close the streak at the next
  job of the pair. Rejected: it conflicts with FR-012, and a quick change None then Failure would
  keep an old streak.
- **Edge**: If the send of the "failure" message fails on every target, the streak still opens. The
  spec does not require a retry of a lost alert. The log holds the send failure (FR-010).

## R-004: How are target types made extendable?

- **Decision**: `INotificationChannel` with `Type`, `DisplayName`, `Fields`, `Validate` and
  `SendAsync(NotificationTarget target, string text, CancellationToken ct)`. The send task gets
  `IEnumerable<INotificationChannel>` from DI and picks by `target.Type`. A target with an unknown
  type is skipped and logged.
- **Rationale**: FR-004 and User Story 5. A new type is one class and one DI line. The `Settings`
  map (`Dictionary<string,string>`) holds type-specific values, so the store and the API do not
  change for a new type.
- **Alternatives considered**: One typed subclass for each target type. It needs a polymorphic JSON
  contract and store changes for each new type. The project has a known pitfall with polymorphic
  JSON, so a flat map is safer.
- **Secret keys**: each channel lists its secret keys (Telegram: `botToken`). The API layer masks
  exactly these keys. No type-specific code lives in the endpoint.

## R-005: How does the Telegram channel send?

- **Decision**: `POST https://api.telegram.org/bot{token}/sendMessage` with a JSON body
  `{ "chat_id": "...", "text": "..." }`. No `parse_mode`. Use the shared `HttpClient` from
  `AddHttpClient`. Timeout 10 s for each attempt. Two attempts with a 1 s pause. A 4xx answer is
  final (no retry).
- **Total limit**: The send task passes a token with a 30 s limit to every channel. The budget of
  21 s (2 attempts of 10 s and a 1 s pause) fits in it. If the limit ends first, the send stops and
  the log has one line (FR-010).
- **Test message**: The text is `GameBot test message`. It has no `parse_mode` (FR-008, FR-015).
- **Rationale**: Plain text cannot break on special characters in names (Edge Cases). Telegram
  cannot color text, so the message uses the emoji circles (FR-009).
- **Reason text**: On a non-2xx answer, read the JSON `description` field and return
  `Telegram answered <status>: <description>`. The description is a fixed Telegram phrase. The token
  is never in it. On a network fault, return `Telegram did not answer: <exception type name>`. Never
  use `ex.Message`, because some messages contain the request URL and so the token.
- **Log rule**: never log the request URI. Log the target ID, the target name and the reason.
- **Alternatives considered**: An unofficial Telegram .NET library. Rejected: a new dependency for
  one HTTP call.
- **Base URL**: a configurable option `Service:Notifications:TelegramBaseUrl` (default
  `https://api.telegram.org`). Tests point it to a fake handler.

## R-006: Where do settings live, and how does the operator set them without the UI?

- **Decision**: `data/notifications/targets.json`, a JSON array of targets, behind
  `INotificationTargetStore`. The REST API and the UI use the store. An operator can also edit the
  file. The store keeps the list in memory. On each read it compares the file write time and the
  file length with the saved values. If they differ, it reloads the file. A hand edit of the
  targets store then applies with no restart (FR-018).
- **Out of scope** (FR-018): a hand edit of the queue file. Only the UI and the API set the queue
  level. The queue file keeps the level, but the operator does not edit it by hand.
- **Reload rule**: A read that finds a corrupt or empty file keeps the last good list and writes one
  log line. It never throws. A store write updates the saved write time, so the store does not
  reload its own write.
- **Rationale**: FR-017, FR-018 and clarification: one store, no second copy. This follows the file
  repository pattern of the project (`FileQueueRepository`).
- **Write safety**: write to a temp file and then replace, to avoid a half-written file on a crash.
  A corrupt file must not give an exception (lesson from the execution-log corrupt-file outage).
- **Alternatives considered**:
  - `appsettings.json` section: the UI could not write it, and the token would sit in a deployment
    file.
  - Add targets to the current config parameter store: it has a different shape and no way to
    hide a secret.
- **Secret at rest**: plain text in the local data folder, the same trust level as the other local
  data. The bot token cannot spend money and the operator can revoke it in Telegram. See R-009.

## R-007: How does the API hide the token?

- **Decision**: The target response has `settings` (public keys only, for Telegram `chatId`),
  `hasSecret` (bool) and `secretHint` (for example `••••Xy9z`, the last 4 characters or fewer). A
  PUT with an empty or absent secret keeps the stored one. A PUT with a new value replaces it.
- **Rationale**: FR-016, FR-013 and SC-006. A contract test scans all responses for the token value.
- **Log and error rule**: the token never appears in a validation error, a log line or a 500 body.

## R-008: What status does each end of a queue run give?

- **Decision**: The hook takes the status that `RecordRunAsync` already computes, with one change
  for the notification only:
  - Success stays success.
  - A fault or a failed result stays failure.
  - A stop request from a queue stop, while the host runs, stays cancelled (FR-011).
  - A stop request from a host stop sends no message (R-001).
  - A watchdog time-limit stop is a failure for notifications (spec Assumptions).
- **Rationale**: the statistics store keeps `Cancelled` for the watchdog case (feature 105). That
  record does not change. The notification passes its own status, so no current test or contract
  changes.
- **Implementation note**: `ClassifyResult` and the two catch blocks already know which case
  applies. Add a small `NotificationStatusFor(SequenceRunStatus status, bool watchdogFired)` helper
  in the engine. Keep `RecordRunAsync` for statistics as it is.
- **Tests**: `Watchdog_TimeLimit_MapsToFailure` (V-11), `QueueStop_WhileHostRuns_SendsCancelled`
  (V-09) and `HostStop_SendsNothing` (V-10).

## R-009: Backup and restore

- **Decision**: Keep `data/notifications/targets.json` out of the author-data backup archive in this
  version. The archive holds author data (games, commands, sequences, images). The token is a
  secret and a backup file is shared more often.
- **Verify in tasks**: check what `BackupService` includes. If it copies the whole data root, add an
  explicit exclusion and a test.

## R-010: Queue delete, level None and rename

- **Decision**: The dispatcher entry point is `ResetStreaks(string queueId)`. It writes a control
  message to the same channel as the run jobs. The worker handles it in order. `QueuesEndpoints`
  gets `INotificationDispatcher` from DI. It calls `ResetStreaks` in two places: after the level
  route saves level None, and after a queue delete. The worker removes every key of that queue.
  A removed key equals a closed streak.
- **Rename**: needs no action. Streaks use IDs (clarification) and names are read at send time.
- **Deleted queue**: a job for a queue that no longer exists is dropped when the worker handles it.
- **Rationale**: FR-023. One owner and one channel remove the race between the cleanup and the
  worker. The endpoint thread never touches the state.
- **Alternatives considered**: A lock around a shared dictionary. It works, but it has two writers.
  Rejected in favor of one owner.
- **Test**: V-16 runs jobs, level changes and deletes from 8 threads and checks that the state class
  is used by the worker thread only.

## R-011: UI structure and quality gate

- **Decision**: New page at `/notifications` with one navigation entry. The page has three parts:
  (1) a numbered Telegram setup guide, (2) a target list with add, edit and delete, a form with
  "Save and test" and a "Send test message" button, (3) a table of all queues with a level select.
  The guide shows the steps: open `@BotFather`, send `/newbot`, copy the token, start a chat with the
  bot (or add it to a group), send one message, open the `getUpdates` URL to find the chat ID, and
  enter both values. Each step names the field that gets the value.
- **Target list** (FR-013): each row shows the name, the type, the enabled state, the chat ID and
  the masked token hint. Each row has "Edit" and "Delete". "Delete" asks for a confirmation. In the
  edit form the token field is empty. An empty token field on save keeps the stored token. A new
  value replaces it.
- **Rationale**: FR-013, FR-014, FR-015. The queue level table sits on the same page so the operator
  needs one place.
- **Quality gate**: the web-ui gate is `vite build` and `jest` (lint and `tsc --noEmit` have known
  old failures). New files must add no new `tsc` error.
- **Alternatives considered**: Put the settings in the current Configuration page. It is a large
  parameter list page, and the guide needs room.

## R-012: OpenAPI and contract tests

- **Decision**: Add the new routes and DTOs to the Swagger output with descriptions. Refresh the
  contract snapshot in `tests/contract/ApiContractSnapshots` and `docs/openapi.json`. Add the
  contract test rule "a bad body gives 400, never 500" for each new endpoint.
- **Rationale**: project pitfall on new inputs (400 not 500) and constitution Principle III.

## R-013: Save and test (FR-020)

- **Decision**: "Save and test" is a UI action. It calls `POST` (new target) or `PUT` (saved
  target). If the save works, it calls `POST /api/notifications/targets/{id}/test`. If the save
  fails, it shows the error and skips the test.
- **Rationale**: One test endpoint on a saved target is enough (clarification). The API gets no new
  route. The operator does not leave the form.
- **Note**: A saved target that fails the test stays saved. The UI says so in the result text.
- **Alternatives considered**: A test endpoint that takes unsaved values. It adds a second code path
  and sends a token in a body that is not stored.

## R-014: The queue level survives other queue changes (FR-021)

- **Decision**: Keep the level as a normal field of `ExecutionQueue`. Only the level route writes
  it.
- **Facts** (checked in `QueuesEndpoints.cs`): `PUT /api/queues/{id}` loads the stored queue and
  changes named fields, so the level stays. The template link, game link and entry routes also load
  the stored queue. `POST {id}/duplicate` and `POST` create build a new queue, so the level is None.
- **Rule**: `UpdateQueueRequest` gets an optional `notificationLevel`. If the field is absent, the
  handler does not touch the level. `CreateQueueRequest` gets the same optional field with the
  default None. A change to None through the update route also calls `ResetStreaks`.
- **Risk**: A future code path that builds a new `ExecutionQueue` and calls `UpdateAsync` would
  reset the level. The plan adds one test for each path (V-23). A grep of `src` found no queue import
  route. A task checks the backup restore path and adds a test if it writes queues.
- **Alternatives considered**: Store the level in a separate file keyed by queue ID. It survives any
  queue rewrite, but it adds a second store and a cleanup rule on queue delete. Rejected.

## R-015: Time check for SC-001 and SC-002

- **Decision**: SC-002 gets an automated test. A fake Telegram handler records the receive time. The
  test fails 20 queue entries and measures the delay from the failure to the receive time. The send
  budget is 21 s and fits in 30 s. A second test uses 3 stuck targets and a backlog of 50 jobs
  (V-12).
- **Decision**: SC-001 gets a timed manual run in the quickstart. A person who has not seen the
  feature follows only the on-screen guide, and the time is recorded.
- **Fallback**: When no real bot or phone exists, the same steps run against a fake target. The team
  records this in the PR. These real-target checks do not block the final Status update (V-25, V-26).
- **Rationale**: The spec assumption says the final quickstart run MUST include a time check.

## R-016: Final status update (FR-022)

- **Decision**: The last task edits the `**Status**:` line in `spec.md` to "Implemented". It then
  changes the row of this feature in `specs/STATUS.md` to "Implemented".
- **Rationale**: Constitution Principle V. The task runs after all tests pass. A absent real-target
  check does not block it.

## R-017: Coverage baseline

- **Decision**: New modules must reach 80% line and 70% branch coverage (constitution Principle II).
  The modules are the `GameBot.Domain.Notifications` namespace, the
  `GameBot.Service.Services.Notifications` namespace, `NotificationsEndpoints` and the UI files under
  `components/notifications`.
- **Check**: run `dotnet test` with `--collect:"XPlat Code Coverage"` and read the Cobertura report
  for these namespaces. Run `jest --coverage` for the UI files. A task records the numbers. If a
  module is below the baseline, the task adds tests for the named uncovered lines. The check is V-27.
- **Alternatives considered**: A coverage gate in CI. Out of scope for this feature.
