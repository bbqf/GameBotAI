# Research: A condition on a Loop step is a guard that the runtime obeys

## R-001: Root cause

**Decision**: The defect has two parts. Both must change.

**Finding 1 (save)**: `SequencesEndpoints.MapToLinearSteps` (`src/GameBot.Service/Endpoints/SequencesEndpoints.cs`) makes a domain `SequenceStep` for each step of the request. For a `Loop` step it copies `StepId`, `Label`, `Loop` and `Body` only. It does not copy `Condition`. Thus the service drops the `condition` of a `Loop` step before validation and before storage. The validation sees no condition and returns `{ "valid": true }`.

**Finding 2 (run)**: `SequenceRunner.ExecuteSingleStepAsync` (`src/GameBot.Domain/Services/SequenceRunner.cs`) sends a `Loop` step to `ExecuteLoopStepAsync` before the per-step guard block (`if (step.Condition is not null) { ... }`). Thus the runner does not look at `step.Condition` of a `Loop` step, also when the step has one (for example, from a stored file).

**Finding 3 (log)**: `SequenceExecutionResult.AddLoopStep` has no condition parameters, and `SequenceExecutionService` writes no `conditionType` and no `conditionResult` in the `Loop` log entry. This agrees with the issue: "Loop 'leave-if-stuck' true after 4 iterations" and no condition trace.

**Alternatives considered**: A fault in the evaluator. Rejected: the evaluator is not called for a `Loop` step at all.

## R-002: Skip on false or 400

**Decision**: Skip on false (issue result 1). See the spec, "Selected result and reason".

**Rationale**: The authors use the condition as a guard. The same rule as for an `Action` step is the least surprise. The response contract and the domain model already have the field. The `Break` workaround cannot stop a loop from starting.

**Alternatives considered**: Reject with 400 (issue result 2). Rejected: it removes a capability that authors expect, and it keeps the workaround in each loop body.

## R-003: One guard evaluator for Action and Loop steps

**Decision**: Move the guard block of `ExecuteSingleStepAsync` into a private method `EvaluateStepGuardAsync`. It returns one of three results: run, skip, or fail. It records the `Skipped` or `Failed` step result itself, with a delegate for the record method. The `Action` path calls it and records with `AddStep`, as now. The `Loop` path calls it before `ExecuteLoopStepAsync` and records with `AddLoopStep`.

**Rationale**: One evaluator and one set of messages keep the two guards equal (spec FR-002, FR-005). The `Action` path keeps the same messages, statuses and fields (FR-009).

**Alternatives considered**: Copy the block into the `Loop` path. Rejected: two copies drift apart. Move the `Loop` dispatch after the guard block. Rejected: the guard block records with `AddStep`, and a `Loop` result needs `LoopIterations` so that the execution log shows it as a loop.

## R-004: Result of a skipped or guarded Loop step

**Decision**:

- False guard: `AddLoopStep(stepKey, "Skipped", [], message, conditionType, conditionResult: "false")`, and `stepOutcomes[stepKey] = "skipped"`. The message is `Loop '<stepKey>' skipped: its condition is false.` When a composite decides the result, the message adds ` condition <path> (<description>) settled the guard`, with the same text as for an `Action` step.
- Error: `AddLoopStep(stepKey, "Failed", [], message, conditionType, conditionResult: "error")`, `result.Fail(message)`, `stepOutcomes[stepKey] = "failed"`, and the sequence stops. The messages are the same as for an `Action` step.
- True guard: `ExecuteLoopStepAsync` runs as now. Then the runner sets `ConditionType` and `ConditionResult = "true"` on the `Loop` result entry.

`AddLoopStep` gets two optional parameters, `conditionType` and `conditionResult`. `SequenceExecutionResult` gets a method `SetConditionForLatestLoopStep(stepKey, conditionType, conditionResult)`. It finds the last entry with `LoopIterations` not null and `CommandId == stepKey`, and sets the two fields. `ExecuteLoopStepAsync` always adds the `Loop` entry last (also on early failure), but the search by key is safer than `_steps[^1]`.

**Rationale**: `LoopIterations` not null makes the execution log write the entry as a `Loop` entry (spec FR-006). `"Skipped"` is the status that a skipped `Action` step has.

