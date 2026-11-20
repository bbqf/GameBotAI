# Feature Specification: Show the newest runs of a running queue in the execution log (B-044)

**Feature Branch**: `198-fix-queue-log-children`  
**Created**: 2026-11-20  
**Status**: Draft  
**Input**: GitHub issue #408, "B-044: The execution log of a long-running queue stops growing near 120 children; later runs are in no log". Closes #408.

## Background

The issue reports that the execution log of a long-running queue stops growing near 120 children, while the queue continues to run its sequences. A stop of the queue makes the missing runs readable. The issue owner thinks that there is a limit near 120 children, but says that this is not proven.

An analysis of the stored execution log files of the production service (read only) gives these facts:

- The service was updated and restarted at 2026-11-19 15:05 UTC (installer log at 16:05:34 local time). The queues that have "resume on service start" started again at 15:05:51 (Exo), 15:05:52 (Farm-2) and 15:06:24 (Monday).
- The root entry of the Exo run that started at 07:07:17 UTC has 123 children. The last child is at 14:34:34 UTC. This root still has the status `running`, because the run ended with the service restart and nobody closed the entry.
- The root entry of the new Exo run (15:05:51 UTC) is on disk and has 105 children, from 15:06:01 to 19:15:34 UTC (the PNS.TrainTroops runs are in it). The new Farm-2 root (15:05:52 UTC) has 96 children. These two roots were not in the list of `GET /api/execution-logs` at 17:10 UTC. The Monday root (15:06:24 UTC) was in the list.
- The stop at 19:21 UTC wrote the final state of the new Exo root. After this write, the root was in the list.

Thus there is no limit on the number of children. The count of about 120 is a coincidence: it is the number of runs between 07:07 and the restart. The real defect: the service keeps the execution log entries in an in-memory list, and the list endpoint and the subtree endpoint read only this list. The retention cleanup reads the list, deletes the expired files, and then replaces the list with the entries that it kept. It does this without the lock that the writes use. Each entry that a write adds or changes while the cleanup runs is lost from the in-memory list (the file stays on disk). At service start, the first cleanup loads all files (about 64,000) and then deletes the expired ones, while the queues that resume write their new root entries. These root entries are lost from the list. The children of the lost root are in the list, but no listed root points to them. A later write of the root (the final state at the stop) puts the root back in the list. A service restart also reloads the list from disk.

## Clarifications

### Session 2026-11-20

The pipeline runs with no manual review. Each answer is the most reasonable option, chosen from the spec, the code, and the stored data.

- Q: Is there a limit on the number of children of a queue log? → A: No. The only rule that ends a queue log while the queue runs is the current 24-hour rotation (a new segment starts after 24 hours). The fix documents this in the API description of the execution logs. Rationale: the stored data shows no limit; the 120 children are the runs before a service restart.
- Q: Which status does a `running` entry of an earlier service process get when the service starts? → A: `failure`, with a summary that says that the run ended when the service stopped. Rationale: the log has only three statuses (`success`, `running`, `failure`), and an abandoned sequence entry already gets `failure` with a summary that gives the cause (`SequenceExecutionService.FinalizeAbandonedAsync`).
- Q: How does the service know that a `running` entry is from an earlier service process? → A: Its timestamp is earlier than the start time of the current service process. Rationale: no run of the current process can have an entry that started before the process started, so the rule needs no order between the hosted services.
- Q: Which entries does the start sweep close: only queue roots, or all `running` entries? → A: All `running` entries of an earlier process (queue roots, queue continuation segments, and sequence entries). Rationale: a sequence entry of an earlier process that says `running` misleads the operator in the same way as a queue root.
- Q: Does the fix change what the cleanup deletes? → A: No. The cleanup deletes the same expired entries as before. Only the update of the in-memory list changes: it removes the expired entries from the current list, under the same lock as the writes. Rationale: the defect is the lost update, not the delete rule.
- Q: Does the cleanup delete the expired files while it holds the lock, or only update the list under the lock? → A: It holds the lock for the whole remove step (list update and file deletes). Rationale: a write cannot then put back an entry whose file the cleanup deletes after the write; a write waits at most for the deletes of one cleanup (about 200 files every 30 minutes in production, less than 1 s), and the queue writes do not need a faster answer.
- Q (analyze I1): Which summary does the start sweep give to an entry that is not a queue entry or a sequence entry? → A: "'<name>' run ended when the service stopped.". Rationale: today only queue and sequence entries have the status `running`, but one rule for all types prevents an entry with no summary.
- Q: Does the service record what the start sweep did? → A: Yes. The service writes one information line to the service log with the number of closed entries, only when the number is more than 0. Rationale: this follows the current cleanup log line ("Execution log cleanup removed {Count} expired entries.").

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The newest log of a running queue is in the list while the queue runs (Priority: P1)

