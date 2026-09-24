# Research: Per-Sequence Run Statistics per Queue

**Feature**: 105-sequence-run-statistics | **Date**: 2026-09-24 | **Spec**: [spec.md](./spec.md)

This file records the design decisions for the plan. Each section gives the decision, the reason, and the alternatives that we did not choose. All decisions come from the spec and from the code on 2026-09-24. No item is open.

## R-001 Where the queue records a run

**Decision**: Record each run in `QueueExecutionService.RunOneSequenceAsync` (`src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`). Record the run after the sequence ends, in one private helper. The helper catches and logs all errors, so a failed write never makes the sequence run of the queue fail. The queue records only the runs that it started. A nested run is not possible at this time, and a later nested run is not recorded (spec edge case "Nested sequence run", R-005).

**Rationale**: Each time that the queue starts a sequence, it calls this one method. This is true for all schedule types: at-start, once-per-run, every-step, before-each-run, timer, relative, live, self-reschedule and retry. The method already knows the queue ID, the sequence ID, the watchdog token and the stop token. That is all the data that the status classification needs (R-002).

**Alternatives considered**:
- Record in `SequenceExecutionService.ExecuteCoreAsync`. Rejected: that method also runs ad-hoc runs and dry-runs. It cannot see the watchdog timer apart from the ambient scope. Also, an exception that the queue catches does not get to it as a result.
- Derive the statistics from the execution logs. Rejected: the logs have only `success` and `failure` (a cancelled run is a `failure` with a summary text). Also, the retention policy deletes old logs, and a read of the logs is slow. The spec asks for a replacement of the log-reader script (SC-005).

## R-002 Status classification

**Decision**: Classify the run in the queue, with this table. "Stop token" is the `ct` of `RunOneSequenceAsync`. "Watchdog timer" is the `watchdogTimer` token (the sequence time limit, feature 094).

| How the run ends | Recorded status |
|---|---|
| Any end while the host is in shutdown (`_appStopping` cancelled). This rule applies before all other rows. | not recorded |
| Result `Succeeded` (includes a Break step and a `lastRun` guard that stops the run) | `success` |
| Result `Failed`, the stop token or the watchdog timer is cancelled, and the host is not in shutdown | `cancelled` |
| Result `Failed`, no cancellation | `failure` |
| `OperationCanceledException`, the stop token is cancelled, the host is not in shutdown | `cancelled` |
| `OperationCanceledException`, the stop token is cancelled, the host is in shutdown (`_appStopping`) | not recorded |
| `OperationCanceledException` from the watchdog | `cancelled` |
| Any other exception | `failure` |

**Rationale**: FR-006 says that a stop by hand, a failure-policy stop and the sequence time limit (watchdog) are `cancelled`. It also says that a Break is `success`. The spec edge case says that a run that the service stop interrupts has no end and is not recorded. The host shutdown cancels the same linked token as an operator stop, so `_appStopping.IsCancellationRequested` is the only signal that tells them apart. A failure-policy stop also cancels the run token. It counts as `cancelled`, because the queue stopped the run and the run did not fail by itself.

**Alternatives considered**:
- Use `SequenceExecutionResult.StartedAt`/`EndedAt`. Rejected: a run that a Break step ends returns without a call to `Complete()`, so `EndedAt` stays `default`. The queue takes both times from its `TimeProvider` instead (see R-006).

## R-003 Storage

**Decision**: A new file store `FileSequenceRunStatisticsStore` in `src/GameBot.Domain/Queues/`, behind the interface `ISequenceRunStatisticsStore`. It keeps one JSON file for each queue at `<data root>/queue-sequence-stats/<queueId>.json`. It keeps an in-memory copy of each file that it read. One `SemaphoreSlim` serializes the writes. Each write goes to a temporary file first, and then replaces the file (the same method as `FileQueueRunStateStore`). The store is a singleton in `GameBotServiceSetup.RegisterRepositories`, with the data root that the other repositories get.

**Rationale**: FR-004 needs persistence across a queue restart and a service restart. The queue repository reads every `*.json` file under `queues/` as a queue, so the statistics must not go there. With one file for each queue, the delete of a queue is a delete of one file. It also keeps the tests isolated. Each contract test creates its queue with a new ID, so it writes its own file in the shared bin data directory. The in-memory copy makes the `lastRun` evaluation and the queue read free of disk access after the first read.

