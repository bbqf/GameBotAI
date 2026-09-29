# Implementation Plan: A Break outcome when its If branch did not run

**Branch**: `117-break-outcome-untaken-branch` | **Date**: 2026-09-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/117-break-outcome-untaken-branch/spec.md` (GitHub issue #250, B-032)

## Summary

A `commandOutcome` condition can name a Break step in an If branch in a Loop body. The save accepts this reference. At run time, when the branch did not run, the Break has no recorded outcome, and the condition fails with `condition-evaluation-error` ("commandOutcome reference '<ref>' is unavailable").

Selected result (spec clarification): at run time, a Break with no recorded outcome evaluates as `no_break`. The fix is in the domain runner. At the start of each run, `SequenceRunner.ExecuteAsync` puts the default `no_break` into the run outcome map for each Break step of the definition. When a Break runs, it writes its own outcome over the default, as it does now. A step that is not a Break gets no default, and an unknown `stepRef` still fails. The save, the evaluator, the API shapes, and the log shapes do not change. The OpenAPI description of `CommandOutcomeCondition.stepRef` tells the new rule.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)
**Primary Dependencies**: domain library (`GameBot.Domain`), ASP.NET Core minimal API host (`GameBot.Service`) with Swashbuckle; no new dependency
**Storage**: N/A. The run outcome map is in memory for one run. No persisted format changes.
**Testing**: xUnit + FluentAssertions. Unit tests in `tests/unit/Sequences`. Integration tests in `tests/integration/Sequences` with `WebApplicationFactory<Program>` and `GAMEBOT_USE_ADB=false`. Contract test in `tests/contract/Sequences`.
**Target Platform**: Windows service (local host), emulator through ADB
**Project Type**: web service (backend only; no web UI change)
**Performance Goals**: No measurable change. One preorder walk of the step tree for each run (a sequence has fewer than 100 steps), before the first step. No I/O.
**Constraints**: No new `expectedState` value (FR-006). No change to save validation or `dryRun` (FR-007). No change for a reference to a step that is not a Break (FR-005). No change to the flow-graph path (research R-002).
**Scale/Scope**: 1 new domain file (about 40 lines), 1 changed line block in `SequenceRunner.cs`, 1 changed Swagger filter, docs and status edits. New unit tests (helper + runner) and new integration tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | A small new static helper with public doc comments. One call in `ExecuteAsync`. No new logic in the large runner methods, so the build-time analyzers stay clean. No dead code. CamelCase method names only. | PASS |
| II. Testing | Bug fix: write the failing tests first. Runner unit tests for the five cases of SC-002 (which include the zero-iteration case) and the `while` own-body case. Helper unit tests. Integration tests through the real API for the issue reproduction. The current tests stay green with no change (SC-004), which includes `ReferencingAStepFromTheNotTakenIfBranchStillFailsHard` (FR-005) and the evaluator test for `CommandOutcomeUnavailable`. | PASS |
| III. UX Consistency | No new API shape. The error message for an unknown reference stays the same. The OpenAPI description tells the new run rule. | PASS |
| IV. Performance | Declared above: one walk of the step tree for each run. Not a hot path. | PASS |
| V. Living Documentation | Change the feature 081 paragraph in `docs/architecture.md` and its "Last reviewed" line. This feature changes a rule of feature 081 (a step that did not run always fails). Set the Status of `specs/081-loop-exit-reason-and-nested-steprefs/spec.md` to "Implemented (iterated by 117)", change row 081 in `specs/STATUS.md`, add row 117, and set the Status of this spec to "Implemented" at the end. Add a "Fixed" entry to `CHANGELOG.md`. | PASS (tasks must include these edits) |
| VI. STE | All new text (plan, research, data model, contract, quickstart, code comments, OpenAPI text, docs, changelog) is in STE. The changed OpenAPI constant `RuntimeUnavailableRule` gets STE text. | PASS |

No violations. Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: The design adds one internal-use public static class in the domain library and changes two OpenAPI description strings. It adds no API field and no persisted field. All gates stay PASS.

## Project Structure

### Documentation (this feature)

```text
specs/117-break-outcome-untaken-branch/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── command-outcome-break-not-run.md   # Run rule, save rule, OpenAPI text
└── tasks.md             # Phase 2 output (/speckit-tasks, not made by this command)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Services/
├── BreakStepIndex.cs                       # ADD: CollectBreakStepIds + SeedNoBreakOutcomes
├── SequenceRunner.cs                       # CHANGE: ExecuteAsync seeds linearStepOutcomes after line 126
└── SequenceStepConditionEvaluator.cs       # NO CHANGE (no code change and no doc comment change)

src/GameBot.Service/Swagger/
└── ConditionReferenceScopeSchemaFilter.cs  # CHANGE: add BreakNotRunRule; STE RuntimeUnavailableRule; add to StepRefDescription and ConditionDescription

