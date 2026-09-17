# Research: Distinct reporting of sequence time-limit cancellation

## R1 — How to tell the watchdog from a user stop

- **Decision**: Give the watchdog its own timer `CancellationTokenSource` (`CancelAfter(bound)`), and link it with the
  queue stop token for the token passed down. Time-limit elapsed = `timer.IsCancellationRequested &&
  !stop.IsCancellationRequested`.
- **Rationale**: Today one linked CTS carries both causes, and `RunOneSequenceAsync` can only tell them apart in its
  catch filters. The sequence layer, which writes the log entry, cannot tell them apart at all. A separate timer token
  keeps that information.
- **Alternatives**: Inspect the exception type — both causes raise the same `OperationCanceledException`, so this fails.
  Compare timestamps — this is racy and needs the start time plumbed through.

## R2 — How the sequence layer learns about the bound

- **Decision**: An ambient `SequenceTimeLimitScope` backed by `AsyncLocal`, pushed in `RunOneSequenceAsync` around the
  `ExecuteAsync` call and disposed in the `finally`.
- **Rationale**: The finalize sites build fresh `ExecutionLogContext`s and never see the queue's tokens. An
  ambient value reaches them with no signature changes to `ISequenceExecutionService`,
  `IExecutionLogService` or the command executor (whose fakes are spread across test projects). The repository already
  uses this pattern for the device context (feature 079). Ad-hoc executions never push a scope, so they are never
  stamped.
- **Alternatives**: A new property on the queue's parent `ExecutionLogContext`. The finalize sites never receive that
  context, and a plain value cannot carry the live timer/stop tokens. A new `ExecuteAsync` parameter changes a widely faked interface.

## R3 — Where to stamp

- **Decision**: At both `LogSequenceFinalizeAsync` call sites in `SequenceExecutionService`. The normal end stamps when
  the status is not success. `FinalizeAbandonedAsync` always writes failure. Both stamp only when the scope has elapsed.
- **Rationale**: This covers a firing that unwinds on the cancellation and one where a step swallowed it and the run
  ended as an ordinary failure (clarification Q3). A success is never stamped (clarification Q5).
- **Alternatives**: Stamp after the fact from `QueueExecutionService`. This needs the entry id, which is not returned
  when the firing throws, so the entry would have to be looked up again. More I/O and more fragile.

## R4 — Status vs reason field

- **Decision**: Keep `FinalStatus = "failure"` and add `cancellationReason` + `timeLimitMs`.
- **Rationale**: `NormalizeStatus` collapses everything to success/running/failure. Status filters, the monitor's
  last-outcome and the daily retry all key off non-success. Clarification Q1.
- **Alternatives**: A new `cancelled` status silently changes filters and retry.

## R5 — Effective bound read-out and constants

- **Decision**: `GameBot.Domain.Commands.SequenceTimeLimits` holds `DefaultWatchdogTimeoutMs = 240000`,
  `MaxWatchdogTimeoutMs = 1800000` and `Resolve(int? overrideMs)` (a positive override wins, otherwise the default). The
  queue service and repository validation use it. `GET /api/sequences/{id}` adds `effectiveWatchdogTimeoutMs =
  SequenceTimeLimits.Resolve(sequence.WatchdogTimeoutMs)`.
- **Rationale**: A single source for the number the API publishes and the engine applies, so they cannot drift.
- **Alternatives**: A config-bound default. Out of scope; the spec says no change to the default.

## R6 — Documenting in OpenAPI

- **Decision**: `[Range(1, 1800000)]` on `WatchdogTimeoutMs` in the upsert/patch contracts, which Swashbuckle emits as
  minimum/maximum. A schema filter adds property descriptions (default 240000 ms, maximum, queue-only meaning) on those
  contracts and on the execution-log entry/tree-node DTOs, where it also adds an `enum: [sequence_time_limit]` note to
  `cancellationReason`. The execution-log DTOs are registered in `ConditionalFlowSchemaDocumentFilter` so they appear
  under components. The `GetSequence` operation gets a description that mentions `effectiveWatchdogTimeoutMs`.
- **Rationale**: The service does not include XML comments in Swagger. Existing features (088, 091) document through
  the document filter and operation descriptions, and contract tests read `/swagger/v1/swagger.json`.
- **Alternatives**: Enabling XML-doc inclusion globally. Broad churn in the generated document.