An operator (or the reporting repository) reads the execution logs of a running queue with `GET /api/execution-logs?objectId=<queue>`, and then the subtree of the newest log with `GET /api/execution-logs/{id}/subtree`. The list gives the log of the current run, and its subtree gives each run of the queue, with the step nodes. This is true also when the retention cleanup runs at the same time as the queue writes its log, and also after a service restart.

**Why this priority**: This is the defect of the issue. The reporting repository cannot read the first runs of a new task on two of four accounts, so a failure on these accounts is invisible.

**Independent Test**: Fill a log store with many expired entries. Start the retention cleanup, and while it deletes the expired files, add a new queue root entry and child entries. After the cleanup ends, read the list with the queue id, and read the subtree of the new root. The list gives the new root, and the subtree gives each child.

**Acceptance Scenarios**:

1. **Given** the retention cleanup deletes expired entries, **When** a queue writes a new root entry at the same time, **Then** after the cleanup the list `GET /api/execution-logs?objectId=<queue>` gives the new root entry.
2. **Given** the retention cleanup deletes expired entries, **When** a sequence of a queue run writes its entry (start or final state) at the same time, **Then** after the cleanup the subtree of the queue root gives this sequence with its latest state and its step nodes.
3. **Given** the service starts and the first cleanup loads all entries and deletes the expired entries, **When** the queues that resume on service start write their new root entries during this time, **Then** each new root entry is in the list, and the newest root of each queue is the root of its current run.
4. **Given** a queue run with more than 120 children, **When** the queue runs a new sequence, **Then** the subtree of the queue root gives the new child, with no limit on the number of children.
5. **Given** the cleanup removes an expired entry, **When** the cleanup ends, **Then** the list and the subtree do not give this entry (the delete rule does not change).

---

### User Story 2 - A run of an earlier service process does not stay "running" (Priority: P2)

An operator looks at the list of execution logs after a service restart. The queue root of the run that the restart ended must not say `running`, because no run continues in it. It must say that the run ended with the service stop. The same applies to a sequence entry that the restart cut off.

**Why this priority**: In the issue, the old root of each restarted queue still says `running`. The operator took it as the log of the current run and saw that it "stopped growing". A closed old root makes the root of the current run the only `running` root of the queue.

**Independent Test**: Write a queue root entry and a sequence entry with the status `running` and a timestamp before the process start time. Run the start sweep. Read both entries: each has the status `failure` and a summary that says that the run ended when the service stopped. An entry with the status `running` and a timestamp after the process start time does not change.

**Acceptance Scenarios**:

1. **Given** a stored queue root entry with the status `running` and a timestamp before the start of the current service process, **When** the service starts, **Then** the entry gets the status `failure` and the summary "Queue '<name>' run ended when the service stopped.", and its children do not change.
2. **Given** a stored sequence entry with the status `running` and a timestamp before the start of the current service process, **When** the service starts, **Then** the entry gets the status `failure` and the summary "Sequence '<name>' run ended when the service stopped.".
3. **Given** a queue resumes on service start and writes a new root entry with the status `running`, **When** the start sweep runs after or at the same time as this write, **Then** the new root entry keeps the status `running`.
4. **Given** an entry with a status other than `running`, **When** the start sweep runs, **Then** the entry does not change.

---

### User Story 3 - The rules of a queue log are documented (Priority: P3)

An operator who writes a client of the execution logs API reads in the API description how a queue log grows: each run of the queue is a child of the queue root, there is no limit on the number of children, a new segment starts after 24 hours, and a root of an earlier service process is closed at service start.

**Why this priority**: Acceptance criterion 3 of the issue asks that a limit is documented. There is no count limit, but the 24-hour rotation is a limit in time, and it is not in the API description now.

**Independent Test**: Read the OpenAPI document of the service. The description of `GET /api/execution-logs` and of `GET /api/execution-logs/{id}/subtree` gives the rules.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document of the service, **When** the operator reads the description of the two execution log endpoints, **Then** it says that a queue log has no limit on the number of children, that a queue log is closed after 24 hours and continues in a new segment (with `rotatedToExecutionId` and `rotatedFromExecutionId`), and that a `running` entry of an earlier service process is closed with the status `failure` at service start.

---

### Edge Cases

