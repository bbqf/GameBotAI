# Research: A Break outcome when its If branch did not run

**Feature**: 117-break-outcome-untaken-branch | **Date**: 2026-09-29 | **Issue**: #250 (B-032)

## R-001: Where the run fails

**Finding**: `SequenceStepConditionEvaluator.EvaluateLeafAsync` (`src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`, the `CommandOutcomeStepCondition` case, lines 183-192) throws `ConditionEvaluationException(CommandOutcomeUnavailable)` when `stepOutcomes` has no key for `stepRef`. `SequenceRunner.EvaluateStepGuardAsync` (`SequenceRunner.cs` line 978) changes this exception into the message "Step '<step>' commandOutcome reference '<ref>' is unavailable". The step then fails and the run stops.

**How the runner fills `stepOutcomes`**: `SequenceRunner.ExecuteAsync` makes one dictionary, `linearStepOutcomes` (case-insensitive), for each run (line 126). The runner gives the same dictionary to each method: `ExecuteSingleStepAsync`, the three loop methods, `ExecuteIfStepAsync`, and `ExecuteLoopBodyAsync`. A step writes its outcome only when the runner gets to that step. `ExecuteLoopBodyAsync` writes `break` or `no_break` for a Break step with the key `StepId` (or `"break"` when `StepId` is empty). `ExecuteIfStepAsync` runs only one branch. Thus a Break in the branch that did not run has no key. A Break in a Loop body that ran zero iterations also has no key.

**Recording in a Loop**: each iteration writes into the same dictionary. The last write stays. An iteration in which the Break does not run writes nothing, so the value of an earlier iteration stays. This agrees with the clarification about "the outcome that the Break recorded last" with no new code.

## R-002: The second failure path at `SequenceRunner.cs` line 319

**Finding**: `EvaluateFlowOperandAsync` (line 310-345) throws "Command outcome '<ref>' is unavailable." This method is only for the legacy flow graph (`CommandSequence.FlowSteps`, `EntryStepId`). `FlowStepType` has only `Action`, `Command`, `Condition`, and `Terminal` (`src/GameBot.Domain/Commands/SequenceFlowGraph.cs`). A flow graph has no Break step, so it has no Break outcome.

**Decision**: Do not change the flow-graph path.

**Rationale**: The defect cannot occur there. A change there would change behaviour for non-Break references (FR-005).

## R-003: How the run knows that a `stepRef` names a Break

**Finding**: The runner has the full definition (`CommandSequence.Steps`) at the start of `ExecuteAsync`. `SequenceStep` has `StepType`, `StepId`, `Body` (Loop body and If then-branch), and `ElseBody` (If else-branch). `SequenceStepValidationService.BuildStepPositionIndex` already walks the same tree in preorder (root steps, then each `Loop.Body`, each `If.Body` and `If.ElseBody`, recursively).

**Decision**: Add a small static helper in the domain library. It walks the step tree in the same preorder and collects the `StepId` of each Break step. Before the first step runs, `ExecuteAsync` puts `no_break` into `linearStepOutcomes` for each collected id (`TryAdd`). When a Break runs, it writes its own outcome over the default, as it does now.

**Rationale**:
- The evaluator, the error messages, and the ~12 runner methods that pass `stepOutcomes` do not change. The change is one call in `ExecuteAsync` plus one new file of about 40 lines. This agrees with the rule "keep big files and methods small" (build-time analyzers).
- The semantics are exact: FR-001 (no recorded outcome gives `no_break`), FR-002 (a recorded outcome replaces the default), and the Loop clarification (the last recorded value stays).
- A `stepRef` that names no step gets no default, so it still fails (FR-004).
- The dictionary is local to one run. The run result does not publish it, so the default does not appear in an API response or a log as a "recorded" outcome.

**Alternatives considered**:
1. *Add a "known Break ids" parameter to `SequenceStepConditionEvaluator.EvaluateAsync` and use it as a fallback.* Rejected: each runner method that passes `stepOutcomes` (about 12) must also pass the new set. This makes a large diff in a file that already has analyzer risk. The observable result is the same.
2. *Wrap `stepOutcomes` in a new read-only view with a fallback.* Rejected: the runner writes into `Dictionary<string, string>` in about 40 places. A new type makes a large change for no gain.
3. *Reject the reference on save.* Rejected by the spec clarification: it breaks sequences that authors already saved, and `dryRun` already says that the pattern is valid.
4. *Give a default to each step type (for example `skipped` for an Action).* Rejected by the spec (FR-005): this is a separate decision.

## R-004: A step id that names a Break and a step of a different type

**Finding**: The save rejects a duplicate `StepId` in the same list (root, one Loop body, one If branch). It does not look at duplicates across lists. Thus a root Action and a Break in a Loop body can have the same `StepId`.

