# Data Model: Queue Sequence Scheduling

**Feature**: 053-schedulable-sequences  
**Date**: 2026-06-03

---

## New: `ScheduleType` Enum

**File**: `src/GameBot.Domain/QueueTemplates/ScheduleType.cs`

```
ScheduleType (enum, JSON-serialized as string)
├── OncePerRun = 0   (default; preserves existing behavior)
├── EveryStep = 1
└── Timer = 2
```

- Decorated with `[JsonConverter(typeof(JsonStringEnumConverter))]`.
- Default `OncePerRun` guarantees backward-compatible deserialization of existing JSON files.

---

## Updated: `QueueTemplateEntry`

**File**: `src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `SequenceId` | `string` | `""` | Existing — unchanged |
| `ScheduleType` | `ScheduleType` | `OncePerRun` | New — serialized as string |
| `TimerTimeOfDay` | `TimeOnly?` | `null` | New — required when `ScheduleType == Timer`; ignored otherwise |

**Persistence**: `FileQueueTemplateRepository` serializes `QueueTemplateEntry` as JSON. `ScheduleType` serializes as a string enum; `TimerTimeOfDay` serializes as `"HH:mm:ss"` via `System.Text.Json`. Existing entries in JSON files without `ScheduleType` deserialize to `OncePerRun` (the enum default).

---

## New: `TemplateEntrySaveRequest` (API contract)

**File**: `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `SequenceId` | `string?` | Yes | The sequence to reference |
| `ScheduleType` | `string?` | No | `"OncePerRun"` \| `"EveryStep"` \| `"Timer"`; defaults to `"OncePerRun"` when omitted |
| `TimerTimeOfDay` | `string?` | Conditional | `HH:mm` (24-hour); required when `ScheduleType == "Timer"` |

Validation rules (enforced at the endpoint):
- `SequenceId` must be non-blank.
- `ScheduleType` must be a recognized value or absent (defaults to `OncePerRun`).
- When `ScheduleType == "Timer"`: `TimerTimeOfDay` must be present and parseable as `HH:mm`.
- When `ScheduleType != "Timer"`: `TimerTimeOfDay` is ignored (set to `null` in domain model).

---

## Updated: `SaveQueueTemplateRequest` (API contract)

**File**: `src/GameBot.Service/Contracts/QueueTemplates/SaveQueueTemplateRequest.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string?` | Unchanged |
| `Entries` | `TemplateEntrySaveRequest[]?` | **Replaces** the old `SequenceIds string[]` |
| `Overwrite` | `bool` | Unchanged |

---

## Updated: `QueueTemplateEntryResponse` (API contract)

**File**: `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs`

| Field | Type | Notes |
|-------|------|-------|
| `SequenceId` | `string` | Existing — unchanged |
| `SequenceName` | `string?` | Existing — unchanged |
| `Stale` | `bool` | Existing — unchanged |
| `ScheduleType` | `string` | New — `"OncePerRun"` \| `"EveryStep"` \| `"Timer"` |
| `TimerTimeOfDay` | `string?` | New — `HH:mm` string or `null` |

---

## Runtime-only: Timer firing state

**Not persisted.** Maintained in `QueueExecutionService.RunAsync` as a local variable:

```
timerFiredDate: Dictionary<int, DateOnly>
```

- Key: zero-based index of the timer entry within `timerSnapshot` (the pre-partitioned list of timer entries for this run).
- Value: the `DateOnly` (server local) on which that entry last fired.
- Missing key = never fired this run.
- Declared **outside and before the `do {` loop body** so it persists across all cycles of the same run.
- Evaluated at each iteration boundary: entry fires if `TimeOnly.FromDateTime(DateTime.Now) >= entry.TimerTimeOfDay` AND the dictionary either has no entry for index `i` or its stored `DateOnly` differs from today's date.
- After firing: `timerFiredDate[i] = DateOnly.FromDateTime(DateTime.Now)`.
- Placing this dictionary inside the `do { }` body would reset it every cycle, causing the timer to fire on every iteration rather than once per calendar day — a critical correctness bug.

---

## Key entities unchanged

- `QueueTemplate` — no changes to top-level fields.
- `QueueEntry` (runtime) — not changed; still holds `EntryId` + `SequenceId` only (schedule is template-layer concern).
- `ExecutionQueue` — not changed.
- `ExecutionLogEntry` / `ExecutionLogContext` — not changed; schedule-typed sequences log identically to once-per-run sequences (index incremented globally across all executions in the run).
