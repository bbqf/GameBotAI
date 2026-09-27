# Tasks: Log each command step under its own step ID (B-020)

**Input**: Design documents from `specs/107-command-step-log-attribution/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required. The spec (FR-010) asks for regression tests. Write the tests before the fix, and make sure that they fail before the fix.

**Format**: `[ID] [P?] [Story] Description`. `[P]` means that the task can run in parallel with other `[P]` tasks (different files, no dependencies).

## Phase 1: Setup

- [x] T001 Read `SequenceRunner.ExecuteSingleStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs` and `SequenceExecutionService.ExecuteAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. Make sure that the root cause in research R-001 is correct.

## Phase 2: Foundational (blocks all stories)

- [x] T002 In `src/GameBot.Domain/Services/SequenceRunner.cs`, add `public string? StepId { get; set; }` with `[System.Text.Json.Serialization.JsonIgnore]` to `SequenceExecutionResult.StepResult`. Add the optional last parameter `string? stepId = null` to `SequenceExecutionResult.AddStep`, and set `StepId` from it (data-model.md).

**Checkpoint**: The solution builds. No behavior change.

## Phase 3: User Story 1 - Find the step that ran a shared command (Priority: P1)

**Goal**: Top-level command steps that share one command each get their own `stepId` in the log.

**Independent Test**: Three top-level steps run the same command. Each command node in the subtree has its own `stepId`.

### Tests for User Story 1

- [x] T003 [P] [US1] Create `tests/unit/Sequences/SequenceRunnerStepIdTests.cs`. Test: three top-level `Action` steps (`a`, `b`, `c`) with the same `CommandId` give three results whose `StepId` values are `a`, `b`, `c` in order. Test: a step whose guard is false gives a `Skipped` result with its own `StepId`. Test: a failed command (the command delegate throws) gives a `Failed` result with its own `StepId`. Test (FR-008): a `SequenceExecutionResult` serialized with `System.Text.Json` has no `stepId` property in its step entries.
- [x] T004 [P] [US1] Create `tests/integration/ExecutionLogs/SharedCommandStepAttributionIntegrationTests.cs`. Test: create one dispatching command (`GoToHomeScreen`) and a session through the API, then a sequence with top-level steps `first`, `second`, `third` that all run this command (primitive action `command`). Run it with `POST /api/sequences/{id}/execute`. Find the sequence execution with `GET /api/execution-logs?objectType=sequence&objectId={id}`. Read `GET /api/execution-logs/{id}/subtree`. Collect all tree nodes whose `message` contains "ran command". Assert that their `deepLink.stepId` values are `first`, `second`, `third` in order, and that each `message` starts with "Step '<stepId>' ran command '".

### Implementation for User Story 1

- [x] T005 [US1] In `ExecuteSingleStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs`, pass `stepId: step.StepId` in each `result.AddStep` call whose first argument is `step.CommandId` (research R-004: condition failure (two calls), guard skip, command exception, dry-run skip, `RequireDispatch` failure, not-dispatched, executed). Do not change other `AddStep` calls.
- [x] T006 [US1] In `ExecuteAsync` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, find the sequence step for each result by `step.StepId` in `sequenceStepsByStepId` first. When the step is found this way, use it for `stepId` and `stepLabel` and do not use the flow step. Else keep the current lookup (flow step, then `sequenceStepsByCommandId`). Add an STE code comment that names issue #221.

**Checkpoint**: T003 and T004 pass.

## Phase 4: User Story 2 - Steps in `If` and `Loop` bodies (Priority: P1)

**Goal**: Command steps in `If` bodies, `If` else branches and `Loop` bodies get their own `stepId` in the log. A step that did not run has no command node.

**Independent Test**: A sequence with `If` steps (true and false conditions), an else branch and a loop, where all leaf steps run one command.

### Tests for User Story 2

- [x] T007 [P] [US2] In `tests/unit/Sequences/SequenceRunnerStepIdTests.cs`, add tests: two `If` steps with bodies `body-false` and `body-true` that share one command, where the first condition is false and the second is true, give exactly one command result, with `StepId` `body-true`. An `If` with a false condition and an `elseBody` step `else-step` gives a result with `StepId` `else-step`. A count loop (2 iterations) with body step `loop-step` gives two results with `StepId` `loop-step`.
- [x] T008 [P] [US2] In `tests/integration/ExecutionLogs/SharedCommandStepAttributionIntegrationTests.cs`, add a test with a sequence that has: top-level `gate`; `If` `if-false` (condition `commandOutcome` `gate` `failed`) with body `body-false`; `If` `if-true` (condition `commandOutcome` `gate` `success`) with body `body-true`; `If` `if-else` (condition `commandOutcome` `gate` `failed`) with an empty body and `elseBody` `else-step`; a count `Loop` `loop-1` (count 2) with body `loop-step`. All leaf steps run the same command. Assert that the command nodes have `deepLink.stepId` `gate`, `body-true`, `else-step`, `loop-step`, `loop-step` in that order, that no command node has `body-false`, and that the `If` and `Loop` nodes keep their own `stepId` values.

### Implementation for User Story 2

- [x] T009 [US2] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, make `FlattenSequenceSteps` also go into `ElseBody` (after `Body`), at all levels (research R-005).

**Checkpoint**: T007 and T008 pass. T003 and T004 still pass.

## Phase 5: Polish and cross-cutting concerns

- [x] T010 [P] Add a "Fixed" entry for B-020 (#221) under `## [Unreleased]` in `CHANGELOG.md`, in STE.
- [x] T011 [P] Add row `| 107 | Log each command step under its own step ID | Implemented |` to the table in `specs/STATUS.md`, and set `**Status**: Implemented` in `specs/107-command-step-log-attribution/spec.md`.
- [x] T012 Build with `dotnet build GameBot.sln -c Release -warnaserror`, and run the unit and integration tests of the affected areas (`Sequences`, `ExecutionLogs`). When the local SDK is not available, rely on CI.

## Dependencies and execution order

- T001 → T002 → (T003, T004 in parallel) → T005 → T006 → (T007, T008 in parallel) → T009 → (T010, T011 in parallel) → T012.
- User Story 2 uses the runner change of User Story 1 (T005). T009 is the only change specific to User Story 2.

## Implementation strategy

MVP is User Story 1 (T001 to T006). User Story 2 adds the `elseBody` lookup and the nested tests. Both stories are P1 and go in the same change.
