# Implementation Plan: Condition-Model Ceiling Retest and Documented Outcome

**Branch**: `103-condition-model-retest-docs` | **Date**: 2026-09-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/103-condition-model-retest-docs/spec.md`
**Issue**: [#193](https://github.com/bbqf/GameBotAI/issues/193) — closes on merge

## Summary

Issue #193 asks whether a reported-fixed condition-model ceiling is actually gone,
and accepts either a confirmation or a precise statement of what remains. The code
reading in [research.md](./research.md) produced the answer: **both halves are
delivered in the API and domain, and neither is gone end-to-end.** Three gaps
remain, all closable by applying the already-delivered design consistently, plus one
absent capability that is recorded rather than built.

The work is therefore, in order of weight:

1. **Evidence** — assertions covering every condition variant and every validated
   slot for nested references, and the loop exit reason's three-way outcome, added to
   the suites that already own those files so they re-run on every build.
2. **Three gap fixes** — reference resolution and ordering for a `commandOutcome`
   reached through a composite; the loop exit reason carried into the persisted run
   log; two stale web-UI client rules that reject what the service accepts.
3. **The written outcome** — the rules published in the OpenAPI document, and the
   retest's conclusion (including what remains) recorded in `docs/architecture.md`.

One correction worth carrying forward: an initial pass concluded the loop exit reason
was never exposed over the API, because `ExitReason` appears nowhere in
`GameBot.Service`. Checking the mechanism showed the execute endpoint returns the
domain result directly, so it *is* serialized. The real gap is narrower — the
persisted run log drops it — and the plan targets that instead.

## Technical Context

**Language/Version**: C# 12 / .NET 8; TypeScript 5 for `src/web-ui`
**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json (polymorphic
conditions), Swashbuckle (OpenAPI), xUnit + FluentAssertions, React + Vite + Jest
**Storage**: JSON file repositories (`FileSequenceRepository`); execution logs as
per-run files
**Testing**: `dotnet test GameBot.sln` (unit / integration / contract); `jest` for
`web-ui`
**Target Platform**: Windows service host, localhost REST API on port 8080
**Project Type**: Web service + React admin UI, with a shared domain library
**Performance Goals**: No hot path touched. The composite reference check runs at save
time over an in-memory tree bounded by `MaxChildren` 16 and `MaxDepth` 4; two added
log attributes are dictionary inserts per loop step. No measurable budget change.
**Constraints**: Additive only — no accepted sequence may become rejected except the
deliberate, stated correction in C-1; no field renamed or removed; no new condition
variant or outcome state (spec FR-013)
**Scale/Scope**: 3 domain/service files, 2 web-UI files, 2 documentation targets,
~6 test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented
maintainer waiver exists.

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. Changes are small and local. The composite validator gains parameters rather than a new traversal, so no method grows past the ~50 LOC guidance — which also matters because this repo's build-time analyzers degrade on large methods with `TreatWarningsAsErrors`. New public parameters get XML docs. No new dependency. |
| **II. Testing Standards** | PASS, and this principle *is* the feature. Every gap gets a test that fails before its fix (FR-007), which satisfies "bug fixes MUST include a failing test reproducing the issue before the fix" literally. Touched areas are already well covered; added assertions raise coverage. Tests are deterministic — no emulator, no screen capture, no timing. |
| **III. UX Consistency** | PASS, and improved. C-3 removes a divergence where the UI rejected what the API accepted. C-1's messages reuse the existing `$`-rooted path idiom rather than inventing a second format, and remain actionable (they name the step and the position). |
| **IV. Performance** | PASS. No hot path. Save-time validation over a bounded tree; two dictionary inserts per loop step in the log path. Perf note: none required, recorded here for completeness. |
| **V. Living Documentation (NON-NEGOTIABLE)** | PASS **and load-bearing** — this principle is half the deliverable. `docs/architecture.md` is updated with the retest conclusion and its "Last reviewed" date refreshed. This spec's `Status` line is set and `specs/STATUS.md` gains its row. Spec 081 is **not** marked superseded: this feature retests and documents it, it does not replace it. A `CHANGELOG.md` entry is added, since C-1/C-2/C-3 are user-visible. |

No violations. **Complexity Tracking** section omitted — nothing to justify.

Post-Phase-1 re-check: unchanged. The design added no project, no dependency, no
abstraction, and no new entity; the one structural decision (threading position maps
into the existing composite walk rather than duplicating traversal) reduces rather
than adds duplication.

## Project Structure

### Documentation (this feature)

```text
specs/103-condition-model-retest-docs/
├── spec.md              # Feature specification (with Clarifications)
├── plan.md              # This file
├── research.md          # Phase 0: the retest's code-reading findings F-001..F-008
├── data-model.md        # Phase 1: existing shapes, rules added, fields added
├── quickstart.md        # Phase 1: reproduce each finding by hand
├── contracts/
│   └── condition-reference-scope-and-loop-exit-reason.md   # C-1..C-4
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   │   └── SequenceStepCondition.cs                 # unchanged — read for shape only
│   └── Services/
│       ├── CompositeConditionValidator.cs           # C-1: reference resolution + ordering
│       ├── SequenceStepValidationService.cs         # C-1: pass position maps to the walk
│       ├── SequenceStepConditionEvaluator.cs        # unchanged — run-time already correct
│       └── SequenceRunner.cs                        # unchanged — LoopExitReason already correct
├── GameBot.Service/
│   ├── Services/SequenceExecution/
│   │   └── SequenceExecutionService.cs              # C-2: two loop log attributes
│   └── Swagger/
│       └── ConditionalFlowSchemaDocumentFilter.cs   # C-4: descriptions
└── web-ui/src/
    ├── types/sequenceFlow.ts                        # C-3: expectedState union
    └── lib/validation.ts                            # C-3: tree walk + allow-list

