# Implementation Plan: Relative-Time Sequence Scheduling

**Branch**: `059-relative-schedule-time` | **Date**: 2026-06-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/059-relative-schedule-time/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Extend the existing "timer" schedule type (feature 053) so a timer can be expressed as a **relative duration offset** in addition to the existing absolute wall-clock time-of-day. Two anchors, per clarification:

1. **Template-saved relative offset** — a new `TimerRelativeOffset` (`TimeSpan?`) on `QueueTemplateEntry`. When a `Timer` entry carries an offset instead of a time-of-day, `QueueExecutionService` evaluates it at each iteration boundary against time elapsed since **run start**, fires the sequence **once per run** when the offset has elapsed, and recomputes fresh on every run.
2. **Live relative schedule** — a new ephemeral, per-run scheduling channel. A `POST /api/queues/{id}/live-schedule` endpoint resolves `now + offset` into a target instant and hands it to the running `QueueExecutionService` via a new `ScheduleRelative` method. The run holds pending live schedules in a `ConcurrentDictionary<string,DateTimeOffset>` on its `QueueRunHandle` (keyed by sequence id, most-recent-wins per FR-011), checks them at each iteration boundary, fires once when due, and discards them when the run ends (never persisted). A live schedule may target **any sequence in the library** (validated against `ISequenceRepository`), firing as an additional execution.

Both relative-offset firings (template) and live firings **count toward** the run's `executed` step total (FR-016a) — diverging from time-of-day timers and every-step entries, which do not. To make elapsed-time evaluation deterministic and unit-testable, `QueueExecutionService` adopts the built-in `System.TimeProvider` (default `TimeProvider.System`) for all wall-clock reads, replacing direct `DateTime.Now` calls.

API and UI both gain the relative mode: the queue-template editor lets the operator pick time-of-day vs relative offset per timer entry (minutes + seconds, optional hours), and the running-queue view gains a "schedule in mm:ss" control with a pending indicator.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); TypeScript + React (web UI, Vite + Jest)
**Primary Dependencies**: ASP.NET Core Minimal API, `GameBot.Domain` / `GameBot.Service`, Microsoft.Extensions.Logging, `System.TimeProvider` (built-in); web UI: React, existing `lib/api` client. No new external packages.
**Storage**: Existing file-backed JSON template store (`IQueueTemplateRepository`); `TimeSpan?` serializes as a `"HH:mm:ss"` string via System.Text.Json — additive and backward compatible. Live schedules are in-memory only.
**Testing**: xUnit + coverlet (backend: contract/integration/unit); Jest + Testing Library (web UI). `vite build` + `jest` is the real web-ui green gate (lint/tsc have pre-existing failures).
**Target Platform**: Windows desktop service (ASP.NET Core host + static web UI)
**Performance Goals**: Iteration-boundary scheduling adds O(timer entries + pending live schedules) comparisons per cycle — negligible (<0.01 ms); no new I/O, no ADB round-trips, no allocations on the hot path beyond a few struct comparisons.
**Constraints**: Relative timers MUST be evaluated only at iteration boundaries (FR-014); offset MUST be non-negative with ≥ minute/second precision (FR-002); live schedules MUST be ephemeral (FR-008) and most-recent-wins per sequence (FR-011); existing time-of-day timer / once-per-run / every-step behavior MUST be unchanged (SC-008).
**Scale/Scope**: Operator-scale (≤50 templates, ≤100 entries each); pending live schedules bounded by sequences in the running queue. ~1 new domain property, ~1 new endpoint, ~1 new execution-service method + handle field, runtime-loop additions, DTO/UI additions, ~20-30 new tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS | Additive, localized changes: one new nullable domain property, one new endpoint, one new service method + handle field, runtime-loop branches, additive DTO/UI fields. No new dependencies (`TimeProvider` is built-in). CamelCase method names (e.g., `ScheduleRelative`, `EvaluateRelativeTimers`). Public members documented. |
| II. Testing Standards | PASS (plan) | New unit tests: offset parse/validation; relative-timer fires once after offset (via fake `TimeProvider`); counts toward `executed`; live `ScheduleRelative` upsert / most-recent-wins / not-running rejection / fires once / re-schedule. Contract tests: template API accepts+returns `timerRelativeOffset`, rejects both-modes and negative; live-schedule API happy path + 404/409/400. Integration: relative timer in a run; live schedule against a running queue. Web-ui Jest: editor relative mode + running-queue live-schedule control. Coverage ≥80% line / ≥70% branch for touched areas. Adopting `TimeProvider` also makes the *existing* time-of-day timer tests deterministic. |
| III. User Experience Consistency | PASS | Relative offset follows the existing schedule-type vocabulary additively (`timerRelativeOffset` alongside `timerTimeOfDay`); same `{ error: { code, message, hint } }` envelope for validation failures; UI reuses the existing entry-row schedule controls and badge pattern; new live-schedule control mirrors existing queue action affordances. No breaking change to existing payloads (new fields optional). |
| IV. Performance Requirements | PASS | O(n) per iteration over a small, operator-scale set of timers/pending schedules; no I/O, no N+1, no new ADB calls. Perf note: scheduling checks run only at iteration boundaries, identical cadence to today's timer evaluation. |
| Quality Gates – DoD | PASS (plan) | No underscores in method names; new API documented in contracts/ + quickstart; public DTO/domain members documented; web-ui validated with `vite build` + `jest`; no user-visible breaking change. |

