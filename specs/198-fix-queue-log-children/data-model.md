# Data Model: Show the newest runs of a running queue in the execution log (B-044)

No stored field changes. The JSON file of an entry keeps its format.

## ExecutionLogEntry (existing, `src/GameBot.Domain/Logging/ExecutionLogModels.cs`)

Fields: `Id`, `TimestampUtc`, `ExecutionType`, `FinalStatus`, `ObjectRef`, `Navigation`, `Hierarchy`, `Summary`, `Details`, `StepOutcomes`, `RetentionExpiresUtc`, `RotatedToExecutionId`, `RotatedFromExecutionId`, `CancellationReason`, `TimeLimitMs`, `Origin`.

State transitions of `FinalStatus`:

| From | To | Cause |
|---|---|---|
| (new) | `running` | `LogQueueStartAsync`, `LogSequenceStartAsync`, `LogQueueRotateAsync` (continuation) |
| `running` | `success` or `failure` | finalize of the run, or the rotation close (`success`) |
| `running` | `failure` | NEW: the start sweep, when `TimestampUtc` is before the start of the current service process |

## Abandoned run close (new, `ExecutionLogAbandonedRun`)

A static rule. Input: one entry with `FinalStatus` `running`. Output: a new entry with:

| Field | Value |
|---|---|
| `FinalStatus` | `failure` |
| `Summary` | `Queue '<name>' run ended when the service stopped.` (type `queue`); `Sequence '<name>' run ended when the service stopped.` (type `sequence`); `'<name>' run ended when the service stopped.` (other types). At most 240 characters. |
| `<name>` | `ObjectRef.DisplayNameSnapshot`, or `ObjectRef.ObjectId` when the name is empty or white space |
| All other fields | Copied with no change |

Validation: the rule does not change an entry with a status other than `running` (it returns the same entry).

## In-memory entry list (existing, `FileExecutionLogRepository._entries`)

Rules after the fix:

- Each change of `_entries` occurs under `_mutex`: `AddAsync`, `UpsertAsync`, the first load, `DeleteExpiredAsync` (remove step), and `CloseAbandonedRunningAsync`.
- `DeleteExpiredAsync` removes from the current list each entry with `RetentionExpiresUtc <= nowUtc`, and deletes its file. An entry whose file delete fails is removed from the list too (no change). The return value is the number of deleted files (no change).
- No operation sets `_entries` to a copy that it read before it got `_mutex`.

## Repository interface (existing, `IExecutionLogRepository`)

New member:

```csharp
/// Closes each entry that has the status "running" and a timestamp before processStartUtc.
/// Returns the number of closed entries.
Task<int> CloseAbandonedRunningAsync(DateTimeOffset processStartUtc, CancellationToken ct = default);
```

## Service log lines (new)

| Event id | Level | Message |
|---|---|---|
| 7102 | Information | `Execution log start sweep closed {Count} running entries of an earlier service process.` (only when the count is more than 0) |
| 7103 | Warning | `Execution log start sweep failed.` |
