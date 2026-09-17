# Phase 0 Research: Condition-Model Ceiling Retest

**Feature**: 103-condition-model-retest-docs | **Date**: 2026-09-17
**Spec**: [spec.md](./spec.md) | **Issue**: [#193](https://github.com/bbqf/GameBotAI/issues/193)

## Purpose of this document

Issue #193 asks whether a reported-fixed ceiling is actually gone. This file
records the **code reading** that located every place the answer lives, and the
preliminary finding at each place. These findings are the plan's targets; they are
**not** the evidence. The evidence is the automated tests that `tasks.md` schedules
— every claim below is restated there as an assertion so it re-runs on each build
(spec FR-007).

Where a finding says "confirmed gone", the corresponding test is a **regression
guard**. Where it says "gap", the test is written to **fail first**.

---

## Half 1 — nested `stepRef` in every condition variant

### F-001: Top-level `commandOutcome` — confirmed gone

**Decision**: No change. Add regression guards only.

`SequenceStepValidationService.ValidateStepCondition` resolves a `stepRef` via a
`positionByStepId` map built over every step reachable from the sequence root, and
enforces priorness via authored-order positions
(`src/GameBot.Domain/Services/SequenceStepValidationService.cs:327-347`). The
comments at :332-334 and :338-339 state the feature-081 widening explicitly.
`AllowedCommandOutcomeStates` includes `break` and `no_break` (:345).

**Rationale**: This is the exact half the issue reported as broken, and it reads as
fully delivered. The original `400 "references unknown prior step"` now fires only
when a step is genuinely absent from the whole sequence.

**Alternatives considered**: Trusting the existing
`tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs` as
sufficient evidence and asserting nothing further. Rejected: that suite covers the
directly-written condition, which is precisely the case feature 081 targeted. The
issue asks about *every* variant, and the variants below are where it diverges.

### F-002: `commandOutcome` nested inside a composite — **GAP**

**Decision**: Fix. Extend the whole-sequence resolution and ordering rules to
composite children.

`ValidateStepCondition` pattern-matches `step.Condition is
CommandOutcomeStepCondition` — a **top-level** condition only. A `commandOutcome`
sitting inside an `all`/`any`/`none` composite is validated exclusively by
`CompositeConditionValidator.Walk`
(`src/GameBot.Domain/Services/CompositeConditionValidator.cs:94-106`), whose
`CommandOutcomeStepCondition` case checks only:

- `StepRef` is non-empty, and
- `ExpectedState` is in the allowed set.

It never resolves the reference and never checks priorness, because it is not given
the position maps. Consequences, both save-time:

| Authored | Directly | Inside a composite |
|---|---|---|
| `stepRef` naming a nonexistent step | 400, rejected | **accepted (201)** |
| `stepRef` naming a structurally later step | 400, rejected | **accepted (201)** |

An accepted dangling reference then fails at run time:
`SequenceStepConditionEvaluator.EvaluateLeafAsync` raises
`ConditionEvaluationException(CommandOutcomeUnavailable)` when the id is missing
from `stepOutcomes`
(`src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs:141-149`). So the
authoring mistake surfaces as a failed run instead of a 400 — the opposite of the
"reject at save time rather than surface as a 500/failure later" posture
`CompositeConditionValidator`'s own class comment states.

**Rationale for fixing rather than recording**: the rule already exists and is
already correct; only the composite walk lacks the inputs to apply it. This is
applying the delivered design consistently (spec A-002), not new capability. It is
also the precise sense in which the answer to issue #193 is "not in every variant".

**Note on direction**: this gap is *too permissive*, where the original ceiling was
*too strict*. It is still part of the same question — whether the reference rules
hold uniformly across variants — and the fix makes composites match, rather than
adding anything new.

**Alternatives considered**:
1. *Record it as remaining, fix nothing.* Rejected: cheap, in-design, and leaving a
   measured inconsistency unfixed recreates the situation the issue complains about.
2. *Move composite reference checking into the per-step validator.* Rejected: the
   composite walk already owns child traversal, depth limits and the `$`-rooted path
   convention; duplicating traversal would risk the two drifting.

### F-003: Run-time evaluation of a nested reference — confirmed gone

**Decision**: No change. Add regression guards.

`SequenceStepConditionEvaluator` resolves a `commandOutcome` against a flat
`stepOutcomes` dictionary keyed by step id, reached identically from a leaf and from
inside a composite (`EvaluateNodeAsync` → `EvaluateCompositeAsync` →
`EvaluateNodeAsync`). Nesting depth is irrelevant to lookup, and `SequenceRunner`
records a `Break` step's fired/not-fired outcome into that map (feature 081, per
`docs/architecture.md:553-556`).

