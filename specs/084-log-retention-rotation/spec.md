# Feature Specification: Execution Log Retention Default & Long-Run Rotation

**Feature Branch**: `084-log-retention-rotation`
**Created**: 2026-09-14
**Status**: Implemented
**Input**: User description: "I want the execution logs to get cleaner. First I want to change the default of hold retention to 1 week. Second, if a queue is running for more than 24h, it's logs has to be rotated. The last entry in the previous should indicate, that log rotation took place and the continued log is available, the one being continued should indicate in the beginning, that it's the continuation of a run. The cut should happen only in between the sequences."

## Clarifications

### Session 2026-09-14

- Q: The rotation markers (FR-008/FR-009) must let an operator "find" the linked segment — should that link be a stable, structured identifier field (queryable/machine-usable), or is descriptive text in the entry's summary sufficient? → A: Structured identifier field. Rationale: the existing execution log model already carries structured ID references between related entries (root/parent execution IDs), so a stable ID field is consistent with that pattern and is what makes SC-003 ("navigate...with no ambiguity") actually verifiable rather than relying on prose matching.
- Q: Within a single queue run, are sequence firings ever concurrent with each other (which would make "the boundary between two firings" ambiguous), or does one queue run always execute its firings strictly one at a time? → A: Strictly one at a time — verified against the queue execution engine, which runs one sequence firing to completion before considering the next within a given queue run (separate queues running concurrently each have their own independent run and 24-hour clock, and never share a run segment or rotation decision). Rationale: this means the rotation check-and-cut needs no additional concurrency handling — it can simply run inline between one firing's completion and the next firing's start, reusing the existing sequential control flow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shorter default retention keeps the log list clean (Priority: P1)

