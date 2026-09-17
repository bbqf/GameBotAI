# Tasks: Condition-Model Ceiling Retest and Documented Outcome

**Feature**: 103-condition-model-retest-docs | **Date**: 2026-09-17
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: [#193](https://github.com/bbqf/GameBotAI/issues/193)

**Tests are required for this feature.** They are not a supporting activity here —
spec FR-001…FR-007 make the automated assertions *the deliverable*. Issue #193 exists
because a fix was reported without being measured, so a task that changes behaviour
without a test that would have caught the gap does not satisfy this feature.

**Ordering principle**: every gap test is written and **observed failing** (T021)
before any fix lands. This satisfies the constitution's "bug fixes MUST include a
failing test reproducing the issue before the fix" literally, and it is also the only
way to prove a gap was real rather than imagined — the lesson issue #193 is about.

**Paths** are repository-relative from `C:\src\GameBot`.

---

## Phase 1: Setup

- [ ] T001 Confirm the branch's baseline is green before changing anything: run `dotnet build GameBot.sln` then `dotnet test GameBot.sln`, and record the pass/fail counts in this file under Notes. A red baseline is a hard stop per the constitution's release-blocker gate.
- [ ] T002 [P] Confirm the `web-ui` baseline: run `vite build` and `jest` in `src/web-ui`. Note that `lint` and `tsc --noEmit` carry pre-existing unrelated failures and are **not** the gate for this feature.

## Phase 2: Foundational

*No foundational tasks.* Every change in this feature is additive to a file that
already exists and already owns the behaviour, so no prerequisite scaffolding blocks
the user stories. This phase is retained to record that the absence is deliberate.

---

## Phase 3: User Story 1 — A trustworthy, non-rotting answer (Priority: P1)

**Goal**: Every claim about nested references and loop exit reasons becomes an
assertion that re-runs on each build — both the claims that pass today (regression
guards) and the claims that fail today (gap tests, written to fail first).

**Independent test**: Run `dotnet test GameBot.sln` and the `web-ui` jest suite. The
regression guards pass; the gap tests fail, and their failure messages name the three
gaps. That failing state *is* the retest result, and is recorded at T021.

**Naming constraint for every task in this phase**: the constitution's quality gates
forbid underscores in method names. Name test methods in CamelCase — the sentence
style the touched files already use (`AnInvalidCompositeInAWhileConditionIsRejectedAtSaveTime`)
— including `[Theory]` cases, where underscore-separated names are a common habit.

### Regression guards — claims confirmed gone (must pass immediately)

- [ ] T003 [P] [US1] In `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, assert a `commandOutcome` condition written **directly** on a step, whose `stepRef` names a `Break` nested inside a different earlier `Loop` body, is accepted at save time (FR-001). Reuse the file's existing sequence builders rather than adding new ones.
- [ ] T004 [P] [US1] In the same file, assert the identical reference wrapped in each composite variant — `all`, `any`, `none` — is also accepted (FR-002). Three cases; a valid nested reference must not be collateral damage of the T014/T015 fix.
- [ ] T005 [P] [US1] In `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs`, assert a valid nested reference inside a **composite nested within a composite** is accepted, confirming depth does not change resolution (FR-002).
- [ ] T006 [P] [US1] In `tests/unit/Sequences/CompositeConditionValidationTests.cs`, assert `expectedState` values `break` and `no_break` are accepted on a `commandOutcome` both directly and inside each composite variant (FR-004), and that the five-value set is exactly `success|failed|skipped|break|no_break` with nothing added (FR-013).
- [ ] T007 [P] [US1] In `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs`, assert run-time evaluation of a `commandOutcome` nested inside each composite variant resolves a nested step's recorded outcome, and distinguishes a `Break` that fired (`break`) from one that did not (`no_break`) (FR-004, research F-003).
- [ ] T008 [P] [US1] In `tests/unit/Sequences/SequenceRunnerLoopTests.cs`, assert the loop exit reason's three mutually exclusive states (FR-005): a fired `Break` gives `BrokeVia` = that `Break`'s `StepId` with `ExhaustedMaxIterations` false; a ceiling reached with no break gives null + true; a normal body/condition finish gives null + false. Cover all three loop kinds (count, while, repeat-until).
- [ ] T009 [P] [US1] In the same file, assert the simultaneous case (FR-012a): a `Break` firing in the iteration that also reaches the ceiling reports the break by name with `ExhaustedMaxIterations` false. This pins existing behaviour — if it fails, the finding in research F-006 was wrong and must be corrected before proceeding.
- [ ] T010 [P] [US1] In the same file, assert `BrokeVia` is the firing `Break`'s own `StepId` and never an enclosing `If`'s, using a `Break` nested inside an `If` inside the loop body (FR-005).
- [ ] T011 [P] [US1] Create `tests/contract/Sequences/SequenceLoopExitReasonContractTests.cs` asserting `POST /api/sequences/{id}/execute` returns `exitReason: { brokeVia, exhaustedMaxIterations }` on a loop step, with exactly those field names (FR-011a). This guards the surface research F-005 corrected course on — it is already delivered and must not be moved or renamed by T025.
- [ ] T012 [P] [US1] In `tests/unit/Sequences/CompositeConditionPositionValidationTests.cs`, extend the existing D-006 boundary coverage: assert a **bare leaf** `commandOutcome` with a dangling `stepRef` written directly in a `while` and in a `repeatUntil` loop condition is still **accepted** (FR-003a). This is the guard that stops the T022 fix from reversing feature 088's decision D-006.
- [ ] T013 [P] [US1] In the same file, assert a composite-wrapped `commandOutcome` is reference-checked in every slot that validates conditions — step guard, `if` condition, break condition (in a loop body and in an `if` branch), and `while`/`repeatUntil` condition (FR-003). Together with T012 this pins the inherited asymmetry: wrapped is checked in the loop-condition slots, bare is not.
- [ ] T013a [P] [US1] In `tests/unit/Sequences/CompositeConditionValidationTests.cs`, assert the **directly-written** variant's rejections explicitly: a bare `commandOutcome` step guard whose `stepRef` names a step absent from the sequence is rejected with "references unknown prior step", and one naming a structurally later step is rejected with "must reference a prior step" (FR-006). These hold today; FR-006 covers every variant from FR-002, and the directly-written one must be asserted by this feature rather than left resting on feature 081's suite.

### Gap tests — written to FAIL before Phase 4

- [ ] T014 [P] [US1] In `tests/unit/Sequences/CompositeConditionValidationTests.cs`, add a failing test: a `commandOutcome` inside an `all` whose `stepRef` names a step absent from the whole sequence is rejected with `Step '<label>' condition at $.children[<i>]: commandOutcome references unknown prior step '<ref>'.` (FR-008, FR-010). Repeat for `any` and `none`.
- [ ] T015 [P] [US1] In the same file, add a failing test: a `commandOutcome` inside a composite whose `stepRef` names a structurally **later** step is rejected with `... must reference a prior step.` (FR-009). Construct it so the target is reachable but authored after the referencing condition, proving ordering — not just reachability — is enforced.
- [ ] T016 [P] [US1] In the same file, add a failing test asserting the reported path for a reference two composite levels deep renders as `$.children[2].children[0]`, matching the convention the validator's other messages already use (FR-010).
- [ ] T017 [P] [US1] In `tests/contract/Sequences/CompositeConditionContractTests.cs`, add a failing contract test: `POST /api/sequences` with a composite-nested dangling reference returns **400**, not 201 (FR-008). Use a sequence id unique to this test and clean up — contract tests in this repo share a data directory.
- [ ] T018 [P] [US1] Create `tests/contract/ExecutionLogs/ExecutionLogsLoopExitReasonContractTests.cs` with a failing contract test: a completed run's persisted run log entry for a loop step carries `brokeVia` and `exhaustedMaxIterations` attributes beside the existing `iterations` (FR-011). Assert all three exits, so the test covers the null/false case and not only the break case.
- [ ] T019 [P] [US1] In `src/web-ui/src/lib/__tests__/validation.spec.ts`, add a failing test: `validatePerStepConditions` accepts a `commandOutcome` whose `stepRef` names a step nested inside a `body` or `elseBody` (FR-012). Today it reports `references unknown prior step` — the original ceiling's own message.
- [ ] T020 [P] [US1] In the same spec file, add a failing test: `validatePerStepConditions` accepts `expectedState` values `break` and `no_break` (FR-012), and keeps rejecting a genuinely unknown value.
- [ ] T020a [P] [US1] In the same spec file, add a failing test for the behaviour T027 newly introduces: a malformed condition on a **nested** step — an `imageVisible` with an empty `imageId` inside a `body` or `elseBody` — is reported, where today the walk never descends and reports nothing (FR-012). Also assert a nonexistent and a forward reference are still rejected once the walk is tree-aware, so widening the scope does not silently drop either rule.

### Checkpoint

- [ ] T021 [US1] Run `dotnet test GameBot.sln` and the `web-ui` jest suite. Confirm T003–T013a **pass** and T014–T020a **fail**, then record the exact failure messages in this file under Notes. This is the retest's measured result and the evidence issue #193 asked for. If any of T014–T020a unexpectedly passes, that gap does not exist: correct `research.md`, delete the corresponding fix task, and say so in the final report rather than inventing work.

---

## Phase 4: User Story 2 — Close the measured gaps (Priority: P2)

**Goal**: Make T014–T020 pass by applying the already-delivered design consistently.
No new condition variant, no new outcome state (FR-013).

**Independent test**: The tests from T014–T020 pass; every test from T003–T013 still
passes, proving nothing was widened into over-rejection or narrowed into silence.

- [ ] T022 [US2] In `src/GameBot.Domain/Services/CompositeConditionValidator.cs`, add **optional** parameters carrying the step-position map and the referencing step's own authored position to `Validate`, thread them through `Walk`, and extend the `CommandOutcomeStepCondition` case with the resolution and ordering checks, worded with the `$`-rooted path (FR-008, FR-009, FR-010). Optional so existing call sites that cannot supply the maps keep today's shape-only behaviour. Document the new parameters with XML comments, and if an analyzer objects to the method's size, extract a small private helper rather than suppressing it.
- [ ] T023 [US2] In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, pass the `positionByStepId` map and the step's own position at both `CompositeConditionValidator.Validate` call sites (`step.Condition` and `step.BreakCondition`, around line 357), and at the break-condition call inside the `if`-branch walk (around line 271). Do not duplicate the per-step leaf checks — the existing `validateLeafAtRoot: false` contract already prevents double-reporting.
- [ ] T024 [US2] Confirm `FileSequenceRepository`'s composite walk still compiles and behaves unchanged now that the parameters exist but are unsupplied there, and add an assertion in `tests/unit/Sequences/CompositeConditionValidationTests.cs` that calling `CompositeConditionValidator.Validate` *without* the position maps still performs shape-only validation and does not newly reject a reference. A repository-layer rejection surfaces as a 500, which is exactly what the composite validator exists to prevent.
- [ ] T025 [P] [US2] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, add `brokeVia` and `exhaustedMaxIterations` to the loop step's `ExecutionDetailItem` attribute dictionary (around lines 413–427), read from `step.ExitReason` with nulls when absent (FR-011). Additive keys only — leave every existing attribute, and the synchronous response shape, untouched (FR-011a).
- [ ] T026 [P] [US2] In `src/web-ui/src/types/sequenceFlow.ts`, widen `CommandOutcomeStepCondition.expectedState` from `'success' | 'failed' | 'skipped'` to include `'break' | 'no_break'` (FR-012). Leave `PerStepConditionType` alone — composites stay unauthorable in the UI by decision (FR-016a).
- [ ] T027 [US2] In `src/web-ui/src/lib/validation.ts`, replace the flat `steps.findIndex` lookup in `validatePerStepConditions` (around lines 263–279) with a walk that flattens the step tree in authored order through `body` and `elseBody`, resolving both the existence and the ordering rule against that flattened order, and extend the `expectedState` allow-list to all five values (FR-012). Validate conditions on nested steps on the same walk, since they are currently skipped entirely.
- [ ] T028 [US2] Re-run `dotnet test GameBot.sln` and the `web-ui` jest suite. Every test from T014–T020a must now pass with no regression in T003–T013a. Record the counts under Notes.

---

## Phase 5: User Story 3 — Write the outcome down (Priority: P3)

**Goal**: A reader can answer issue #193's question from the published interface
description and the project documentation alone.

**Independent test**: Read the OpenAPI document and `docs/architecture.md` without
opening any test file, and the reference scope, the five outcome states, the loop
exit-reason shape, and each remaining limitation are all stated.

- [ ] T029 [P] [US3] In `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`, describe a step condition's `stepRef`: the resolution scope (any step reachable from the sequence root, nested `Loop` bodies and `If` branches included), the authored-order ordering constraint, and the inherited exception for a bare leaf in a `while`/`repeatUntil` condition (FR-014, contract C-4). Also list all five `expectedState` values and what `break`/`no_break` mean.
- [ ] T030 [P] [US3] In the same filter, describe a loop step's `exitReason` on the run response: the `brokeVia` / `exhaustedMaxIterations` shape, the three exits it distinguishes, their mutual exclusivity, and the simultaneous-case rule (FR-015). Descriptions only — rename nothing, require nothing new.
- [ ] T031 [US3] In `tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs`, assert the OpenAPI document actually carries the descriptions from T029 and T030, so the documentation cannot silently rot the way the claim in issue #193 did (FR-014, FR-015).
- [ ] T032 [US3] In `docs/architecture.md`, record the retest conclusion (FR-016, FR-017): for each half of the ceiling, its status and the tests that establish it; the three gaps closed here; and a correction of the now-misleading impression that the exit reason is only a domain-internal value. Name the test files so a reader can re-run the evidence rather than trust the prose. Refresh the `_Last reviewed:_` line at the top — this is a NON-NEGOTIABLE constitution gate.
- [ ] T033 [US3] In the same document, state the two remaining limitations explicitly (FR-016a, FR-003a): the web UI offers no way to author a composite condition at all (absent capability, not a contradicting rule), and a bare leaf reference in a `while`/`repeatUntil` condition is deliberately unvalidated per feature 088 decision D-006 — with the resulting asymmetry that a composite-wrapped reference in those slots *is* checked.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T034 [P] Add a `CHANGELOG.md` entry covering the three user-visible changes: composite-nested references now validated at save time (with the compatibility note that a stored sequence carrying a dangling one will be rejected on its next save), the loop exit reason now recorded in the persisted run log, and the web UI no longer rejecting nested references or `break`/`no_break`.
- [ ] T035 [P] Set this spec's `**Status**:` line in `specs/103-condition-model-retest-docs/spec.md` to `Implemented`, per the constitution's living-documentation principle. Leave `specs/081-loop-exit-reason-and-nested-steprefs/spec.md` as-is — this feature retests and documents that one, it does not supersede it.
- [ ] T036 [P] Add the row for 103 to `specs/STATUS.md`, keeping it consistent with T035.
- [ ] T037 Run the full gate: `dotnet build GameBot.sln`, `dotnet test GameBot.sln`, and `vite build` + `jest` in `src/web-ui`. All green, with no pre-existing assertion weakened or deleted to get there (SC-007). A flaky failure in `MaskedTemplateMatchTests` or `QueueTemplateLink` is known CI noise in this repo — rerun rather than "fixing".
- [ ] T037a Record coverage for the touched areas against the constitution's Principle II baseline (≥80% line, ≥70% branch): `CompositeConditionValidator`, `SequenceStepValidationService`, and the loop branch of `SequenceExecutionService`. Note the figures under Notes. Given that this feature is overwhelmingly test additions the baseline should rise, but the constitution requires it measured, not assumed — which is this feature's whole point.
- [ ] T038 Walk `quickstart.md` examples 2, 3, 5 and 7 against a running service and correct any request or response that does not match reality. Example 7 in particular must still return 201 — if it now returns 400, T022 crossed the D-006 boundary and must be narrowed.

---

## Dependencies

```text
Phase 1 (Setup)
    ↓
Phase 2 (Foundational — empty)
    ↓
Phase 3 (US1: evidence)  ──  T003…T020a all parallel; T021 gates on all of them
    ↓                         T021 is a hard gate: no fix begins until the gaps are observed
Phase 4 (US2: fixes)     ──  T022 → T023 → T024 sequential (same call graph)
    ↓                         T025, T026 parallel; T027 after T026 (type before use)
    ↓                         T028 gates on T022…T027
Phase 5 (US3: docs)      ──  T029, T030 parallel; T031 after both; T032 → T033 (same file)
    ↓
Phase 6 (Polish)         ──  T034, T035, T036 parallel; T037 → T037a; T038 last
```

**Cross-story**: US3's T032 depends on US1's T021 result and US2's T028 outcome — the
conclusion cannot be written before the measurement and the fixes it describes. US2
depends on US1 by design, not by convenience: the gap tests are the specification of
each fix.

**Same-file serialization**: T006, T013a, T014, T015, T016 and T024 all touch
`CompositeConditionValidationTests.cs`; T008, T009, T010 all touch
`SequenceRunnerLoopTests.cs`; T012 and T013 both touch
`CompositeConditionPositionValidationTests.cs`; T019, T020 and T020a all touch
`validation.spec.ts`; T032 and T033 both touch `docs/architecture.md`. They are marked
`[P]` because they are independent in content, but within each group apply them one
edit at a time.

## Parallel Execution Examples

**Phase 3, the wide fan-out** — twenty test tasks across eight files, no
interdependencies:

```text
T003, T004, T005        → NestedStepOutcomeReferenceIntegrationTests.cs
T006, T013a, T014,
T015, T016              → CompositeConditionValidationTests.cs
T007                    → CompositeConditionEvaluatorTests.cs
T008, T009, T010        → SequenceRunnerLoopTests.cs
T011                    → SequenceLoopExitReasonContractTests.cs (new)
T017                    → CompositeConditionContractTests.cs
T012, T013              → CompositeConditionPositionValidationTests.cs
T018                    → ExecutionLogsLoopExitReasonContractTests.cs (new)
T019, T020, T020a       → web-ui validation.spec.ts
```

**Phase 4, three independent fixes** after the domain change lands:

```text
T025 (service log attributes)  ⟂  T026 → T027 (web-ui)  ⟂  T022 → T023 → T024 (domain)
```

**Phase 5**: `T029 ⟂ T030`, then `T031`.

## Implementation Strategy

**MVP = User Story 1 alone.** This is unusual and deliberate: for this feature the
measurement *is* the minimum viable product. Issue #193 explicitly accepts "a
statement of what remains" as a valid outcome, so US1 plus a written conclusion would
close it honestly even if no fix shipped. US2 exists because the gaps US1 finds turn
out to be cheap and in-design — not because the issue demands them.

**Incremental delivery**:

1. **US1** → the answer exists, as tests, and the three gaps are proven rather than
   asserted. Independently valuable; closes the issue's core complaint.
2. **US2** → the three gaps closed, each by the test that found it.
3. **US3** → the answer discoverable without reading tests.
4. **Polish** → living-documentation gates, changelog, full green suite.

**Stop condition worth naming**: if T021 shows a gap test passing unexpectedly, the
corresponding finding in `research.md` was wrong. Correct the research, drop the fix,
and report the correction. A retest that manufactures work to justify itself would
repeat exactly the error issue #193 was filed about.

## Notes

*T001, T002, T021, T028, T037 and T037a record their measured results here as
implementation proceeds.*
