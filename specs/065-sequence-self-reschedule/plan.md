# Implementation Plan: Sequence Self-Rescheduling into the Originating Queue Run

**Branch**: `065-sequence-self-reschedule` | **Date**: 2026-06-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/065-sequence-self-reschedule/spec.md`

## Summary

Add an authorable **self-reschedule sequence action** that, when reached during a queue-driven run,
schedules **one additional firing of the same sequence into the current run** using any of the four
existing schedule options (**At Queue Start**, **Once Per Run**, **Timer** time-of-day/relative
offset, **After Every Step**). It is placeable under the sequence's existing IF/conditional flow so
the decision is made live; it applies only to the current run and is never persisted; it is reflected
in the execution logs; and when the sequence was not started from a queue it is a **successful
no-op**.

Technical approach — three layers, all additive:

1. **Origin propagation (Service).** `QueueExecutionService` already creates each sequence firing's
   `ExecutionLogContext`. Add an `OriginatingQueueId` to that context, set it when the queue run
   launches a sequence, and copy it through nested invocations (FR-018). A running sequence with a
   non-empty `OriginatingQueueId` is "started from a queue"; empty → no-op success (FR-011).

2. **Ephemeral run-scoped schedule registers (Service).** The active-run registry (today the
   private `_runs` dictionary inside `QueueExecutionService`) is extracted into a singleton
   `IQueueRunRegistry` so it can be shared without a DI cycle. `QueueRunHandle` gains per-option,
   non-persisted registers for self-rescheduled firings (next-cycle-start, current-cycle once-per-run
   append, after-every-step set, and resolved timer instants). The run loop drains each register at
   the matching boundary, exactly mirroring the existing template/live behaviors for each option
   (features 053/059/060). A self-reschedule whose firing never becomes due is discarded with the
   handle (FR-015) and never marks the run failed.

3. **The action itself (Domain + UI).** A new `reschedule-self` action type carried in the existing
   generic `SequenceActionPayload`. `SequenceRunner` dispatches non-command/non-wait actions through
   a new optional `actionDispatcher` callback; `SequenceExecutionService` supplies a dispatcher that
   reads `OriginatingQueueId` and calls a narrow `ISelfRescheduleCoordinator` (backed by the run
   registry) to inject the ephemeral entry, recording the decision (option, resolved timing,
   scheduled vs. no-op + reason) as a sequence step in the execution log (FR-013). The sequence
   editor gains the action as a third authorable action type (alongside `command` and
   `WaitForImage`) with the same schedule-option inputs used in the queue-template editor; it
   round-trips through the unchanged sequence authoring API as an opaque action payload (FR-001a).

No new external dependencies. Elapsed/wall-clock reads reuse the `TimeProvider` already adopted by
`QueueExecutionService` in feature 059, keeping firing timing deterministic and unit-testable.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend `GameBot.Domain` + `GameBot.Service`); TypeScript +
React 18 (web UI, Vite + Jest)
**Primary Dependencies**: ASP.NET Core Minimal API, Microsoft.Extensions.Logging,
`System.TimeProvider` (built-in); web UI: React, existing `lib/api` client, existing sequence editor
(`SequencesPage.tsx`) and the queue-template timer inputs (`QueueEntryList.tsx`). No new packages.
**Storage**: File-backed JSON sequence store (`ISequenceRepository`) — the action persists inside the
existing `SequenceActionPayload` (`Type` + `Parameters` dictionary), additive and backward
compatible. All run schedules are **in-memory only**, on `QueueRunHandle`; nothing about a reschedule
is ever written to the queue template or any saved config (FR-010, SC-005).
**Testing**: xUnit + coverlet (backend contract/integration/unit); Jest + React Testing Library
(web UI). `vite build` + `jest` is the real web-ui green gate (lint/`tsc` have pre-existing
failures — see memory).
**Target Platform**: Windows desktop service (ASP.NET Core host serving the static web UI).
**Project Type**: Web application (C# backend + React SPA front end) — the existing
`GameBot.Domain` / `GameBot.Service` / `src/web-ui` / `tests` layout.
**Performance Goals**: The action's runtime cost is one register append + a timing resolution — O(1),
no I/O, no ADB round-trip; comfortably within the conditional-step budget of p95 ≤ 200 ms
established in feature 031 (SC-006). Run-loop draining adds O(pending self-reschedules) comparisons
per boundary, identical cadence to existing timer/live evaluation (negligible).
**Constraints**: Reschedule applies to the current run only and MUST NOT persist (FR-010); option
semantics MUST match existing template/live behavior, the only novel case being *At Queue Start
mid-run* (FR-009); After-Every-Step injection MUST be loop-safe (FR-008, feature 060); the no-op path
MUST report success and not terminate the sequence (FR-011/FR-012); pending firings MUST be abandoned
on stop/abort (FR-017); origin MUST propagate through nesting (FR-018).
**Scale/Scope**: Operator scale (≤50 templates, ≤100 entries; a handful of pending self-reschedules
per run). ~1 new action type + validator, ~1 new coordinator interface + run-registry extraction,
new `QueueRunHandle` registers + run-loop drains, `ExecutionLogContext` field, sequence-editor action
panel, ~30–45 new tests across unit/contract/integration/Jest.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS (plan) | Additive, localized changes layered onto established scheduling patterns. New types small and cohesive (`ISelfRescheduleCoordinator`, `IQueueRunRegistry`, a `SelfReschedulePayload`/validator, per-option handle registers). CamelCase method names (`ScheduleSelf`, `ResolveSelfRescheduleTiming`, `DrainNextCycleStart`). No new dependencies. The registry extraction removes duplicated run-lookup state rather than adding dead code. Public members documented. |
| II. Testing Standards | PASS (plan) | Each option's firing timing covered by unit tests via fake `TimeProvider`: once-per-run append fires before cycle end; relative/time-of-day timer fire-once; after-every-step registers + loop-safe (no unbounded chain); at-queue-start next-cycle vs non-cycling fallback. No-op-success path (no originating queue) and origin-through-nesting covered. Contract tests: action payload round-trips through sequence create/read/update; validation rejects malformed option/timer combos. Integration: end-to-end queue run that self-reschedules and observes the extra firing + log entries. Web-ui Jest: action authorable under an IF branch, option inputs mirror template editor. Coverage ≥80% line / ≥70% branch for touched areas. |
| III. User Experience Consistency | PASS | Option vocabulary and inputs reuse the queue-template editor verbatim (no new schedule concepts surfaced); validation errors use the existing `{ error: { code, message, hint } }` envelope; the no-op path is a *success* exit with an explicit log reason; logs present the reschedule and its firing consistently with features 059/063. No breaking change — the action payload is additive and optional. |
| IV. Performance Requirements | PASS | Action dispatch is O(1) in-memory with no I/O (SC-006); run-loop draining is O(pending) per boundary on an operator-scale set, same cadence as today's timer/live checks. Perf note will accompany the run-loop change. |
| V. Living Documentation | PASS (plan) | `docs/architecture.md` will gain the self-reschedule action in the domain model / capability map / sequence-action surface (refreshed "Last reviewed" date) in the implementation PR; this spec's `Status` and `specs/STATUS.md` updated on completion. No earlier spec is superseded — this extends 059/060/031–033 rather than replacing them. |
| Quality Gates – DoD | PASS (plan) | No underscores in method names; new behavior documented in `contracts/` + `quickstart.md`; public domain/DTO members documented; web-ui validated with `vite build` + `jest`; no user-visible breaking change. |

No violations → Complexity Tracking left empty. (The `IQueueRunRegistry` extraction is a
cycle-avoidance refactor, not added complexity — it relocates state that already exists.)

## Project Structure

### Documentation (this feature)

```text
specs/065-sequence-self-reschedule/
├── spec.md              # Feature specification (clarified, 5 Q&A)
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/           # Phase 1 output
│   └── sequence-self-reschedule-action.md   # Action payload shape + option semantics contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

