# Implementation Plan: Queue-Start and After-Every-Step Scheduling

**Branch**: `060-queue-start-after-every-scheduling` | **Date**: 2026-06-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/060-queue-start-after-every-scheduling/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Two changes to queue-template scheduling, building directly on features 053 (schedule types) and 059 (relative timers):

1. **New "At Queue Start" schedule option** — a new `ScheduleType.AtQueueStart` enum value. Entries with this type run **once per run**, in template order, **before** the run loop begins (before the first iteration's timer evaluation and before the first `OncePerRun` step). They **count toward** the run's `executed` total (per clarification), and a failure is non-fatal (recorded in `failed`, run continues), consistent with `OncePerRun` handling. Cycling does not repeat them.

2. **Rename "Every Step" → "After Every Step" (display label only)** — per clarification, the underlying enum/identifier `EveryStep` is **unchanged**; only the operator-facing label changes (UI dropdown + badge). Behavior is identical to today (fires after each `OncePerRun` step and once after the final step; never self-triggers). This keeps existing templates and API/script clients fully backward compatible — no data migration, no new accepted/returned identifier.

No new endpoint is required: both options flow through the existing `POST /api/queue-templates` (save) and `GET /api/queue-templates/{id}` (read). `AtQueueStart` is automatically accepted by the existing `Enum.TryParse` + `Enum.IsDefined` validation once added to the enum; the human-readable "accepted values" error string is updated. The UI dropdown and badges are data-driven from a label map, so adding the option and renaming the label are localized edits.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); TypeScript + React (web UI, Vite + Jest)
**Primary Dependencies**: ASP.NET Core Minimal API, `GameBot.Domain` / `GameBot.Service`, Microsoft.Extensions.Logging; web UI: React, existing `lib/api` client. No new external packages.
**Storage**: Existing file-backed JSON template store (`IQueueTemplateRepository`). `ScheduleType` serializes as its string name via `JsonStringEnumConverter`; adding `AtQueueStart` is additive and backward compatible. Existing `EveryStep` values keep serializing/deserializing unchanged.
**Testing**: xUnit + coverlet (backend: contract/integration/unit); Jest + Testing Library (web UI). `vite build` + `jest` is the real web-ui green gate (lint/tsc have pre-existing failures).
**Target Platform**: Windows desktop service (ASP.NET Core host + static web UI)
**Project Type**: Web application (ASP.NET Core backend + React frontend) — Option 2 structure.
**Performance Goals**: At-queue-start execution adds O(at-queue-start entries) sequential sequence runs once per run; iteration-boundary cost is unchanged. No new I/O, no new ADB round-trips, no hot-path allocations.
**Constraints**: At-queue-start entries MUST execute before any timer evaluation and before the first `OncePerRun` step (FR-003/FR-014); once per run, not per cycle (FR-004); count toward `executed` (FR-015); "After Every Step" behavior and its `EveryStep` identifier MUST be unchanged (FR-002/FR-005/FR-006/FR-010); default schedule option unchanged (FR-011).
**Scale/Scope**: Operator-scale (≤50 templates, ≤100 entries each). ~1 new enum value, runtime-loop pre-pass, validation-string + doc updates, UI label-map + badge edits, DTO doc updates, ~15-20 new/updated tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code Quality Discipline | PASS | Additive, localized changes: one new enum value, one execution-service pre-pass block, one validation-string update, UI label-map edits, DTO/doc comment updates. No new dependencies. CamelCase identifiers (`AtQueueStart`); public members documented. |
| II. Testing Standards | PASS (plan) | New unit tests: at-queue-start runs first / in template order / before timers / counts toward `executed` / once-per-run on cycling / non-fatal failure / only-at-queue-start template. Contract tests: save+read `AtQueueStart`; reject invalid; `EveryStep` still round-trips (backward compat). Integration: end-to-end ordering. Web-ui Jest: "At Queue Start" option + badge; "After Every Step" label rename; round-trip. Coverage ≥80% line / ≥70% branch for touched areas. |
| III. User Experience Consistency | PASS | New option follows the existing schedule-option vocabulary additively; same `{ error: { code, message, hint } }` envelope for invalid schedule types; UI reuses the existing dropdown + badge pattern; the rename is label-only with no breaking API change (identifier `EveryStep` preserved). |
| IV. Performance Requirements | PASS | At-queue-start is a one-time, O(n) sequential pass over a small operator-scale set; iteration-boundary cadence unchanged; no I/O or new ADB calls. |
| Quality Gates – DoD | PASS (plan) | No underscores in method names; extended scheduleType documented in contracts/ + quickstart; public DTO/domain members documented; web-ui validated with `vite build` + `jest`; no user-visible breaking change (label-only rename, additive option). |

No violations — Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/060-queue-start-after-every-scheduling/
├── spec.md              # Feature specification (complete, clarified)
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI fragment)
│   └── queue-template-schedule-types.openapi.yaml
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── QueueTemplates/
│       └── ScheduleType.cs                    # + AtQueueStart = 3; doc note: EveryStep displays as "After Every Step"
└── GameBot.Service/
    ├── Contracts/QueueTemplates/
    │   ├── TemplateEntrySaveRequest.cs        # doc: accepted scheduleType values now include AtQueueStart
    │   └── QueueTemplateDetailResponse.cs     # doc: ScheduleType may be "AtQueueStart"
    ├── Endpoints/
    │   └── QueueTemplatesEndpoints.cs         # update "accepted values" error string to include AtQueueStart
    └── Services/QueueExecution/
        └── QueueExecutionService.cs           # partition AtQueueStart entries; run-start pre-pass (before loop); count toward executed

src/web-ui/src/
├── services/
│   └── queueTemplates.ts                      # ScheduleType union + 'AtQueueStart'
├── components/queues/
│   └── QueueEntryList.tsx                     # SCHEDULE_LABELS: EveryStep→'After Every Step', + AtQueueStart:'At Queue Start'; + badge
└── pages/
    └── QueuesPage.tsx                         # no behavioral change (scheduleType passes through); verify defaults

tests/
├── unit/
│   └── Queues/
│       └── QueueExecutionServiceTests.cs      # + at-queue-start: order/before-timers/counts/once-per-run/non-fatal/only-start
├── contract/QueueTemplates/
│   └── QueueTemplatesApiContractTests.cs      # + AtQueueStart accept/return/reject-invalid; EveryStep backward-compat round-trip
├── integration/QueueTemplates/
│   └── QueueTemplatesScheduleTypeTests.cs     # + at-queue-start end-to-end ordering
└── (web-ui Jest, colocated __tests__/)
    ├── QueueEntryList.test.tsx                 # + At Queue Start option/badge; After Every Step label
    └── QueuesPage.templates.spec.tsx          # + AtQueueStart round-trip
```

**Structure Decision**: All changes fit the existing `GameBot.Domain` / `GameBot.Service` / `src/web-ui` / `tests` layout established by features 053/059. `AtQueueStart` is a sibling enum value; the execution pre-pass mirrors the existing partition-and-run pattern in `QueueExecutionService`. The "After Every Step" rename is intentionally a label-only change at the UI layer (and doc comments), preserving the `EveryStep` identifier end-to-end for backward compatibility — so it touches no persistence, validation-accept logic, or execution behavior.

## Complexity Tracking

No constitution violations. All changes are additive (new enum value, runtime pre-pass, UI label edits) or label-only (rename), within the existing project structure and scheduling patterns established by features 053/059.
