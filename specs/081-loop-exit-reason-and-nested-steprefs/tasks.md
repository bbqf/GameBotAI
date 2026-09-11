---

description: "Task list for Loop Exit Reason & Nested Step-Outcome References"
---

# Tasks: Loop Exit Reason & Nested Step-Outcome References

**Input**: Design documents from `/specs/081-loop-exit-reason-and-nested-steprefs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/loop-exit-reason-and-stepref-scope.md, quickstart.md

**Tests**: Included — Constitution Principle II requires tests for all executable
logic and a failing-test-first approach for the bug-adjacent behavior this feature
fixes as necessary plumbing (Break outcomes never being recorded for lookup).

**Organization**: Tasks are grouped by user story (US1 = Loop exit reason, US2 =
cross-scope `Break`-outcome reference, US3 = cross-scope non-`Break` reference).
US1 is fully independent (touches only `SequenceRunner`'s loop-completion sites).
US2 and US3 share the same whole-tree `stepRef` resolution mechanism in
`SequenceStepValidationService`; US2 (P1) builds it plus the `Break`-outcome
recording it needs, and US3 (P2) is additional test coverage confirming the same
mechanism already generalizes to non-`Break` references with no further code.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

No new project, dependency, or tooling setup is required — this feature adds no
new projects and no new third-party dependencies (plan.md Technical Context).

## Phase 2: Foundational

None strictly blocking. US1 and US2 touch different methods in
`SequenceRunner.cs` and can proceed independently or in parallel; US3 depends only
on US2's validation mechanism, not on new foundational work of its own.

---

## Phase 3: User Story 1 - A Loop's result reveals why it stopped (Priority: P1) 🎯 MVP

**Goal**: Every `Loop` step's execution result includes a structured
`exitReason` (`brokeVia` / `exhaustedMaxIterations`) populated correctly for all
three loop kinds, including when the firing `Break` is nested inside an `If`
within the loop body.

**Independent Test**: Run a `Loop(maxIterations: 3)` with a conditional `Break`
once with input that fires the break and once that exhausts iterations; confirm
`exitReason` correctly distinguishes the two runs, with no dependency on any
`stepRef`/condition change.

### Tests for User Story 1 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T001 [P] [US1] Add failing unit test(s) in `tests/unit/Sequences/SequenceRunnerLoopTests.cs` asserting a count `Loop`'s `StepResult.ExitReason.BrokeVia` equals the firing `Break` step's `StepId` when a conditional break fires, and is `null` with `ExhaustedMaxIterations: true` when the loop exhausts `MaxIterations` without any break firing (cover both `ExitOnMaxIterations: true` and `ExitOnMaxIterations: false`, since spec FR-004 requires `ExhaustedMaxIterations` to be `true` in both cases even though overall status differs)
- [X] T002 [P] [US1] Add failing unit test(s) in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`'s existing "T018: While loop" section asserting the same `exitReason` semantics for a `while` loop: break-fired case, condition-became-false-normally case (`brokeVia: null`, `exhaustedMaxIterations: false`), and max-iterations-exhausted case — NOT `SequenceRunnerWhileBreakOnTests.cs`, which covers the unrelated legacy block-based `while`/`breakOn` mechanism
- [X] T003 [P] [US1] Add failing unit test(s) covering a `repeat-until` loop's equivalent three cases (break-fired / normal-condition-true completion / exhausted) in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`'s existing "T020: Repeat-until loop" section, alongside `RepeatUntilLoopConditionNeverTrueFailsAtLimit`/`RepeatUntilLoopExitOnMaxIterationsGivesUpWithoutFailingTheSequence` — NOT `tests/unit/Sequences/BlocksLoopTests.cs`, which tests an unrelated legacy JSON-block execution path (`SequenceRunner.Blocks`/`BlockResult`), not the `SequenceStep`/`RepeatUntilLoopConfig` model this feature changes
- [X] T004 [P] [US1] Add failing unit test in `tests/unit/Sequences/SequenceRunnerIfBodyScopeTests.cs` (or `SequenceRunnerIfTests.cs`) asserting that when the firing `Break` is nested inside an `If` body within the `Loop` body, `exitReason.brokeVia` is the `Break` step's own `StepId`, not the enclosing `If` step's id (spec FR-003)
- [X] T005 [P] [US1] Add a regression test confirming a `Loop` with a body containing no `Break` step at all, completing normally under its max iterations, reports `exitReason: { brokeVia: null, exhaustedMaxIterations: false }` (spec FR-005) — covers the "neither" case
- [X] T006 [P] [US1] Add a regression test confirming every pre-existing assertion on `Loop` `StepResult.Status`/`Message`/`LoopIterations` in `SequenceRunnerLoopTests.cs` and sibling files is unaffected by the new field (spec FR-012) — run the full existing suite for these files and confirm no existing assertion needs to change

