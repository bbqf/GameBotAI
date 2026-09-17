# Implementation Plan: Before-Each-Run Schedule Type

**Branch**: `095-before-each-run-schedule` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/095-before-each-run-schedule/spec.md` (issue #202)

## Summary

Add a fifth queue-template schedule type, `BeforeEachRun` ("Before Each Run"). The queue run loop gains a
Before Each Run pass — the mirror of the existing every-step pass — that runs all enabled BeforeEachRun
entries in template order immediately before the first *triggering firing* (time-of-day timer, its daily
retry, relative timer, live schedule, self-reschedule Timer / AtQueueStart / OncePerRun firing) of each
scheduling-loop iteration, at most once per iteration. Accounting mirrors After Every Step (not counted
as executed; failures counted and non-fatal). The type is accepted by the template API, surfaced by the
queue monitor, documented in the contract XML docs, and exposed as a new "Before each run" area in the
web template editor.

## Technical Context

**Language/Version**: C# / .NET 9 (service, domain); TypeScript + React (web-ui, Vite)
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, dnd-kit (editor), Jest
**Storage**: JSON file repositories (queue templates) — enum persisted by name via `JsonStringEnumConverter`; no migration
**Testing**: xUnit unit (`tests/unit`), integration (`tests/integration`), contract (`tests/contract`); Jest for web-ui
**Target Platform**: Windows service + browser UI
**Project Type**: web-service + web application
**Performance Goals**: No added cost for templates without BeforeEachRun entries (one empty-list check per firing); the pass itself costs only the configured sequences' own run time, once per wake-up
**Constraints**: Existing schedule types' behaviour unchanged (FR-014); loop-safety preserved (pass never re-enters itself or the every-step pass)
**Scale/Scope**: ~10 source files, ~6 test files, docs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate | Status | Notes |
|------|--------|-------|
| I. Code quality | PASS | Pass implemented as a local function beside `RunEveryStepPassAsync`, same idiom; CamelCase names; XML docs on the new enum members |
| II. Testing | PASS | Unit tests for engine ordering/once-per-wake-up/accounting/non-triggers; contract + integration tests for the API; Jest tests for editor areas |
| III. UX consistency | PASS | Wire id + operator label split identical to `EveryStep`/"After Every Step"; error message lists accepted values |
| IV. Performance | PASS | Hot path adds a boolean + count check per firing; perf note in PR |
| V. Living docs | PASS | `docs/architecture.md` (schedule list + Last reviewed), `specs/STATUS.md` row, spec Status → Implemented |

Post-design re-check: PASS (no new projects, no persistence change, no violations).

## Project Structure

### Documentation (this feature)

```text
specs/095-before-each-run-schedule/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/api-changes.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/QueueTemplates/ScheduleType.cs                         # + BeforeEachRun = 4
src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs                  # accepted-values error text
src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs  # XML doc
src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs # XML doc
src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs              # ScheduleKind XML doc
src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs      # Before Each Run pass
src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs       # + ScheduleKind.BeforeEachRun
src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs        # list once, label, KindFor/ReasonFor
src/web-ui/src/services/queueTemplates.ts                                 # ScheduleType union
src/web-ui/src/services/queues.ts                                         # ScheduleKind union
src/web-ui/src/components/queues/schedulingAreas.ts                       # beforeEachRun area
src/web-ui/src/components/queues/QueueSchedulingAreas.tsx                 # render area
src/web-ui/src/components/queues/SchedulingSequenceCard.tsx               # label + badge
src/web-ui/src/components/queues/QueueEntryList.tsx                       # label + badge

tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs # engine behaviour (partial of QueueExecutionServiceTests)
tests/unit/Queues/QueueMonitorServiceTests.cs        # monitor projection
tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs
tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs
src/web-ui/src/components/queues/__tests__/schedulingAreas.test.ts
src/web-ui/src/components/queues/__tests__/QueueSchedulingAreas.test.tsx

docs/architecture.md, specs/STATUS.md, CHANGELOG.md
```

**Structure Decision**: Existing single service + web-ui layout; no new projects or files beyond tests.

## Complexity Tracking

No violations.
