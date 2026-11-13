# Data Model: Before Each Run Before Every Firing

This feature changes no stored data, no API shape, and no domain type.

## Queue template entry (no change)

- `ScheduleType` keeps the value `BeforeEachRun` (wire value `"BeforeEachRun"`, numeric value 4).
- No new field. No new flag. Stored templates load with no change.

## Before Each Run pass (behaviour change only)

- **Contents**: all enabled entries with schedule type `BeforeEachRun`, in template order.
- **Old rule**: at most one pass in each wake-up, before the first triggering firing.
- **New rule**: one pass immediately before each triggering firing that runs.
- **Not counted**: a pass execution does not count toward the executed total.
- **Failure**: a failed execution adds 1 to the failed total for each execution. It is non-fatal, and the triggering firing still runs.

## Run-loop state (internal)

- The local flag `beforeEachRunRanThisIteration` in `QueueExecutionService.RunLoopAsync` goes away. No other state changes.