As the operator of GameBot, I want newly-created installations (and any deployment that hasn't explicitly overridden the retention setting) to keep execution logs for one week by default, instead of the current two months, so that the execution log list stays small and relevant without me having to configure anything.

**Why this priority**: This is a one-line default change that immediately reduces log clutter for every user who hasn't customized retention, with no other feature dependency. It delivers value on its own.

**Independent Test**: Start GameBot fresh (no saved retention policy file). Query the current retention policy. Verify the retention period is 7 days. Confirm an operator can still override it to a longer or shorter value exactly as before.

**Acceptance Scenarios**:

1. **Given** a fresh installation with no previously saved retention policy, **When** the retention policy is read, **Then** the effective retention period is 7 days.
2. **Given** an existing installation that already has a saved retention policy (regardless of its value), **When** GameBot starts up with this change applied, **Then** the previously saved value is preserved unchanged (the new default only applies where no explicit value was ever saved).
3. **Given** the 7-day default is in effect, **When** an operator explicitly sets a different retention period, **Then** that explicit value is honored and persisted as before.

---

### User Story 2 - Long-running queues get their execution history rotated (Priority: P1)

As the operator of GameBot, when a queue runs continuously for more than 24 hours (looping through many scheduled sequence firings), I want its execution history split into successive, clearly-linked runs instead of growing as one unbounded run, so that the execution log stays navigable and no single run's history grows without bound.

**Why this priority**: This is the core of "cleaner logs" for the always-on queues that are GameBot's primary use case (daily automation queues that run for days or weeks). Without it, a single queue run's history keeps growing indefinitely as long as the queue is active.

**Independent Test**: Start a queue and let it (or a test double standing in for elapsed time) execute sequence firings spanning more than 24 hours of run time. Inspect the execution log. Verify the run is split into at least two linked segments at a boundary that falls between two sequence firings, never in the middle of one.

**Acceptance Scenarios**:

1. **Given** a queue has been running continuously for more than 24 hours, **When** the next sequence firing is about to start, **Then** that firing begins a new run segment instead of continuing to attach to the run segment that started more than 24 hours ago.
2. **Given** a run segment is being rotated, **When** the rotation happens, **Then** it only ever happens right before a sequence firing starts — never while a sequence is mid-execution — so no single sequence's execution is ever split across two segments.
3. **Given** a run segment has just been closed out due to rotation, **When** an operator views that segment's final entry, **Then** it clearly states that log rotation occurred and that the continuation is available, and provides enough information to find the continuing segment.
4. **Given** a new run segment has just started because of rotation, **When** an operator views that segment's first entry, **Then** it clearly states that this segment is a continuation of a prior run, and provides enough information to find the segment it continues from.
5. **Given** a queue runs for multiple multiples of 24 hours (e.g. 3 days) without stopping, **When** the execution log is inspected, **Then** it contains a chain of segments (original run plus one rotation per additional 24-hour period elapsed), each linked to the next/previous as described above.
6. **Given** a queue's run ends (queue stopped) before 24 hours have elapsed since the run — or since the last rotation — **When** the execution log is inspected, **Then** no rotation has occurred and the run appears as a single, uninterrupted segment, exactly as it does today.
7. **Given** rotation has occurred, **When** the retention policy from User Story 1 later expires the closed-out (earlier) segment, **Then** that segment is deleted independently of the still-active continuation segment, without breaking the continuation segment's own display or retention handling.

### Edge Cases

- A queue with only a handful of sequence firings per day (e.g. one every few hours) that happens to run past the 24-hour mark: rotation still only happens at the next sequence-firing boundary after the 24-hour threshold is crossed, even if that boundary is hours after the threshold was technically reached. The rotation check runs once per firing, not on a background timer.
- A queue that is stopped and restarted: each start begins a fresh run; the 24-hour clock and rotation chain resets and is not resumed from before the stop.
- A queue with zero sequence firings for over 24 hours (e.g., all its schedules are far apart or paused): since rotation only happens right before a firing starts, the run segment simply keeps accumulating no new entries until the next firing occurs, at which point the overdue rotation happens then.
- Retention cleanup running concurrently with rotation: cleanup must never delete a segment while it is in the middle of being closed out/rotated, and must not treat a freshly-rotated new segment as orphaned.
- Viewing the execution log for a run that has been rotated several times: an operator should be able to follow the chain in either direction (from an old segment forward to the latest, and from the latest segment back to the original) using the links described above.
- Two separate queues each running past 24 hours at the same time: each rotates independently on its own clock and produces its own segment chain; they never affect each other's rotation decisions (per FR-009a).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST default the execution log retention period to 7 days for any deployment that has not explicitly saved a retention policy value.
- **FR-002**: The system MUST continue to honor an explicitly saved retention policy value exactly as before; the new default MUST NOT overwrite or migrate a previously saved value.
- **FR-003**: The system MUST continue to allow operators to view and change the retention period, with the same validation rules as today (only the default value changes).
- **FR-004**: The system MUST track, for each actively running queue, how long the current run segment has been open.
- **FR-005**: The system MUST check, immediately before each sequence firing within a queue run, whether the current run segment has been open for more than 24 hours.
- **FR-006**: When that check finds the current run segment has exceeded 24 hours, the system MUST close out the current run segment and start a new run segment before the sequence firing proceeds, such that the firing about to run belongs entirely to the new segment.
- **FR-007**: The system MUST NOT rotate a run segment while a sequence firing is in progress; a rotation decision MUST only be evaluated at the boundary between two sequence firings.
- **FR-008**: When a run segment is closed out due to rotation, the system MUST record, as that segment's final entry, an indication that rotation occurred and that a continuation exists, including a structured, stable identifier field (not only descriptive text) that identifies the continuing segment.
- **FR-009**: When a new run segment is started as a result of rotation, the system MUST record, as that segment's first entry, an indication that it is a continuation of a prior run, including a structured, stable identifier field (not only descriptive text) that identifies the segment it continues from.
- **FR-009a**: Each queue run's own sequence firings execute strictly one at a time, so the rotation decision (checking and, if due, cutting over to a new segment) MUST be made once, inline, between one firing's completion and the next firing's start; separate queue runs MUST each maintain their own independent 24-hour clock and rotation chain, never sharing a run segment.
- **FR-010**: A queue run that ends (queue stopped) without ever exceeding 24 hours of continuous operation since its start (or since its last rotation) MUST NOT be rotated and MUST appear as a single, uninterrupted run segment.
- **FR-011**: A queue run that remains active for multiple 24-hour periods MUST be rotated once per elapsed 24-hour period, producing a chain of segments each linked to its neighbor(s).
- **FR-012**: Stopping and restarting a queue MUST begin a new run from a fresh 24-hour window; rotation state MUST NOT be carried over from a previous run.
- **FR-013**: Retention expiry (per FR-001-FR-003) MUST apply to each run segment independently, so an older, closed-out segment can be deleted on schedule without affecting a newer, still-active continuation segment.
- **FR-014**: Existing execution log entries recorded before this feature ships MUST continue to display and query correctly (no retroactive rotation of historical runs).

### Key Entities

- **Run Segment**: A bounded stretch of a queue's execution history, spanning one or more sequence firings, with a defined start and (once closed) end. A queue run that never crosses 24 hours is exactly one run segment. A queue run active longer than 24 hours is represented as a chain of run segments, each aware of the segment before and/or after it.
- **Rotation Link**: The pair of markers connecting two consecutive run segments — the closing marker on the earlier segment ("rotated, continuation available, see X") and the opening marker on the later segment ("continuation of prior run, see X"), where "X" in both cases is a structured, stable identifier field, not only human-readable text.
- **Retention Policy**: The existing configuration governing how long execution log data is kept before deletion; unchanged in shape, only its default value changes (60 days → 7 days).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh deployment with no saved retention configuration, the effective execution log retention period is 7 days, verifiable without any manual configuration step.
- **SC-002**: For any queue run that never exceeds 24 continuous hours, the execution log shows exactly one uninterrupted run segment for that run, with zero rotation markers — matching current behavior exactly.
- **SC-003**: For a queue run that continues past 24 hours, an operator can, within a couple of clicks/lookups, navigate from the run's starting segment to its latest segment (and back) purely by following the rotation markers on the segment boundaries, with no ambiguity about which segment comes next.
- **SC-004**: No sequence firing's execution history is ever split across two run segments — 100% of rotations occur strictly between sequence firings.
- **SC-005**: A long-running queue (multiple weeks of continuous operation) no longer produces a single run segment whose size grows without bound; instead the segment count grows roughly in proportion to elapsed days (about one new segment per 24 hours of continuous run time).
- **SC-006**: Retention cleanup removes eligible old run segments on the same schedule as before, without operator-visible errors or gaps, even when some of those segments are rotation-closed continuations of a still-active run.

## Assumptions

- "Hold retention" in the feature request refers to the existing execution log retention policy (`RetentionDays`), which currently defaults to 60 days; this feature changes only that default to 7 days. No other retention mechanism exists in the system today.
- "A queue's logs" refers to the queue's execution log history (the chain of log entries produced by its sequence firings while it is running), not a separate physical log file; rotation is expressed as a logical split in that history rather than a new file format.
- The 24-hour window is measured as continuous wall-clock time since the run (or its last rotation) started, evaluated lazily at the next sequence-firing boundary rather than by a background timer — this keeps the "cut only between sequences" constraint trivially satisfied and avoids adding a new always-on timer.
- If a queue happens to have no sequence firings for a long stretch (e.g., sparse schedule) while past the 24-hour mark, rotation is simply deferred until the next firing occurs; this is acceptable because the goal is bounding segment growth relative to activity, not enforcing a hard wall-clock cutoff.
- Stopping and restarting a queue is already a natural, existing boundary for its execution history, so rotation state does not need to persist across a stop/start cycle.
- The 24-hour threshold and the 7-day default are both simple fixed numbers for this feature (not user-configurable); the request does not ask for either to be adjustable, and adding configuration for them is out of scope unless a real need surfaces later.

## Out of Scope

- Making the 24-hour rotation threshold configurable per queue or globally.
- Changing how retention cleanup deletes data, beyond applying it per run segment as it already does per log entry.
- Any UI redesign of the execution log viewer beyond surfacing the rotation markers/links described above.
- Retroactively rotating or restructuring execution log history recorded before this feature ships.
