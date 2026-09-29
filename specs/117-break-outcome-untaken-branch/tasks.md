---

description: "Task list for feature 117: a Break outcome when its If branch did not run"
---

# Tasks: A Break outcome when its If branch did not run

**Input**: Design documents from `/specs/117-break-outcome-untaken-branch/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/command-outcome-break-not-run.md, quickstart.md

**Tests**: This is a bug fix (GitHub issue #250, B-032). Constitution Principle II and the plan (section "Test approach") tell you to write the failing tests first. Thus each user story has test tasks. Write them, and make sure that the "did not run" tests fail before you do the fix (T008).

**Organization**: The tasks are in groups for each user story, so that you can do and test each story independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: You can do the task in parallel with other [P] tasks (different files, no dependencies).
- **[Story]**: The user story of the task (US1, US2, US3).
- Each description has the exact file path.

## Path Conventions

- Domain code: `src/GameBot.Domain/Services/`
- OpenAPI filter: `src/GameBot.Service/Swagger/`
- Unit tests: `tests/unit/Sequences/` (project `C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj`)
- Integration tests: `tests/integration/Sequences/` (project `C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj`)
- Contract tests: `tests/contract/Sequences/` (project `C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj`)
- Use the PowerShell tool with absolute backslash paths. Do not put `cd <dir>;` before a command. Use the Edit and Write tools for UTF-8 files. Write all new comments, OpenAPI text, and docs in ASD-STE100 Simplified Technical English (Constitution Principle VI).

---

## Phase 1: Setup (Baseline)

**Purpose**: Make sure that the build and the current tests are green before you change the code.

- [X] T001 Run the baseline gate and record the result: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Sequences"`, `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~Sequences"`, and `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~Sequences"`. T023 uses the same filters. If a test fails before any change, record it as a pre-existing failure (do not fix unrelated failures in this feature).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the `BreakStepIndex` helper. It does not change the run behavior. The runner call (T008) uses it.

**CRITICAL**: The fix of all user stories uses this helper.

- [X] T002 Add `tests/unit/Sequences/BreakStepIndexTests.cs` (xUnit + FluentAssertions; build `SequenceStep` objects the same way as `tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs`). Add these tests for `BreakStepIndex.CollectBreakStepIds` and `BreakStepIndex.SeedNoBreakOutcomes` (plan "Test approach" item 1):
  - A Break directly in a Loop `Body`, a Break in an If `Body` in a Loop body, and a Break in an If `ElseBody` in a Loop body: each id is in the set.
  - The ids of an Action, a Loop, and an If step are not in the set.
  - A Break with an empty or null `StepId` is not in the set.
  - An id that names a Break and also a root Action is not in the set (FR-005, research R-004).
  - The set is case-insensitive (`Contains("BRK")` is true for `brk`).
  - `SeedNoBreakOutcomes` adds `BreakOutcomes.NoBreak` for each Break id, and does not replace a value that is already in the map (for example `brk` = `break` stays `break`).
  - A null `steps` or null `outcomes` argument throws `ArgumentNullException`.
  The tests do not compile before T003. This is the expected "fail first" state.
- [X] T003 Add `src/GameBot.Domain/Services/BreakStepIndex.cs`: a `public static class BreakStepIndex` with STE XML doc comments (plan "Design", "Change" item 1):
  - `public static IReadOnlySet<string> CollectBreakStepIds(IReadOnlyList<SequenceStep> steps)`: preorder walk of the root steps, of each `Body`, and of each `ElseBody` (null is empty). Collect the `StepId` of each `SequenceStepType.Break` step with a non-empty `StepId`. Also collect the ids of all other steps. Return a `StringComparer.OrdinalIgnoreCase` set of the Break ids that are not also the id of a step of a different type.
  - `public static void SeedNoBreakOutcomes(IDictionary<string, string> outcomes, IReadOnlyList<SequenceStep> steps)`: for each collected id, `outcomes.TryAdd(id, BreakOutcomes.NoBreak)`.
  - Use `ArgumentNullException.ThrowIfNull` for both arguments. Keep the methods small, so that the build-time analyzers stay clean. Then run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~BreakStepIndex"` and make sure that all tests of T002 pass.

**Checkpoint**: The helper is ready and tested. The run behavior is the same as before.

---

## Phase 3: User Story 1 - Read the outcome of a Break whose If branch did not run (Priority: P1) MVP

**Goal**: A `commandOutcome` condition that names a Break in an If branch that did not run evaluates the Break as `no_break` (FR-001, FR-003, FR-008, SC-001).

