# Phase 1 Data Model: Enable/Disable Template Sequences

## Entity: QueueTemplateEntry (modified)

A positional reference to a sequence within a `QueueTemplate`. Existing fields unchanged; one field added.

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| SequenceId | string | "" | Existing. ID of the referenced sequence. May repeat within a template. |
| ScheduleType | ScheduleType | OncePerRun | Existing. When the entry fires during a run. |
| TimerTimeOfDay | TimeOnly? | null | Existing. Time-of-day mode for a Timer entry. |
| TimerRelativeOffset | TimeSpan? | null | Existing. Relative mode for a Timer entry. |
| **Enabled** | **bool** | **true** | **NEW.** When `false`, the entry is retained in the template but excluded from queue runs. Absent in legacy JSON → deserializes to `true` (property initializer). Independent per entry (duplicate sequence ids toggle independently). |

**Validation rules**:
- No new validation. `Enabled` is a free boolean; any value is legal. Toggling it MUST NOT change `ScheduleType`, `TimerTimeOfDay`, or `TimerRelativeOffset` (FR-008).

**Persistence**:
- Serialized as part of the template JSON by `FileQueueTemplateRepository` (System.Text.Json, default options). New key `"Enabled": true|false`.
- Backward compatible: files without the key load as enabled.

## Entity: QueueTemplate (unchanged)

Ordered collection of `QueueTemplateEntry`. No structural change; entries now each carry `Enabled`.

## Derived / runtime view (behavioral, not persisted)

- **Queue run entry set**: When a run is built from the template (`RunAsync`, reading `template.Entries` directly), only entries with `Enabled == true` are materialized into the run's schedule partitions. Disabled entries are absent from execution and from schedule accounting for that run (FR-005/FR-006); the queue monitor projects from this filtered schedule and so excludes them too. A change to `Enabled` takes effect at the next run build (queue start), not mid-run.
- **Runtime store / loaded-entries display**: The runtime store (backing `GET /queues/{id}`) retains ALL entries in template order — the template editor renders from these and merges each entry's schedule/`enabled` from the template detail by position, so disabled entries must remain visible and correctly aligned to be re-enabled. Only the run (above) filters.

## Contract DTO deltas

- **TemplateEntrySaveRequest** (request): add `bool? Enabled`. Null → treated as `true` on save (so older clients that omit it keep entries enabled).
- **QueueTemplateEntryResponse** (response): add `bool Enabled` (default `true`), always populated from the stored entry so the editor renders last-saved state.
- **web-ui `QueueTemplateEntryDto`** (read): add `enabled: boolean`.
- **web-ui `TemplateEntrySaveDto`** (write): add `enabled?: boolean` (omit or `true` = on).
- **web-ui `EntrySchedule`** (view-model): add `enabled?: boolean` (undefined = on).

## State transitions

```
             toggle off (save)
  ENABLED  ────────────────────▶  DISABLED
 (runs on   ◀────────────────────  (retained in template,
  next run)   toggle on (save)      skipped on next run)
```

Both states preserve position, sequence reference, and schedule configuration. There is no third state; absence of stored state == ENABLED.