No OpenAPI fragment: the feature adds **no new HTTP endpoint**. The action is an opaque payload on the
existing sequence create/read/update endpoints; the run-side scheduling is an in-process call. The
contract doc pins the payload schema and the per-option firing semantics instead.

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Actions/
│   │   └── ActionTypes.cs                     # + RescheduleSelf = "reschedule-self"
│   ├── Commands/
│   │   └── SelfReschedule/
│   │       ├── SelfRescheduleOption.cs        # NEW enum: AtQueueStart | OncePerRun | Timer | EveryStep
│   │       └── SelfReschedulePayload.cs       # NEW typed view over SequenceActionPayload.Parameters
│   ├── Services/
│   │   ├── SequenceRunner.cs                  # + optional actionDispatcher callback; dispatch reschedule-self action step
│   │   └── ActionPayloadValidationService.cs  # recognize reschedule-self; validate option + timer params
│   └── (SequenceStep already carries Action: SequenceActionPayload — no change)
└── GameBot.Service/
    ├── Services/ExecutionLog/
    │   └── ExecutionLogContext.cs             # + OriginatingQueueId (propagated through nesting)
    ├── Services/SequenceExecution/
    │   └── SequenceExecutionService.cs        # supply actionDispatcher; read OriginatingQueueId; log decision
    └── Services/QueueExecution/
        ├── IQueueRunRegistry.cs               # NEW: singleton store of active QueueRunHandles (extracted)
        ├── QueueRunRegistry.cs                # NEW: implementation
        ├── ISelfRescheduleCoordinator.cs      # NEW: ScheduleSelf(queueId, sequenceId, option, params) -> result
        ├── SelfRescheduleCoordinator.cs       # NEW: resolve timing + inject ephemeral entry on the handle
        ├── QueueRunHandle.cs                  # + ephemeral self-reschedule registers (per option)
        └── QueueExecutionService.cs           # use registry; drain self-reschedule registers at boundaries; mark firings