No violations — Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/059-relative-schedule-time/
├── spec.md              # Feature specification (complete, clarified)
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI fragments)
│   ├── queue-template-relative-offset.openapi.yaml
│   └── queue-live-schedule.openapi.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── QueueTemplates/
│       └── QueueTemplateEntry.cs            # + TimerRelativeOffset (TimeSpan?); relative mode when set
└── GameBot.Service/
    ├── Contracts/QueueTemplates/
    │   ├── TemplateEntrySaveRequest.cs      # + TimerRelativeOffset (string?, "HH:mm:ss")
    │   └── QueueTemplateDetailResponse.cs   # + TimerRelativeOffset on QueueTemplateEntryResponse
    ├── Contracts/Queues/
    │   └── LiveScheduleRequest.cs           # NEW: { sequenceId, offset }
    ├── Endpoints/
    │   ├── QueueTemplatesEndpoints.cs       # validate/persist/project TimerRelativeOffset; reject both-modes & negative
    │   └── QueuesEndpoints.cs               # NEW POST {id}/live-schedule (validate offset, sequence exists, run active)
    └── Services/QueueExecution/
        ├── IQueueExecutionService.cs        # + ScheduleRelative(queueId, sequenceId, offset) -> LiveScheduleOutcome
        ├── QueueRunHandle.cs                # + PendingLiveSchedules (ConcurrentDictionary<string,DateTimeOffset>); RunStartedAt
        └── QueueExecutionService.cs         # TimeProvider injection; relative-timer eval vs run start; live-schedule eval; count firings

src/web-ui/src/
├── services/
│   ├── queueTemplates.ts                    # + timerRelativeOffset on DTOs/save payload
│   └── queues.ts                            # + liveScheduleSequence(queueId, sequenceId, offset)
├── components/queues/
│   └── QueueEntryList.tsx                   # timer mode toggle (time-of-day | relative) + offset inputs + badge
└── pages/
    └── QueuesPage.tsx                       # running-queue live-schedule control + pending indicator

tests/
├── unit/
│   └── Queues/
│       ├── QueueExecutionServiceTests.cs            # + relative-timer fires once / counts; live fires once / most-recent-wins / not-running
│       └── RelativeOffsetValidationTests.cs         # NEW: offset parse/validate helper
├── contract/QueueTemplates/
│   └── QueueTemplatesApiContractTests.cs            # + timerRelativeOffset accept/return/reject-both/reject-negative
├── contract/Queues/
│   └── QueueLiveScheduleApiContractTests.cs         # NEW: live-schedule 200/400/404/409 + template-unchanged (SC-004)
├── integration/QueueTemplates/
│   └── QueueTemplatesScheduleTypeTests.cs           # + relative-offset end-to-end run
└── (web-ui Jest, colocated __tests__/)
    ├── QueueEntryList.test.tsx                       # + relative mode rendering/validation
    └── QueuesPage live-schedule control spec
```

**Structure Decision**: All changes fit the existing `GameBot.Domain` / `GameBot.Service` / `src/web-ui` / `tests` layout. The relative offset is modeled as a sibling of the existing `TimerTimeOfDay` on `QueueTemplateEntry` (mode inferred by which is non-null), keeping persistence additive and backward compatible. Live scheduling is layered onto the existing single owner of running queues (`QueueExecutionService` + `QueueRunHandle`) so it requires no new background service and is naturally ephemeral.

## Complexity Tracking

No constitution violations. All changes are additive and within the existing project structure and scheduling patterns established by feature 053.