**Alternatives considered**:
- Put the statistics in the queue file (`ExecutionQueue`). Rejected: a queue edit (PUT) and a run record would race on the same file. Also, the queue update path would have to keep a field that the API does not write.
- One file for each (queue, sequence) pair. Rejected: sequence IDs are not guaranteed to be safe file names, and the queue read would need a directory scan.
- One file for all queues. Rejected: each record would rewrite the data of all queues, and a damaged file would clear the statistics of all queues.

## R-004 Damaged or absent file

**Decision**: An absent file gives empty statistics. A file that does not parse gives empty statistics and a Warning log with the queue ID and the path. The next record for that queue overwrites the file. The store never throws for a bad file on a read.

**Rationale**: The spec edge case: "The queue starts with empty statistics for that queue and logs a warning. The queue start does not fail." The execution-log outage of 2026-09 showed that one bad file must not break a read path.

**Alternatives considered**:
- Rename the bad file to `*.corrupt` for later analysis. Rejected for now: it adds a second write path and a clean-up problem. The warning log gives the path.

## R-005 How the runner learns the queue

**Decision**: A new ambient context `SequenceRunContext` in `src/GameBot.Domain/Services/`. It holds `QueueId`, `SequenceId` and a `LastRunEvaluator` delegate (`Func<LastRunStepCondition, CancellationToken, Task<bool>>`). It uses `AsyncLocal<T>`, with a `Push(...)` method that returns an `IDisposable`, the same as `SequenceTimeLimitScope` and `AsyncLocalDeviceContextAccessor`.

- `SequenceExecutionService.ExecuteCoreAsync` pushes the context when `parentContext?.OriginatingQueueId` is not empty and `dryRun` is false. The delegate calls `LastRunConditionEvaluator.EvaluateAsync(queueId, sequenceId, condition, ct)`.
- `SequenceStepConditionEvaluator.EvaluateAsync` gets one new optional parameter, `lastRunEvaluator`. For a `lastRun` leaf it calls the delegate. When the delegate is null, the leaf is `false` (FR-012). It does not throw.
- `SequenceRunner` passes `SequenceRunContext.Current?.LastRunEvaluator` at its two calls to the evaluator (the step guard at line ~520 and `EvaluateLoopConditionAsync` at line ~1463). These two calls cover step conditions, If conditions, loop conditions and Break conditions.

**Rationale**: The runner threads `conditionEvaluator` and `stepOutcomes` through more than ten private methods. A new explicit parameter would touch all of them and make the large methods larger. The build-time analyzers degrade on large methods (see memory "GameBot build-time analyzers"). The ambient pattern already exists for the device (feature 079) and for the time limit (feature 094). The static evaluator stays pure and testable: a unit test gives it a delegate directly.

At this time, no step type runs another sequence. Only `QueueExecutionService.RunOneSequenceAsync` and `SequencesEndpoints` call `ExecuteAsync`, so a nested run is not possible. The rule below protects a possible later nested-run feature. A nested run pushes its own context, so `self` always names the sequence that owns the step. The `AsyncLocal` value goes back to the outer value when the nested run ends. The queue does not record the nested run (spec edge case "Nested sequence run"). A unit test simulates the nested case with a second direct `Push` or `ExecuteAsync` call (task T031).

**Alternatives considered**:
- Put the queue ID into the `ParameterScope` built-ins. Rejected: the `queue.*` names are author-visible parameters, and a queue ID there would change the parameter contract.
- Send a `Condition { Source = "lastRun" }` through the current `conditionEvaluator` delegate. Rejected: `Condition` has no fields for status, `since` or `within`, and it mixes two unrelated concepts.
- Add a constructor dependency to the singleton `SequenceRunner`. Rejected: the runner still needs the per-run queue ID and sequence ID, so this does not remove the ambient context. It only adds a second part that can go wrong.

## R-006 Clock and time zone

**Decision**: Use the registered `TimeProvider` everywhere. The queue takes `startedAt` just before `_sequenceExecution.ExecuteAsync` and `endedAt` after it, with `_timeProvider.GetLocalNow()`. The evaluator takes "now" from `GetLocalNow()` and the zone from `TimeProvider.LocalTimeZone`. The store keeps `DateTimeOffset` values with their offset. Window comparisons use `DateTimeOffset`, which compares absolute instants.

