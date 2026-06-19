# Phase 0 Research: Drag-and-Drop Scheduling Areas

All Technical Context items were resolvable from the existing codebase; there were no open `NEEDS CLARIFICATION` markers (the two spec ambiguities were resolved in `/speckit-clarify`). This document records the design decisions that shape Phase 1.

## Decision 1 — Drag-and-drop library and pattern

**Decision**: Use `@dnd-kit/core` + `@dnd-kit/sortable` in a **multi-container** configuration: a single `DndContext` wrapping four droppable areas, each rendering a `SortableContext` of cards. Reuse the existing `DropIndicator` and the `SortableStepItem`/`useSortable` conventions already used by `SortableSequenceStepList`.

**Rationale**: These packages are already direct dependencies and are the established DnD primitives in this repo (sequence step editor). Multi-container sorting (drag within and across lists, with the area itself as a drop target so empty areas can receive cards) is a first-class dnd-kit use case. Reusing the in-repo pattern keeps the code idiomatic and avoids a new dependency (Constitution I).

**Alternatives considered**:
- HTML5 native drag-and-drop — rejected: clunky cross-list semantics, inconsistent across browsers, no momentum from existing code.
- `react-beautiful-dnd` / `react-dnd` — rejected: new dependency duplicating capability already present via dnd-kit.

## Decision 2 — Area ↔ schedule-option mapping

**Decision**: Exactly four areas, each bound 1:1 to a `ScheduleType`:

| Area label | `ScheduleType` |
|------------|----------------|
| Start of execution | `AtQueueStart` |
| Once per run | `OncePerRun` |
| Scheduled | `Timer` |
| After every step | `EveryStep` (display label "After Every Step") |

The "Scheduled" area hosts both Timer sub-modes (time-of-day and relative offset); the sub-mode and its values are per-card controls inside the area, not separate areas.

**Rationale**: Directly satisfies FR-001/FR-002 and the Assumptions ("one area per schedule option"). Keeps the wire/stored identifier `EveryStep` unchanged (feature 060 backward-compat), with the area label being the only operator-facing text.

**Alternatives considered**: A fifth area splitting Timer into time-of-day vs relative — rejected: contradicts the single "Timer" schedule option and the spec's explicit one-area-per-option assumption.

## Decision 3 — Ordering & persistence strategy (the key design constraint)

**Context**: Schedule type is **UI-local** (`entrySchedule`, keyed by runtime `entryId`); it is not stored on the runtime queue. It round-trips to disk only through the linked template. Critically, `QueuesPage.buildScheduleFromTemplateEntries` restores schedule state by **positional index** — runtime entry *i* is paired with template entry *i*. Therefore **runtime entry order must equal the saved template entry order** for schedule restoration to remain correct on reload.

**Decision**:
- During editing, area membership and within-area order live entirely in client state (no API calls per drag). The view-model derives, from `detail.entries` order + each entry's `scheduleType`, the cards in each area; a drag produces a new `(entryId → scheduleType)` map plus a new desired linear order of `entryId`s.
- The **canonical linear order** is the concatenation of areas in a fixed sequence — **AtQueueStart → OncePerRun → Timer → EveryStep** — and, within each area, the operator's drag order. This linear order is the single source of truth that both the editor display and the save path use.
- On **Save Template**: first persist the linear order to the runtime queue via the existing `replaceQueueEntries(detail.id, orderedSequenceIds)`, then `getQueue` to obtain the regenerated entries (in that order), then build the template entries from those entries in order, applying the schedule type the operator assigned to each position. Because runtime order now equals template order, reload's index pairing stays correct (SC-002/SC-003, FR-011).
- `replaceQueueEntries` regenerates `entryId`s, so the schedule map is re-keyed onto the new entries **by position** as part of the save, exactly as the existing load path already maps by position.

**Rationale**: Respects the existing positional coupling rather than fighting it, keeps drag interactions instant (state-only), and reuses endpoints that already exist (no API change, FR-012). The fixed inter-area order is invisible to execution (each schedule type runs in its own pass — see spec "Cross-area order" assumption), so concatenation order only needs to be *stable*, which it is.