**Rationale**: The run-time half of half 1 is variant-independent by construction.
The gap in F-002 is purely save-time.

### F-004: Web-UI client-side validation — **GAP** (the original ceiling, intact)

**Decision**: Fix the two stale rules.

`src/web-ui/src/lib/validation.ts` `validatePerStepConditions` (:236-283) is still
pre-feature-081 on two counts:

1. **Flat reference resolution** (:268) —
   `steps.findIndex(c => c.stepId === stepRef)` searches only the top-level array
   it was handed. `SequenceLinearStep` carries `body` and `elseBody`
   (`src/web-ui/src/types/sequenceFlow.ts`), so a reference to a step nested in a
   loop body or an `if` branch yields `refIndex < 0` and produces the *literal
   original error message*: `references unknown prior step`. The function also never
   descends into nested bodies, so conditions on nested steps go unvalidated.
2. **Stale outcome-state set** (:276) — `['success','failed','skipped']`, rejecting
   `break` and `no_break`, which is the second half of the originally reported
   ceiling. The TypeScript type is equally stale
   (`expectedState: 'success' | 'failed' | 'skipped'`, sequenceFlow.ts:90).

Called from `SequencesPage.tsx:1741` and `:1976` on save, so it blocks authoring in
the UI before the request is sent.

**Rationale for fixing**: these rules reject exactly what the service accepts
(spec FR-012). An author working in the UI still experiences the ceiling issue #193
describes, which makes "the ceiling is gone" false end-to-end. Both are contained
edits to one function plus one type.

**Also found, recorded not fixed**: `PerStepConditionType`
(sequenceFlow.ts:78) is `'imageVisible' | 'commandOutcome'` — the UI's condition
model has no composite variants at all, so composite conditions cannot be authored
there. Per spec FR-016a and Out of Scope this is absent capability rather than a
contradicting rule, and is recorded, not built.

---

## Half 2 — readable loop exit reason

### F-005: On a directly-invoked run — confirmed gone (initial reading corrected)

**Decision**: No change. Regression guard on the field names.

An initial pass over `src/GameBot.Service` found no occurrence of `ExitReason` and
suggested the exit reason was never published. **That inference was wrong**, and
checking the mechanism rather than trusting the grep corrected it:
`SequencesEndpoints.ExecuteSequenceAsync` ends in `Results.Ok(res)`
(`src/GameBot.Service/Endpoints/SequencesEndpoints.cs:399`) where `res` is the
**domain** `SequenceRunner.SequenceExecutionResult`. Its `Steps` are domain
`StepResult`s carrying `ExitReason`, so `System.Text.Json` serializes
`exitReason: { brokeVia, exhaustedMaxIterations }` with no DTO in between. The
absence of `ExitReason` from the service project is the *reason* there is nothing to
map, not evidence of a gap. `docs/architecture.md:499-500` is accurate.

**Rationale for recording the correction**: a retest that reports a phantom gap is
worth no more than one that reports a phantom fix.

### F-006: `LoopExitReason` semantics — confirmed correct

**Decision**: No change. Pin the three-way outcome with tests.