**Rationale**: The spec says "local time" is the service-local zone that queue Timer schedules use. The queue code already uses `_timeProvider.GetLocalNow()` for the monitor and the health block. Unit tests can set the time and the zone with `FakeTimeProvider`.

## R-007 The `since` window and daylight-saving time

**Decision**: A pure static function `LastRunWindow.SinceStart(DateTimeOffset nowLocal, TimeOnly since, TimeZoneInfo zone)`:

1. Let `day` be the local date of `now`.
2. Make the wall-clock value `day + since`.
3. If the zone marks that value as invalid (a spring-forward gap), go back one day and try again.
4. If the zone marks the value as ambiguous (a fall-back hour), take the later of its two instants that is at or before `now`. If no instant is at or before `now`, take the earlier one.
5. If the result is after `now`, go back one day and repeat from step 2.
6. Do not go back more than two days. (A real zone cannot need more.)

The window is `[start, now]`, inclusive at both ends. A run matches when its `endedAt` is in the window.

**Rationale**: FR-008 says: "the window starts at the most recent occurrence of that service-local time of day, at or before now". The edge case says: "at the current minute, the window starts now". The spec DST case asks for "the most recent real occurrence of that wall-clock time". Step 3 skips a day where the time does not occur. Step 4 selects the most recent real instant.

## R-008 The `within` format

**Decision**: Parse `within` with a strict pattern, not with `TimeSpan.Parse`: `^(?:(\d{1,3})\.)?(\d{1,4}):([0-5]\d):([0-5]\d)$`, read as `[days.]hours:minutes:seconds`. The hours part may be 24 or more, so `"24:00:00"` is 24 hours. The value must be more than zero and not more than 366 days. The same parser serves validation and evaluation (`LastRunConditionRules.TryParseWithin`).

**Rationale**: The spec example `within: "24:00:00"` must mean 24 hours (User Story 2, scenario 5). `TimeSpan.Parse("24:00:00")` returns 24 days, and `TimeSpan.ParseExact(..., "c")` rejects it. Both are wrong for this contract. The upper limit prevents an overflow when the evaluator subtracts the duration from now. An overflow would be a 500 or a run failure. 366 days is much more than the 100-record history can cover (R-010).

**Alternatives considered**: ISO 8601 durations (`PT24H`). Rejected: FR-007 names `hh:mm:ss` and `d.hh:mm:ss`, and the queue API already uses `hh:mm:ss` (`live-schedule` offset, `timerRelativeOffset`).

## R-009 The `since` format

**Decision**: `since` must match `^([01]\d|2[0-3]):[0-5]\d$` (two-digit `HH:mm`, 00:00 to 23:59). Parse with `TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture)`.

**Rationale**: FR-007 says `HH:mm`. A strict form gives one text form for each value. It also gives a clear 400 for `"9:00"`, `"24:00"` or `"11:00:00"`.

## R-010 History size and count semantics

**Decision**: Keep the 100 most recent run records for each (queue, sequence) pair, oldest first, and drop the oldest when a new record makes 101. Keep the totals (`successCount`, `failureCount`, `cancelledCount`) as separate counters that never drop. The `lastRun` evaluation scans the kept records only.

**Rationale**: The clarification session fixed the limit at 100. An hourly task keeps about four days; a daily task keeps about three months. Separate counters keep the totals correct after records drop (spec Assumptions).

## R-011 Validation location and messages

**Decision**: One rule class `LastRunConditionRules` in `src/GameBot.Domain/Commands/` owns the field rules and the parsers. `CompositeConditionValidator.Walk` gets a `LastRunStepCondition` case that calls the rules and adds each error with the `$`-rooted path. `CompositeConditionValidator.Validate` also walks a `lastRun` leaf at the root of a slot. Today it skips root leaves, because each slot checks `imageVisible` and `commandOutcome` itself.

With that one change, all six slots in `SequenceStepValidationService` validate `lastRun` without six new copies. The six slots are: step condition, Break condition, If condition, loop condition, loop-body Break condition, If-branch Break condition. `FileSequenceRepository.GuardConditionLeaves` also calls the rules and throws on the first error, as a last guard.

Messages (each starts with `Step '<label>' condition at <path>: `):