**Alternatives considered**:
- Reorder runtime entries on every drag via the API — rejected: needless latency and entryId churn mid-edit; violates the "no API call during a drag" performance note.
- Decouple template order from runtime order — rejected: would break the positional `buildScheduleFromTemplateEntries` restore without a larger refactor that the spec scopes out.
- A dedicated reorder endpoint — rejected: out of scope (no API changes); `replaceQueueEntries` already expresses "set this exact order".

## Decision 4 — Cross-area move auto-changes schedule type; timer-detail retention

**Decision**: Dropping a card into an area sets that card's `scheduleType` to the area's type (FR-004). Moving **into** "Scheduled" sets `Timer` with an unset/empty timer value the operator then fills in (FR-008, matching today's freshly-chosen-Timer behavior). Moving **out of** "Scheduled" changes the type to the destination's and stops applying timer details at run time, but **retains** the previously entered `timerTimeOfDay` / `timerRelativeOffset` (inactive) on the card's UI state so they are restored if the card is dragged back into "Scheduled" (clarified 2026-06-18; FR-009).

**Rationale**: Implements the clarified retain-inactive behavior with the least surprise and full recoverability. Retention is purely client-side view state; the saved template only emits timer fields for entries whose current `scheduleType` is `Timer` (the existing `handleSaveTemplate` already gates timer fields on `scheduleType === 'Timer'`), so inactive details never leak into a non-Timer saved entry.

**Alternatives considered**: Clearing timer details on exit — rejected by clarification (Option A chosen).

## Decision 5 — Default area for newly added sequences

**Decision**: A newly added sequence is placed in the **Once per run** area with `scheduleType: 'OncePerRun'` (FR-007). The existing `onAddEntry` already seeds `entrySchedule[newEntry.entryId] = { scheduleType: 'OncePerRun', ... }`; the area view-model will render any entry with that type in the "Once per run" area, so the default falls out naturally. New cards append to the end of that area.

**Rationale**: Matches the existing add behavior and the spec default; minimal change.

## Decision 6 — Layout & responsive behavior

**Decision**: CSS Grid for the editor: row 1 is the full-width "Start of execution" area; row 2 is a two-column grid where the left column stacks "Once per run" (top) and "Scheduled" (bottom) and the right column holds "After every step" spanning both. Each area is always rendered (even when empty) with a heading and an empty-state drop hint (FR-010). Areas scroll/grow within their panel for many cards. Styling follows existing `reorderable-list` / `empty-state` class conventions.

**Rationale**: Directly encodes the layout in FR-002 with standard CSS Grid; reuses existing visual language. On narrow viewports the columns can collapse to stacked full-width areas (progressive enhancement; not a hard requirement of the spec).

**Alternatives considered**: Flexbox-only — workable but Grid expresses the "full-width top + 2×2-ish body" intent more directly and keeps the empty areas sized consistently.

## Decision 7 — Testability of drag logic

**Decision**: Extract all move/reorder/reassign/default logic into a pure module (`schedulingAreas.ts`): `groupEntriesIntoAreas(entries, schedule)`, `applyDragMove(state, { activeId, overAreaOrIndex })` → new `{ orderedEntryIds, schedule }`, and `areaForScheduleType` / `scheduleTypeForArea`. Component tests cover rendering/grouping/badges/empty areas; the pure module is unit-tested for every cross-area pair, within-area reorder, default-add, no-op drop, and timer-detail retention.

**Rationale**: jsdom cannot faithfully simulate pointer-based dnd-kit dragging, so testing the *decision logic* as pure functions (fed the same inputs dnd-kit's `onDragEnd` provides) gives deterministic, fast coverage (Constitution II) without brittle DOM-drag simulation. A thin `onDragEnd` in the component just forwards to the pure reducer.

## Resolved unknowns summary

| Unknown | Resolution |
|---------|-----------|
| Which DnD approach | dnd-kit multi-container, reuse existing in-repo pattern |
| How order persists given UI-local schedule | Canonical linear order → `replaceQueueEntries` on save → positional template build/restore |
| What happens to timer details on area exit | Retained inactive, restored on return (client state only) |
| Default area for new sequences | Once per run (existing add seed already does this) |
| Layout mechanism | CSS Grid: full-width top + left stack + right column |
| How to test pointer drag | Pure reducer module unit-tested; components tested for render/grouping |
