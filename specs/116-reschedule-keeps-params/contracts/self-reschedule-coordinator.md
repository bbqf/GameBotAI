# Contract: ISelfRescheduleCoordinator.ScheduleSelf (internal)

This feature changes no external interface. The HTTP API, the OpenAPI document, the `reschedule-self` action payload, the queue template format, and the execution log fields stay the same (FR-006, FR-009).

The only changed contract is the internal interface in `src/GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs`.

## Signature

```csharp
SelfRescheduleResult ScheduleSelf(
  string queueId,
  string sequenceId,
  SelfRescheduleOption option,
  TimeOnly? timerTimeOfDay,
  TimeSpan? timerRelativeOffset,
  GameBot.Domain.Parameters.ParameterScope? scope = null);
```

## Behavior

| Input | Result |
|-------|--------|
| No active run for `queueId` | `Outcome = NotRunning`. No booking. The scope is not kept. (No change.) |
| `option = OncePerRun` | One entry with `Scope = scope` goes into `PendingOncePerRun`. |
| `option = EveryStep` | `EveryStepInjections[sequenceId]` gets one entry with `Scope = scope`. It replaces an older entry of the same sequence. |
| `option = AtQueueStart`, cycling queue | One entry with `Scope = scope` goes into `PendingNextCycleStart`. |
| `option = AtQueueStart`, queue with no cycling | One entry with `Scope = scope` goes into `PendingOncePerRun`. |
| `option = Timer` | One entry with `Scope = scope` and the resolved `FireAt` goes into the Timer register. It replaces an older Timer entry of the same sequence. |
| `scope` is null (omitted) | Each entry has `Scope = null`. The run loop fires it with the queue scope, as before this change (FR-007). |

`SelfRescheduleResult` does not change.

## Caller

`SequenceExecutionService.DispatchSelfReschedule` gives the `scope` argument of `ExecuteAsync`: the scope that the queue run loop gave the run, before the runner adds the sequence layer.
