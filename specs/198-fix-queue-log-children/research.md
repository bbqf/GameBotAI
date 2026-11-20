# Research: Show the newest runs of a running queue in the execution log (B-044)

## R-001: Cause of the missing runs

**Decision**: The cause is a lost update in `FileExecutionLogRepository.DeleteExpiredAsync`. There is no limit on the number of children.

**Evidence** (production data directory, read only, 2026-11-20):

- The installer log `msiexec-932143eb...log` and the Windows event log show a service update at 2026-11-19 16:05:34 local time (15:05:34 UTC).
- Root entries of queue runs on disk: Exo `10053e15...` (07:07:17 UTC, status `running`, 123 children, last child 14:34:34 UTC), Exo `db870f26...` (15:05:51 UTC, 105 children from 15:06:01 to 19:15:34 UTC), Farm-2 `25626bbd...` (15:05:52 UTC, 96 children), Monday `14e8fa72...` (15:06:24 UTC).
- At 17:10 UTC the issue table lists Monday 15:06:24 as the newest Monday log, but lists the 07:07 and 07:09 roots as the newest Exo and Farm-2 logs. Thus the Exo and Farm-2 roots of 15:05 were on disk but not in the in-memory list.
- The root `db870f26...` now has the final state of the stop at 19:21 UTC. The issue owner saw the runs after this stop.

**Mechanism**:

1. `ExecutionLogRetentionCleanupService.ExecuteAsync` calls `CleanupExpiredAsync` at once at host start.
2. `DeleteExpiredAsync` calls `EnsureEntriesLoadedAsync`, which loads all files under `_mutex`. The queues that resume on service start call `LogQueueStartAsync` → `AddAsync`, which waits for `_mutex`.
3. After the load, `DeleteExpiredAsync` reads `_entries` one time (the `foreach`), deletes the expired files one by one, and at the end sets `_entries = remaining`. It does not hold `_mutex`.
4. The waiting `AddAsync` calls get `_mutex` and add their entries to `_entries`. The set in step 3 then replaces `_entries` with the copy from before these adds.
5. The list endpoint (`QueryAsync`) reads only `_entries`, so the new roots are not in the list. The children are added later, so they are in `_entries`, but the reader cannot find their root. `GetSubtreeAsync` can read a root from disk, but the reader does not know the id.
6. `LogQueueFinalizeAsync` at the stop calls `UpsertAsync`, which adds the root to `_entries` when it is not there. This is why the stop makes the runs readable. A service restart also reloads the list.

The 07:07 roots stay `running` because the update stopped the service and nothing wrote their final state. The issue owner read these old roots as "the log that stopped growing".

**Alternatives considered**: a limit on the number of children (no such code: `GetSubtreeAsync` and `BuildTreeNode` have no limit, and the stored roots have 96 to 123 children with no gap); the 24-hour rotation (the roots are younger than 24 hours, and no root of 2026-11-19 has a rotation link); a wrong `RootExecutionId` of the children (the children on disk point to the right root).

## R-002: How to make the cleanup safe

**Decision**: `DeleteExpiredAsync` holds `_mutex` for the whole remove step. Under the lock it reads the current `_entries`, deletes the file of each expired entry, and sets `_entries` to the current entries that are not expired. The load (`EnsureEntriesLoadedAsync`) stays before the lock, as now.

**Rationale**: With the lock, no write can come between the read and the set. Each write is either before the remove step (and the remove step sees it) or after it (and it updates the new list). A write of an expired entry after the remove step writes the file again; the next cleanup removes it again. This is the same rule as a write after the delete today.

**Alternatives considered**:

- Remove from the list under the lock, then delete the files outside the lock. Rejected: a write between the two steps can put back an entry whose file the cleanup then deletes, so the list and the disk do not agree until the next start.
- Compare-and-swap of `_entries` (`ImmutableInterlocked`). Rejected: the writes already use `_mutex`; two lock kinds make the code harder to read.
- Keep no in-memory list (read the files for each query). Rejected: 64,000 files for each list call is too slow.

## R-003: Close the `running` entries of an earlier service process

**Decision**: A new method `IExecutionLogRepository.CloseAbandonedRunningAsync(DateTimeOffset processStartUtc, CancellationToken ct)`. It loads the list, then under `_mutex` it finds each entry with `FinalStatus` `running` (ordinal, ignore case) and `TimestampUtc < processStartUtc`. For each one it writes a closed copy (`ExecutionLogAbandonedRun.Close(entry)`) to disk and to the list. It returns the number of closed entries.

