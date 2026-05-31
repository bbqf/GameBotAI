# Phase 1 Data Model: Queue Templates

**Feature**: 047-queue-templates | **Date**: 2026-05-31

## Domain entities

### QueueTemplate (persisted)

`GameBot.Domain.QueueTemplates.QueueTemplate` — stored as
`{storageRoot}/queue-templates/{id}.json`.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | GUID (`N` format); generated on first save. Filename = `{Id}.json`. |
| `Name` | `string` | Human-readable, unique case-insensitively. Stored with the operator's casing. Rules: trimmed, non-blank, `[A-Za-z0-9 _-]` only, ≤100 chars. |
| `Entries` | `List<QueueTemplateEntry>` | Ordered; persisted. May be empty (empty template allowed, FR-011). |
| `CreatedAt` | `DateTimeOffset?` | Set on first create. |
| `UpdatedAt` | `DateTimeOffset?` | Set on every save (including overwrite). |

**Validation (enforced in the save endpoint, see contracts):**
- `Name` required → else `400 invalid_request` "name is required" (FR-008).
- `Name` charset violation → `400 invalid_request` naming the constraint (FR-008a).
- `Name` length > 100 → `400 invalid_request` "name must be 100 characters or fewer" (FR-008b).
- `Name` collides (case-insensitive) with an existing template and `overwrite != true`
  → `409 template_exists` (FR-009).

### QueueTemplateEntry (persisted)

`GameBot.Domain.QueueTemplates.QueueTemplateEntry`

| Field | Type | Notes |
|-------|------|-------|
| `SequenceId` | `string` | Reference to an authored sequence. Order within the list is run order (FR-005). The same id may appear multiple times (FR-006). No per-entry id is persisted — entries are positional. |

> A template entry stores **only** the sequence reference. Resolved display name and
> staleness are computed at read time (not stored), identical to queue entries.

## Persisted JSON shape

```json
{
  "id": "8f3c2b1a4d5e6f70891a2b3c4d5e6f70",
  "name": "Daily Farm",
  "entries": [
    { "sequenceId": "seq-collect" },
    { "sequenceId": "seq-upgrade" },
    { "sequenceId": "seq-collect" }
  ],
  "createdAt": "2026-05-31T10:00:00+00:00",
  "updatedAt": "2026-05-31T10:00:00+00:00"
}
```

## Repository contract

`GameBot.Domain.QueueTemplates.IQueueTemplateRepository`

| Method | Returns | Notes |
|--------|---------|-------|
| `GetAsync(string id)` | `QueueTemplate?` | `null` if missing/invalid id. |
| `ListAsync()` | `IReadOnlyList<QueueTemplate>` | All templates (config + entries). |
| `FindByNameAsync(string name)` | `QueueTemplate?` | Case-insensitive (`OrdinalIgnoreCase`) name match; drives uniqueness/overwrite. |
| `CreateAsync(QueueTemplate t)` | `QueueTemplate` | Assigns `Id` + timestamps; writes file. |
| `UpdateAsync(QueueTemplate t)` | `QueueTemplate` | Overwrite existing (entries + `UpdatedAt`). |
| `DeleteAsync(string id)` | `bool` | `false` if not found. |

`FileQueueTemplateRepository` mirrors `FileQueueRepository`: safe-id regex,
path-traversal guard, indented JSON, `Directory.CreateDirectory` of
`{storageRoot}/queue-templates` in the constructor.

## Touched queue-side model (existing module)

### IQueueRuntimeStore — new method

| Method | Returns | Notes |
|--------|---------|-------|
| `SetEntries(string queueId, IEnumerable<string> sequenceIds)` | `IReadOnlyList<QueueEntry>` | Replaces all entries for the queue; assigns a fresh `EntryId` per entry; preserves the given order; empty input clears entries. Thread-safe under the per-state lock, consistent with existing `AddEntry`/`RemoveEntry`. |

No change to `QueueEntry`, `ExecutionQueue`, or queue persistence. The replace is a
runtime-only operation (queue entries remain non-persistent by 046's design).

## Relationships

```
QueueTemplate 1 ──── * QueueTemplateEntry ──ref──> Sequence (by SequenceId, may be stale)

QueueTemplate  ──(copied at load time, no live link)──>  Queue runtime entries
Queue          ──(read at save time)──────────────────>  QueueTemplate entries
```

- Loading copies template entries into a queue's runtime store as **new** entries
  (new `EntryId`s); no ongoing link (FR-015, FR-016).
- Deleting a template does not touch any queue's entries (FR-020).

## State & lifecycle

Templates have no execution state. Lifecycle: **created** (first save) →
**overwritten** (subsequent save under same name, replaces entries) → **deleted**.
Only the latest saved state is retained (no versioning — Out of Scope).

## Scale

≤ ~50 templates, ≤ ~100 entries each (Assumptions). List scans and full-file
read/writes are well within the <1s responsiveness target (SC-007).