### Implementation for User Story 1

- [X] T007 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, add a `LoopExitReason` record (`BrokeVia: string?`, `ExhaustedMaxIterations: bool`) and an `ExitReason` property on `StepResult`, alongside the existing `LoopIterations` property
- [X] T008 [US1] In the same file, extend `SequenceExecutionResult.AddLoopStep(...)` to accept and store the new `LoopExitReason` (additive overload or additional parameter — do not change any existing call site's other arguments)
- [X] T009 [US1] In the same file, change `ExecuteLoopBodyAsync`'s return tuple to also carry `string? BrokeVia` (the firing `Break` step's own `StepId`, captured at both the unconditional-break and conditional-break-fired call sites, ~lines 1266 and 1297) and thread it through `ExecuteIfStepAsync`'s existing `BreakTriggered` return path so a break fired inside a nested `If` still surfaces its own id
- [X] T010 [US1] In `ExecuteCountLoopAsync`, `ExecuteWhileLoopAsync`, and `ExecuteRepeatUntilLoopAsync`, compute and pass `LoopExitReason` at each existing `AddLoopStep` call site using the break-fired/exhausted/neither fact each site already branches on (per research.md R-001/R-002) — `ExhaustedMaxIterations` MUST be computed from "ran full configured MaxIterations without a break firing," independent of the `ExitOnMaxIterations` flag or resulting status string

**Checkpoint**: Run T001-T006 — all must now pass. User Story 1 is independently
complete and testable; `exitReason` is visible in `/api/sequences/{id}/execute`
responses with no dependency on Part B.

---

## Phase 4: User Story 2 - A condition can ask "did that Break fire?" from anywhere in the sequence (Priority: P1)

**Goal**: A `commandOutcome` condition can reference a `Break` step nested inside
any `Loop`/`If` body elsewhere in the sequence (not just its own immediate
sibling list) and correctly resolve `break`/`no_break`, while a non-prior
reference is still rejected at creation time.

**Independent Test**: Author a sequence with a `Break` nested in one `Loop` body
and a separate top-level `If` gating on `commandOutcome { stepRef: <break-id>,
expectedState: "break" }`; confirm creation succeeds and the condition correctly
tracks whether the break fired across two runs.

