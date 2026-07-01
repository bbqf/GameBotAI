# Phase 1 Data Model: Sequence Self-Rescheduling

Entities derived from the spec's **Key Entities** plus the concrete types introduced by the plan.
Persisted shapes are additive and backward compatible; run-scoped shapes are in-memory only and never
serialized.

---

## 1. Self-Reschedule Action (persisted, inside the sequence definition)

Carried by the **existing** `SequenceActionPayload` (`src/GameBot.Domain/Commands/SequenceStep.cs`) —
no new persisted container. A self-reschedule step is a `SequenceStep` with
`StepType = Action` and:

```jsonc
"action": {
  "type": "reschedule-self",
  "schemaVersion": "1",
  "parameters": {
    "option": "Timer",              // AtQueueStart | OncePerRun | Timer | EveryStep
    "timerTimeOfDay": "14:30:00",   // present only when option == Timer AND time-of-day mode
    "timerRelativeOffset": "00:10:00" // present only when option == Timer AND relative mode
  }
}
```

### `SelfRescheduleOption` (NEW enum, `GameBot.Domain.Commands.SelfReschedule`)

| Value | Wire string | Meaning |
|-------|-------------|---------|
| `AtQueueStart` | `"AtQueueStart"` | Fire at next cycle start (cycling) / next iteration boundary (non-cycling) — FR-009 |
| `OncePerRun` | `"OncePerRun"` | Append after remaining once-per-run steps of the current cycle — FR-007 |
| `Timer` | `"Timer"` | Fire once at a target instant (relative offset or time-of-day) — FR-005/FR-006 |
| `EveryStep` | `"EveryStep"` | Register to fire after each subsequent normal step (loop-safe) — FR-008 |

Mirrors `QueueTemplates.ScheduleType` semantically; named distinctly because the wire value
`EveryStep` is shared but the at-queue-start mid-run meaning is option-specific.

### `SelfReschedulePayload` (NEW typed view, `GameBot.Domain.Commands.SelfReschedule`)

A thin reader/validator over `SequenceActionPayload.Parameters` (the dictionary stays the storage):

| Field | Type | Rules |
|-------|------|-------|
| `Option` | `SelfRescheduleOption` | Required; must parse to a known value |
| `TimerTimeOfDay` | `TimeOnly?` | Allowed **only** when `Option == Timer`; mutually exclusive with `TimerRelativeOffset` |
| `TimerRelativeOffset` | `TimeSpan?` | Allowed **only** when `Option == Timer`; non-negative; mutually exclusive with `TimerTimeOfDay` |

**Validation rules** (enforced in `ActionPayloadValidationService`, mirroring the template editor):
- `option` required and a known enum value.
- When `option == Timer`: **exactly one** of `timerTimeOfDay` / `timerRelativeOffset` present.
- When `option != Timer`: neither timer field present (ignored/rejected as malformed).
- `timerRelativeOffset` ≥ 0 and within the same range bound as feature 059's `RelativeOffsetParser`.
- `reschedule-self` added to the set of recognized action types so an unknown-type error is not raised.

---

## 2. Originating Queue Run Context (in-memory, threaded)

### `ExecutionLogContext` (MODIFIED, `GameBot.Service.Services.ExecutionLog`)

Add one field; propagated through nesting exactly like `RootExecutionId`:

| New field | Type | Set by | Propagation |
|-----------|------|--------|-------------|
| `OriginatingQueueId` | `string?` | `QueueExecutionService.RunOneSequenceAsync` (= `queue.Id`) | Copied onto every child context built in `SequenceExecutionService.ExecuteAsync` |

`OriginatingQueueId == null/empty` ⇒ the running sequence was **not** started from a queue ⇒ the
action is a no-op success (FR-011). Standalone `sequences/{id}/execute` runs never set it.

---

## 3. Ephemeral Run Schedule Entry (in-memory, run-scoped)

### `SelfRescheduleEntry` (NEW record, `GameBot.Service.Services.QueueExecution`)

```csharp
internal sealed record SelfRescheduleEntry(
  string Id,                 // unique; links the action's log entry to the resulting firing
  string SequenceId,         // the sequence rescheduling itself
  SelfRescheduleOption Option,
  DateTimeOffset? FireAt);   // resolved instant for Timer options; null for the others
```

### `QueueRunHandle` (MODIFIED, `QueueRunHandle.cs`) — new registers, all non-persisted

