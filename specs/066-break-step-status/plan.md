# Implementation Plan: Break Step Success/Failure Execution Statuses

**Branch**: `066-break-step-status` | **Date**: 2026-07-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/066-break-step-status/spec.md`

## Summary

Redefine how a break's own outcome is reported in the execution log, for **both** break
mechanisms — the discrete break step inside a loop body (`SequenceStepType.Break`) and the
loop-level break condition (`breakOn`) on a while-style block — and guarantee that a break
which does **not** fire never influences run health.

Concretely:

- A break that **fires** (condition true, or an unconditional "Always break") is a **success**.
- A break that **does not fire** (condition false) is a distinct, neutral **"No break"** outcome
  — not the red "Failed" indicator, and no longer the old `Skipped` representation.
- A break condition that **cannot be evaluated** (runtime error) is treated exactly like a false
  condition: a non-influential "No break" outcome; execution continues and the run is **not**
  failed. This reverses today's behavior where the break-condition catch calls `result.Fail()`
  and stops the run.
- A "No break" outcome never marks the enclosing loop, sequence/run, or any ancestor as failed,
  never alters execution flow, and never contributes to failure counts/alerts.

Technical approach — a thin vertical slice through the existing three layers, additive except
for the two behavior reversals (error → no-break; false → "No break" instead of `Skipped`):

1. **Domain (`SequenceRunner`).** Introduce a single canonical outcome vocabulary for breaks:
   `break` (fired) and `no_break` (did not fire, whether false or eval-error). In
   `ExecuteLoopBodyAsync`, the condition-false branch records `no_break` instead of
   `Skipped`/`continue`, and the condition-error `catch` records `no_break` and continues
   instead of calling `Fail()` + early-stop. In `ExecuteWhileBlockAsync`, wrap each `breakOn`
   evaluation in a guard that treats an exception as `false` (no break) so a broken breakOn
   condition can no longer throw out and fail the run; the block's existing `"true"` end-status
   continues to represent "break fired" (success).

2. **Service (`ExecutionLogService` mapping).** Map the break outcomes to node statuses so the
   tree/grid render correctly: `break` → `success`, `no_break` → a new neutral `no_break` node
   status (distinct from `failure` and from `skipped`). This also fixes a latent miscolor where
   `MapStepStatus("break")` currently falls through to `failure`.

3. **UI (web-ui).** Extend `ExecutionTreeNodeStatus` with `no_break`, render it as a distinct,
   neutral "No break" badge/label in the execution-log grid (`executionLogGrid.ts` /
   `ExecutionLogs.tsx` + CSS), separate from the red failed styling.

No new endpoints, no schema/persistence changes, no new dependencies. Authoring
(`BreakStepRow`) is unchanged — this is purely about how the *outcome* of a break is reported and
how it (does not) propagate.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (`GameBot.Domain` + `GameBot.Service`); TypeScript + React 18
(web UI, Vite + Jest).
**Primary Dependencies**: ASP.NET Core Minimal API, Microsoft.Extensions.Logging; web UI: React,
existing execution-log grid/tree components and `executionLogsApi` client. No new packages.
**Storage**: None added. Break outcomes flow through the existing in-memory `StepResult`
(`Status`, `ActionOutcome`, `ConditionResult`, `Message`) → `ExecutionDetailItem` →
`ExecutionStepOutcome` projection. No persisted-log format field is added or removed; only the
*values* carried in the existing `actionOutcome`/`status` attributes change for break steps.
**Testing**: xUnit + coverlet (backend unit/contract/integration); Jest + React Testing Library
(web UI). `vite build` + `jest` is the real web-ui green gate — lint / `tsc --noEmit` have
pre-existing failures (see memory), so they are not the gate.
**Target Platform**: Windows desktop service (ASP.NET Core host serving the static web UI).
**Project Type**: Web application (C# backend + React SPA) — existing
`GameBot.Domain` / `GameBot.Service` / `src/web-ui` / `tests` layout.
**Performance Goals**: No new cost. The condition-false / error branches already run today; this
changes only the recorded outcome value. `breakOn` guarding adds a `try/catch` around an
evaluation that already happens. Well within the conditional-step p95 ≤ 200 ms budget; no I/O, no
ADB round-trip added.
**Constraints**: MUST NOT influence run health (FR-004–FR-008): a `no_break` outcome must keep
`SequenceExecutionResult.Status` = `Succeeded` (no `Fail()` call on the break path), map to a
non-`failure` node status, and stay out of failure counts. MUST NOT alter execution flow
(FR-006): the condition-false and error branches continue exactly as the false branch does today
(fall through to the next body step / next iteration). MUST apply to every loop construct hosting
breaks (FR-009) and to the loop-level `breakOn` (FR-010). Distinct neutral "No break" indicator,
never reusing `failure` styling (Clarification 2026-07-01).
**Scale/Scope**: Operator scale. ~3 branches changed in `SequenceRunner` (loop-body false,
loop-body error, `breakOn` guard), 1–2 mapping lines in `ExecutionLogService`, 1 UI status token
+ label/CSS, and ~10–16 new/updated tests (unit + web-ui). One existing test
(`CountLoopBreakConditionThrowsLoopFails`) is rewritten to assert the new no-fail behavior.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS (plan) | Small, localized, mostly additive edits along existing break/loop code paths. One shared constant set for the break outcome tokens (`break` / `no_break`) avoids magic strings. CamelCase methods (e.g. a `RecordNoBreak` helper, `TryEvaluateBreakOn`). No new dependencies; removes the `Skipped`/`continue` special-case and the `Fail()`-on-error branch (net dead-path reduction). Public members keep XML docs. |
| II. Testing Standards | PASS (plan) | Bug-fix-style TDD: the reversed behaviors get failing-first tests. Unit (`SequenceRunnerLoopTests`): fired→success/`break`; condition-false→`no_break` (not `Skipped`) + loop continues + run `Succeeded`; **condition-error→`no_break` + loop continues + run `Succeeded`** (rewrites `CountLoopBreakConditionThrowsLoopFails`); unconditional→success; nested-loop non-influence; `breakOn` fired→block `true`, `breakOn` error→no-throw/continue. Mapping unit test: `no_break`→neutral node status, `break`→success. Web-ui Jest: grid renders distinct "No break" badge, run row stays non-failed. Coverage ≥80% line / ≥70% branch for touched areas. |
| III. User Experience Consistency | PASS | Introduces one clearly-labelled neutral "No break" state, consistent with the existing status vocabulary/chips; does not reuse the alarming red "Failed" for normal loop iterations; error messages retain the condition detail (FR-007). No breaking change to authoring or API. |
| IV. Performance Requirements | PASS | No added I/O or polling; only the recorded outcome value changes on branches that already execute. `breakOn` `try/catch` is negligible. Perf note included below. |
| V. Living Documentation | PASS (plan) | `docs/architecture.md` break/loop-execution and execution-log status vocabulary updated (refreshed "Last reviewed" date) in the implementation PR. This spec's `Status` and `specs/STATUS.md` updated on completion. No earlier spec is superseded — this refines the break behavior introduced in 014/034/042 without replacing them; those specs' `Status` lines get an "iterated by 066" note if warranted. |
| Quality Gates – DoD | PASS (plan) | No underscores in method names (the `no_break` token is a data value, not a method); behavior pinned in `contracts/` + `quickstart.md`; changelog entry for the user-visible log change; web-ui validated with `vite build` + `jest`. |

No violations → Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/066-break-step-status/
├── spec.md              # Feature specification (clarified, 3 Q&A)
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (outcome vocabulary + status mapping)
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/
│   └── break-step-outcome.md   # Break outcome tokens + node-status mapping + non-influence rules
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

No OpenAPI fragment: the feature adds **no new HTTP endpoint** and no request/response shape
change. The contract doc instead pins the internal outcome-token → node-status mapping and the
non-influence invariants that tests assert against.

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Services/
│       └── SequenceRunner.cs
│           # ExecuteLoopBodyAsync: condition-false branch -> record `no_break` (was Skipped/continue)
│           # ExecuteLoopBodyAsync: condition-error catch  -> record `no_break` + continue (was Fail()+earlyStop)
│           # DescribeBreakCondition message retained on both branches (FR-007)
│           # ExecuteWhileBlockAsync: guard breakOn evaluation (breakOn-start & breakOn-mid) so an
│           #   evaluation error is treated as false (no break); block `"true"` still = break fired
│           # (optional) small BreakOutcomes constants: "break" / "no_break"
└── GameBot.Service/
    └── Services/
        └── ExecutionLog/
            └── ExecutionLogService.cs
                # MapStepStatus: "break" -> "success"; "no_break" -> "no_break" (new neutral node status)
        (SequenceExecutionService.cs: the actionOutcome for break steps now flows as
         "break"/"no_break"; verify the flat detail-item mapping passes them through unchanged —
         no Skipped special-casing needed for break)

src/web-ui/src/
├── services/
│   └── executionLogsApi.ts          # ExecutionTreeNodeStatus += 'no_break'
├── pages/
│   ├── executionLogGrid.ts          # surface 'no_break' status value for the grid row
│   ├── ExecutionLogs.tsx            # label/aria for the 'no_break' status cell
│   └── ExecutionLogs.css (or execution-logs styles)  # distinct neutral badge for [data-status="no_break"]
└── (StatusChip.tsx unchanged — session/run chip; a run with only no_break breaks stays Succeeded)

tests/
├── unit/
│   └── Sequences/
│       └── SequenceRunnerLoopTests.cs
│           # rewrite CountLoopBreakConditionThrowsLoopFails -> ...ErrorRecordedAsNoBreakLoopContinues
│           # update CountLoopConditionalBreakNeverTriggered... to assert `no_break` (not Skipped) + run Succeeded
│           # add fired->success/`break`; nested-loop non-influence; run-level Status Succeeded
│       └── SequenceRunnerWhileBreakOnTests.cs (new or existing while tests)
│           # breakOn fired -> block "true"; breakOn evaluation error -> no throw, loop continues, not failed
├── unit/ (Service)
│   └── ExecutionLog/ExecutionLogServiceMapStepStatusTests.cs (or existing projection test)
│           # "break" -> success, "no_break" -> no_break
└── (web-ui Jest colocated __tests__/)
    └── executionLogGrid + ExecutionLogs: renders distinct "No break" badge; run row not failed
```

**Structure Decision**: All changes fit the existing four-project layout; no new files are
strictly required beyond tests and a small optional constants holder. The core behavior lives in
`SequenceRunner` (Domain); the display correctness lives in `ExecutionLogService` (Service) and
the grid (web-ui). Keeping a single canonical `break` / `no_break` outcome vocabulary shared by
both the loop-body break step and the (block-level) `breakOn` end-state avoids divergent status
strings across the two mechanisms.

## Complexity Tracking

No constitution violations. The two behavior reversals (error→no-break, false→`no_break` instead
of `Skipped`) are simplifications of existing branches, not added complexity.

## Performance note (implementation, Principle IV)

This change does not add I/O, polling, or ADB round-trips. The condition-false and
condition-error branches already execute today; only the recorded outcome value changes. The
`breakOn` guard wraps an evaluation that already runs in a `try/catch`, which is negligible. No
hot-path regression is possible; no benchmark required.