`LoopExitReason { BrokeVia, ExhaustedMaxIterations }`
(`src/GameBot.Domain/Services/SequenceRunner.cs:2288-2293`) is populated across all
three loop kinds (:1043, :1090, :1106, :1111, :1134, :1192, :1197, :1220).
`BrokeVia` is threaded out of `ExecuteLoopBodyAsync`/`ExecuteIfStepAsync` as
`iterBrokeVia`, so a `Break` nested in an `If` inside the body reports the `Break`'s
own id, not the enclosing `If`'s.

On the simultaneous case (spec FR-012a): the break check runs after body execution
and exits with `ExhaustedMaxIterations = false` (:1039 → :1043), so **the break
wins and is named**. This is existing behaviour; the test pins it rather than
changing it.

### F-007: In the persisted run log — **GAP**

**Decision**: Fix. Record the exit reason on the loop step's log entry.

`SequenceExecutionService` renders a loop step into an `ExecutionDetailItem` whose
attributes are `stepOrder`, `stepType`, `status`, `actionOutcome`, delay fields,
`iterations`, `message`, `sequenceId`, `sequenceLabel`, `stepId`, `stepLabel`
(`src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs:404-429`).
`brokeVia` and `exhaustedMaxIterations` are absent. The exit reason therefore exists
only on the synchronous response from F-005 and is dropped from the persisted record.

Why this matters rather than being cosmetic: a **queue-driven** run leaves no
synchronous response — the execution log is its only record. Queue-driven execution
is how the downstream consumer runs sequences, so for the runs that matter the exit
reason is unreadable after the fact. At best it is inferable from the prose in
`message`, which is exactly the "readable" standard the issue asks us to beat.

**Rationale for fixing**: two attributes carrying an already-computed, already-correct
value into the record beside the `iterations` count it belongs with. No new concept.

**Alternatives considered**:
1. *Add it to `ExecutionTreeNodeDto` as well.* Rejected per spec Out of Scope: a
   separate published surface, and (per prior experience in this repo) that DTO has
   its own Swagger schema-id sensitivities. No added evidence for #193.
2. *Parse it back out of the message prose.* Rejected: that is the failure mode, not
   a fix.

### F-009: A directly-written reference in an `If` condition is never checked — **GAP**

**Decision**: Fix. Apply the same two rules in the `If` condition slot.

Found by measurement rather than reading (see "Measured probe" below).
`ValidateIfStep` routes an `If` step's condition to `ValidateIfCondition`
(`src/GameBot.Domain/Services/SequenceStepValidationService.cs:209-228`), which checks
that `stepRef` is non-empty and that `expectedState` is known — and nothing else. The
`positionByStepId` and `ownPosition` maps are never passed to it, so a reference in an
`If` condition is neither resolved nor ordered. An `If` step never reaches
`ValidateStepCondition`, because `Validate` dispatches it to `ValidateIfStep` and
`continue`s.

Measured: a dangling reference on an **action step guard** is rejected; the identical
reference in an **`If` condition** is accepted with zero errors.

Why this matters more than it first looks: feature 081's headline acceptance scenario
was *"a sequence-creation request whose `If` condition's `stepRef` names a step nested
inside a `Loop`/`If` body … creation succeeds"*. It does succeed — but for the wrong
reason. Nothing in that slot checks references at all, so the scenario would have
passed without the widening. For the `If` slot the ceiling was never lifted; it was
never enforced. A typo'd reference there is accepted at save time and fails the run.

**Distinct from the D-006 boundary in F-008**: the `If` condition slot *does* validate
leaf conditions — the `imageVisible` `imageId` check and the `expectedState` check both
fire there. It simply omits the two reference rules. So this is an incomplete check, not
a deliberate exemption, and completing it is consistent with D-006 rather than a
reversal of it.

**Compatibility**: same class as F-002 — a stored sequence with a dangling `If`
reference will be rejected on its next save, and already fails at run time when that
condition is reached (spec A-005).

### F-010: The `If` slot's outcome-state message names three of five values — **GAP**

**Decision**: Fix. One string.

