# Implementation Plan: Drag-and-Drop Scheduling Areas in the Queue Template Editor

**Branch**: `061-queue-scheduling-areas` | **Date**: 2026-06-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/061-queue-scheduling-areas/spec.md`

## Summary

Replace the queue template editor's single flat sequence list with **four labeled, drag-and-drop areas** — one per schedule option:

- **Start of execution** (full-width top) ↔ `AtQueueStart`
- **Once per run** (upper-left) ↔ `OncePerRun`
- **Scheduled** (lower-left, under "Once per run") ↔ `Timer`
- **After every step** (right column) ↔ `EveryStep`

Operators drag sequence cards **between** areas (which auto-changes the sequence's schedule option to the destination area's type) and **within** an area (which reorders execution). New sequences default to the "Once per run" area. This is a **web-UI-only** change: no API, scheduler, or stored-model changes. It reuses the existing `@dnd-kit` multi-container drag pattern already established in the sequence step editor. Schedule assignment and within-area order round-trip through the existing template save/load path; timer details are retained (inactive) when a card leaves "Scheduled" and restored if it returns.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3 (ESM, Vite 7)
**Primary Dependencies**: `@dnd-kit/core` 6.3, `@dnd-kit/sortable` 10.0, `@dnd-kit/utilities` 3.2 (all already installed and used by `SortableSequenceStepList`/`SortableStepItem`)
**Storage**: None new. Schedule option + timer details persist via the existing `/api/queue-templates` save/load; runtime entry order persists via the existing `/api/queues/{id}/entries` (`replaceQueueEntries`)
**Testing**: Jest + `@testing-library/react` (unit/component), Playwright (e2e). Quality gate per memory: `vite build` + `jest` must be green (lint/`tsc --noEmit` have pre-existing failures and are NOT the gate)
**Target Platform**: Modern browser (web UI served by the GameBot backend)
**Project Type**: Web application — frontend change only (`src/web-ui`)
**Performance Goals**: Editor interaction is local React state; drag operations must feel instant (<16ms frame budget; no API call during a drag). Persistence (save) is an existing, already-acceptable round-trip
**Constraints**: No API/contract/scheduler changes; fully backward compatible with existing templates; reuse existing dnd-kit patterns and CSS conventions; no new runtime dependencies
**Scale/Scope**: One queue template at a time; a handful to a few dozen sequence cards across four areas. New/changed files confined to `src/web-ui/src/components/queues/` and `src/web-ui/src/pages/QueuesPage.tsx`, plus styles and tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality Discipline | PASS — change is contained, modular (new presentational components + a view-model mapping helper), no dead code, CamelCase methods, no new deps. Reuses existing dnd-kit + `DropIndicator` building blocks. |
| II. Testing Standards | PASS — component tests for grouping/empty-areas/badges, drag-reassign and drag-reorder via the same `onDragEnd` handler tested directly (jsdom can't do real pointer DnD, so the reorder/reassign reducer is unit-tested as a pure function), and a `QueuesPage` integration test for save/reload round-trip and the "new sequence → Once per run" default. Existing scheduling/queue suite must stay green (no behavior change). |
| III. User Experience Consistency | PASS — labels, badges, and empty-state hints follow existing conventions; the schedule selector remains available as the non-drag path (keyboard alternative explicitly out of scope per clarification). Error/disabled (running) states preserved. |
| IV. Performance Requirements | PASS — pure client-side state manipulation; no new hot paths or N+1; no API calls mid-drag. Perf note: save reuses the existing single round-trip. |

**Gate result: PASS** (no violations; Complexity Tracking not required).

## Project Structure

### Documentation (this feature)

```text
specs/061-queue-scheduling-areas/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (UI view model)
├── quickstart.md        # Phase 1 output (manual + test walkthrough)
├── contracts/
│   └── scheduling-areas-ui.md   # Phase 1 output (component/interaction contract — no API change)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/web-ui/src/
├── components/queues/
│   ├── QueueSchedulingAreas.tsx        # NEW — four-area layout + DndContext orchestration
│   ├── SchedulingArea.tsx              # NEW — one droppable area (label, empty state, sortable list)
│   ├── SchedulingSequenceCard.tsx      # NEW — one draggable/sortable sequence card (badge, timer controls, remove)
│   ├── schedulingAreas.ts              # NEW — pure view-model: group entries by area, drag reducer, area↔scheduleType map
│   ├── QueueEntryList.tsx              # EXISTING — kept for non-area callers / fallback; areas component supersedes it in the editor
│   └── __tests__/
│       ├── QueueSchedulingAreas.test.tsx   # NEW — grouping, empty areas, badges, render
│       └── schedulingAreas.test.ts         # NEW — pure reducer: reassign + reorder + default add + timer retention
├── pages/
│   ├── QueuesPage.tsx                  # EDIT — render areas component; route new entries to OncePerRun; order-aware save
│   └── __tests__/QueuesPage.templates.spec.tsx  # EDIT — round-trip + default-area integration tests
└── components/queues/
    └── QueueSchedulingAreas.css        # NEW — four-area responsive grid + area/card styling (co-located; follows existing reorderable-list/empty-state conventions)
```

**Structure Decision**: Web application, frontend-only. All work lives under `src/web-ui/src`. The four-area editor is implemented as a small component tree (`QueueSchedulingAreas` → `SchedulingArea` → `SchedulingSequenceCard`) backed by a pure, fully unit-testable view-model module (`schedulingAreas.ts`) so the drag/reorder/reassign logic is verifiable without simulating pointer events. `QueuesPage` wires the component to existing state (`entrySchedule`, `detail.entries`) and the existing save/load handlers.

## Complexity Tracking

> No constitution violations — section intentionally empty.