- The cleanup finds no expired entries: the in-memory list does not change, and a write at the same time is not lost.
- The cleanup cannot delete one expired file (for example, the file is locked): as now, the entry is removed from the in-memory list, and the next service start loads it again and the cleanup tries again.
- A write changes an entry that the cleanup removes at the same time (an expired entry that gets a final state): the final rule is the order of the two operations under the lock. If the write comes after the removal, the entry is in the list with its new state and the file is written again; the next cleanup removes it again when it is still expired.
- The in-memory list is not loaded yet (no read since the service start) when a write occurs: as now, the write goes to disk only, and the first load reads it.
- A rotation (24 hours) occurs: the closed segment and the new segment are both in the list, as now.
- The start sweep finds a `running` entry with no readable name: the summary uses the object id.
- The start sweep runs when the list of entries is very large (about 64,000 files): the sweep uses the same one load as the cleanup and writes only the `running` entries of an earlier process.
- A step-through run or a standalone sequence run of an earlier process: its `running` entry is closed in the same way.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The retention cleanup MUST NOT lose an entry that a write adds or changes while the cleanup runs. After the cleanup, the in-memory list MUST contain each entry that was written (added or changed) during the cleanup and is not expired, in its latest written state.
- **FR-002**: The cleanup MUST hold the same lock that the writes use for its whole remove step (the update of the in-memory list and the deletes of the expired files), and it MUST remove only the expired entries from the current list (not replace the list with an earlier copy).
- **FR-003**: The cleanup MUST delete the same expired entries as before (an entry with `RetentionExpiresUtc` at or before the cleanup time), and the list and the subtree MUST NOT give a removed entry.
- **FR-004**: `GET /api/execution-logs` MUST give each root entry that is stored and not expired, also a root entry that a queue wrote during a cleanup or during the first load after a service start.
- **FR-005**: `GET /api/execution-logs/{id}/subtree` MUST give each stored child of the root, with no limit on the number of children.
- **FR-006**: At service start, the service MUST close each stored entry that has the status `running` and a timestamp before the start time of the current service process: the status becomes `failure`, the summary becomes "Queue '<name>' run ended when the service stopped." for a queue entry and "Sequence '<name>' run ended when the service stopped." for a sequence entry, and "'<name>' run ended when the service stopped." for an entry of another type (the name is the display name, or the object id when the name is empty; the summary has at most 240 characters). The hierarchy, the timestamp, the details, the step outcomes, the rotation links, and the retention time of the entry MUST NOT change.
- **FR-006a**: When the start sweep closes 1 or more entries, the service MUST write one information line to the service log with the number of closed entries.
- **FR-007**: The start sweep MUST NOT change an entry with a timestamp at or after the start time of the current service process, and MUST NOT change an entry with a status other than `running`.
- **FR-008**: The API description (OpenAPI) of `GET /api/execution-logs` and `GET /api/execution-logs/{id}/subtree` MUST state the rules of a queue log: no limit on the number of children, the 24-hour rotation with its two link fields, and the start sweep of FR-006.
- **FR-009**: The fix MUST NOT change the queue scheduling, the `SelfReschedule` bookings, the queue monitor API, the rotation rule, or the response shape of the execution log endpoints.

### Key Entities

- **Execution log entry**: One stored record of a queue run, a sequence run, a command run, or a step. Attributes used here: id, timestamp, execution type, final status, object reference (type, id, display name), hierarchy (root id, parent id, depth, sequence index), summary, retention expiry time, rotation links.
- **In-memory entry list**: The copy of all stored entries that the list and subtree endpoints read. It is loaded from disk one time after the service start, and each write updates it.
- **Retention cleanup**: The periodic job (at service start, then every `CleanupIntervalMinutes`) that deletes the expired entries.
- **Start sweep**: The new one-time job at service start that closes the `running` entries of an earlier service process.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test where a queue writes a root entry and 10 child entries while the cleanup deletes 1,000 expired entries, 100% of the 11 entries are readable through the list and the subtree after the cleanup. The test holds the cleanup in its remove step while the writes start, so one run proves the rule; a second test runs 20 times in one loop with no hook and 200 expired entries in each run, and 100% of these runs give all 11 entries.
- **SC-002**: In a test where a sequence entry gets its final state while the cleanup runs, the subtree gives the final state (not `running`) in 100% of runs.
- **SC-003**: A queue root with 500 children gives 500 children in its subtree.
- **SC-004**: After a service start, 0 entries of an earlier process keep the status `running`, and 100% of the entries of the current process keep their status.
- **SC-005**: All current tests of the execution log repository, the retention cleanup, the rotation, and the execution log endpoints pass with no change to their expected results.
- **SC-006**: The OpenAPI document gives the queue log rules of FR-008.

## Assumptions

- The production data (read only) is the evidence for the cause: the lost roots are on disk, and the stop wrote them back to the in-memory list. A service restart before the fix also makes them readable.
- The retention policy of production is the default (enabled, 7 days, cleanup every 30 minutes), so the first cleanup after each start deletes the entries that expired while the service was down or since the last cleanup.
- The service process start time is a correct boundary for the start sweep: one service process at a time uses one data directory.
- Out of scope: a new limit on the number of children, a change of the 24-hour rotation, a change of the queue engine, a database instead of files, and changes to the web UI.