`ValidateIfCondition` emits `commandOutcome expectedState must be one of
success|failed|skipped` (:225) while validating against a set that includes `break` and
`no_break`. The per-step validator's equivalent message (:345) correctly names all
five. Measured side by side: the action-step guard says
`success|failed|skipped|break|no_break`, the `If` condition says
`success|failed|skipped`.

So an author working on an `If` condition is told `break` is invalid by the very
validator that accepts it. This is issue #193's half-2 complaint — *"`expectedState`
accepted only `success|failed|skipped`"* — surviving verbatim as a message after the
behaviour was fixed, and it is exactly the kind of stale claim the issue was filed
about. Constitution Principle III requires actionable messages; a message listing the
wrong permitted set is worse than none.

### F-008: A deliberate pre-existing boundary that must be respected, not "fixed"

**Decision**: Assert the boundary; do not change it.

`tests/unit/Sequences/CompositeConditionPositionValidationTests.cs` covers composite
conditions in every slot that accepts one (step guard, `while`, `repeatUntil`, break
condition in a loop body and in an `if` branch, `if` condition) — but only for
composite **shape** (empty/oversized/too deep). It asserts nothing about
`commandOutcome` reference resolution, so F-002 is genuinely uncovered; "position" in
that file means condition slot, not reference position.

It also pins a boundary this feature must not cross. `ALeafInALoopConditionIsStillNotNewlyValidated`
asserts that a **leaf** condition sitting directly in a `while`/`repeatUntil` loop
condition is deliberately *not* validated (feature 088 research decision D-006):
validating it would newly reject sequences that save today. Consequences for this
feature:

- A bare `commandOutcome` in a `while`/`repeatUntil` condition gets **no** reference
  check today, by design. That is not one of the gaps in scope; spec FR-003's "every
  slot" is read as every slot that validates conditions at all, and this boundary is
  asserted rather than removed.
- The F-002 fix rides the composite walk, which *is* invoked in those slots. So a
  composite-wrapped reference in a `while` condition gains the checks while a bare one
  does not. That asymmetry is inherited from D-006, not introduced here; it is
  asserted in the tests and stated in the documentation so the next reader is not
  surprised by it.

**Alternatives considered**: extending validation to leaves in those two slots to make
the treatment uniform. Rejected: it reverses a documented earlier decision, risks
rejecting stored sequences, and is outside what issue #193 asks for.

---

## Measured probe

Before writing any test, a throwaway probe ran the real
`SequenceStepValidationService` over twelve hand-built sequences, one per
slot-and-variant combination, and printed the errors each produced. It was deleted
once the findings below were converted into permanent tests. Its results:

| # | Arrangement | Errors | Reading |
|---|---|---|---|
| 1 | Action guard, dangling ref, written directly | rejected | F-001 confirmed |
| 2 | Action guard, dangling ref inside `all` | **accepted** | F-002 confirmed |
| 3 | `If` condition, dangling ref, written directly | **accepted** | **F-009, new** |
| 4 | `If` condition, unknown `expectedState` | names 3 of 5 values | **F-010, new** |
| 5 | Action guard, unknown `expectedState` | names all 5 | correct |
| 6 | Action guard, forward ref, written directly | rejected | F-001 confirmed |
| 7 | Action guard, forward ref inside `all` | **accepted** | F-002 confirmed |
| 8 | Action guard, nested ref, written directly | accepted | feature 081 works |
| 9 | Action guard, nested ref inside `all` | accepted | valid case unharmed |
| 10 | Break condition, dangling ref inside `any` | **accepted** | F-002, break slot |
| 11 | `while` condition, dangling ref inside `all` | **accepted** | F-002, loop slot |
| 12 | `while` condition, dangling ref as bare leaf | accepted | D-006 boundary intact |

Rows 3 and 4 are why the probe was worth running: neither was visible from reading the
composite validator, because neither is about composites. Both were found by measuring
a slot nobody had thought to question — which is the entire argument of issue #193.

## Consolidated answer to issue #193