**Close rule** (`ExecutionLogAbandonedRun.Close`): status `failure`; summary `Queue '<name>' run ended when the service stopped.` when `ExecutionType` is `queue`, else `Sequence '<name>' run ended when the service stopped.` for `sequence`, else `'<name>' run ended when the service stopped.`. The name is `ObjectRef.DisplayNameSnapshot`, or `ObjectRef.ObjectId` when the name is empty. All other fields are copied (id, timestamp, type, object reference, navigation, hierarchy, details, step outcomes, retention expiry, cancellation fields, rotation links, origin). The summary is trimmed to 240 characters (the `TrimSummary` rule).

**Process start time**: `Process.GetCurrentProcess().StartTime.ToUniversalTime()`, read one time in the hosted service. The hosted service has an internal constructor that takes the time, for tests.

**Rationale**: No run of the current process can have an entry that started before the process. The rule needs no order between `QueueResumeOnStartupService` and the cleanup service. In integration tests, all hosts run in the test process, so entries that a test host writes are newer than the process start, and the sweep does not change them. A test that seeds a `running` entry with a timestamp before the process start gets this entry closed by the default sweep; no current test seeds such an entry (search of `tests/integration` and `tests/contract` for `FinalStatus = "running"`). A test that needs a different boundary passes the time through the internal constructor.

**Alternatives considered**: a "service run id" field on each entry (rejected: a stored-format change for a one-time rule); a close at the stop (rejected: an update or a crash does not reach the finalize, which is the observed case); a host start time instead of the process start time (rejected: integration tests that seed `running` entries before the host start would change).

## R-004: Performance

**Decision**: Accept the lock wait of the remove step.

**Rationale**: The default cleanup interval is 30 minutes. Production writes about 9,000 entries each day, so one cleanup deletes about 190 files. A file delete takes less than 1 ms to a few ms, so the lock wait is less than 1 s. After a long downtime, the first cleanup can delete more files; the queue writes then wait longer, one time. The queue engine does not need a fast log write, and the load at start already holds the lock for a longer time (64,000 file reads). The remove step stays O(n) over the list.

## R-005: Where the start sweep runs

**Decision**: In `ExecutionLogRetentionCleanupService.ExecuteAsync`, one time, before the loop. It runs also when the retention policy is off. A failure of the sweep writes the warning line 7103 and does not stop the cleanup loop.

**Rationale**: This hosted service already loads the list at start, so the sweep adds no second load. The cleanup service has a scope and the logger.

## R-006: Documentation of the queue log rules

**Decision**: Set `operation.Description` of `GET /api/execution-logs` to the queue log rules, and add the same rules at the start of the description of `GET /api/execution-logs/{id}/subtree` (the current cancellation text stays after it). The text is in `SwaggerConfig.QueueLogRulesDescription` (one constant, used two times).

**Text**: "A queue log is the root entry of one queue run. Each sequence run of the queue is a child of this root. There is no limit on the number of children. After 24 hours, the service closes the root at the next firing and continues the run in a new root: rotatedToExecutionId and rotatedFromExecutionId link the two roots. At service start, each entry that has the status running and started before the service started gets the status failure, because its run ended when the service stopped."

**Rationale**: Acceptance criterion 3 of the issue asks that a limit is documented. The rotation is the only limit.

## R-007: Test design for the race

**Decision**: Add `internal Func<Task>? RemoveStepHookForTests { get; set; }` to `FileExecutionLogRepository`. `DeleteExpiredAsync` awaits it after it reads the list and before it sets the new list (inside the remove step). The test:

1. Writes 50 expired entries, and loads the list (one query).
2. Sets the hook to a `TaskCompletionSource` that the test controls, and starts `DeleteExpiredAsync`.
3. When the hook runs, starts `AddAsync` of a new root entry and `UpsertAsync` of an existing entry, then waits 200 ms, then releases the hook.
4. Awaits all tasks, then checks that `QueryAsync` gives the new root and the new state, and does not give an expired entry.

Before the fix, the hook point is outside the lock: the add finishes during the wait, and the set loses it, so the test fails. After the fix, the hook point is inside the lock: the add waits and comes after the remove step, so the test passes. The test does not depend on the time of the wait for its result after the fix.

**Alternatives considered**: a stress test with many parallel writes (rejected: not deterministic).
