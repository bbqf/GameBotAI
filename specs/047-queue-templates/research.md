# Phase 0 Research: Queue Templates

**Feature**: 047-queue-templates | **Date**: 2026-05-31

This feature extends the existing **Emulator Execution Queue** (046). All technical
context is known from the existing codebase; there are no open `NEEDS CLARIFICATION`
items. The notes below record the design decisions that shape Phase 1.

## Decision 1 — Templates are a standalone persisted module, decoupled from queues

- **Decision**: Model a queue template as an independent, file-backed aggregate
  (`GameBot.Domain.QueueTemplates`) that stores only a name plus an ordered list of
  sequence references. It has **no** reference to any queue, emulator, or status.
- **Rationale**: The spec requires templates to be "unbound from the queue itself"
  and shareable across queues (FR-001, FR-003). Keeping templates as their own
  module (own repository, own endpoints) mirrors the existing per-aggregate pattern
  (`FileSequenceRepository`, `FileQueueRepository`) and keeps the queue module
  unaware of templates.
- **Alternatives considered**:
  - *Store templates inside queue config* — rejected; couples a template to one
    queue and contradicts "unbound / shareable."
  - *Reuse `IQueueRuntimeStore`* — rejected; that store is intentionally
    non-persistent (entries vanish on restart), which is the exact gap templates fill.

## Decision 2 — Persistence: file-backed JSON, GUID id, name held in the document

- **Decision**: Persist each template as `{storageRoot}/queue-templates/{id}.json`
  via `FileQueueTemplateRepository`, copying the safe-id / path-traversal guard and
  `JsonSerializerOptions { WriteIndented = true }` from `FileQueueRepository`. The id
  is a GUID; the human-readable `Name` lives inside the JSON. **Unlike** the queue
  repository, template **entries are persisted** (that is the whole point).
- **Rationale**: Template names allow spaces and other characters that the existing
  `SafeIdPattern` (`^[A-Za-z0-9_-]+$`) forbids, so the name cannot be the filename.
  A GUID id + name field reuses the proven pattern unchanged. Persisting entries
  satisfies FR-002. Survives restart per SC-002.
- **Alternatives considered**:
  - *Slugified-name filename* — rejected; lossy, collision-prone, and breaks on
    spaces/Unicode permitted by the name rules.
  - *Single aggregate file for all templates* — rejected; diverges from the
    one-file-per-aggregate convention used everywhere else.

## Decision 3 — Name uniqueness & validation live in the save endpoint

- **Decision**: Enforce name rules in the save handler: trim; reject blank
  (FR-008); allow only letters, digits, spaces, `-`, `_` (FR-008a); reject `>100`
  chars (FR-008b). Uniqueness is **case-insensitive** (FR-004a) via a list scan
  comparing `OrdinalIgnoreCase`; the entered casing is stored for display.
- **Rationale**: At operator scale (~50 templates) a list scan is trivially fast and
  matches how endpoints already validate (`QueuesEndpoints` trims and checks fields).
  Centralizing validation in the endpoint keeps the repository a thin store.
- **Alternatives considered**: Unique index/locking — unnecessary for a
  single-operator desktop tool (last-write-wins is acceptable per Assumptions).

## Decision 4 — Overwrite is a server-mediated 409 → confirm → retry

- **Decision**: `POST /api/queue-templates` accepts `{ name, sequenceIds, overwrite }`.
  If a template with the same name (case-insensitive) already exists and `overwrite`
  is not `true`, respond `409 template_exists`. The UI shows the overwrite
  confirmation, then re-sends with `overwrite: true`, which replaces the existing
  template's entries wholesale (FR-009, FR-010).
- **Rationale**: Makes the "ask me if I'm sure overwriting" rule authoritative on the
  server and race-tolerant, while keeping a single save entry point (FR-025 — no
  separate template editor). Reuses the existing `{ error: { code, message, hint } }`
  error envelope.
- **Alternatives considered**: Separate `PUT` for overwrite — rejected; two code
  paths for one user action and the client would still need to know which to call.

## Decision 5 — Load = generic queue "replace entries" + frontend orchestration

- **Decision**: Add a generic capability to the **queue** side:
  `IQueueRuntimeStore.SetEntries(queueId, sequenceIds)` and
  `PUT /api/queues/{id}/entries` (`{ sequenceIds }`) that replaces all runtime
  entries (assigning fresh `EntryId`s), preserving order, and returns the updated
  queue detail. Loading a template is then a **frontend** orchestration:
  `GET /api/queue-templates/{id}` → `PUT /api/queues/{id}/entries` with the
  template's `sequenceIds`.
- **Rationale**: Keeps the queue and template modules decoupled (the backend never
  links a queue to a template), directly yields FR-013 (full replacement,
  order-preserving) and FR-015 (load is a copy — new EntryIds, no live link). A
  single bulk call is atomic and far cleaner than N add/remove round-trips.
- **Alternatives considered**:
  - *Loop existing add/remove endpoints from the client* — rejected; many
    round-trips, non-atomic, harder to keep ordered.
  - *Dedicated `POST /api/queues/{id}/load-template/{templateId}`* — rejected;
    couples the queue module to templates and re-introduces a hidden link.

## Decision 6 — Block load while running; allow save while running (server-enforced)

- **Decision**: `PUT /api/queues/{id}/entries` returns `409 queue_running` when the
  queue is Running (FR-014a), mirroring the existing update/delete guards. Saving a
  template reads entries only and is always allowed. The Load button is additionally
  disabled in the UI while running (defense in depth); Save is not.
- **Rationale**: A bulk replace of the running work-set is disruptive (clarified
  decision), so it is gated like other structural changes; saving is read-only and
  harmless.
- **Note**: The existing Queues UI already disables **Edit** while running, so the
  entry editor (and thus Load/Save) is normally only reachable when stopped; the 409
  guard is the authoritative backstop for the API contract.

## Decision 7 — Stale references handled on read, identical to queues

- **Decision**: Template entries store only `SequenceId`. Stale resolution happens
  when reading (`GET /api/queue-templates/{id}` and the queue detail after load):
  `SequenceName` resolved from `ISequenceRepository`, `Stale = name is null`
  (reusing the exact projection logic from `QueuesEndpoints.ProjectEntry`).
- **Rationale**: Satisfies FR-017 and keeps behavior consistent with how queues
  already flag deleted-sequence references. Saving never validates references, so a
  stale id round-trips untouched.

## Decision 8 — No logging of template actions; reuse existing UI primitives

- **Decision**: No log entries for save/load/delete (clarified). Reuse
  `ConfirmDeleteModal` for delete and overwrite/replace confirmations, the
  `SearchableDropdown`/list patterns for the picker, and `FormField`-style inputs for
  the name field. No new dependencies.
- **Rationale**: Consistent with the queue feature (logs execution only) and the
  constitution's "no unjustified new dependencies" gate; maximizes UX consistency.

## Summary of resolved unknowns

| Topic | Resolution |
|-------|-----------|
| Module boundary | New `QueueTemplates` domain module + endpoints; queue module unchanged except a generic replace-entries endpoint |
| Storage | `{storageRoot}/queue-templates/{id}.json`, GUID id, entries persisted |
| Name rules | Trim, non-blank, `[A-Za-z0-9 _-]`, ≤100 chars, case-insensitive unique |
| Overwrite | 409 `template_exists` → confirm → retry with `overwrite:true` |
| Load | `PUT /api/queues/{id}/entries` replace, frontend-orchestrated copy |
| Running guard | Load blocked (409 `queue_running`); save allowed |
| Stale refs | Resolved on read; ids stored verbatim |
| Logging | None this iteration |