| Problem | Message tail |
|---|---|
| `sequence` absent or blank | `lastRun condition requires sequence ('self' or a sequence id).` |
| `status` absent or unknown | `lastRun status must be one of success\|failure\|cancelled.` |
| both `since` and `within` | `lastRun condition accepts only one of since or within, not both.` |
| neither | `lastRun condition requires one of since or within.` |
| bad `since` | `lastRun since must be a time of day in HH:mm format (00:00 to 23:59).` |
| bad `within` | `lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format.` |

**Rationale**: FR-011 and SC-003: every bad condition gets a 400 that names the problem, never a 500. The save path turns validation errors into 400. The repository guard throws `InvalidOperationException`, which is a 500, so it must never be the first gate.

**Alternatives considered**: Validate in each of the six slots. Rejected: six copies drift. Feature 103 found exactly this drift for `commandOutcome`.

## R-012 `sequence` value

**Decision**: `sequence` is the literal `self` (ordinal compare) or any non-blank sequence ID. The save path does not check that the ID exists (spec Assumptions). At run time, `self` resolves to `SequenceRunContext.Current.SequenceId`.

## R-013 Status words

**Decision**: `status` accepts `success`, `failure`, `cancelled`, compared without case, and the store and the API write them in lower case. These are the same words that the queue read returns in `lastRunStatus`.

**Rationale**: One term for one thing (Principle VI). Note: `commandOutcome` uses `failed`, and the execution logs use `failure`. The spec chose `failure` for this feature. This is the same word as in the logs.

## R-014 API shape of the statistics

**Decision**: `QueueDetailResponse` gets `sequenceStats`, a JSON object keyed by sequence ID (ordinal sort of the keys). The value is `QueueSequenceStatsResponse`: `sequenceName`, `lastRunStartedAt`, `lastRunEndedAt`, `lastRunStatus`, `lastSuccessAt`, `successCount`, `failureCount`, `cancelledCount`. `sequenceStats` is always present; it is `{}` when the queue has no recorded run. The recent-run history is not in the response. `BuildDetailAsync` fills it for every response that returns a queue detail (GET, PUT entries, PUT template, PUT game). Thus the detail shape is the same on each path. The list read and the monitor read do not change.

**Rationale**: FR-005 says "keyed by sequence ID". An object map lets a script read `sequenceStats["<id>"]` directly. The history stays internal, so the contract stays small (clarification Q2). A detail without this field on some paths would be a trap for a client.

**Alternatives considered**:
- An array of entries with a `sequenceId` field (the `entries` style). Rejected: the spec says keyed, and a map has no duplicate keys by design.
- Return the history as `recentRuns`. Rejected: 100 records for each sequence makes the read large, and no requirement needs it.

## R-015 OpenAPI

**Decision**:
- Condition: add `LastRunConditionContract` to the `JsonDerivedType` list of `SequenceStepConditionContract` (`src/GameBot.Service/Models/SequenceStepContracts.cs`). Add a `GenerateSchema` call and an alias `LastRunCondition` in `ConditionalFlowSchemaDocumentFilter`. Add a new `LastRunConditionSchemaFilter` (ISchemaFilter). It sets these parts:
  - a description for each field,
  - the `status` enum,
  - the `pattern` of `since` and `within`,
  - a schema description of the "exactly one of `since` or `within`" rule and of the "no queue means false" rule.
- Queue: add a new `QueueSequenceStatsSchemaFilter` (ISchemaFilter) for `QueueSequenceStatsResponse` (field descriptions, `lastRunStatus` enum) and for the `sequenceStats` property of `QueueDetailResponse`. Extend the `GET /api/queues/{id}` operation description and `QueueDetailExample()` in `SwaggerConfig.cs`.
- Register both filters in `GameBotServiceSetup` next to `QueueHealthSchemaFilter`.

**Rationale**: FR-013. The service does not feed XML comments to Swagger (memory "Idle pause in health.paused"). Thus descriptions come from schema filters, as in features 094 to 103.

## R-016 Web UI

**Decision**: No web UI change (spec Assumptions). The web UI does not know the `lastRun` type. An author who opens a sequence with a `lastRun` condition in the web UI and saves it can lose the condition. The quickstart and the CHANGELOG entry tell authors to write `lastRun` conditions through the API.

**Rationale**: The spec puts the UI out of scope. Composite conditions (feature 088) shipped with the same limit.