**Independent Test**: Save the issue #250 reproduction sequence, run it with no If branch taken, and make sure that `fail-no-booking` runs and does not fail with `condition-evaluation-error`.

### Tests for User Story 1 (write first, make sure that they FAIL)

- [X] T004 [P] [US1] Add `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`. Use the `StubRepo` pattern of `tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs`. Each If condition is `commandOutcome` on an earlier Action `probe` with the outcome `success`, so no image evaluator is necessary. Add the "Break in an If branch that did not run" tests (in a count Loop): a later step with `commandOutcome brk no_break` runs; a later step with `commandOutcome brk break` is skipped; a later step with `commandOutcome brk break negate:true` runs. Assert that the run status is `Succeeded` and that no step message contains "is unavailable".
- [X] T005 [US1] In `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs` (same file as T004, so not [P] with T004), add the issue #250 reproduction test in its full form (the same steps as the spec "Reproduction" and `quickstart.md` section 2): a count-3 Loop `book` with the body If `if-empty` { Action `book-reset`, Break `brk-empty` }, If `if-wait` { Action `book-wait`, Break `brk-wait` }, Action `settle`. No branch is taken. After the loop, the Action `fail-no-booking` has `RequireDispatch = true` and the guard `all(commandOutcome brk-empty break negate:true, commandOutcome brk-wait break negate:true)`. The stub dispatcher reports a dispatch for each Action, so `RequireDispatch` does not fail the step. Assert that `fail-no-booking` runs (status `Succeeded`, `ConditionResult` "true"), that `book-reset` and `book-wait` have no step result, and that the run status is `Succeeded`.
- [X] T006 [P] [US1] In `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, add the issue #250 reproduction test through the real API (`GAMEBOT_USE_ADB=false`). Use the helpers `DispatchingStep` and `CommandOutcomeCondition` of this file. The current helpers do not have all the parameters that this test needs: `CommandOutcomeCondition(stepRef, expectedState)` has no `negate` parameter, and `DispatchingStep(stepId, condition)` has no `requireDispatch` parameter. Add an optional parameter `bool negate = false` to `CommandOutcomeCondition` and an optional parameter `bool requireDispatch = false` to `DispatchingStep`, and put each value in the JSON object. Keep the defaults, so that the current tests send the same JSON as before. (Alternative: write these two objects inline in the test.) Use the full steps of `specs/117-break-outcome-untaken-branch/quickstart.md` section 2:
  - `probe`: Action (`DispatchingStep`, `go-to-home-screen`). Its outcome is `success`.
  - `book`: count Loop (`count: 3`, `maxIterations: 3`). Body: If `if-empty` with the condition `commandOutcome probe failed` (false), body: Action `book-reset`, then Break `brk-empty` (no condition). Then If `if-wait` with the condition `commandOutcome probe failed` (false), body: Action `book-wait`, then Break `brk-wait` (no condition). Then Action `settle`.
  - `fail-no-booking`: Action with `requireDispatch: true` and the condition `all(commandOutcome brk-empty break negate:true, commandOutcome brk-wait break negate:true)`. A `go-to-home-screen` step dispatches in stub mode, so `requireDispatch` does not fail it. `POST /api/sequences` with `dryRun: true` gives 200 and `valid: true`. The same request with no `dryRun` gives 201. Execute the sequence. Assert these results (SC-001, FR-008):
  - The run status is `Succeeded`. No step message contains "unavailable".
  - In the execute response, the step with `commandId` `fail-no-booking` has `status` `Succeeded` and `conditionResult` "true" (not "false" and not "error").
  - The execution log agrees: get the run with `GET /api/execution-logs?objectType=sequence&objectId={sequenceId}`, then `GET /api/execution-logs/{id}` (the pattern of `tests/integration/Sequences/DeterministicSequenceOutcomeIntegrationTests.cs`). In `stepOutcomes`, the entry with `stepId` `fail-no-booking` has `conditionTrace.finalResult` true and has no `conditionTrace.failureReason` (null or absent, not `condition-evaluation-error`).
- [X] T007 [US1] Run the tests of T004, T005, and T006. Make sure that they FAIL on the current code with the message "commandOutcome reference '<ref>' is unavailable" (or `condition-evaluation-error`). If a test passes before the fix, change the test until it shows the defect.

### Implementation for User Story 1

- [X] T008 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, in `ExecuteAsync`, directly after the line `var linearStepOutcomes = ...` (about line 126), add `BreakStepIndex.SeedNoBreakOutcomes(linearStepOutcomes, sequence.Steps);` with the STE comment "Feature 117: a Break that does not run reads as no_break. A Break that runs writes its own outcome over this default." Make no other runner change. Do not change the flow-graph path or the legacy blocks path (research R-002), and do not change `SequenceStepConditionEvaluator.cs`.
- [X] T009 [US1] Run the tests of T004, T005, and T006 again. Make sure that they all pass.

**Checkpoint**: User Story 1 works. The MVP fixes issue #250.

---

## Phase 4: User Story 2 - Keep the outcome of a Break that ran (Priority: P1)

**Goal**: A Break that ran keeps the outcome that it recorded last (`break` or `no_break`). The workaround pattern has the same result (FR-002, SC-002, SC-004).

**Independent Test**: Run sequences in which the branch runs and the Break fires, and in which the branch runs and the Break does not fire. The conditions give the same results as before the fix.

### Tests for User Story 2

- [X] T010 [US2] In `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`, add these runner tests (plan "Test approach" item 2):
  - **Break fired**: the branch runs and an unconditional Break fires. `commandOutcome brk break` is true.
  - **Break ran and did not fire**: the branch runs and the Break condition is false. `commandOutcome brk no_break` is true.
  - **Last recorded value stays**: a Loop body in which the If branch runs in iteration 1 only. Use an `imageVisible` If condition and an image evaluator stub that returns true for the first call only. With a Break that does not fire, `commandOutcome brk no_break` is true after the loop. In a variant in which the Break fires in iteration 1, `commandOutcome brk break` is true after the loop.
- [X] T011 [US2] In `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, add the "branch taken and Break fired" test: the reproduction sequence of T006, but the `if-empty` condition is `commandOutcome probe success` (true). Assert that `fail-no-booking` has the status `Skipped` and `conditionResult` "false". (Same file as T006, so not [P]. Edit this file after T006.) Do not change the current test `ReferencingAStepFromTheNotTakenIfBranchStillFailsHard`, and do not change the current tests of the workaround pattern (one Break directly in the loop body, and `commandOutcome brk-booked no_break` after the loop).
- [X] T012 [US2] Run the tests of T010 and T011 on the code from Phase 3. Make sure that they pass. These tests are regression guards: they can pass before the fix too. If a test fails, find the cause before you continue.