tests/
├── unit/Sequences/
│   ├── BreakStepIndexTests.cs              # ADD: walk, nested If in Loop, else branch, empty id, mixed-type id, TryAdd keeps a value
│   └── SequenceRunnerUntakenBreakTests.cs  # ADD: runner cases (see Test approach)
├── integration/Sequences/
│   └── NestedStepOutcomeReferenceIntegrationTests.cs  # ADD: issue #250 reproduction, fired case, zero-iteration case, unknown-reference save
└── contract/Sequences/
    └── SequencePerStepConditionsOpenApiTests.cs       # ADD: stepRef and schema descriptions of CommandOutcomeCondition contain "did not execute" and "no_break"

docs/architecture.md                                   # CHANGE: feature 081 paragraph + Last reviewed
CHANGELOG.md                                           # CHANGE: Fixed entry (117, #250)
specs/081-loop-exit-reason-and-nested-steprefs/spec.md # CHANGE: Status "Implemented (iterated by 117)"
specs/STATUS.md                                        # CHANGE: row 081; ADD row 117
specs/117-break-outcome-untaken-branch/spec.md         # CHANGE: Status "Implemented" at the end
```

**Structure Decision**: The fix is in the domain library (`src/GameBot.Domain`). The service changes only an OpenAPI description. The web UI, the endpoints, and the save validation do not change.

## Design

### Root cause

1. `SequenceRunner.ExecuteAsync` makes `linearStepOutcomes` empty (line 126). A step writes its outcome only when it runs.
2. `ExecuteIfStepAsync` runs only one branch (line 1425). A Break in the other branch writes nothing. A Loop body that runs zero iterations also writes nothing.
3. `SequenceStepConditionEvaluator` (lines 183-192) throws `CommandOutcomeUnavailable` when the key is not in the map. `EvaluateStepGuardAsync` (line 978) makes the "is unavailable" message and fails the run.

The second message at `SequenceRunner.cs` line 319 is only in the legacy flow-graph path. A flow graph has no Break step (`FlowStepType` has `Action`, `Command`, `Condition`, `Terminal`), so it needs no change (research R-002).

### Change

1. **New `src/GameBot.Domain/Services/BreakStepIndex.cs`** (public static class, doc comments in STE):
   - `CollectBreakStepIds(IReadOnlyList<SequenceStep> steps)`: preorder walk of root steps, each `Body`, and each `ElseBody` (null is empty). Collect the `StepId` of each `SequenceStepType.Break` step with a non-empty `StepId`. Also collect the ids of all other steps. Return a case-insensitive set of the Break ids that are not also ids of a step of a different type (research R-004).
   - `SeedNoBreakOutcomes(IDictionary<string, string> outcomes, IReadOnlyList<SequenceStep> steps)`: for each collected id, `outcomes.TryAdd(id, BreakOutcomes.NoBreak)`.
2. **`SequenceRunner.ExecuteAsync`**: after `var linearStepOutcomes = ...` (line 126), call `BreakStepIndex.SeedNoBreakOutcomes(linearStepOutcomes, sequence.Steps);`. Add a short STE comment: "Feature 117: a Break that does not run reads as no_break. A Break that runs writes its own outcome over this default." No other runner change. The flow-graph path and the legacy blocks path do not change.
3. **`ConditionReferenceScopeSchemaFilter`**: add `BreakNotRunRule` and change `RuntimeUnavailableRule` with the exact STE text of `contracts/command-outcome-break-not-run.md`. `StepRefDescription` = current + `BreakNotRunRule`. `ConditionDescription` = current, with `BreakNotRunRule` after `RuntimeUnavailableRule`.
4. **Docs and status**: see Constitution Check, principle V.

Why this approach and not a change in the evaluator: see research R-003. The result is the same, and the diff in the large runner file is one line.

Behaviour that follows from the design:
- A Break that ran in iteration 1 and did not run in iteration 2 keeps the value of iteration 1 (the last write stays).
- A Break in a Loop that ran zero iterations reads `no_break`.
- A `while`/`repeatUntil` leaf condition that reads a Break in its own body reads `no_break` before the first run of the Break (research R-006). Before the fix, it failed with "reference is not available". The spec states this in Edge Cases, and a runner test covers it.
- A Break in an If branch at the top level also reads `no_break` when the branch did not run. The save rejects this form, so this is a runner-level guard only (research R-005).
- The run result and the execution log have no entry for a Break that did not run, as before (FR-008).

### Test approach

Write the tests first. The runner and integration tests for the "did not run" cases must fail before the fix (Constitution II).

1. **Helper unit tests** (`tests/unit/Sequences/BreakStepIndexTests.cs`):
   - A Break directly in a Loop body, and a Break in an If `Body` and in an If `ElseBody` in a Loop body: each id is collected.
   - An Action, a Loop, and an If id are not collected.
   - A Break with an empty `StepId` is not collected.
   - An id that names a Break and a root Action is not collected (FR-005).
   - The set is case-insensitive.
   - `SeedNoBreakOutcomes` does not replace a value that is already in the map.
   - A null argument throws `ArgumentNullException`.
2. **Runner unit tests** (`tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`; `StubRepo` pattern of `SequenceRunnerConditionScopeTests`; If conditions are `commandOutcome` on an earlier Action `probe` with outcome `success`, so no image evaluator is needed):
   - **Break in If branch that did not run** (in a count Loop): a later step with `commandOutcome brk no_break` runs; with `commandOutcome brk break` it is skipped; with `commandOutcome brk break negate:true` it runs. The run status is `Succeeded`, with no "is unavailable" message.
   - **Issue #250 reproduction** (full form of the spec "Reproduction"): two If steps in a count-3 Loop body, each with an Action (`book-reset`, `book-wait`) before its Break, no branch taken, and the `all(... negate:true, ... negate:true)` guard on `fail-no-booking` with `RequireDispatch = true`. The guarded step runs.
   - **Break fired**: the branch runs and an unconditional Break fires. `commandOutcome brk break` is true.
   - **Break ran and did not fire**: the branch runs and the Break condition is false. `commandOutcome brk no_break` is true.
   - **Last recorded value stays**: a Loop body in which the If branch (with a Break that does not fire) runs in iteration 1 only. Use an `imageVisible` If condition and an image evaluator stub that returns true for the first call only. After the loop, `commandOutcome brk no_break` is true. Then a variant in which the Break fires in iteration 1: `commandOutcome brk break` is true.
   - **Zero iterations**: a count-0 Loop with a Break in its body. `commandOutcome brk no_break` is true.
   - **Break in an If at the top level** (runner-level guard only; the save rejects this form, so it is not in FR-003 or in an acceptance scenario, research R-005): the branch does not run, and `commandOutcome brk no_break` is true.
   - **While condition reads its own body's Break** (research R-006, spec Edge Cases): a `while` Loop with the bare leaf condition `commandOutcome brk no_break` and a body with an unconditional Break `brk`. Before the fix, the run fails with "is unavailable". After the fix, the condition reads `no_break` before the first iteration, iteration 1 runs, the Break fires, and the run status is `Succeeded`.
   - **Not a Break (FR-005)**: an Action in an If branch that did not run. A later reference still fails with "is unavailable".
   - **Unknown reference (FR-004)**: `stepRef` `no-such-step` still fails with "is unavailable".
3. **Integration tests** (`tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, current helpers `DispatchingStep` and `CommandOutcomeCondition`):
   - **Issue #250 reproduction** (full form: `book-reset` and `book-wait` Actions before the Breaks, `requireDispatch: true` on `fail-no-booking`): create with `dryRun: true` (expect valid), create (201), execute. The run status is `Succeeded`. `fail-no-booking` has the status `Succeeded` and `conditionResult` "true". No step message contains "unavailable". In the execution log detail, the `fail-no-booking` entry has `conditionTrace.finalResult` true and no `conditionTrace.failureReason` (FR-008).
   - **Branch taken and Break fired**: the same sequence with the `if-empty` condition true. `fail-no-booking` has the status `Skipped` and `conditionResult` "false".
   - **Zero iterations**: a count-0 Loop with a Break, and a later step with `commandOutcome brk no_break`. The step has the status `Succeeded`.
   - **Unknown reference (FR-004)**: a guard with `stepRef` `no-such-step`. The create with `dryRun: true` and the create with no `dryRun` both give 400 with "references unknown prior step 'no-such-step'".
   - `ReferencingAStepFromTheNotTakenIfBranchStillFailsHard` stays with no change (FR-005).
4. **Contract test** (`tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs`, in the current test `SwaggerDocumentPublishesTheCommandOutcomeReferenceRules`; the schema key is `CommandOutcomeCondition`): the `stepRef` description contains "did not execute" and "no_break" (FR-009). The schema-level `description` (`ConditionDescription`) also contains "did not execute", "no_break", and the phrase "evaluates as no_break" (the current text already has the first two words, so only this phrase shows the change). The current asserts stay.
5. Run all tests in `tests/unit/Sequences`, `tests/integration/Sequences`, and `tests/contract/Sequences` with no change to their expected results (SC-004).

Build and test gate: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`, then `dotnet test` for the unit, integration, and contract projects, then `dotnet format whitespace "C:\src\GameBot\GameBot.sln" --verify-no-changes --include <changed and new C# files>` (Constitution I; tasks T023 has the full command). The web UI does not change, so its gate does not apply.

## Complexity Tracking

No violations.