**Decision**: The helper gives the default only to an id that names Break steps and no step of a different type. When an id also names a step of a different type, the helper does not give a default for it, and the behaviour stays as it is now.

**Rationale**: FR-005. A reference to a step that is not a Break must not change.

## R-005: Where a Break can be

**Finding**: The save accepts a Break only in a Loop body, directly or in an If branch in a Loop body (`SequenceStepValidationService`, lines 53 and 303). A Loop body and an If branch cannot contain a Loop. An If branch cannot contain an If.

**Consequence for the spec and tests**: A Break in an If branch that is not in a Loop body cannot come through the API, because the save rejects it. Thus FR-003 and User Story 3 name only the two forms that the save accepts (see below). The runner does not validate, so a unit test at runner level covers the top-level If form as a guard only. The acceptance scenarios and the integration tests use the valid API forms:
- a Break in an If branch in a Loop body, branch not taken (the reproduction of issue #250);
- a Break directly in a Loop body that ran zero iterations (count 0). This is a second form of "the Break did not run".

## R-006: Other positions that read `stepOutcomes`

**Finding**: The same dictionary also goes to the If condition, the Break condition, and the `while`/`repeatUntil` loop condition (`EvaluateResolvedConditionAsync`, `EvaluateLoopConditionAsync`).

**Decision**: The default applies in each position. This is correct: FR-001 does not name one position. The D-006 exception is about save validation only, and the save does not change (FR-007).

**Consequence**: A `while` loop whose own leaf condition reads a Break in its own body (allowed by D-006) now reads `no_break` before the first iteration. Before, the loop failed with "reference is not available". This is the FR-001 behaviour and it is not a regression. The save accepts this form, because a bare leaf in a `while`/`repeatUntil` slot is not reference-checked (D-006, `SequenceStepValidationService` lines 128-142). The spec states this in Edge Cases, and a runner unit test (tasks T013) covers it.

## R-007: Trace and log

**Finding**: The runner reports a guard with `conditionType` and `conditionResult` (`true`/`false`/`error`). The value comes from `ConditionEvaluation.Value`.

**Decision**: No trace change. A guard that reads a default `no_break` reports `conditionResult` `true` or `false`, as for a recorded outcome (FR-008). No marker is added (clarification: optional, not required).

## R-008: Documentation

**Finding**: `ConditionReferenceScopeSchemaFilter` (`src/GameBot.Service/Swagger/ConditionReferenceScopeSchemaFilter.cs`) sets the OpenAPI descriptions. `RuntimeUnavailableRule` says that a reference to a step that did not run always fails. `StepRefDescription` does not name the Break case. `docs/architecture.md` (lines 643-647, feature 081 paragraph) says the same as `RuntimeUnavailableRule`.

**Decision**:
- Add the constant `BreakNotRunRule` (exact STE text in `contracts/command-outcome-break-not-run.md`). Add it to `StepRefDescription` and to `ConditionDescription`.
- Change `RuntimeUnavailableRule` so that it names the Break exception (exact STE text in the same contract).
- Change the feature 081 paragraph in `docs/architecture.md` and its "Last reviewed" line.
- Add a "Fixed" entry to `CHANGELOG.md`.
- Feature 117 changes a rule of feature 081 (FR-011 of 081: "a step that did not execute still fails"). Set the Status of `specs/081-loop-exit-reason-and-nested-steprefs/spec.md` to "Implemented (iterated by 117)" and change its row in `specs/STATUS.md`. Add row 117.

## R-009: Test tools

**Finding**:
- Runner unit tests use a `StubRepo : ISequenceRepository` and `new SequenceRunner(repo)` (example: `tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs`). A `commandOutcome` condition on an earlier Action step gives a deterministic If result, with no image evaluator.
- `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs` uses `WebApplicationFactory<Program>`, `GAMEBOT_USE_ADB=false`, a `go-to-home-screen` primitive step as a deterministic `success`, and `POST /api/sequences/{id}/execute`. Its test `ReferencingAStepFromTheNotTakenIfBranchStillFailsHard` covers FR-005 (an Action in a branch that did not run still fails). It must stay green with no change.
- `tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs` asserts parts of the `stepRef` description in the test `SwaggerDocumentPublishesTheCommandOutcomeReferenceRules`. It reads the schema with the key `CommandOutcomeCondition` (not `CommandOutcomeConditionContract`). The schema-level `description` of that schema is `ConditionDescription`.
- `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs` asserts that the evaluator throws `CommandOutcomeUnavailable` for a missing key. The evaluator does not change, so this test stays green.

**Decision**: Add the new integration tests to `NestedStepOutcomeReferenceIntegrationTests.cs` (same helpers). Add the new runner unit tests in a new file `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`, and helper unit tests in `tests/unit/Sequences/BreakStepIndexTests.cs`.