src/web-ui/src/
├── types/
│   └── sequenceFlow.ts                        # + 'reschedule-self' action shape + option/timer params
├── pages/
│   └── SequencesPage.tsx                      # actionType gains 'reschedule-self'; option + timer inputs; IF placement
└── components/sequences/ (+ a small RescheduleActionConfig component if SequencesPage grows too large)

src/GameBot.Service/Program.cs                 # register IQueueRunRegistry, ISelfRescheduleCoordinator (singletons)

tests/
├── unit/
│   ├── Queues/
│   │   └── SelfRescheduleCoordinatorTests.cs          # NEW: per-option timing resolution + register injection
│   ├── Queues/QueueExecutionServiceTests.cs           # + run-loop drains each option; loop-safety; not-due-discard
│   └── Sequences/SequenceRunnerActionDispatchTests.cs # NEW: action dispatched; no-op success; non-terminating
├── contract/
│   └── Sequences/SelfRescheduleActionContractTests.cs # NEW: payload round-trips; validation accept/reject
└── integration/
    └── Queues/SelfRescheduleRunIntegrationTests.cs    # NEW: end-to-end run self-reschedules; logs show firing + origin propagation
    (+ web-ui Jest colocated __tests__/ for SequencesPage reschedule action)
```

**Structure Decision**: All changes fit the existing four-project layout. The DI cycle that would
arise from `SequenceExecutionService → QueueExecutionService` (the latter already depends on the
former) is avoided by extracting the active-run registry into a dependency-free singleton
(`IQueueRunRegistry`) that both the queue engine and the new `SelfRescheduleCoordinator` consume. The
action stays a generic `SequenceActionPayload` so the authoring API needs no change; `SequenceRunner`
remains web/queue-agnostic by dispatching the action through an injected callback rather than
referencing any scheduling type directly.

## Complexity Tracking

No constitution violations. The only structural change beyond additive members — extracting the
active-run registry into `IQueueRunRegistry` — exists solely to keep dependency flow acyclic and
relocates state that already lives inside `QueueExecutionService`; it adds no new behavior.