tests/
├── unit/Sequences/
│   ├── CompositeConditionValidationTests.cs         # C-1 unit assertions
│   ├── CompositeConditionPositionValidationTests.cs # D-006 boundary guard (FR-003a)
│   └── SequenceRunnerLoopTests.cs                   # exit-reason three-way + simultaneous
├── integration/Sequences/
│   └── NestedStepOutcomeReferenceIntegrationTests.cs # nested refs across variants and slots
└── contract/
    ├── Sequences/CompositeConditionContractTests.cs  # C-1 at the API boundary (400 vs 201)
    ├── Sequences/SequenceLoopExitReasonContractTests.cs  # FR-011a guard on the run response
    ├── Sequences/SequencePerStepConditionsOpenApiTests.cs # C-4 assertions
    └── ExecutionLogs/ExecutionLogsLoopExitReasonContractTests.cs # C-2 in the persisted run log

docs/architecture.md                                  # FR-016 retest conclusion
CHANGELOG.md                                          # user-visible changes
specs/STATUS.md                                       # this spec's row
```

**Structure Decision**: No new project or directory. Every change lands in a file
that already owns the behaviour, and every test beside the tests for the feature it
belongs to — per the research decision that a separate "issue 193" suite would be
orphaned as soon as those files move.

## Implementation Approach

### C-1 — Reference checks for a composite-nested `commandOutcome`

`CompositeConditionValidator.Validate` currently receives
`(condition, stepLabel, errors, validateLeafAtRoot)` and has no way to resolve a
reference. Thread the two inputs the per-step validator already builds — the
`positionByStepId` map and the referencing step's own authored position — through
`Validate` into `Walk`, and extend the existing `CommandOutcomeStepCondition` case
with the same two checks the per-step validator applies, worded with the `$`-rooted
path.

Make the new inputs **optional**. `CompositeConditionValidator.Validate` is called
from more than one site (step guard and break guard in
`SequenceStepValidationService`, and `FileSequenceRepository` walks composites too);
a caller that cannot supply the maps keeps today's shape-only behaviour rather than
being forced to fabricate them. This keeps the change additive at every call site
and avoids a repository-layer 500 turning into a different failure.

**Why not** move the checks into the per-step validator: the composite walk already
owns traversal, depth limiting and path rendering. A second traversal would be two
things to keep in step.

### C-2 — Loop exit reason in the persisted run log

In `SequenceExecutionService`, the loop branch already reads `step.LoopIterations`
and `step.Message` from the domain `StepResult`. Add `brokeVia` and
`exhaustedMaxIterations` to that entry's attribute dictionary, read from
`step.ExitReason`, with nulls when it is absent. Additive keys in an open dictionary,
exactly as `cancellationReason` / `timeLimitMs` were added before.

### C-3 — Web-UI client rules

In `validation.ts`, replace the flat `steps.findIndex` with a walk that flattens the
tree in authored order (descending `body` and `elseBody`) and resolves against that,
keeping both the resolution and the ordering rule. Extend the `expectedState`
allow-list to all five values, and widen the `expectedState` union in
`sequenceFlow.ts` to match. Leave `PerStepConditionType` alone — composites stay
unauthorable in the UI and that is recorded, not fixed.

### C-4 / FR-016 — The written outcome

OpenAPI descriptions for reference scope, ordering, the five `expectedState` values,
and the loop `exitReason` shape. Then the retest conclusion in
`docs/architecture.md`: each half's status, the three gaps closed here, the absent
web-UI composite authoring, and the inherited D-006 boundary — with the tests named
so the conclusion can be re-run rather than trusted (FR-017).

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| C-1 rejects a stored sequence on its next save | Only one whose composite-nested reference is already dangling or forward — which fails at run time today. Stated in the contract and spec A-005; a test pins that a *valid* nested reference in a composite still saves, so the fix cannot over-reach. |
| C-1 accidentally validates leaves in `while`/`repeatUntil`, reversing D-006 | An explicit boundary test (FR-003a) asserts a bare dangling reference there is still accepted. Already present in `CompositeConditionPositionValidationTests`; extended rather than replaced. |
| Contract tests share a data directory and can pollute each other | Distinct sequence ids per test and cleanup, per known harness behaviour in this repo. |
| `web-ui` gates are noisy | Green gate is `vite build` + `jest`; `lint` and `tsc --noEmit` have pre-existing unrelated failures. Do not treat those as this feature's regressions. |
| Analyzer failure from a method grown too large | The changes add parameters and a short case body, not new methods on a large type. If an analyzer does fire, extract the check into a small private helper rather than suppressing. |

## Phase Status

- [x] Phase 0 — research complete ([research.md](./research.md), F-001..F-008; no
      unresolved unknowns)
- [x] Phase 1 — design complete ([data-model.md](./data-model.md),
      [contracts/](./contracts/), [quickstart.md](./quickstart.md); agent context
      updated)
- [ ] Phase 2 — tasks (`/speckit-tasks`)