| # | Claim under test | Status | Action |
|---|---|---|---|
| 1 | Nested `stepRef`, condition written directly on a step guard | Confirmed gone | Regression guard |
| 2 | Nested `stepRef`, inside `all`/`any`/`none`, any slot | **Gap** — no resolution or ordering check | Fix + failing-first tests |
| 3 | Nested `stepRef` at run time, any variant | Confirmed gone | Regression guard |
| 4 | `break`/`no_break` accepted by the service | Confirmed gone | Regression guard |
| 5 | Nested `stepRef` / `break` states in the web UI | **Gap** — original ceiling intact | Fix + tests |
| 6 | Composite conditions authorable in the web UI | **Absent capability** | Record only (FR-016a) |
| 7 | Loop exit reason on a direct run response | Confirmed gone | Regression guard |
| 8 | Loop exit reason semantics incl. simultaneous case | Confirmed correct | Pin with tests |
| 9 | Loop exit reason in the persisted run log | **Gap** — dropped | Fix + failing-first test |
| 10 | Reference written directly in an `If` condition | **Gap** — never resolved or ordered | Fix + failing-first tests |
| 11 | The `If` slot's accepted-outcome-state message | **Gap** — names 3 of 5 | Fix + test |

So the honest answer the issue asked for: **half 1 and half 2 are both delivered in
the API and domain, and neither is gone end-to-end.** Five measured gaps remain, all
closable within the delivered design (2, 5, 9, 10, 11), plus one absent capability
recorded rather than built (6).

Finding 10 sharpens the answer: for the `If` condition slot the reference rules were
never enforced at all, so feature 081's acceptance scenario for that slot passed
vacuously. That is precisely the "reported fixed versus observed fixed" distinction
issue #193 was filed to force.

---

## Technical unknowns resolved

| Unknown | Resolution |
|---|---|
| Does the composite walk receive position maps? | No — `Validate(condition, stepLabel, errors, validateLeafAtRoot)` has no map parameter. Threading them in is the fix's shape. |
| Would adding the checks break existing saved sequences? | Only sequences whose composite-nested reference is already dangling or forward — which fail at run time today. A deliberate, stated correction (spec A-005). |
| Is the log-attribute dictionary schema-bound? | No — `ExecutionDetailItem` attributes are a free `Dictionary<string, object?>`; additive keys are routine (`cancellationReason`/`timeLimitMs` were added the same way). |
| Do `break`/`no_break` need adding anywhere? | No. Both validators already allow them (FR-013 forbids additions). Only the web-UI allow-list is stale. |
| Where is the exit reason documented today? | `docs/architecture.md:491-500` (accurate). Absent from the OpenAPI document — hence spec FR-015. |

## Testing approach

**Decision**: Extend the existing suites rather than create parallel ones, per spec
Dependencies.

| Concern | Home | Style |
|---|---|---|
| Composite-nested reference resolution/ordering | `tests/unit/Sequences/CompositeConditionValidationTests.cs` | Unit, over the validator |
| Same, at the API boundary (400 vs 201) | `tests/contract/Sequences/CompositeConditionContractTests.cs` | Contract |
| Nested reference across variants and slots | `tests/integration/Sequences/NestedStepOutcomeReferenceIntegrationTests.cs` | Integration |
| Loop exit-reason three-way + simultaneous case | `tests/unit/Sequences/SequenceRunnerLoopTests.cs` | Unit |
| Exit reason in the log record | contract test over the execution log | Contract |
| Web-UI validator | `src/web-ui/src/lib/__tests__` (jest) | Unit |

**Rationale**: a gap test placed beside the tests for the feature it belongs to is
found by the next person changing that code. A separate "issue 193" suite would be
orphaned the moment the files move.

**Known harness constraints** (from repository experience, respected by `tasks.md`):
contract tests share a data directory, so a test that writes sequences must use
distinct ids and clean up; and the real green gate for `web-ui` is
`vite build` + `jest`, since `lint`/`tsc --noEmit` carry pre-existing failures.