**Checkpoint**: User Stories 1 and 2 work. A Break that ran keeps its outcome.

---

## Phase 5: User Story 3 - The save and the run agree (Priority: P2)

**Goal**: A reference that the save accepts does not fail at run time only because the Break did not run (If branch in a Loop body, zero-iteration Loop). The top-level If form is a runner-level guard only, because the save rejects it. An unknown reference and a reference to a step that is not a Break keep their current errors. The OpenAPI description tells the new rule (FR-003, FR-004, FR-005, FR-007, FR-009, SC-003).

**Independent Test**: Save two sequences, with `dryRun` and without it: a Break in an If branch in a Loop body, and a Break directly in a count-0 Loop body. All saves give a valid result. Run the first with the branch not taken, run the second, and make sure that the condition evaluates in both runs. Read the OpenAPI descriptions of `CommandOutcomeCondition` and of its `stepRef`, and find "did not execute" and "no_break".

### Tests for User Story 3

- [X] T013 [US3] In `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`, add these runner tests:
  - **Zero iterations**: a count-0 Loop with a Break in its body. `commandOutcome brk no_break` is true.
  - **Break in an If at the top level** (runner-level guard only; the save rejects this form, so it is not in FR-003 or in a US3 acceptance scenario, research R-005): the branch does not run, and `commandOutcome brk no_break` is true.
  - **While condition reads its own body's Break** (spec Edge Cases, research R-006): a `while` Loop with the bare leaf condition `commandOutcome brk no_break` (the save accepts this, D-006) and a body with an unconditional Break `brk`. Assert that the run status is `Succeeded`, that no step message contains "is unavailable", and that the loop ran one iteration (the Break fired). Before the fix (T008), this run failed with "is unavailable".
  - **Not a Break (FR-005)**: an Action in an If branch that did not run. A later reference to it still fails with "is unavailable".
  - **Unknown reference (FR-004)**: `stepRef` `no-such-step` still fails with "is unavailable".
