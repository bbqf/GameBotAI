# Phase 0 Research: Enable/Disable Template Sequences

All Technical Context items were resolvable from the existing codebase; no external research required. Below are the decisions that shape the design.

## Decision 1 — Where the enabled/disabled state lives

**Decision**: Add `Enabled` (bool) to `QueueTemplateEntry` (the persisted domain entry), defaulting to `true`.
**Rationale**: The state is a property of a template entry (per the clarification, independent per entry including duplicates). `QueueTemplateEntry` already holds the per-entry `ScheduleType`/timer fields, so `Enabled` is a natural sibling. Storing it on the entry (not on the sequence, not on the runtime queue) means the same sequence can be enabled in one template/entry and disabled in another.
**Alternatives considered**: A separate "disabled entry ids" set on `QueueTemplate` — rejected: breaks with duplicate sequence ids and splits entry state across two places. A flag on the runtime queue entry — rejected: runtime entries aren't persisted; the request is to persist to the template.

## Decision 2 — Backward compatibility / legacy default-on

**Decision**: Rely on the C# property initializer `public bool Enabled { get; set; } = true;`. No migration, no version bump of the stored JSON.
**Rationale**: `FileQueueTemplateRepository` uses `System.Text.Json` with default options. When a property is **absent** from the JSON being deserialized, System.Text.Json leaves the value set by the object initializer — so legacy template files (written before this field existed) deserialize with `Enabled == true`. This satisfies FR-007 (default on) at zero cost.
**Alternatives considered**: An explicit migration pass rewriting every template file — rejected as unnecessary given the initializer behavior; adds risk and IO for no benefit.

## Decision 3 — Single execution filter point

**Decision**: Exclude disabled entries once, at run build, where `QueueExecutionService.RunAsync` materializes `var allEntries = template.Entries.ToList();` (change to `template.Entries.Where(e => e.Enabled).ToList();`).
**Rationale**: Every schedule partition (`AtQueueStart`, `OncePerRun`, `EveryStep`, `Timer`) and the `QueueRunSchedule` monitor projection are all derived from `allEntries`. Filtering there makes disabled entries invisible to firing AND to scheduling accounting (FR-005/FR-006) with one change, and cannot desync partitions. `AtQueueStart` uses a separate `.Where` over `allEntries`, so it is covered too.
**Alternatives considered**: Filtering inside each partition's `.Where` — rejected: four edit sites, easy to miss one, and the monitor's index-keyed schedule would drift. Skipping at fire time (check `Enabled` before each fire) — rejected: the entry would still occupy schedule slots and confuse the monitor.

## Decision 4 — Runtime store keeps ALL entries; only the run filters (revised during implementation)

**Decision**: Filter disabled entries **only** in `QueueExecutionService.RunAsync`, where the template is read directly (`allEntries = template.Entries.Where(e => e.Enabled).ToList()`). Do **not** filter the runtime store in `StartAsync` or `MaybeAutoLoadAsync`; it retains all entries in template order.
**Rationale**: The template editor (`QueuesPage`) renders cards from the **runtime** queue entries (`GET /queues/{id}`) and merges each entry's schedule and `enabled` state from the template detail **by position** (`tplEntries[i]`). If the runtime store dropped disabled entries, (a) disabled entries would vanish from the editor so the operator could never re-enable them (violates FR-003), and (b) the positional merge would misalign schedule/enabled onto the wrong entries. The actual run reads `template.Entries` directly and is independent of the runtime store, so filtering there fully satisfies FR-005/FR-006. The queue **monitor** projects from the run's `QueueRunSchedule` (built from the filtered `allEntries`), so it already excludes disabled entries.
**Alternatives considered**: Filtering the runtime store to keep a strict "GET count == run count" invariant — rejected: it breaks the editor's positional merge and hides disabled entries, defeating the feature. The loaded-entries list showing a disabled entry is correct (it belongs to the template); "what will actually run/what's scheduled" is the monitor's job, and the monitor excludes them.

## Decision 5 — UI: enabled rides on the EntrySchedule view-model

**Decision**: Extend the front-end `EntrySchedule` view-model with `enabled?: boolean` (undefined/true = on), thread it through `SchedulingCard` and `groupEntriesIntoAreas`, render an on/off switch on `SchedulingSequenceCard`, and include `enabled` in the `handleSaveTemplate` payload and in `buildScheduleFromTemplateEntries` on load.
**Rationale**: `EntrySchedule` is already the per-entry, entryId-keyed client state the editor threads through the scheduling areas and emits on save; `enabled` is one more per-entry attribute exactly like the timer fields. Reusing it avoids a parallel state map and keeps drag/reorder/reassign untouched (toggling enabled changes neither area nor order).
**Alternatives considered**: A separate `Record<entryId, boolean>` map in `QueuesPage` — rejected: duplicates the plumbing that `EntrySchedule` already provides and risks the two maps diverging during add/remove/rekey.

## Decision 6 — Persistence trigger (no new save step)

**Decision**: The switch mutates client state and is persisted by the existing "Save as template" action (FR-011). No autosave, no dedicated toggle endpoint.
**Rationale**: Matches the existing editor model where order and schedule edits are also only persisted on the explicit template save. Consistent, least-surprising, and requires no new API surface.
**Alternatives considered**: A dedicated `PATCH entry/enabled` endpoint with autosave — rejected: introduces new API surface and a different persistence model than the rest of the editor for no user benefit.
