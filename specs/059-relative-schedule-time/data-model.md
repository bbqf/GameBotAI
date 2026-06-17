# Phase 1 Data Model: Relative-Time Sequence Scheduling

## Entities

### QueueTemplateEntry (extended)

`src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`

| Field | Type | Existing? | Notes |
|-------|------|-----------|-------|
| `SequenceId` | `string` | existing | Referenced sequence. |
| `ScheduleType` | `ScheduleType` | existing | `OncePerRun` (default) \| `EveryStep` \| `Timer`. Unchanged. |
| `TimerTimeOfDay` | `TimeOnly?` | existing | Time-of-day for a `Timer` entry in **time-of-day mode**. Null otherwise. |
| `TimerRelativeOffset` | `TimeSpan?` | **NEW** | Duration offset for a `Timer` entry in **relative mode**. Null otherwise. Serializes as `"HH:mm:ss"`. |

**Mode inference / invariants** (a `Timer` entry):
- Relative mode ⇔ `TimerRelativeOffset != null`. Time-of-day mode ⇔ `TimerTimeOfDay != null`.
- Exactly one of `TimerTimeOfDay` / `TimerRelativeOffset` MUST be non-null (FR-003). Both-null or both-set is invalid → API 400.
- Non-`Timer` entries: both timer fields are null (ignored if present).

**Validation rules** (applied at the API layer, FR-002/FR-017):
- `TimerRelativeOffset` must parse from `"HH:mm:ss"`, be `>= TimeSpan.Zero`, and `<= 24:00:00`.
- A value of `00:00:00` is valid (fires at the first iteration boundary, FR edge case "Offset of zero").

**Backward compatibility**: entries persisted before this feature have no `timerRelativeOffset` key → deserialize as `null` → time-of-day mode (existing behavior). No migration (SC-008).

### Relative Offset (value)

A non-negative `TimeSpan` (hours/minutes/seconds; ≥ minute/second precision). Wire/persisted form `"HH:mm:ss"`. UI composes it from hours/minutes/seconds inputs.

### Live Relative Schedule (in-memory, per run)

Held on `QueueRunHandle` (`src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`):

| Field | Type | Notes |
|-------|------|-------|
| `RunStartedAt` | `DateTimeOffset` | Captured once at run start; the anchor for template relative offsets. |
| `PendingLiveSchedules` | `ConcurrentDictionary<string, DateTimeOffset>` | Key = `SequenceId`; value = `expectedFireAt` (= call time + offset). Upsert = most-recent-wins (FR-011). Entry removed when fired. Never persisted (FR-008). |

**Lifecycle**: created empty with the handle; mutated by `ScheduleRelative` (endpoint thread); read + entries removed by the run loop at iteration boundaries; discarded with the handle when the run ends.

## State & evaluation (runtime, `QueueExecutionService`)

Per-run state, declared outside the cycle `do/while` so it survives cycles:
- `runStartedAt` (`DateTimeOffset`) — run-start anchor.
- `timerFiredDate : Dictionary<int, DateOnly>` — existing, for time-of-day timers (per calendar day).
- `relativeTimerFired : HashSet<int>` — **NEW**, indices of relative timers already fired this run (fire-once-per-run, FR-005).

At each **iteration boundary** (before `OncePerRun` steps), in this order (all before regular steps, FR-015):
1. **Time-of-day timers** — unchanged; do **not** count toward `executed`.
2. **Relative timers** — for each relative `Timer` entry index `i` not in `relativeTimerFired`: if `now - runStartedAt >= offset`, fire once via `RunOneSequenceAsync`, `executed++` (FR-016a), add `i` to `relativeTimerFired`.
3. **Live schedules** — snapshot `PendingLiveSchedules` where `fireAt <= now`; for each, fire via `RunOneSequenceAsync`, `executed++` (FR-016a), then `TryRemove` the key (fires once, FR-009).

Termination is unchanged: the run completes when all `OncePerRun` entries have executed (non-cyclic) or on stop/cancel; `executed` does not gate termination (SC-010).

## API contracts (DTOs)

### TemplateEntrySaveRequest (extended)
`+ string? TimerRelativeOffset` — `"HH:mm:ss"`; required iff `Timer` mode is relative; mutually exclusive with `TimerTimeOfDay`.

### QueueTemplateEntryResponse (extended)
`+ string? TimerRelativeOffset` — `"HH:mm:ss"` when the entry is a relative timer; null otherwise.

### LiveScheduleRequest (new)
`{ string? SequenceId; string? Offset }` — `Offset` is `"HH:mm:ss"`.

### LiveScheduleResponse (new)
`{ string SequenceId; string Offset; DateTimeOffset ExpectedFireAt }`.

### LiveScheduleOutcome (new enum, service layer)
`Scheduled` \| `NotRunning` (queue-not-found and unknown-sequence are handled at the endpoint before calling the service).

## Web-UI types (extended)
- `queueTemplates.ts`: `QueueTemplateEntryDto` and `TemplateEntrySaveDto` gain `timerRelativeOffset?: string | null`.
- `queues.ts`: new `liveScheduleSequence(queueId, sequenceId, offset)` → `{ sequenceId, offset, expectedFireAt }`.
- `QueueEntryList.tsx` `EntrySchedule`: gains a relative mode + offset value (hours/minutes/seconds composed to `"HH:mm:ss"`).
