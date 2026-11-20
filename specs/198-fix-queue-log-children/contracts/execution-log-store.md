# Contract: Execution log store and API description (B-044)

## C-1: `IExecutionLogRepository.DeleteExpiredAsync(nowUtc, ct)`

- Removes each entry with `RetentionExpiresUtc <= nowUtc` from the list and from disk. Returns the number of deleted files.
- The remove step holds the same lock as `AddAsync` and `UpsertAsync`.
- Guarantee: after the call, each entry that `AddAsync` or `UpsertAsync` wrote before or during the call, and that is not expired, is in the list in its latest written state.

## C-2: `IExecutionLogRepository.CloseAbandonedRunningAsync(processStartUtc, ct)` (new)

- For each entry with `FinalStatus` `running` (ignore case) and `TimestampUtc < processStartUtc`: writes the closed entry (`ExecutionLogAbandonedRun.Close`) to its file and to the list.
- Does not change an entry with `TimestampUtc >= processStartUtc`, or with another status.
- Holds the write lock while it changes the list and the files. Returns the number of closed entries.

## C-3: `ExecutionLogRetentionCleanupService` start

- Before the first cleanup, calls `CloseAbandonedRunningAsync` one time with the start time of the service process.
- Count > 0: writes log line 7102. Exception: writes log line 7103 and continues with the cleanup loop.
- The sweep runs also when the retention policy is off.

## C-4: HTTP API (no shape change)

- `GET /api/execution-logs`: response shape not changed. Each stored root entry that is not expired is in the result, also a root that was written during a cleanup.
- `GET /api/execution-logs/{id}/subtree`: response shape not changed. Each stored child of the root is in the tree, with no limit on the number of children.

## C-5: OpenAPI description

- `GET /api/execution-logs` → `description` is the queue log rules text (research R-006).
- `GET /api/execution-logs/{id}/subtree` → `description` starts with the queue log rules text, then the current cancellation text.
- Test: the generated OpenAPI document contains the phrases "no limit on the number of children", "rotatedToExecutionId", and "ended when the service stopped" in both operation descriptions.
