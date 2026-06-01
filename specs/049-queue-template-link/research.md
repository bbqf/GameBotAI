# Phase 0 Research: Queue–Template Link with Auto-Load

All spec clarifications were resolved during `/speckit-specify` and `/speckit-clarify`
(see spec `## Clarifications`). No open `NEEDS CLARIFICATION` remained entering planning.
This document records the design decisions that translate those clarifications onto the
existing codebase.

## Decision 1 — Where the link is stored

**Decision**: Add `LinkedTemplateId` (nullable string) to `ExecutionQueue`, the persisted
queue-config document (`data/queues/{id}.json`, via `FileQueueRepository`).

**Rationale**: `ExecutionQueue` is the only durable part of a queue (entries and status are
deliberately runtime-only in `QueueRuntimeStore`). The link must survive restarts (FR-002),
so it belongs on the persisted config. Whole-object JSON serialization writes it for free;
existing files without the property deserialize to `null` (= unlinked), so no migration is
required.

**Alternatives considered**:
- *Separate link store / index file*: more moving parts, extra I/O, and a second source of
  truth to keep consistent on queue delete. Rejected — the config doc already exists.
- *Store on the template (back-reference to queues)*: contradicts FR-015 (template holds no
  back-reference) and breaks the 0..n fan-out cleanly. Rejected.

## Decision 2 — Reference by stable ID, not name

**Decision**: The link stores the template's `Id` (GUID "N"), matching the Q1 clarification.

**Rationale**: Templates already have stable IDs; the save endpoint returns the ID for both
create and overwrite, and the load picker already knows the ID. ID-based linking keeps the
link intact across a rename and breaks only on delete (FR-001a, FR-011). The 048 *manual*
reload resolves by name, but that is a separate, session-scoped affordance; the persisted
link should be robust to rename.

**Alternatives considered**: Name-based link (Q1 option B) — simpler to reconcile with 048's
reload-by-name, but a future rename would orphan the link. Rejected per Q1.

## Decision 3 — Auto-load trigger location: GET queue detail

**Decision**: Perform auto-load as a side effect of `GET /api/queues/{id}` (the queue
detail endpoint). The list endpoint `GET /api/queues` is untouched.

**Rationale**: The editor's `openEdit` is the only caller of `getQueue(id)` → `GET {id}`;
the queue list uses `GET ""`. Hooking the detail GET therefore triggers auto-load exactly
on "opening the queue's edit/detail page" and nowhere else (Q3). Doing it server-side (vs.
a client-only view fill) means the materialized entries are real runtime entries that
execution will use (FR-006a / Q2), and it reuses the existing `SetEntries` path.

**Alternatives considered**:
- *Frontend-only auto-load* (call `replaceQueueEntries` from `openEdit`): would not help a
  queue started directly from the list, and duplicates server logic. Rejected per Q2
  (must populate server-side runtime).
- *Auto-load inside `GetEntries`/on Start*: would broaden the trigger beyond the editor,
  contradicting Q3. Rejected (noted as a known limitation below).

## Decision 4 — "Only when empty" operationalized as "no runtime state yet"

**Decision**: Auto-load fires only when `IQueueRuntimeStore.HasRuntimeState(queueId)` is
false (no entry in the in-memory dictionary), in addition to "not Running". Add
`HasRuntimeState` to the store interface/impl (`_states.ContainsKey`).

**Rationale**: FR-012 says auto-load runs only when the queue "currently has no sequence
entries", with intent = restore after a restart, never silently discard edits. A naive
"entries.Count == 0" check would *re-fill* a queue right after the operator manually
removes its last entry (because `reloadDetail` re-issues GET detail after each entry op).
"No runtime state exists yet" is true only on the first display after a service start and
becomes false as soon as the queue is materialized or edited, giving the desired
restore-once behavior without clobbering an intentional clear. An empty linked template
still yields an empty queue (valid no-content load).

**Alternatives considered**: explicit per-queue "materialized" boolean flag — equivalent but
adds state; `ContainsKey` already encodes it. Rejected as redundant.

## Decision 5 — Setting/clearing the link: dedicated endpoint

**Decision**: New `PUT /api/queues/{id}/template` `{ templateId | null }`. Not gated on
Running. The frontend calls it after a successful load (`templateId` of the loaded template)
and after a successful save (`id` from the save response). Null clears.

**Rationale**: The link must be set on **both** load and save. Load already calls
`PUT {id}/entries`, but save does not touch entries — and crucially, save-from-running is
allowed (047) while `PUT {id}/entries` is blocked when Running. A single small endpoint that
only mutates the persisted link (never entries) works for all cases, including
save-while-running, and keeps each call's responsibility clear. No new visual control is
introduced; the calls are side effects of existing load/save actions (FR-005).

**Alternatives considered**:
- *Fold `templateId` into `ReplaceQueueEntriesRequest`*: covers load in one call but cannot
  set the link during a running-queue save, and conflates two concerns. Rejected.
- *Extend `UpdateQueueRequest` (`PUT {id}`)*: that endpoint requires a name and is the
  page-level Save for name/cycle; overloading it with link changes on every load/save would
  couple unrelated flows and is also Running-blocked. Rejected.

## Decision 6 — Broken-link behavior persists the clear

**Decision**: When auto-load cannot resolve `LinkedTemplateId`, set it to null and persist
via `repo.UpdateAsync`, then open the queue empty without error (FR-011, FR-006a).

**Rationale**: Matches Q's "clear the link" answer; persisting avoids repeatedly attempting
to resolve a dead template on every open. `UpdateAsync` succeeds because Name/EmulatorSerial
remain valid.

## Known limitation (in scope, documented)

Starting a linked queue **directly from the list** (without opening its editor) after a
restart will run it empty, because auto-load is scoped to the detail GET (Q3). Opening the
queue first materializes it. This is consistent with the clarified trigger surface and is
called out in `quickstart.md`. Broadening the trigger is out of scope for this iteration.

## Standards reused (no new dependencies)

- Error envelope: `{ error: { code, message, hint } }` with the same status codes used by
  existing queue/template endpoints.
- File repos: existing safe-id/path-traversal guard and whole-object JSON serialization.
- Frontend: existing `getJson/putJson` helpers, `ApiError`, and the
  `QueueTemplateControls` component (unchanged).
- No template-action logging (consistent with 047).
