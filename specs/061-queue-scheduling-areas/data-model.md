# Phase 1 Data Model: Drag-and-Drop Scheduling Areas

This feature introduces **no new persisted entities**. The data model below is the **client-side view model** the editor derives from existing state and maps back to the existing template/queue payloads on save. Persisted shapes (`QueueTemplateEntryDto`, `QueueEntryDto`, `ScheduleType`) are unchanged.

## Existing types reused (unchanged)

- `ScheduleType = 'OncePerRun' | 'EveryStep' | 'Timer' | 'AtQueueStart'` — from `services/queueTemplates.ts`.
- `EntrySchedule = { scheduleType, timerTimeOfDay, timerMode?, timerRelativeOffset? }` — from `components/queues/QueueEntryList.tsx` (UI-local per-entry schedule state, keyed by runtime `entryId`).
- `QueueEntryDto = { entryId, sequenceId, sequenceName, stale }` — runtime queue entry (ordered).
- `QueueTemplateEntryDto` / `TemplateEntrySaveDto` — template persistence shapes.

## New view-model concepts (client-only)

### SchedulingAreaId

Enumeration of the four areas, each mapped 1:1 to a `ScheduleType`:

| `SchedulingAreaId` | Label | `ScheduleType` |
|--------------------|-------|----------------|
| `startOfExecution` | "Start of execution" | `AtQueueStart` |
| `oncePerRun` | "Once per run" | `OncePerRun` |
| `scheduled` | "Scheduled" | `Timer` |
| `afterEveryStep` | "After every step" | `EveryStep` |

Mapping helpers (pure):
- `scheduleTypeForArea(areaId): ScheduleType`
- `areaForScheduleType(scheduleType): SchedulingAreaId`

**Canonical area order** (for the linear save order, invisible to execution): `startOfExecution`, `oncePerRun`, `scheduled`, `afterEveryStep`.

### SchedulingCard

A presentational projection of one entry within an area:

| Field | Source | Notes |
|-------|--------|-------|
| `entryId` | `QueueEntryDto.entryId` | Stable drag id during an edit session |
| `sequenceId` | `QueueEntryDto.sequenceId` | |
| `label` | `sequenceName ?? sequenceId` | |
| `stale` | `QueueEntryDto.stale` | Renders stale badge; card stays draggable (FR-016) |
| `schedule` | `EntrySchedule` for this entry | Drives badge + (in "Scheduled") timer controls |

### SchedulingAreasState

The editor's working state, derived from `detail.entries` (order) + `entrySchedule`:

| Field | Type | Notes |
|-------|------|-------|
| `orderedEntryIds` | `string[]` | Single linear order = concatenation of areas in canonical order, within-area drag order preserved. Source of truth for display and save. |
| `schedule` | `Record<string, EntrySchedule>` | Per-entry schedule incl. retained-but-inactive timer details. |
| `areas` | `Record<SchedulingAreaId, SchedulingCard[]>` | Derived view: entries grouped by `areaForScheduleType(schedule[id].scheduleType)`, ordered per `orderedEntryIds`. |

## Validation & rules

- **Grouping (FR-003)**: every entry appears in exactly one area, the one matching its current `scheduleType`; no entry lost or duplicated.
- **Default (FR-007)**: a new entry has `scheduleType: 'OncePerRun'` and therefore lands in `oncePerRun`, appended last in that area.
- **Cross-area move (FR-004)**: dropping an entry into an area sets `schedule[id].scheduleType = scheduleTypeForArea(targetArea)`.
- **Into "Scheduled" (FR-008)**: sets `Timer`; if no prior timer value, `timerTimeOfDay`/`timerRelativeOffset` start empty (operator fills in).
- **Out of "Scheduled" (FR-009)**: changes `scheduleType` to the destination's; **retains** existing `timerTimeOfDay`/`timerRelativeOffset`/`timerMode` in state (inactive) for restoration if dragged back.
- **Within-area reorder (FR-005)**: only `orderedEntryIds` changes for the affected cards; `scheduleType` unchanged.
- **No-op drop (Edge case)**: dropping at origin position leaves both `orderedEntryIds` and `schedule` unchanged.
- **Disabled/running (FR-014)**: when `disabled`, areas still render grouped; cards are not draggable and schedule is not reassignable.

## State transitions (drag reducer)

`applyDragMove(state, move) -> state'` where `move` carries the dragged `entryId`, the target area, and the target index within that area:

1. Determine `targetArea` and `targetIndex` from the drop.
2. If `targetArea === currentArea(entryId)` and index unchanged → return `state` unchanged (no-op).
3. Set `schedule'[entryId].scheduleType = scheduleTypeForArea(targetArea)` (preserving retained timer fields per FR-009; clearing active application only).
4. Rebuild `orderedEntryIds'` by removing `entryId` and reinserting it at the position corresponding to `targetIndex` within `targetArea`, keeping the canonical inter-area order.
5. Return `{ orderedEntryIds', schedule' }`.

## Save mapping (to existing payloads)

On Save Template (`QueuesPage.handleSaveTemplate`, order-aware):
1. `orderedSequenceIds = orderedEntryIds.map(id -> sequenceId)`.
2. `await replaceQueueEntries(detail.id, orderedSequenceIds)` → reordered runtime entries (new `entryId`s, same order).
3. `const refreshed = await getQueue(detail.id)`.
4. Build `TemplateEntrySaveDto[]` from `refreshed.entries` **in order**, taking each position's intended `scheduleType` (and, only for `Timer`, the timer fields) from the position-aligned schedule.
5. `await saveQueueTemplate({ name, entries, overwrite })`; re-key `entrySchedule` onto `refreshed.entries` by position.

On Load/reload: unchanged — `buildScheduleFromTemplateEntries` already maps template entry *i* ↔ runtime entry *i*; because save now guarantees identical order, restoration is correct.