| Register | Type | Option(s) served | Lifetime / drain |
|----------|------|------------------|------------------|
| `PendingOncePerRun` | `ConcurrentQueue<SelfRescheduleEntry>` | OncePerRun, non-cycling AtQueueStart fallback | Drained after the once-per-run pass of the current cycle |
| `PendingNextCycleStart` | `ConcurrentQueue<SelfRescheduleEntry>` | AtQueueStart (cycling) | Drained at the top of the next cycle, before once-per-run |
| `PendingTimerFirings` | `List<SelfRescheduleEntry>` (guarded) | Timer (relative + time-of-day) | Each fired once when `now ≥ FireAt`, then removed; unfired entries discarded at run end |
| `EveryStepInjections` | `ConcurrentDictionary<string,SelfRescheduleEntry>` keyed by `SequenceId` | EveryStep | Drained (snapshot) after each subsequent normal step; idempotent per sequence (loop-safe) |

Existing `PendingLiveSchedules` (feature 059) is unchanged and unrelated. All four new registers are
created with the handle and die with it — on normal completion **and** on stop/abort (FR-017), because
the handle is removed and disposed in `RunAsync`'s `finally`.

**State transitions of an entry**:
`created (Scheduled)` → `due` (Timer: `now ≥ FireAt`; others: matching boundary reached) → `fired`
(removed; produces a child-sequence log entry) **or** `abandoned` (run ends/stops first → discarded,
no firing, no failure — FR-015/FR-017).

---

## 4. Execution Log Entries (persisted record of what ran)

No schema change — reuses existing execution-log objects with added detail metadata:

| Log entry | Produced by | Detail added |
|-----------|-------------|--------------|
| **Reschedule decision** (a sequence step) | `SequenceExecutionService` via `SequenceExecutionResult` step + `detailItems` | `option`, `resolvedTiming` (instant / "next cycle" / "this cycle"), `outcome` = `scheduled`\|`noop`, `noopReason` (e.g. "not started from a queue") — FR-013 |
| **Resulting firing** (a child sequence execution) | `QueueExecutionService.RunOneSequenceAsync` | note "scheduled by self-reschedule" + the originating action `Id`, so the extra firing is attributable — FR-014 |

---

## 5. Coordinator contract (in-memory service)

### `ISelfRescheduleCoordinator` (NEW) / `SelfRescheduleCoordinator` (NEW)

```csharp
SelfRescheduleResult ScheduleSelf(
  string queueId,
  string sequenceId,
  SelfRescheduleOption option,
  TimeOnly? timerTimeOfDay,
  TimeSpan? timerRelativeOffset);
```

| `SelfRescheduleOutcome` | When | Action log outcome |
|-------------------------|------|--------------------|
| `Scheduled` | Run found in `IQueueRunRegistry`; entry injected into the matching register | `scheduled` (+ resolved timing) |
| `NotRunning` | No active run for `queueId` (race: run ended mid-sequence) | treated as no-op; logged with reason |

`SelfRescheduleResult` carries the outcome, the resolved `FireAt`/target description, and the entry
`Id` for log linkage. The **no-op-because-not-from-a-queue** case (FR-011) is decided *before* the
coordinator is called (when `OriginatingQueueId` is empty) and never reaches it.

### `IQueueRunRegistry` (NEW) / `QueueRunRegistry` (NEW)

Singleton owner of `ConcurrentDictionary<string queueId, QueueRunHandle>`, extracted from
`QueueExecutionService._runs`. Methods: `TryAdd`, `TryGet`, `Remove` (names CamelCase). Holds no
service dependencies, breaking the `SequenceExecutionService ↔ QueueExecutionService` cycle.

---

## Relationships

```
SequenceStep ──has──▶ SequenceActionPayload (type "reschedule-self")
                              │ read/validated as
                              ▼
                      SelfReschedulePayload { Option, TimerTimeOfDay?, TimerRelativeOffset? }

SequenceExecutionService.ExecuteAsync
   ├─ reads ExecutionLogContext.OriginatingQueueId   (null ⇒ no-op success)
   └─ actionDispatcher ─▶ ISelfRescheduleCoordinator.ScheduleSelf
                                   │ via
                                   ▼
                          IQueueRunRegistry.TryGet(queueId) ─▶ QueueRunHandle
                                   │ injects SelfRescheduleEntry into
                                   ▼
       PendingOncePerRun | PendingNextCycleStart | PendingTimerFirings | EveryStepInjections

QueueExecutionService run loop ─ drains each register at its boundary ─▶ RunOneSequenceAsync (the firing)
```
