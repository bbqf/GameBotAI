# Data Model: Keep the parameter values of a queue entry on a run that reschedule-self starts

No persisted entity changes. The records below stay in memory for the life of one queue run.

## SelfRescheduleEntry (existing record, `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`)

| Field | Type | Change | Description |
|-------|------|--------|-------------|
| `Id` | `string` | none | Unique id of the booking. It links the log entry of the action to the booked run. |
| `SequenceId` | `string` | none | The sequence that the booked run starts. |
| `Option` | `SelfRescheduleOption` | none | OncePerRun, EveryStep, AtQueueStart, or Timer. |
| `FireAt` | `DateTimeOffset?` | none | The fire time for Timer. Null for the other options. |
| `Scope` | `ParameterScope?` | now set | The parameter scope that the queue run loop gave the run that made the booking. The coordinator now sets it for each option. Null means "no kept scope": the run loop then uses the queue scope. |

### Rules

- The coordinator sets `Scope` to the `scope` argument of `ScheduleSelf`, with no copy. `ParameterScope` is immutable, so the kept scope does not change when the template changes (FR-008).
- EveryStep register: one booking for each sequence. A new booking replaces the old booking and its scope (FR-010).
- Timer register: one booking for each sequence (most recent wins). A new booking replaces the old booking and its scope (FR-010). A held booking goes back with the same object, so it keeps its scope.
- OncePerRun and next-cycle-start registers: each booking stays in the queue with its own scope.

### State transitions

```text
reschedule-self step runs with scope S
  -> SelfRescheduleCoordinator.ScheduleSelf(..., scope: S)
  -> SelfRescheduleEntry { Scope = S } in its register
  -> run loop drains the register
  -> RunOneSequenceAsync(..., scope: entry.Scope ?? queueScope)
  -> booked run gets S as its scope (its own reschedule-self step keeps S again)
```

## ParameterScope (existing, `GameBot.Domain.Parameters`)

No change. For a template entry run the scope is the queue layer (`ParameterScope.FromQueue(queue)`) with the entry layer (`ParameterScopeLayers.Entry`, from `entry.ParameterValues`) under it. The runner adds the sequence layer when the run starts.
