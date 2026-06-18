---
description: "Task list for Drag-and-Drop Scheduling Areas in the Queue Template Editor"
---

# Tasks: Drag-and-Drop Scheduling Areas in the Queue Template Editor

**Input**: Design documents from `specs/061-queue-scheduling-areas/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/scheduling-areas-ui.md, quickstart.md

**Tests**: Included — the project constitution (Testing Standards, NON-NEGOTIABLE) requires tests for executable logic, and the quality gate is `vite build` + `jest` green.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md) for independent implementation and testing.

## Path Conventions

Web application, frontend only. All paths under `src/web-ui/src/`:
- Components: `components/queues/`
- Page: `pages/QueuesPage.tsx`
- Tests: co-located `__tests__/` (Jest + `@testing-library/react`)
- Styles: co-located `components/queues/QueueSchedulingAreas.css`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm prerequisites and create file skeletons. Dependencies (`@dnd-kit/*`) are already installed.

- [ ] T001 Verify `@dnd-kit/core`, `@dnd-kit/sortable`, `@dnd-kit/utilities` resolve and review the existing dnd-kit usage in `src/web-ui/src/components/SortableSequenceStepList.tsx`, `src/web-ui/src/components/SortableStepItem.tsx`, and `src/web-ui/src/components/DropIndicator.tsx` to reuse their conventions.
- [ ] T002 Create empty skeleton files: `src/web-ui/src/components/queues/schedulingAreas.ts`, `src/web-ui/src/components/queues/SchedulingSequenceCard.tsx`, `src/web-ui/src/components/queues/SchedulingArea.tsx`, `src/web-ui/src/components/queues/QueueSchedulingAreas.tsx`, and `src/web-ui/src/components/queues/QueueSchedulingAreas.css`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Pure area↔schedule-type mapping used by every user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 In `src/web-ui/src/components/queues/schedulingAreas.ts`, define `SchedulingAreaId` (`'startOfExecution' | 'oncePerRun' | 'scheduled' | 'afterEveryStep'`), `AREA_LABELS`, `CANONICAL_AREA_ORDER` (`startOfExecution, oncePerRun, scheduled, afterEveryStep`), and the pure helpers `areaForScheduleType(scheduleType): SchedulingAreaId` and `scheduleTypeForArea(areaId): ScheduleType` (1:1 with `ScheduleType` from `services/queueTemplates.ts`; `Timer`↔`scheduled`, `EveryStep`↔`afterEveryStep`).
- [ ] T004 [P] Add `src/web-ui/src/components/queues/__tests__/schedulingAreas.test.ts` covering the mapping helpers round-trip (every `ScheduleType` ↔ `SchedulingAreaId` both directions) and the canonical order constant.

**Checkpoint**: Mapping foundation ready.

---

## Phase 3: User Story 1 - See the template organized by schedule type (Priority: P1) 🎯 MVP

**Goal**: Replace the flat list with four labeled areas (full-width "Start of execution" top; "Once per run" above "Scheduled" left; "After every step" right), each rendering its sequences with the correct badge and an empty-state drop hint.

**Independent Test**: Open a template containing one sequence of each schedule type → each appears in its matching area, all four areas render labeled (empty ones show a hint), layout matches the spec.

### Tests for User Story 1

- [ ] T005 [P] [US1] In `src/web-ui/src/components/queues/__tests__/schedulingAreas.test.ts`, add tests for `groupEntriesIntoAreas` (entries grouped by current `scheduleType`; missing schedule defaults to `oncePerRun`; ordering within an area follows `orderedEntryIds`; no entry lost/duplicated).
- [ ] T006 [P] [US1] Add `src/web-ui/src/components/queues/__tests__/QueueSchedulingAreas.test.tsx` asserting contract C1–C5 and C7: four labeled areas render, entries grouped by schedule type, empty area shows label + hint, badges per type (At Queue Start / After Every Step / Timer; none for OncePerRun), stale badge preserved, and `disabled` renders grouping without draggable cards.

### Implementation for User Story 1

- [ ] T007 [US1] Implement `groupEntriesIntoAreas(orderedEntryIds, schedule, entriesById)` in `src/web-ui/src/components/queues/schedulingAreas.ts` returning `Record<SchedulingAreaId, SchedulingCard[]>` (uses `areaForScheduleType`; default `OncePerRun`).
- [ ] T008 [P] [US1] Implement `SchedulingSequenceCard` in `src/web-ui/src/components/queues/SchedulingSequenceCard.tsx`: renders label, stale badge, schedule badge consistent with its area, and a Remove button; uses `useSortable` (drag handle/attributes) following `SortableStepItem` conventions; respects `disabled`.
- [ ] T009 [P] [US1] Implement `SchedulingArea` in `src/web-ui/src/components/queues/SchedulingArea.tsx`: a droppable area with heading (`AREA_LABELS`), a `SortableContext` over its card ids, an empty-state hint when no cards, and `DropIndicator` support (reuse existing `DropIndicator`).
- [ ] T010 [US1] Implement `QueueSchedulingAreas` shell in `src/web-ui/src/components/queues/QueueSchedulingAreas.tsx`: props per contract (`entries`, `sequences`, `entrySchedule`, `onAdd`, `onRemove`, `onReorderAndReassign`, timer handlers, `disabled`); derive areas via `groupEntriesIntoAreas`; render the four `SchedulingArea`s inside one `DndContext`; keep the existing "Add sequence" control (routes through `onAdd`). Drag handlers may be stubs at this stage (wired in US2/US3).
- [ ] T011 [US1] Add four-area responsive CSS grid in `src/web-ui/src/components/queues/QueueSchedulingAreas.css` (full-width top row; left column stacking Once-per-run over Scheduled; right column "After every step" spanning both) and import it in `QueueSchedulingAreas.tsx`; follow existing `reorderable-list`/`empty-state` visual conventions.
- [ ] T012 [US1] Wire `QueueSchedulingAreas` into `src/web-ui/src/pages/QueuesPage.tsx` in place of `QueueEntryList` (within `QueueTemplateControls`), passing existing `detail.entries`, `entrySchedule`, `onAdd`/`onRemove`, and `disabled={detail.status === 'Running'}`; existing `buildScheduleFromTemplateEntries` continues to restore grouping on open.

**Checkpoint**: Read-only grouped four-area view works end-to-end and is independently testable (MVP).

---

## Phase 4: User Story 2 - Reassign a sequence's schedule by dragging it to another area (Priority: P1)

**Goal**: Dragging a card into another area changes its schedule option to that area's type; the change persists across save/reload. Moving into/out of "Scheduled" handles Timer details with retain-on-exit.

**Independent Test**: Drag a card "Once per run" → "After every step"; its badge becomes "After Every Step"; save + reload keeps it there. Timer: drag into "Scheduled", set a time, drag out (controls hide), drag back (time restored).

### Tests for User Story 2

- [ ] T013 [P] [US2] In `schedulingAreas.test.ts`, add reducer tests R1–R5 and R7 for `applyDragMove`: cross-area reassign sets destination `scheduleType` (each pair incl. → `scheduled` = `Timer`), move out of `scheduled` retains `timerTimeOfDay`/`timerRelativeOffset`/`timerMode` (inactive), move back restores them, a no-op drop leaves state unchanged, and a cancelled/out-of-area drop (no/`null` target area, FR-013) leaves both `orderedEntryIds` and `schedule` unchanged.
- [ ] T014 [P] [US2] In `QueueSchedulingAreas.test.tsx`, add contract C6: a card in the "Scheduled" area exposes timer controls (mode toggle + time-of-day input or relative-offset inputs); cards in other areas do not.
- [ ] T015 [P] [US2] Extend `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` with integration tests I1 and I4: reassigning a card to another area then saving + reloading the template shows it in the destination area with the new `scheduleType`; a `Timer` card retains its timer value across save/reload, and a card moved out of "Scheduled" before save is emitted with the destination type and no timer fields.

### Implementation for User Story 2

- [ ] T016 [US2] Implement the cross-area branch of `applyDragMove(state, { entryId, targetArea, targetIndex })` in `src/web-ui/src/components/queues/schedulingAreas.ts`: set `schedule[entryId].scheduleType = scheduleTypeForArea(targetArea)`; on enter `scheduled` keep/empty timer fields; on exit `scheduled` retain timer fields inactive; rebuild `orderedEntryIds` keeping canonical inter-area order; no-op when area+index unchanged.
- [ ] T017 [US2] Render per-card Timer controls in `SchedulingSequenceCard`/`SchedulingArea` for cards in the "Scheduled" area only (mode toggle, time-of-day input, relative-offset inputs), reusing the existing markup/handlers from `QueueEntryList.tsx` and forwarding `onTimerTimeChange`/`onTimerModeChange`/`onTimerRelativeOffsetChange`.
- [ ] T018 [US2] Wire `onDragEnd` in `QueueSchedulingAreas.tsx` to translate the dnd-kit event into `{ entryId, targetArea, targetIndex }`, call `applyDragMove`, and invoke `onReorderAndReassign(next)`; track `activeId`/`overId` for `DropIndicator`. Guard a cancelled/dropped-outside drag (FR-013): when the dnd-kit `over` is `null` or resolves to no valid area, return early without calling `applyDragMove`/`onReorderAndReassign` so the card returns to its origin unchanged.
- [ ] T019 [US2] In `src/web-ui/src/pages/QueuesPage.tsx`, add an `onReorderAndReassign` handler that updates `entrySchedule` (and the working ordered-entry state) from the reducer output so reassignment is reflected immediately; cross-area reassignment persists via the existing `handleSaveTemplate` (schedule type is stored positionally — no entry reorder needed for US2).

**Checkpoint**: Cross-area reassignment + Timer retention work and round-trip; US1 still functions.

---

## Phase 5: User Story 3 - Reorder sequences within an area by dragging (Priority: P2)

**Goal**: Dragging cards up/down within an area changes their order, which becomes the per-type execution order and persists across save/reload.

**Independent Test**: In an area with A, B, C, drag C above A → order C, A, B; save + reload preserves it.

### Tests for User Story 3

- [ ] T020 [P] [US3] In `schedulingAreas.test.ts`, add reducer test R6: within-area reorder changes only `orderedEntryIds` for the affected cards and leaves every `scheduleType` unchanged.
- [ ] T021 [P] [US3] Extend `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` with integration test I2: reordering within an area, saving, and reloading preserves the within-area order (and thus per-type execution order), verifying the order-aware save path.

### Implementation for User Story 3

- [ ] T022 [US3] Implement the within-area reorder branch of `applyDragMove` in `src/web-ui/src/components/queues/schedulingAreas.ts` (reinsert `entryId` at `targetIndex` within the same area; `scheduleType` unchanged); confirm `onDragEnd` already routes same-area drops here.
- [ ] T023 [US3] Make `handleSaveTemplate` in `src/web-ui/src/pages/QueuesPage.tsx` order-aware: before saving, compute `orderedSequenceIds` from the working ordered-entry state, call `replaceQueueEntries(detail.id, orderedSequenceIds)`, `getQueue` to fetch reordered entries, re-key `entrySchedule` onto the new entries by position, then build the template entries in that order (timer fields only for `Timer`). Keeps runtime order == saved template order so positional reload restore stays correct.

**Checkpoint**: Within-area reorder persists; US1 and US2 still function.

---

## Phase 6: User Story 4 - New sequences default to "Once per run" (Priority: P2)

**Goal**: A newly added sequence appears at the bottom of the "Once per run" area with `OncePerRun`.

**Independent Test**: Add a sequence → it appears last in "Once per run"; saving emits it with `scheduleType: 'OncePerRun'`.

### Tests for User Story 4

- [ ] T024 [P] [US4] In `schedulingAreas.test.ts`, add reducer/grouping test R8: an entry with `scheduleType 'OncePerRun'` appears last in the `oncePerRun` area after being appended to `orderedEntryIds`.
- [ ] T025 [P] [US4] Extend `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` with integration test I3: adding a sequence places it in "Once per run" and saving emits it with `scheduleType: 'OncePerRun'`.

### Implementation for User Story 4

- [ ] T026 [US4] Confirm/adjust `onAddEntry` in `src/web-ui/src/pages/QueuesPage.tsx` so a new entry seeds `entrySchedule[newEntry.entryId] = { scheduleType: 'OncePerRun', timerTimeOfDay: '' }` and is appended to the working ordered-entry state (so it renders last in "Once per run"); ensure `QueueSchedulingAreas` reflects the new card without manual area selection.

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Consistency, regression safety, and gate validation.

- [ ] T027 [P] Responsive/empty-area polish in `src/web-ui/src/components/queues/QueueSchedulingAreas.css` (narrow-viewport stacking; consistent empty-area sizing) and verify ARIA labels on areas/cards/badges follow existing conventions.
- [ ] T028 Confirm the existing scheduling/queue suites pass unchanged (I5): run `src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx` and existing `QueuesPage.templates.spec.tsx` cases; update only references made obsolete by replacing `QueueEntryList` in the editor (do not change runtime/API behavior).
- [ ] T029 Run the quality gate: `npm run build` (vite) and `npm test` (jest) from `src/web-ui` must both be green; fix any failures before marking complete (constitution NON-NEGOTIABLE).
- [ ] T030 [P] Execute the `specs/061-queue-scheduling-areas/quickstart.md` manual verification (US1–US4 walkthroughs incl. Timer retain/restore) and note results.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Foundational. US1 is the MVP and establishes the components US2–US4 extend; US2, US3, US4 each depend on US1 (shared components/page wiring) but are independently testable increments and do not depend on each other.
- **Polish (Phase 7)**: Depends on all targeted stories.

### User Story Dependencies

- **US1 (P1)**: After Foundational. No dependency on other stories.
- **US2 (P1)**: Builds on US1 components (drag wiring + Timer controls). Independently testable.
- **US3 (P2)**: Builds on US1 components; adds order-aware save. Independent of US2.
- **US4 (P2)**: Builds on US1 add flow. Independent of US2/US3.

### Within Each User Story

- Write tests first and confirm they fail before implementing.
- Pure reducer/grouping logic before component wiring; components before page wiring.

### Parallel Opportunities

- T004 with T003 (test alongside helper) once T003 lands.
- US1 tests T005, T006 in parallel; components T008, T009 in parallel (different files) before T010 wires them.
- US2 tests T013, T014, T015 in parallel.
- US3 tests T020, T021 in parallel; US4 tests T024, T025 in parallel.
- After US1 completes, US2/US3/US4 can be staffed in parallel by different developers (they touch `applyDragMove`/`QueuesPage` in different branches — coordinate `schedulingAreas.ts` and `QueuesPage.tsx` edits to avoid conflicts).

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "groupEntriesIntoAreas tests in schedulingAreas.test.ts"
Task: "QueueSchedulingAreas render/grouping/badges tests in QueueSchedulingAreas.test.tsx"

# Components together (different files):
Task: "SchedulingSequenceCard in SchedulingSequenceCard.tsx"
Task: "SchedulingArea in SchedulingArea.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP and VALIDATE**: grouped four-area view renders correctly from existing templates.
3. Demo: the "better overview" the feature is fundamentally about.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → grouped layout (MVP) → validate.
3. US2 → drag-to-reassign + Timer retention → validate round-trip.
4. US3 → drag-to-reorder + order-aware save → validate.
5. US4 → default-to-Once-per-run on add → validate.
6. Polish → gate green.

---

## Notes

- [P] = different files, no dependencies.
- This is a frontend-only feature: no API/scheduler/stored-model changes (FR-012).
- Keyboard-operable drag alternative is explicitly out of scope (clarified 2026-06-18); the existing schedule selector path is not required.
- Quality gate is `vite build` + `jest` green (lint/`tsc --noEmit` have pre-existing failures and are not the gate).
- Commit after each task or logical group.
