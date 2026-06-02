# Phase 1 Data Model: Queue–Template Link with Auto-Load

This feature adds **one persisted field** and **one runtime capability**. No new entity is
introduced; the "Queue–Template Link" from the spec is realized as an attribute on the
existing queue config.

## Entities

### ExecutionQueue (MODIFIED — persisted config, `data/queues/{id}.json`)

| Field | Type | Notes |
|-------|------|-------|
| Id | string | Existing. Stable GUID "N". |
| Name | string | Existing. Required, non-empty. |
| EmulatorSerial | string | Existing. Immutable after create. |
| CycleExecution | bool | Existing. |
| **LinkedTemplateId** | **string?** | **NEW.** Stable ID of the linked `QueueTemplate`, or `null` when unlinked (0..1). Persisted. References by ID (FR-001a) — rename-safe, broken only by delete. |
| CreatedAt / UpdatedAt | DateTimeOffset? | Existing. `UpdatedAt` bumped when the link is set/cleared. |

**Validation / rules**:
- `LinkedTemplateId` is optional; absent in old files ⇒ `null` (backward compatible, no migration).
- Setting the link to a non-null value requires that template to exist (enforced at the
  endpoint, not the repository).
- A queue has at most one link (the field is scalar) — FR-001/FR-003 satisfied by assignment.

### QueueTemplate (UNCHANGED)

No change. Holds **no** back-reference to linking queues (FR-015). May be referenced by
0..n queues. Deleting it leaves linking queues' `LinkedTemplateId` dangling until each is
next opened, at which point auto-load clears it (FR-011).

### Queue runtime state (MODIFIED capability — `QueueRuntimeStore`, in-memory)

Entries and status remain non-persistent. New capability:

| Operation | Signature | Behavior |
|-----------|-----------|----------|
| **HasRuntimeState** | `bool HasRuntimeState(string queueId)` | **NEW.** True iff a runtime state record exists for the queue (`_states.ContainsKey`). Distinguishes "never materialized since startup" (false) from "exists, possibly empty" (true). Backs the auto-load "first display only" guard. |

Existing `GetEntries`, `AddEntry`, `RemoveEntry`, `SetEntries`, `GetStatus`, `SetStatus`,
`Remove` are unchanged. Note: `GetEntries` does **not** create state; `AddEntry`,
`SetEntries`, `SetStatus` do (via `StateFor`). `Remove` deletes the record (so
`HasRuntimeState` returns false again — relevant on queue delete).

## Lifecycle / state transitions

### The link

```
unlinked ──load template T──▶ linked→T ──load template U──▶ linked→U
   ▲                              │
   │                              ├──save entries as template S──▶ linked→S
   │                              │
   └──auto-load finds T missing───┘   (link cleared + persisted on next open)
```

- **Set/replace**: load a template (link ← loaded template id) or save entries to a
  template (link ← saved template id), via `PUT {id}/template`. Replaces any prior link.
- **Clear**: only automatic — when auto-load cannot resolve the linked template (FR-011).
  No explicit unlink control this iteration (FR-005a). (`PUT {id}/template {null}` exists
  as the mechanism but is not surfaced as a user action.)

### Auto-load (first display per service lifetime)

```
GET /api/queues/{id}:
  linked? ──no──▶ open with current runtime entries (no auto-load)         (FR-007)
     │yes
  Running? ──yes──▶ open with current entries, no replace                  (FR-010)
     │no
  HasRuntimeState? ──yes──▶ open with current entries (no re-fill)         (FR-012)
     │no
  resolve template:
     missing ──▶ clear link (persist), open empty, no error                (FR-011)
     found   ──▶ SetEntries(queueId, template.Entries.sequenceIds), open   (FR-006/006a/008/009)
```

After the first materialization, `HasRuntimeState` is true, so subsequent opens (and the
GETs issued after each add/remove) do not re-load — protecting deliberate edits, including
a deliberate clear-to-empty.

## Response projections (Service contracts)

| Contract | Change |
|----------|--------|
| `QueueResponse` (list/summary) | add `LinkedTemplateId` (string?) |
| `QueueDetailResponse` (detail) | inherits `LinkedTemplateId`; add `LinkedTemplateName` (string?, resolved from template repo; null if unlinked/unresolved) |
| `SetQueueTemplateLinkRequest` (NEW) | `{ TemplateId: string? }` |

Frontend DTOs (`queues.ts`) mirror these: `linkedTemplateId`, `linkedTemplateName` on
`QueueDto`/`QueueDetailDto`.
