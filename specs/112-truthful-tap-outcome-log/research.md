# Research: Truthful tap outcome and an execution log for a single step

## R-001: Where can a false "not executed" outcome come from?

**Result**: In `CommandExecutor.TryDetectAndTap`, the service calls `_sessions.SendInputsAsync(...)` and adds the `executed` outcome only after that. Between these two actions, the code reads back `tapArgs["x1"]` and `tapArgs["y1"]` (the session changes them for the jitter) and the detection confidence, with `Convert.ToInt32` and `Convert.ToDouble`. An exception or a cancellation in this code goes to the `catch` blocks of `ExecuteOneStepAsync`. These blocks return `skipped_detection_failed` / `primitive_tap_exception` or `cancelled` / `cancelled_during_retry_{n}`, with `accepted = 0`. Also, `SendInputsAsync` itself can throw after it gave the input to ADB.

**Decision**: Record the dispatch state in a small object, `TapDispatchState`, that `TryDetectAndTap` fills. The `catch` blocks read it:

- Dispatch not started: the current outcomes, without change.
- Dispatch started, but `SendInputsAsync` did not return: `dispatch_unknown` with `dispatch_error` or `dispatch_cancelled`, `accepted = 0`, and the resolved point.
- `SendInputsAsync` returned: `executed` with `executed_then_error` or `executed_then_cancelled`, `accepted` equal to the returned count, the resolved point, and the executed point and the confidence when known.

The code reads the detection confidence before the dispatch, so less code runs after the dispatch.

**Alternatives considered**:

- Put a `try`/`catch` around the read-back code only. Rejected: it does not cover a cancellation or an error inside `SendInputsAsync`, and it hides the problem from the service log.
- Report `cancelled` with `accepted > 0`. Rejected: the caller reads `cancelled` as "not executed", so the contract stays false.

## R-002: How can a unit test make the code after the dispatch fail?

**Result**: The real session manager changes the tap args in place (the jitter). The existing test `PrimitiveTapExecutedPointReflectsJitteredArgsWhileResolvedPointKeepsTarget` uses a session stub that does the same.

**Decision**: A new session stub puts a value in `x1` whose `IConvertible.ToInt32` throws. One variant throws `InvalidOperationException`. The other variant throws `OperationCanceledException`. The stub returns an accepted count of 1. So the failure occurs in the real read-back code after the dispatch, with no test-only hook in the product code. A third stub throws from `SendInputsAsync` itself, for `dispatch_unknown`. A fourth stub counts the calls to `SendInputsAsync`, for the "no dispatch when detection fails" test.

**Alternatives considered**: A test-only hook in `CommandExecutor`. Rejected: it adds product code for tests only.

## R-003: Which component writes the execution-log entry for a single step call?

**Result**: `StepsEndpoints` makes a 10-second `CancellationTokenSource` and catches `OperationCanceledException when timeoutCts.IsCancellationRequested`. The endpoint does not know the resolved session id when the caller did not send one. `ForceExecuteStepAsync` is called only from `StepsEndpoints` and from two unit tests. `CommandExecutor` is the only implementation of `ICommandExecutor`.

**Decision**: Add an overload `ForceExecuteStepAsync(string? sessionId, CommandStep step, TimeSpan? timeout, CancellationToken ct)`. The existing overload calls it with `timeout: null`. After the session check, the executor makes the timeout token, runs the step and writes exactly one entry in all cases:

- The step returned an outcome: the entry has that outcome. A `PrimitiveTap` step catches a cancellation itself and returns an outcome (`cancelled` before a dispatch). This behavior does not change, so the limit does not give a `timeout` for a `PrimitiveTap` step.
- The timeout ended the step: the entry has the status `timeout`. The executor then throws `TimeoutException("step_execution_timeout")`.
- The caller cancelled the step: the entry has the status `cancelled`. The executor then throws the `OperationCanceledException` again.
- Any other exception: the entry has the status `failed` and the reason `step_exception: <exception type>`. The executor then throws the exception again, so the error handlers of the endpoint do not change.

The endpoint passes `TimeSpan.FromSeconds(10)` and catches `TimeoutException` to return the current timeout body. The response body for a timeout does not change.

**Alternatives considered**:

- The endpoint writes the entry. Rejected: it does not know the resolved session id, and a second writer can give two entries.
- The executor writes the entry, and the endpoint keeps the timeout. Rejected: the executor cannot tell a timeout from a cancellation by the caller.

## R-004: The form of the entry

**Result**: `ExecutionLogEntry` has `ExecutionType`, `ObjectRef`, `Summary`, `Details` and `StepOutcomes`. `GET /api/execution-logs` filters by `fromUtc`, `toUtc`, `objectType` and `objectId`, and returns only root entries. An entry with no parent is a root. `ExecutionNavigationBuilder.Build` accepts any object type. The web UI shows the execution type as text, so `step` shows as "step".

**Decision**: Add `LogStepExecutionAsync(StepExecutionLogRecord record, CancellationToken ct)` to `IExecutionLogService`. It writes one entry: `ExecutionType = "step"`, `ObjectRef = ("step", <sessionId>, "<StepType> step")`, final status `success` for `executed` and `failure` for all other statuses, one `ExecutionStepOutcome`, and one detail item of the kind `step` with the attributes of the spec. The step outcome maps `executed` to `executed`, `dispatch_unknown` to `dispatch_unknown`, `timeout` to `timeout`, and all other statuses to `not_executed`.

**Alternatives considered**: Use `LogCommandExecutionAsync` with a fake command id. Rejected: the entry would show as a command, and a link would go to a command that does not exist.

## R-005: A failure of the log write

**Decision**: The executor calls the log service in a `try`/`catch`. On an exception, it writes a warning with a new `LoggerMessage` and continues. The write uses `CancellationToken.None`, so a timeout or a cancellation of the call does not stop it.

## R-006: Test platform

**Result**: The `PrimitiveTap` detection path runs only on Windows (`OperatingSystem.IsWindows()`), and the unit tests that use it need `System.Drawing` and OpenCV. CI runs on `windows-latest`.

**Decision**: The new `PrimitiveTap` unit tests follow the existing tests in `CommandExecutorPrimitiveTapTests`. The log tests use steps that run on all platforms (`KeyInput`, and `WaitForImage` without an image for the timeout), so they also run on Linux. CI is the final gate.

## R-007: Documentation

**Decision**: Update the Swagger text of `POST /api/steps/execute` in `SwaggerConfig.ApplyStepExamples` (a `Description`). Update `docs/architecture.md` (the primitive action paragraph and the "Last reviewed" line), `CHANGELOG.md`, `specs/STATUS.md` and the spec `Status` line. `specs/openapi.json` has no `steps` path, so it does not change.
