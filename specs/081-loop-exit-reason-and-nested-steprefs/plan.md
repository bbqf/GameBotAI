# Implementation Plan: Loop Exit Reason & Nested Step-Outcome References

**Branch**: `081-loop-exit-reason-and-nested-steprefs` | **Date**: 2026-09-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/081-loop-exit-reason-and-nested-steprefs/spec.md`

## Summary

Deliver FR-001 from the PNS project's feature-request tracker
(`C:\src\PNS\docs\api-feature-requests.md`), two additive changes to sequence
authoring/execution:

1. **Loop exit reason** — a `Loop` step's `StepResult` gains a structured
   `ExitReason { BrokeVia: string?, ExhaustedMaxIterations: bool }`, populated by
   every loop-execution path (count/while/repeat-until), including when the firing
   `Break` is nested inside an `If` body within the loop.
2. **Nested `stepRef` resolution** — `SequenceStepValidationService.ValidateStepCondition`
   resolves a `commandOutcome` condition's `stepRef` against every step reachable
   from the sequence root (not just the condition's immediate sibling list), using
   the sequence's authored (document) order to keep enforcing "must reference a
   prior step" without needing runtime information. This requires two pieces of
   supporting plumbing, both necessary for the feature to deliver its stated value
   (querying whether a nested `Break` fired), not optional extras:
   - `Break` step outcomes (`"break"`/`"no_break"`) must actually be recorded into
     `SequenceRunner`'s runtime `stepOutcomes` dictionary — today they are written
     only to the execution log, never to the dictionary `commandOutcome` resolves
     against, so even an already-legal same-body reference to a `Break` step always
     fails at runtime with "reference unavailable."
   - `break`/`no_break` must be added to `SequenceStepValidationService`'s allowed
     `commandOutcome` `expectedState` values (currently only
     `success|failed|skipped`).

## Technical Context

**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: ASP.NET Core Minimal APIs (GameBot.Service), no new
third-party dependencies required
**Storage**: File-backed repositories (`FileSequenceRepository` et al.) — unchanged;
no schema migration (the `Loop` `StepResult` is serialized as-is with no persisted
contract to migrate)
**Testing**: xUnit 2.7.1 across `tests/unit`, `tests/integration`, `tests/contract`
(GameBot.UnitTests / GameBot.IntegrationTests / GameBot.ContractTests)
**Target Platform**: Windows service host (existing GameBot.Service)
**Project Type**: Backend web service (single solution, layered: Domain / Emulator /
Service)
**Performance Goals**: No new performance budget — additive fields on an existing
per-step result object and a widened (still O(sequence size), still validation-time
only) lookup; no measurable latency/throughput change expected on the low-frequency
sequence-authoring and execution paths this touches.
**Constraints**: MUST NOT change behavior for any already-valid sequence, condition,
or Loop/Break execution result (regression-free per spec FR-012); MUST NOT weaken
the existing "reference must be prior" ordering rule, only widen its search scope
(FR-007); MUST NOT change the existing "reference to a step that didn't execute this
run fails the run" semantics (FR-011).
**Scale/Scope**: Two related, additive defect-adjacent changes confined to
`GameBot.Domain/Services/SequenceRunner.cs`,
`GameBot.Domain/Services/SequenceStepValidationService.cs`,
plus their existing test counterparts. No new endpoints, entities, or persisted
schema changes — the widened result shape flows through the existing
`/api/sequences/{id}/execute` response with no separate contract to update (per
research; the Service layer consumes `StepResult` fields directly).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented
maintainer waiver exists.

- **I. Code Quality Discipline**: No new lint/static-analysis suppressions
  anticipated; changes are additive fields/logic in existing, already-modular
  services (`SequenceRunner`, `SequenceStepValidationService`). No underscores in
  new method names. PASS.
- **II. Testing Standards**: Each behavior change requires a failing test added
  first that reproduces the gap through the real execution/validation path (not a
  hand-built result bypassing the engine) — mirrors the precedent set by feature
  080. PASS (gate honored by task design, not yet executed).
- **III. UX Consistency**: The new `ExitReason` field is additive JSON on an
  existing result shape (no breaking change, no new error-response shape to
  invent); the relaxed `stepRef` validation reuses the existing error-message
  format, only changing which references are accepted. PASS.
- **IV. Performance**: No hot-path change; walking the full step tree at
  sequence-creation validation time is bounded by sequence size (already small,
  authoring-time only, not executed per-run). No new benchmark required. PASS.
- **V. Living Documentation**: `docs/architecture.md` §"Break & loop execution and
  the execution-log status vocabulary" documents today's `Loop`/`Break` status
  vocabulary and will be updated in the same PR to describe `ExitReason` and the
  widened `stepRef` scope; spec Status line will be set to `Implemented` once
  implementation lands.

No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/081-loop-exit-reason-and-nested-steprefs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
└── GameBot.Domain/
    └── Services/
        ├── SequenceRunner.cs                    # Loop ExitReason (all 3 loop kinds
        │                                         # + Break-through-If), Break outcome
        │                                         # recording into stepOutcomes
        └── SequenceStepValidationService.cs      # Whole-tree stepRef resolution +
                                                    # break/no_break expectedState

tests/
├── unit/Sequences/
│   ├── SequenceRunnerLoopTests.cs                # ExitReason: count loop
│   ├── SequenceRunnerWhileBreakOnTests.cs        # ExitReason: while loop
│   ├── SequenceRunnerIfTests.cs / SequenceRunnerIfBodyScopeTests.cs
│   │                                              # ExitReason: Break nested in If
│   │                                              # within a Loop body
│   └── LoopValidationTests.cs / IfValidationTests.cs
│                                                  # nested stepRef acceptance,
│                                                  # ordering-still-enforced, and
│                                                  # break/no_break expectedState
└── integration/Sequences/
    └── (new) NestedStepOutcomeReferenceIntegrationTests.cs
                                                    # real HTTP-through-repository:
                                                    # cross-scope Break reference,
                                                    # end-to-end break vs no_break run
```

**Structure Decision**: Existing single-solution layered structure
(`GameBot.Domain` / `GameBot.Emulator` / `GameBot.Service`, with `tests/unit`,
`tests/integration`, `tests/contract`) is unchanged. This feature adds no new
projects or top-level directories — both changes land in existing files, per the
research below.

## Complexity Tracking

*No Constitution Check violations — table intentionally omitted.*
