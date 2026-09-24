# Feature Specification: Per-Sequence Run Statistics per Queue

**Feature Branch**: `105-sequence-run-statistics`  
**Created**: 2026-09-24  
**Status**: Draft  
**Issue**: [#224](https://github.com/bbqf/GameBotAI/issues/224) (Closes #224). Source: row FR-008 of the external request list (`docs/api-feature-requests.md` in the repository that holds the PNS automation). Note: that row is not FR-008 of this spec.  
**Input**: User description: "Per-sequence run statistics per queue, readable by the API and by a sequence condition."

## Background

A sequence cannot find out how its earlier runs ended. On 2026-09-24, sequence validation accepted only these condition types: `imageVisible`, `commandOutcome`, `all`, `any`, `none`. All other type names (for example `lastRun`, `schedule`, `time`, `counter`) got the error "Read unrecognized type discriminator". A `commandOutcome` condition reads only steps of the current run.

The queue does not keep this data either. A queue restart clears each time slot that `reschedule-self` added, and the template schedule applies again. The `health` block of a queue holds queue-level counters only (`cyclesCompleted`, `lastCycleStatus`). It holds no per-sequence data.

The main use case of this feature: a daily task must run one time in each "training day" (11:00 to 11:00 on the next day). After a queue restart, the task must run at once if the current training day has no successful run. It must not run if the day has a successful run.

At this time, an external script does this work. The script does not cover a start by hand in the web UI. It also does not cover a queue that `resumeOnServiceStart` starts again. In those cases, the task skips the day or runs two times.

## Clarifications

### Session 2026-09-24

Autonomous pipeline run: the answers below were chosen without a user, from the spec and the codebase.

- Q: Are the statistics kept per sequence ID, or per queue entry (one sequence can have two entries in a template)? → A: Per sequence ID in the queue. Two entries of the same sequence share one statistics entry. Rationale: `sequence: "self"` names a sequence, not an entry. "Did this task succeed today" is a question about the task.
- Q: Which read returns the statistics: the queue read, the monitor, or both? → A: The queue read (`GET /api/queues/{id}`) only. Rationale: the issue accepts either. The queue read works for a stopped queue, which the restart case needs. One place keeps the contract small.
- Q: How many recent run records are kept for each (queue, sequence) pair? → A: The 100 most recent. Rationale: a daily or hourly task needs only a few days of history. The limit keeps the stored data small.
- Q: Does a run that ends because a step broke the run (a Break step) count as success? → A: Yes. Rationale: a Break is a normal, author-planned end. The queue already treats it as a non-failure.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the run statistics of each sequence in a queue (Priority: P1)

An operator or an external tool reads a queue. For each sequence that the queue ran, the read gives these values:

- the start time and the end time of the last run,
- the status of the last run (`success`, `failure` or `cancelled`),
- the time of the last successful run,
- the counts of successful, failed and cancelled runs.

The values stay correct after a queue restart and after a service restart.

**Why this priority**: The statistics are the base for all other stories. Also, the external workaround script can read them in place of the execution logs. This is faster and less fragile.

**Independent Test**: Start a queue that runs a sequence that succeeds and a sequence that fails. Read the queue. Make sure that each sequence shows the correct last status, times and counts. Restart the queue and then the service. Read the queue again and make sure that the values did not change.

**Acceptance Scenarios**:

1. **Given**: a queue ran sequence A two times with success and one time with failure. **When**: the operator reads the queue. **Then**: the statistics of A show these values:
   - 2 successes, 1 failure and 0 cancelled runs.
   - The last status `failure`.
   - The start time and the end time of the failed run.
   - The end time of the second successful run, as the last success time.
2. **Given**: a queue has statistics. **When**: the operator stops and starts the queue. **Then**: the statistics are the same as before the restart.
3. **Given**: a queue has statistics. **When**: the service stops and starts again. **Then**: the statistics are the same as before the restart.
4. **Given**: the queue did not run a sequence yet. **When**: the operator reads the queue. **Then**: the statistics contain no entry for that sequence, and the read does not fail.
5. **Given**: the queue cancels a run (a stop by hand, a failure-policy stop, or the sequence time limit (watchdog)). **When**: the operator reads the queue. **Then**: the run counts as `cancelled`, not as `failure`.

---

### User Story 2 - Guard a sequence with a condition on its own earlier runs (Priority: P1)

A sequence author adds a condition of the new type `lastRun` to a step. The condition is true when the named sequence had a run with the given status in the same queue in a given time window. The named sequence is `self` or a given sequence ID. The window has one of two forms:

- "since the last occurrence of a local time of day" (`since: "11:00"`),
- "within a duration before now" (`within: "24:00:00"`).

The author uses the condition to stop the run early when the task is already done for the day.

**Why this priority**: This is the capability that the main use case needs. Without it, a restart runs a daily task two times or skips it.

**Independent Test**: Make a sequence whose first step breaks the run when `{ "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00" }` is true. Run it in a queue two times after 11:00. Make sure that the first run does the work and the second run stops at the guard.

**Acceptance Scenarios**:

1. **Given**: the local time is 14:00, and the sequence had a successful run in this queue at 12:00 on the same day. **When**: the runner evaluates a `lastRun` condition with `sequence: "self"`, `status: "success"` and `since: "11:00"`. **Then**: the condition is true.
2. **Given**: the local time is 10:00, and the last successful run was at 12:00 on the previous day. **When**: the runner evaluates the same condition. **Then**: the condition is true. The window starts at 11:00 on the previous day.
3. **Given**: the local time is 11:30, and the last successful run was at 10:30 on the same day. **When**: the runner evaluates the same condition. **Then**: the condition is false.
4. **Given**: the sequence had only failed runs in the window. **When**: the runner evaluates a `lastRun` condition with `status: "success"`. **Then**: the condition is false.
5. **Given**: a condition with `within: "24:00:00"` and a successful run 23 hours ago. **When**: the runner evaluates the condition. **Then**: the condition is true. With a successful run 25 hours ago only, the condition is false.
6. **Given**: a queue restart after a successful run in the current window. **When**: the sequence runs again after the restart. **Then**: the condition is true.
7. **Given**: a `lastRun` condition with `negate: true`, or in a `none` composite. **When**: the runner evaluates it. **Then**: the result is the inverse, the same as for other condition types.

---

### User Story 3 - Author and validate the new condition like the other types (Priority: P2)

A sequence author saves a sequence that contains a `lastRun` condition, with or without `dryRun`. The service accepts a correct condition. It rejects an incorrect condition with a clear 400 error. The OpenAPI document describes the new type and its fields in the same way as the other condition types.

**Why this priority**: Authors must be able to save and check the condition before they can use it. It is P2 because it is a direct part of stories 1 and 2, not a separate value.

**Independent Test**: Send correct and incorrect `lastRun` conditions to sequence create, update, PATCH and `dryRun`. Make sure that the service accepts the correct ones. Make sure that the incorrect ones get a 400 with a message that names the problem. Read the OpenAPI document and find the `lastRun` type.

**Acceptance Scenarios**:

1. **Given**: a sequence with a correct `lastRun` condition. **When**: the author saves it with `dryRun: true`. **Then**: the service accepts it.
2. **Given**: a `lastRun` condition with both `since` and `within`, or with neither. **When**: the author saves it. **Then**: the service rejects it with a 400 that names the problem.
3. **Given**: a `lastRun` condition with an unknown status, a malformed `since` time, or a zero or negative `within` duration. **When**: the author saves it. **Then**: the service rejects it with a 400 that names the field.
4. **Given**: a `lastRun` condition in an `all`, `any` or `none` composite. **When**: the author saves it. **Then**: the service accepts it. The composite limits (children, depth) apply as before.
5. **Given**: the OpenAPI document. **When**: a reader looks for the condition types. **Then**: the document lists `lastRun` with each field, its permitted values and its description.

---

### Edge Cases

- **Run outside a queue**: A sequence that runs ad hoc (not from a queue) has no queue. A `lastRun` condition in that run evaluates to false, because the statistics contain no applicable run. The ad-hoc run does not add to the statistics of a queue.
- **The current run**: The run that evaluates the condition is not complete, so it is not in the statistics. `sequence: "self"` reads only the earlier, completed runs.
- **A run that stops early at the guard**: A run that stops at a `lastRun` guard with a normal (success) result counts as a success. This does not change the result of the guard, because a successful run already exists in the window.
- **`since` at the current minute**: When the local time is exactly the `since` time, the window starts now. Earlier runs are outside the window.
- **Daylight-saving change**: The `since` time is service-local wall-clock time. On a day with a clock change, the window starts at the most recent real occurrence of that wall-clock time.
- **Sequence ID that is not in the queue**: A `lastRun` condition that names a sequence which never ran in the queue evaluates to false.
- **Sequence removed from the queue template**: Its statistics stay in the queue and are still readable, until the operator deletes the queue.
- **Queue deleted**: Its statistics are deleted with it.
- **Guard sequences** (for example `EveryStep` or `BeforeEachRun` establishers): the queue records each sequence run that it executes. This also applies to guard sequences. Each sequence has its own entry.
- **Nested sequence run**: At this time, a nested run is not possible. No step type runs another sequence. Only the queue and the ad-hoc execute endpoint start a sequence run. This rule protects a possible later nested-run feature: a nested run pushes its own run context, so `self` names the nested sequence, not the outer sequence. The queue does not record the nested run. The queue records only the outer run that it started.
- **Service stops during a run**: The interrupted run has no end, so the queue does not record it. The statistics keep the values of the last completed run.
- **Damaged or absent statistics data at start**: The queue starts with empty statistics for that queue and logs a warning. The queue start does not fail.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST record the result of each completed run, for each queue and each sequence that the queue runs. The result is the start time, the end time and the status (`success`, `failure` or `cancelled`).
- **FR-002**: The system MUST keep these values for each queue and sequence:
  - the start time and the end time of the last run,
  - the status of the last run,
  - the end time of the last successful run,
  - the total counts of successful, failed and cancelled runs.
- **FR-003**: The system MUST keep a history of the recent run results of each sequence in each queue, with a limit (see Assumptions). A condition uses the history to find a run with a given status in a time window.
- **FR-004**: The system MUST persist the statistics. The statistics MUST stay the same after a queue stop and start, and after a service restart.
- **FR-005**: The queue read (`GET /api/queues/{id}`) MUST return the statistics of each sequence in the queue. This applies to a queue in the `Running` status and to a stopped queue. The statistics are keyed by sequence ID: two template entries of the same sequence share one statistics entry. The monitor read does not change.
- **FR-006**: A run that the queue cancels MUST count as `cancelled`. The queue cancels a run on a stop by hand, on a failure-policy stop, or at the sequence time limit (watchdog). A run that ends with an error or a failed step MUST count as `failure`. All other completed runs MUST count as `success`. This also applies to a run that a Break step ends. When the queue cancels a run and the run also has a failed step, the run MUST count as `cancelled`.
- **FR-007**: The system MUST accept a new sequence condition type `lastRun` with these fields:
  - `sequence`: `self`, or a sequence ID.
  - `status`: `success`, `failure` or `cancelled`, compared without case.
  - Exactly one of `since` (a local time of day, `HH:mm`) or `within` (a positive duration, `hh:mm:ss`, or `d.hh:mm:ss` for more than one day). The maximum `within` is 366 days (`366.00:00:00`).
- **FR-008**: A `lastRun` condition MUST be true when these two statements are true:
  - The named sequence has at least one completed run in the current queue with the given status.
  - The end time of that run is in the window.

  For `since`, the window starts at the most recent occurrence of that service-local time of day, at or before now. For `within`, the window starts at now minus the duration. The window ends now.
- **FR-009**: A `lastRun` condition MUST support `negate`, and MUST work as a child of `all`, `any` and `none`, the same as the other condition types.
- **FR-010**: A `lastRun` condition MUST be permitted in each place that permits a step condition at this time. These places are step conditions, If conditions, loop conditions and Break conditions (also a Break condition in a loop body or an If branch).
- **FR-011**: The sequence create, update, PATCH and `dryRun` validation MUST accept a correct `lastRun` condition. It MUST reject an incorrect condition with a 400 error that names the problem. These are the problems:
  - an unknown `status`,
  - an absent or empty `sequence`,
  - both or neither of `since` and `within`,
  - a malformed `since`,
  - a zero, negative or malformed `within`, or a `within` of more than 366 days.
- **FR-012**: A `lastRun` condition evaluated in a run that has no queue MUST evaluate to false and MUST NOT fail the run.
- **FR-013**: The OpenAPI document MUST describe the `lastRun` condition type and each of its fields. It MUST also describe the new statistics fields on the queue read. The descriptions MUST have the same form as for the current condition types and queue fields.
- **FR-014**: The feature MUST NOT change how `reschedule-self` time slots or template schedules apply when a queue starts again.
- **FR-015**: The system MUST NOT add other new condition types (variables, counters, time of day) as part of this feature.

### Key Entities

- **Sequence run statistics**: One entry for each (queue, sequence) pair. It holds the last run start and end time, the last run status and the last success time. It also holds the counts of successful, failed and cancelled runs, and a limited list of recent run records.
- **Run record**: One completed run of a sequence in a queue: start time, end time, status.
- **`lastRun` condition**: A step condition that names a sequence, a status and a time window, and reads the run statistics of the current queue.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a queue restart or a service restart, 100% of the per-sequence statistics stay visible. The values are the same as before the restart.
- **SC-002**: A daily task with a `lastRun` guard with `since` runs its work exactly one time in each window. This is true in 100% of the test cases that restart the queue before and after the successful run.
- **SC-003**: Each incorrect `lastRun` condition in the test set gets a 400 error that names the problem. No incorrect condition gets a 500 error.
- **SC-004**: An operator can find the last status and last success time of each sequence in a queue with one read of the queue. No search in the execution logs is necessary.
- **SC-005**: The external restart script that reads the execution logs is no longer necessary for the main use case.

## Assumptions

- "Local time" is the service-local time zone, the same time zone that queue Timer schedules use.
- The history of run records for each (queue, sequence) pair has a limit of the 100 most recent records. A `within` or `since` window that is longer than the history can find only the kept records. The total counts have no limit.
- The counts are totals since the first recorded run. The API shows totals only. The queue read does not return a count "over a window". The `lastRun` condition gives the window check.
- The statistics start empty for queues that exist before this feature. The feature does not import old execution logs.
- A run is "completed" when the queue gets its final result. A run in progress is not in the statistics.
- At save time, the service does not check a sequence ID in `sequence` against the sequence store. The reason: a queue can run sequences that an author creates later. A wrong ID only makes the condition false.
- No web UI change is part of this feature.