## R-005: Execution log entry

**Decision**: `SequenceExecutionService` adds `conditionType` and `conditionResult` to the attributes of the `Loop` entry. The values are `null` for a `Loop` step without a guard, as now for other entries without a condition. The message of a skipped `Loop` step comes from the runner (the entry uses `step.Message` when it is set).

**Rationale**: The `If` entry and the `Action` entry have these two attributes already. The execution-log contract has a free `details` attributes map, so the contract type does not change. Check that `ExecutionLogsEndpoints` does not filter the attributes; if it maps known attribute names into typed fields, add the two names there too.

## R-006: Save-time validation of the guard

**Decision**: `SequenceStepValidationService.ValidateLoopStep` calls `ValidateStepCondition(step, stepLabel, ...)` for the `Loop` step itself, with `insideLoop: false`. The method checks `commandOutcome` (reference exists, reference is a prior step in document order, `expectedState` is known), `imageVisible` (`imageId` is set) and composites. Its action checks do nothing, because a `Loop` step has no `Action`.

`SequencesEndpoints.ValidatePerStepImageReferencesAsync` walks `step.Condition` of each top-level step. It thus checks the images of a `Loop` guard with no change. The `minSimilarity` range check in `ValidatePerStepForPersistenceAsync` runs only for steps with an `Action`; move the check before the `Action` test so that it applies to a `Loop` guard too.

**Rationale**: Spec FR-008. A reference to a body step of the same loop is later in document order (the body follows the `Loop` step in the position index), so the "prior step" rule rejects it.

## R-007: Step contract and response

**Finding**: `SequenceStepContract.Condition` exists for all step types. `MapStepToDto` writes `condition = MapPerStepConditionToDto(step.Condition)` for all step types. `FileSequenceRepository` stores `SequenceStep.Condition` with no filter by step type.

**Decision**: Only add `Condition = MapPerStepCondition(step.Condition)` to the `Loop` branch of `MapToLinearSteps`. No other mapping changes. Do not change the `If` branch (spec FR-009, non-goal).

## R-008: OpenAPI and documents

**Decision**:

- `SequenceNestingRulesSchemaFilter` adds a guard rule to the description of the `SequenceStepContract` schema (the `condition` property is a `$ref`, and the OpenAPI writer drops a description beside a `$ref`): a step guard; on an `Action` step, a false guard skips the step; on a `Loop` step, the guard is evaluated one time before the first iteration and a false guard skips the full loop; `If` and `Break` steps use `if.condition` and `breakCondition`.
- `docs/architecture.md`: add one sentence to the **Sequence** item and set a new "Last reviewed" line.
- `CHANGELOG.md`: add an item under `### Fixed` in `[Unreleased]`.
- `specs/STATUS.md`: add row 110.
- `CLAUDE.md`: point the "current plan" line to this plan.

## R-009: Tests

**Decision**:

- Unit (`tests/unit/Sequences/SequenceRunnerLoopGuardTests.cs`, new): false guard skips the body (fails before the fix), composite false guard, true guard runs all iterations and records `true`, no guard records no condition, evaluation error fails the sequence, later `commandOutcome` on the skipped loop reads `skipped`.
- Unit (`tests/unit/Sequences/LoopValidationTests.cs`): a `Loop` guard with an unknown `commandOutcome` reference, a reference to its own body step, and an `imageVisible` leaf without `imageId` give errors; a correct guard gives no error.
- Contract (`tests/contract/Sequences/SequenceLoopGuardContractTests.cs`, new): `POST /api/sequences` keeps the guard on read; `dryRun: true` with an unknown `commandOutcome` reference returns 400 that names the step.
- Contract (`tests/contract/ExecutionLogs/ExecutionLogsLoopGuardContractTests.cs`, new): an executed sequence with a false `lastRun` guard (false in an ad-hoc run) writes a `Loop` entry with `status` `Skipped`, `iterations` 0, `conditionType` and `conditionResult` `false`; with `none(lastRun ...)` (true) the entry has `conditionResult` `true` and 2 iterations.
- Contract (`tests/contract/Sequences/SequenceLoopGuardContractTests.cs`): the description of the step schema has the guard rule.