### Tests for User Story 2 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T011 [P] [US2] Add failing unit test in `tests/unit/Sequences/LoopValidationTests.cs` (or a new `SequenceStepValidationServiceNestedRefTests.cs` alongside it) asserting `SequenceStepValidationService.Validate` accepts a `commandOutcome` condition on a top-level `If` step whose `stepRef` names a `Break` step nested inside a **different**, earlier top-level `Loop`'s body (today rejected as "references unknown prior step")
- [X] T012 [P] [US2] Add a failing unit test in the same file asserting `expectedState: "break"` and `expectedState: "no_break"` are now accepted (not rejected as "must be one of success|failed|skipped") on a `commandOutcome` condition
- [X] T013 [P] [US2] Add a regression test in the same file confirming a `stepRef` naming a step that appears **later** in the sequence's authored structure than the referencing condition is still rejected with the existing "must reference a prior step" error, now using whole-sequence order (spec FR-007) — construct this so the referenced step is reachable (exists somewhere in the tree) but structurally after the referencer, to prove ordering — not just reachability — is still enforced
- [X] T014 [P] [US2] Add a failing integration test in `tests/integration/Sequences/` (new file `NestedStepOutcomeReferenceIntegrationTests.cs`) that POSTs a sequence via the real HTTP path with a `Break` nested in a `Loop` body and a separate top-level `If` step referencing it with `expectedState: "break"`, asserts `201 Created` (today `400`), then runs the sequence twice (break-firing input, non-firing input) and asserts the `If` step's condition result differs correctly between the two runs — this is the end-to-end proof that `Break` outcomes are actually recorded for lookup (research.md R-005), not just validation-legal
- [X] T015 [P] [US2] Add a regression test in the same integration file confirming a `commandOutcome` condition referencing a `Break` step **within its own loop body** (already validation-legal before this feature) now actually resolves at runtime instead of always failing with "reference unavailable" (research.md R-005's latent-gap fix)
- [X] T016 [P] [US2] Add a regression test in the same integration file confirming a `stepRef` that is validation-legal (reachable + prior) but names a step that did not execute during a given run (e.g. an `If` branch not taken) still fails the referencing step and the sequence with the existing "commandOutcome reference '...' is unavailable" error (spec FR-011) — proves widened scope does not introduce a new silent-success/silent-skip path

### Implementation for User Story 2

- [X] T017 [US2] In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, build a flattened, authored-order index of every step reachable from the sequence root (root `steps` plus every nested `Loop.Body` and `If.Body`/`ElseBody`, recursively expanded in place) once per top-level `Validate(...)` call, per research.md R-003 / data-model.md's "Flattened step index"
- [X] T018 [US2] In the same file, change `ValidateStepCondition`'s `commandOutcome` `stepRef` resolution to look up against the flattened index from T017 instead of the immediate `siblings` list, and redefine "prior" as "appears earlier in the flattened index" instead of "has a lower same-list index" — thread the flattened index down through the existing `ValidateLoopStep`/`ValidateIfBranch` recursion in place of (or alongside) the current `siblings`/`indexInSiblings` parameters
- [X] T019 [US2] In the same file, add `"break"` and `"no_break"` to `AllowedCommandOutcomeStates` (matching `SequenceRunner.BreakOutcomes.Break`/`BreakOutcomes.NoBreak`'s existing string constants exactly)
- [X] T020 [US2] In `src/GameBot.Domain/Services/SequenceRunner.cs`'s `ExecuteLoopBodyAsync`, write `stepOutcomes[brkKey] = BreakOutcomes.Break` or `BreakOutcomes.NoBreak` at each of the four existing `result.AddStep(brkKey, ...)` call sites for a `Break` step (unconditional-fired, conditional-fired, conditional-not-fired, eval-error-treated-as-no-break) — today only the execution-log entry is written, never the runtime dictionary `commandOutcome` resolves against (research.md R-005)

**Checkpoint**: Run T011-T016 — all must now pass. User Stories 1 AND 2 both work
independently; the alliance-donation-style "did this loop's Break fire" gate is
now buildable as a structural check.

---

## Phase 5: User Story 3 - A condition can reference any nested step's outcome, not just Break (Priority: P2)

**Goal**: Confirm the whole-tree `stepRef` resolution built for US2 generalizes,
with no further code, to a `commandOutcome` condition referencing an ordinary
(non-`Break`) step nested inside a different `Loop`/`If` body.

**Independent Test**: Author a sequence with an ordinary `Command`-typed step
nested in one `Loop` body and a separate `If` step elsewhere referencing its
`success`/`failed`/`skipped` outcome; confirm it resolves correctly using only
US2's mechanism, no additional implementation.

### Tests for User Story 3 ⚠️

> Write these first; confirm they FAIL against current code before implementing
> (they exercise the pre-US2 code path, so they fail the same way US2's tests do
> until Phase 4 is implemented — if run only after Phase 4, confirm they pass
> without needing new implementation, proving generality).

- [X] T021 [P] [US3] Add failing (pre-US2) / passing (post-US2) unit test in `tests/unit/Sequences/LoopValidationTests.cs` (or the new file from T011) asserting a `commandOutcome` condition's `stepRef` naming a non-`Break`, `Command`-typed step nested inside a different `Loop`/`If` body is accepted at validation time
- [X] T022 [P] [US3] Add a failing/passing integration test in `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs` (from T014) that POSTs and runs a sequence where a top-level `If` step's `commandOutcome` condition references a non-`Break` step nested inside a different `Loop`'s body, using `expectedState: "success"`, and asserts the condition resolves against that step's actual runtime outcome exactly as a top-level reference does today

### Implementation for User Story 3

- [X] T023 [US3] No new implementation expected — if T021/T022 fail after Phase 4 is complete, that indicates T017/T018's resolution logic is accidentally `Break`-specific; fix `ValidateStepCondition`/`SequenceRunner` so the whole-tree resolution and existing runtime `stepOutcomes` dictionary lookup (already scope-agnostic per research.md R-003's exploration finding) apply uniformly regardless of the referenced step's type

**Checkpoint**: All three user stories are independently functional; nested
`stepRef` resolution is proven general, not `Break`-specific.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 Update `docs/architecture.md` §"Break & loop execution and the
  execution-log status vocabulary" to document `exitReason` and the widened
  `commandOutcome` `stepRef` scope, refreshing the "Last reviewed" date
  (Constitution Principle V)
- [X] T025 Run `dotnet build` and the full `dotnet test` suite (unit + integration
  + contract projects) and confirm zero failures/regressions
- [X] T026 Manually validate quickstart.md's scenarios (Part A, Part B, and the
  ordering-regression check) against a locally running `GameBot.Service`