- [X] T014 [US3] In `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, add these tests. (Same file as T006 and T011, so not [P]. Edit this file after T006 and T011.)
  - **Zero iterations**: a count-0 Loop with a Break `brk`, and a later step with the guard `commandOutcome brk no_break`. Save with `dryRun: true` (valid), save (201), and execute. Assert that the later step has the status `Succeeded`.
  - **Unknown reference stays a save error (FR-004, US3 acceptance scenario 3)**: a sequence with a step whose guard is `commandOutcome no-such-step success`. `POST /api/sequences` with `dryRun: true` gives 400, and the same request with no `dryRun` gives 400. In both responses, `errors` contains "references unknown prior step 'no-such-step'". (A create with `dryRun: true` runs the same validation as a real create, so it also gives 400 and not 200 with `valid: false`. See `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` and `tests/unit/Sequences/ConditionReferenceScopeValidationTests.cs`.)
- [X] T015 [P] [US3] In `tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs`, in the current test `SwaggerDocumentPublishesTheCommandOutcomeReferenceRules` (the schema key is `CommandOutcomeCondition`), add these asserts (FR-009, contract section "OpenAPI description"):
  - The description of `components.schemas.CommandOutcomeCondition.properties.stepRef` contains "did not execute" and "no_break".
  - The schema-level description `components.schemas.CommandOutcomeCondition.description` (`ConditionDescription`) contains "did not execute" and "no_break", and also the phrase "evaluates as no_break" of `BreakNotRunRule`. The current `ConditionDescription` already contains "did not execute" (`RuntimeUnavailableRule`) and "no_break" (`ExpectedStateRule`), so only the phrase "evaluates as no_break" makes this assert fail before T016.
  Keep the current asserts with no change. Run it and make sure that it FAILS before T016 (the `stepRef` description does not contain "did not execute" or "no_break" now, and the schema description does not contain "evaluates as no_break" now).

### Implementation for User Story 3

- [X] T016 [US3] In `src/GameBot.Service/Swagger/ConditionReferenceScopeSchemaFilter.cs`, add the constant `BreakNotRunRule` and change the constant `RuntimeUnavailableRule`. Use the exact STE text of `specs/117-break-outcome-untaken-branch/contracts/command-outcome-break-not-run.md` (section "OpenAPI description"). Set `StepRefDescription` to the current rules + `BreakNotRunRule`. In `ConditionDescription`, put `BreakNotRunRule` after `RuntimeUnavailableRule`. If a current contract assert checks the old `RuntimeUnavailableRule` text, make sure that it still passes (the new text keeps the words "did not execute" and "reference is not available").
- [X] T017 [US3] Run the tests of T013, T014, and T015. Make sure that they all pass.

**Checkpoint**: All user stories work. The save and the run agree for a Break reference.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Living documentation (Constitution V), status updates, and the final gate.

- [X] T018 [P] In `docs/architecture.md`, change the feature 081 paragraph "Widened `commandOutcome` `stepRef` scope" (about line 635) and its runtime rule: a `commandOutcome` reference to a Break that did not run (If branch not taken, or a Loop body with zero iterations) reads as `no_break` (feature 117, issue #250). A Break that ran keeps the outcome that it recorded last. A reference to a step that is not a Break and that did not run still fails. Update the "Last reviewed" line (line 13) to 2026-09-29 with feature 117. Use STE.
- [X] T019 [P] In `CHANGELOG.md`, under `## [Unreleased]` / `### Fixed`, add an STE entry: "A `commandOutcome` condition that names a Break in an If branch that did not run (or in a loop body that ran zero iterations) now evaluates as `no_break`. Before, it failed with `condition-evaluation-error` (feature 117, #250)."
- [X] T020 [P] In `specs/081-loop-exit-reason-and-nested-steprefs/spec.md`, set the Status line to "Implemented (iterated by 117)".
- [X] T021 [P] In `specs/STATUS.md`, set the status of row 081 to "Implemented (iterated by 117)". Add row 117 with the status "Implemented": `| 117 | A Break outcome when its If branch did not run | Implemented |` (issue #250).
- [X] T022 [P] In `specs/117-break-outcome-untaken-branch/spec.md`, set the Status line from "Draft" to "Implemented".
- [X] T023 Run the final validation gate: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with no new analyzer warnings, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Sequences"`, `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~Sequences"`, and `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~Sequences"`. Compare with the baseline of T001: all current tests pass with no change to their expected results (SC-004). Then run the full unit and contract projects with no filter. Then run the format check (Constitution I) on the changed and new C# files only, because a whole-solution run can show pre-existing findings that are not part of this feature (see `scripts/analyze-test-results.ps1` and `docs/validation.md`): `dotnet format whitespace "C:\src\GameBot\GameBot.sln" --verify-no-changes --verbosity minimal --include "C:\src\GameBot\src\GameBot.Domain\Services\BreakStepIndex.cs" "C:\src\GameBot\src\GameBot.Domain\Services\SequenceRunner.cs" "C:\src\GameBot\src\GameBot.Service\Swagger\ConditionReferenceScopeSchemaFilter.cs" "C:\src\GameBot\tests\unit\Sequences\BreakStepIndexTests.cs" "C:\src\GameBot\tests\unit\Sequences\SequenceRunnerUntakenBreakTests.cs" "C:\src\GameBot\tests\integration\Sequences\NestedStepOutcomeReferenceIntegrationTests.cs" "C:\src\GameBot\tests\contract\Sequences\SequencePerStepConditionsOpenApiTests.cs"`. The exit code must be 0. If it is not 0, run the same command without `--verify-no-changes`, examine the diff, and run the check again. The web UI does not change, so its gate does not apply.
- [X] T024 Do section 1 of `specs/117-break-outcome-untaken-branch/quickstart.md` (automated tests) and make sure that the result is as the quickstart states. Sections 2 to 4 (live service) need a deploy; they are not part of this pipeline. T006 is the automated proxy for SC-001.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. BLOCKS all user stories (the runner call needs the helper).
- **User Story 1 (Phase 3)**: Depends on Phase 2. T008 is the fix for all stories.
- **User Story 2 (Phase 4)**: Regression tests. They can be written after Phase 2, but T012 runs after T008.
- **User Story 3 (Phase 5)**: The runner tests (T013, T014) need T008 to pass. T015 and T016 (OpenAPI) are independent of the runner fix.
- **Polish (Phase 6)**: Depends on all user stories.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories. This is the MVP.
- **US2 (P1)**: Uses the fix of US1. It adds tests only.
- **US3 (P2)**: Uses the fix of US1 for the run cases. The OpenAPI change is independent.

### Within Each User Story

- Tests first. Make sure that the "did not run" tests fail (T007, T015) before the fix.
- Helper (T003) before the runner call (T008).
- Tasks that edit the same file are not [P] with each other: T004, T005, T010, and T013 all edit `tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs`; T006, T011, and T014 all edit `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`.

### Parallel Opportunities

- T004 and T006: two different test files. T005 follows T004 (same file).
- T015 and T016 (OpenAPI) in parallel with the runner work of Phase 3 and Phase 4 (different projects).
- T018, T019, T020, T021, T022: five different documentation files.

---

## Parallel Example: User Story 1

```text
# Write the failing tests together (T005 after T004, same file):
Task: "T004 Break in an If branch that did not run in tests/unit/Sequences/SequenceRunnerUntakenBreakTests.cs"
Task: "T006 Issue #250 reproduction through the API in tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs"

# In parallel, the OpenAPI work of US3 (different project):
Task: "T015 stepRef description assert in tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs"
Task: "T016 BreakNotRunRule in src/GameBot.Service/Swagger/ConditionReferenceScopeSchemaFilter.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do Phase 1 (baseline) and Phase 2 (helper and its tests).
2. Do Phase 3: failing tests, then the one runner call.
3. **STOP and VALIDATE**: T009 is green. Issue #250 is fixed.

### Incremental Delivery

1. Setup + Foundational: the helper is ready, the behavior is the same.
2. US1: the fix and its tests (MVP).
3. US2: the regression tests prove FR-002 and the "last recorded value" edge case.
4. US3: the zero-iteration, top-level (runner guard only), `while` own-body, FR-004, and FR-005 tests, and the OpenAPI descriptions (FR-009).
5. Polish: docs, changelog, status rows, spec status, final gate.

---

## Notes

- There is no change to the save validation, `dryRun`, the API shapes, the `expectedState` values, the evaluator, or the execution log (FR-006, FR-007, FR-008). Do not add a field.
- Coverage of the "no change" requirements FR-006 and FR-007: T006 and T014 save sequences with only the current `expectedState` values (`success`, `failed`, `break`, `no_break`), with `dryRun` and without it. T006 makes sure that the save still accepts a reference to a Break in an If branch (`valid: true`, then 201). T014 makes sure that the unknown-reference save error does not change. T023 runs all current Sequences tests (unit, integration, contract) and makes sure that their expected results do not change (SC-004).
- The execution log has no entry for a Break that did not run, as before.
- Do not commit in this pipeline step. The pipeline commits later.
