# Research: Log each command step under its own step ID (B-020)

## R-001: Root cause

**Finding**: `SequenceExecutionService.ExecuteAsync` builds `sequenceStepsByCommandId` with `GroupBy(step => step.CommandId)` and `group.First()`. For each run result, it calls `sequenceStepsByCommandId.TryGetValue(step.CommandId, ...)`. When several steps use the same command, every result finds the first step. The node then gets `stepId`, `stepLabel` and the message name from that first step.

**Why the runner result does not help today**: For the command path, `ExecuteSingleStepAsync` calls `result.AddStep(step.CommandId, ...)`. The result has only the command ID, not the step ID.

**Why `If` and `Loop` nodes are correct**: The runner records them with `stepKey`, which is the `stepId`. The service finds them through `sequenceStepsByStepId`.

## R-002: Where to carry the step ID

**Decision**: Add `StepId` to `SequenceExecutionResult.StepResult`, and an optional `stepId` parameter to `AddStep`. `ExecuteSingleStepAsync` passes `step.StepId` in each `AddStep` call whose first argument is `step.CommandId`.

**Rationale**: All leaf steps (top level, `If` bodies, `Loop` bodies, at all nesting levels) go through `ExecuteSingleStepAsync`. One change there covers all positions.

**Alternatives considered**:
- Put the step ID in `CommandId`. Rejected: the service uses `CommandId` to get the command name, and the sequence summary lists command IDs.
- Match results to steps by position in the tree. Rejected: skipped `If` bodies and loop iterations make the position hard to compute, and the result would be fragile.

## R-003: JSON shape of the run result

**Finding**: `POST /api/sequences/{id}/execute` returns `Results.Ok(res)`. Thus each public property of `StepResult` goes into the JSON response.

**Decision**: Mark `StepResult.StepId` with `[System.Text.Json.Serialization.JsonIgnore]`.

**Rationale**: Spec FR-008 forbids a change to the API shape.

## R-004: Which `AddStep` calls get the step ID

**Decision**: Only the calls in `ExecuteSingleStepAsync` whose first argument is `step.CommandId`: condition failure (two calls), guard skip, command exception, dry-run skip, `RequireDispatch` failure, not-dispatched, executed.

**Rationale**: The other calls already use `stepKey` as the command ID. The service already finds those steps by step ID, or they are wait-for-image, service-level and primitive actions whose log output must not change (FR-007).

## R-005: `elseBody` steps

**Finding**: `SequenceExecutionService.FlattenSequenceSteps` goes into `Body` only. `SequencesEndpoints.FlattenSequenceSteps` and `QueuesEndpoints.FlattenSequenceSteps` also go into `ElseBody`.

**Decision**: Make the service helper also go into `ElseBody`.

**Rationale**: Spec FR-003. Without it, an else-branch step is not in `sequenceStepsByStepId`, and the fallback finds the wrong step.

**Side effect check**: `sequenceStepsByStepId` and `sequenceStepsByCommandId` get more entries. The `GroupBy(...).First()` keeps the first step in pre-order, and `Body` comes before `ElseBody`. Thus the result for the old keys stays the same, except that a command used only in an `elseBody` now resolves to its step. This is a correction.

## R-006: Test approach

**Decision**:
- Unit test (`tests/unit/Sequences/SequenceRunnerStepIdTests.cs`): run `SequenceRunner` with a sequence where several steps share a command, and check that each command step result has the correct `StepId`, and that a skipped `If` body gives no result.
- Integration test (`tests/integration/ExecutionLogs/SharedCommandStepAttributionIntegrationTests.cs`): create commands, a session and a sequence through the API, run it, read `GET /api/execution-logs/{id}/subtree`, and check `deepLink.stepId` and `message` of each command node.