- [X] T027 Update spec.md's Status line to `Implemented` once all tasks above are
  verified complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup / Foundational**: None — skipped, no shared prerequisites.
- **User Story 1 (Phase 3)**: Fully independent — touches only loop-completion
  sites in `SequenceRunner.cs`, disjoint from US2/US3's files/methods.
- **User Story 2 (Phase 4)**: Independent of US1; builds the whole-tree
  resolution mechanism and `Break`-outcome recording that US3 reuses.
- **User Story 3 (Phase 5)**: Depends on US2's implementation (T017-T020) being
  in place — its tests specifically verify US2's mechanism generalizes, and its
  one implementation task (T023) is a contingency, not new work, if it doesn't.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests (T001-T006 / T011-T016 / T021-T022) MUST be written and confirmed
  FAILING before their story's implementation tasks.
- T007 before T008 before T009 before T010 (US1): result-shape fields first,
  then the `AddLoopStep` plumbing to carry them, then threading the firing
  `Break`'s id up through body/if execution, then wiring each loop kind's call
  sites.
- T017 before T018 (US2): build the flattened index before changing the lookup
  to use it. T019 and T020 can proceed in parallel with T017/T018 (different
  concerns: allow-list vs. runtime recording) but all four must land before
  T014-T016's integration tests can pass.

### Parallel Opportunities

- T001-T006, T011-T016, and T021-T022 are each independently parallelizable
  within their story (distinct test files or distinct assertions), except where
  a task explicitly says "in the same file" as a preceding task in this list —
  those should be authored together rather than truly concurrently to avoid
  merge conflicts in one new file.
- US1's implementation (T007-T010) can proceed in parallel with US2's
  (T017-T020) — disjoint methods in the same file (`SequenceRunner.cs`) for
  T009/T010 vs. T020, and a fully separate file for T017-T019
  (`SequenceStepValidationService.cs`); coordinate merges if worked by different
  people since T009/T010/T020 land in the same file.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001-T010 (US1). **STOP and VALIDATE**: run the new tests, confirm no
   regression in existing `Loop`/`Break` execution tests. This alone already
   exposes the exit-reason signal FR-001 asks for, usable by any caller
   inspecting a run's result directly.

### Incremental Delivery

1. US1 (T001-T010) → validate → `exitReason` ships, independently useful.
2. US2 (T011-T020) → validate → closes the specifically evidenced
   donation-exhaustion-gate gap (cross-scope `Break` reference).
3. US3 (T021-T023) → validate → confirms the mechanism is general, not a
   `Break`-only special case.
4. Phase 6 polish once all three are in.

## Notes

- US1 is purely additive to an execution-result type with no persisted schema —
  lowest-risk slice, ship first.
- US2's implementation is deliberately three small, independent changes
  (T017-T018 resolution scope, T019 allow-list, T020 outcome recording) because
  research.md R-005 established that *all three* are required for the feature's
  stated motivation to actually work — a widened `stepRef` alone (skipping T020)
  would still validate but always fail at runtime with "reference unavailable"
  for any `Break` reference, defeating the point.
- Commit after each user story's checkpoint, not after every individual task.
