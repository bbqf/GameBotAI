# Feature Specification: Relative-Time Sequence Scheduling

**Feature Branch**: `059-relative-schedule-time`  
**Created**: 2026-06-17  
**Status**: Draft  
**Input**: User description: "I need the next feature: when scheduling the sequences in the queue I want to be able to schedule them as relative time: e.g. in 10 min 0 sec from now. This should be possible via API and UI."

## Context

This feature extends the existing "timer" schedule type for queue sequences (delivered in feature 053, Queue Sequence Scheduling). Today, a timer-scheduled sequence is configured with an absolute wall-clock **time-of-day** (e.g., 15:30). This feature adds the ability to schedule a sequence using a **relative time offset** — "in 10 minutes 0 seconds from now" — instead of an absolute clock time, available through both the API and the UI.

Per clarification, a relative offset has two anchor behaviors depending on where it is set:

- **Saved in a queue template**: the offset is anchored to **queue run start** and is recomputed on every run, so a "+10 min" entry fires roughly 10 minutes into each run and is repeatable across runs.
- **Set via a live API/UI call against a running queue**: the offset is anchored to **the moment of the call** ("from now") and resolves to a single absolute target instant for the current run only. It is not persisted to the template.

In both anchors the sequence fires **once**. Re-running on a later interval is achieved by issuing another live call, not by an automatic recurrence.

## Clarifications

### Session 2026-06-17

- Q: Live relative scheduling — which sequences can be targeted? → A: Any sequence in the sequence library, executed as an additional firing in the current run (it need not already be an entry in the running queue's template).
- Q: Do relative-offset (template) and live firings count toward the queue run's step-completion total? → A: Yes — each such firing is counted as a completed step in the run's step-completion total/metrics. (Run termination remains governed by the once-per-run steps; this counting affects reported totals, not the stop condition.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Schedule a template timer entry with a relative offset from run start (Priority: P1)

An operator editing a queue template marks a sequence entry as a timer and chooses to express its schedule as a relative offset (e.g., 10 minutes 0 seconds) instead of an absolute time-of-day. When a queue using that template runs, the system measures the offset from the moment the run starts. Once that much time has elapsed, the sequence fires once at the next iteration boundary. The same template, run again later, again fires the sequence ~10 minutes into the new run.

**Why this priority**: This is the core capability the operator asked for — expressing a sequence's schedule relative to run start — and it is repeatable, persisted in the template, and reusable across runs. It is the foundation the live-scheduling stories build on.

**Independent Test**: Create a template with one timer entry configured as a relative offset of a small duration (e.g., 30 seconds), start a queue whose iteration boundaries occur every few seconds, and confirm the sequence does not fire before the offset elapses, fires once at the first iteration boundary after it elapses, and does not fire again during that run. Restart the queue and confirm it fires again ~30 seconds into the new run.

**Acceptance Scenarios**:

1. **Given** a template timer entry with a relative offset of 10 min 0 sec, **When** a queue run starts and 10 minutes have elapsed since the run began, **Then** the sequence fires once at the next iteration boundary.
2. **Given** the same entry, **When** fewer than 10 minutes have elapsed since run start, **Then** the sequence does not fire and is re-evaluated at each subsequent iteration boundary.
3. **Given** the same entry, **When** the offset has elapsed and the sequence has already fired once during the run, **Then** it does not fire again for the remainder of that run.
4. **Given** the same template, **When** a new run is started later, **Then** the offset is measured fresh from the new run's start and the sequence fires again ~10 minutes into the new run.
5. **Given** a relative offset of 0, **When** the run starts, **Then** the sequence fires at the first iteration boundary of the run.

---

### User Story 2 - Live-schedule a sequence relative to "now" via the API (Priority: P1)

A developer or automation script issues a live API call against a running queue to schedule a specific sequence to run "in 10 min 0 sec from now". The offset is measured from the moment the call is received, resolves to a single target instant, and the sequence fires once at the next iteration boundary after that instant passes. The schedule is not written into the template and applies only to the current run.

**Why this priority**: This is the explicit "via API" requirement and the most direct realization of "in 10 min 0 sec from now" — the operator reacts to live conditions during a run without editing and reloading the template.

**Independent Test**: With a queue running, POST a live relative-schedule request for a known sequence with a small offset (e.g., 20 seconds); confirm the sequence fires once ~20 seconds later at an iteration boundary, does not fire again, and that the template on disk is unchanged.

**Acceptance Scenarios**:

1. **Given** a running queue, **When** a live API call schedules sequence S with a relative offset of 10 min 0 sec, **Then** S fires once at the first iteration boundary at or after (call time + 10 min) and does not fire again.
2. **Given** the same call, **When** the queue's template is later inspected, **Then** it contains no record of the live schedule (the schedule was ephemeral to the run).
3. **Given** a sequence that has already fired from a live schedule, **When** the operator issues another live call for the same sequence with a new offset, **Then** the sequence is scheduled again and fires once more when the new offset elapses.
4. **Given** a live schedule request with an offset of 0, **When** it is accepted, **Then** the sequence fires at the next iteration boundary.
5. **Given** a live schedule request submitted when no queue run is active, **When** the request is processed, **Then** it is rejected with a clear error explaining that a running queue is required.

---

### User Story 3 - Live-schedule a sequence relative to "now" via the UI (Priority: P2)

While watching a queue run, an operator selects a sequence and schedules it to run after a relative offset (entered as minutes and seconds) directly from the UI. The UI confirms the schedule, shows that the sequence is pending, and the sequence fires once when the offset elapses — equivalent to the live API call but driven from the running-queue view.

**Why this priority**: This is the explicit "via UI" requirement for live scheduling. It depends on the live-scheduling behavior from US2 and makes the capability accessible to non-developer operators.

**Independent Test**: With a queue running in the UI, use the relative-schedule control to schedule a sequence for a small offset, observe the pending indication, and confirm the sequence fires once when the offset elapses.

**Acceptance Scenarios**:

1. **Given** a running queue shown in the UI, **When** the operator schedules sequence S with a relative offset of 10 min 0 sec, **Then** the UI confirms the schedule and S fires once when the offset elapses.
2. **Given** an entered offset, **When** the operator submits a malformed or negative value, **Then** the UI prevents submission and shows a validation message before any request is sent.
3. **Given** a scheduled-but-not-yet-fired sequence, **When** the operator views the running queue, **Then** the UI indicates that the sequence is pending and when it is expected to fire.

---

### User Story 4 - Configure relative offsets through the template editor UI (Priority: P2)

An operator configuring a queue template chooses, per timer entry, whether the timer is expressed as an absolute time-of-day (existing behavior) or as a relative offset from run start (new). For the relative option, the operator enters minutes and seconds. The choice and value are saved with the template and shown when the template is reopened.

**Why this priority**: Pairs the template-side relative offset (US1) with an authoring surface in the UI. Without it, relative offsets in templates could only be set through the API.

**Independent Test**: Open the template editor, switch a timer entry to the relative-offset mode, enter a duration, save, reopen the template, and confirm the mode and value are preserved.

**Acceptance Scenarios**:

1. **Given** a timer entry in the template editor, **When** the operator selects the relative-offset mode and enters 10 min 0 sec, **Then** saving and reopening the template shows the relative-offset mode with the same value.
2. **Given** a timer entry, **When** the operator switches between time-of-day and relative-offset modes, **Then** the editor shows the input appropriate to the selected mode and the unselected mode's value is not used at runtime.
3. **Given** an entry configured as a relative offset, **When** the operator views the template entry list, **Then** the entry is visibly identified as a relative-offset timer (so it is distinguishable from a time-of-day timer at a glance).

---

### Edge Cases

- **Offset elapses mid-step**: Consistent with timer behavior, relative timers are evaluated only at iteration boundaries. An offset that elapses while a step is running fires at the next iteration boundary, not mid-step.
- **Offset of zero**: Fires at the first iteration boundary at or after the anchor (run start for template entries, call time for live calls).
- **Negative or non-numeric offset**: Rejected with a clear validation error at both the API and the UI; nothing is scheduled.
- **Very large offset that never elapses during the run**: If the offset never elapses before a non-cyclic run completes (or before the operator stops the run), the sequence never fires — consistent with the existing time-of-day timer that never becomes due.
- **Live schedule when no run is active**: Rejected with a clear error; live relative scheduling targets the current run only.
- **Live schedule for a sequence that does not exist in the library**: Rejected with a clear error identifying the unknown sequence. A sequence that exists but is not a template entry is accepted and fires as an additional execution in the current run.
- **Multiple relative timers due at the same iteration boundary**: All fire before that iteration's regular steps; their relative order among themselves is unspecified, consistent with existing timer ordering.
- **Live re-schedule before the previous one fires**: Issuing a new live schedule for a sequence that is still pending replaces the pending schedule with the new offset (the most recent live request wins for that sequence).
- **Template entry has both an absolute time-of-day and a relative offset configured**: Only the selected mode is used at runtime; the other value is ignored. Exactly one mode is in effect per timer entry.
- **Run restarted after a template relative timer fired**: The offset is measured from the new run's start, so the timer can fire again in the new run (per-run firing state, consistent with feature 053).
- **Offset precision finer than the iteration interval**: The sequence fires at the first iteration boundary at or after the offset elapses; sub-iteration precision is not guaranteed.

## Requirements *(mandatory)*

### Functional Requirements

#### Relative offset specification

- **FR-001**: The system MUST allow a timer-scheduled sequence to be expressed as a **relative time offset** (a non-negative duration in minutes and seconds, with optional hours) in addition to the existing absolute time-of-day option.
- **FR-002**: A relative offset MUST support at least minutes-and-seconds precision (e.g., "10 min 0 sec"), accept a value of zero, and reject negative values.
- **FR-003**: Each timer entry MUST resolve to exactly one schedule mode at runtime — either absolute time-of-day (existing) or relative offset (new) — never both simultaneously.

#### Template-saved relative offsets (anchored to run start)

- **FR-004**: When a relative offset is saved as part of a queue template entry, the system MUST anchor the offset to the **start of each queue run** and recompute the target instant fresh on every run.
- **FR-005**: A template relative-offset timer MUST fire **once per run**, at the first iteration boundary at or after the offset has elapsed since the run started, and MUST NOT fire again for the remainder of that run.
- **FR-006**: The system MUST persist the relative offset (mode and value) as part of the template entry so it survives service restarts and is shared across all queues that load the template.

#### Live relative scheduling (anchored to the call moment)

- **FR-007**: The system MUST provide a live API operation that schedules an identified sequence to fire, anchored to **the moment the call is received**, after a supplied relative offset.
- **FR-008**: A live relative schedule MUST be **ephemeral to the current run**: it MUST NOT be written to the template and MUST apply only to the queue run that is active when the call is made.
- **FR-009**: A live relative schedule MUST fire the sequence **once**, at the first iteration boundary at or after (call time + offset), and MUST NOT fire again unless a new live call is made.
- **FR-010**: The system MUST allow a sequence to be **re-scheduled** by a subsequent live call after it has fired; a new live call schedules a fresh single firing.
- **FR-011**: If a new live schedule is issued for a sequence whose previous live schedule has not yet fired, the system MUST replace the pending schedule with the new one (most recent request wins for that sequence).
- **FR-012**: The system MUST reject a live relative schedule request when no queue run is active, returning a clear error.
- **FR-013**: A live relative schedule MAY target **any sequence in the sequence library**, not only sequences already present as entries in the running queue's template. The targeted sequence fires as an **additional execution** within the current run (it does not alter or replace the queue's authored entries). The system MUST reject a request that targets a sequence that does not exist in the library, returning a clear error identifying the problem.

#### Evaluation semantics

- **FR-014**: Relative-offset timers (both template and live) MUST be evaluated only at **iteration boundaries**, consistent with existing timer evaluation; they MUST NOT interrupt a currently executing step.
- **FR-015**: When multiple relative-offset timers are due at the same iteration boundary, all MUST fire before that iteration's regular steps, with unspecified relative order among themselves, consistent with existing timer behavior.
- **FR-016**: Relative-offset timer firings MUST follow the existing non-fatal failure policy: a failure is recorded in the execution log and the run continues.
- **FR-016a**: Each relative-offset timer firing (template) and each live additional firing MUST be **counted as a completed step** in the queue run's step-completion total/metrics. This counting affects reported totals only; run termination remains governed by the once-per-run steps (these firings do not change the stop condition). This differs from "every-step" sequences (feature 053), which do not count.

#### API

- **FR-017**: The template entry API (create, update, read) MUST accept and return a relative-offset timer specification (mode indicator plus the offset value) alongside the existing time-of-day option.
- **FR-018**: The live relative-schedule API MUST accept the target sequence identifier and the relative offset, and MUST validate both, returning a descriptive error for invalid offsets, unknown sequences, or no active run.

#### UI

- **FR-019**: The template editor UI MUST let the operator choose, per timer entry, between absolute time-of-day and relative-offset modes, enter the offset in minutes and seconds (with optional hours), and persist the choice.
- **FR-020**: The running-queue UI MUST let the operator live-schedule a selected sequence with a relative offset and surface that a sequence has a pending relative schedule (and its expected fire time) until it fires.
- **FR-021**: The UI MUST validate the entered offset (non-negative, well-formed) before submission and show a clear message when invalid.

### Key Entities *(include if feature involves data)*

- **Timer Schedule (extended)**: The schedule parameter of a timer-type template entry, extended so it can be expressed either as an absolute time-of-day (existing) or as a relative offset (new) measured from run start. Carries a mode indicator and the relevant value.
- **Relative Offset**: A non-negative duration (hours/minutes/seconds, at least minutes-and-seconds precision) representing how long after its anchor the sequence should fire.
- **Live Relative Schedule**: An ephemeral, per-run scheduling request that targets one sequence (any sequence in the library) with a relative offset anchored to the call moment. Causes an additional execution in the current run; not persisted to the template; superseded by a later live request for the same sequence; cleared after it fires.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A template timer entry configured with a relative offset fires exactly once per run, at the first iteration boundary at or after the offset elapses since run start, in 100% of runs where the offset elapses before the run ends.
- **SC-002**: The same template, run on separate occasions, fires its relative-offset timer at a consistent elapsed time (within one iteration interval of the configured offset) in 100% of runs — demonstrating the offset is recomputed per run.
- **SC-003**: A live relative-schedule API call against a running queue causes the targeted sequence to fire exactly once, within one iteration interval of (call time + offset), in 100% of accepted calls.
- **SC-004**: Live relative schedules leave the template unchanged in 100% of cases (no persisted record of the live schedule).
- **SC-005**: A sequence that has fired from a live schedule can be re-scheduled and fires again in 100% of cases where a second valid live call is made.
- **SC-006**: Invalid offsets (negative or malformed) and live calls with no active run or an unknown sequence are rejected with a clear error in 100% of cases, with nothing scheduled.
- **SC-007**: An operator can configure a relative-offset timer in the template editor, save, reopen, and see the same mode and value preserved in 100% of cases.
- **SC-008**: Existing time-of-day timer entries and all non-timer entries continue to behave exactly as before in 100% of existing templates (no regression from adding the relative-offset option).
- **SC-009**: An operator can live-schedule a sequence relative to now from the UI and confirm it fired, completing the full action in under 1 minute.
- **SC-010**: A run that includes N once-per-run step executions plus K relative/live firings reports N+K completed steps in its step-completion total in 100% of runs, while still terminating based solely on the once-per-run steps.

## Assumptions

- **Builds on feature 053**: Relative offsets are an additional way to express the existing "timer" schedule type, not a new top-level schedule type. The "once-per-run", "every-step", and absolute time-of-day "timer" behaviors from feature 053 are unchanged.
- **Iteration-boundary evaluation**: Like the existing time-of-day timer, relative-offset timers are evaluated only at iteration (cycle) boundaries, never mid-step. "Fires when the offset elapses" means "fires at the first iteration boundary at or after the offset elapses".
- **Run start anchor for templates**: For template-saved offsets, "now" is the start of the queue run. For non-cyclic queues this is the single iteration's start; for cyclic queues the offset is still measured from the overall run start (not re-anchored each cycle).
- **Call-moment anchor for live calls**: For live API/UI scheduling, "now" is the moment the request is received by the server, evaluated against the server's local clock (consistent with feature 053's server-local-time evaluation).
- **Fire-once semantics**: Both anchors fire the sequence once. Recurrence is achieved by issuing additional live calls, not by an automatic repeat. No recurring-interval timer is introduced by this feature.
- **Live scheduling targets the active run**: Live relative scheduling applies to the currently running queue. If no run is active, the request is rejected rather than queued for a future run.
- **Offset precision and bounds**: Offsets are accepted with at least minutes-and-seconds precision and a value of zero. A reasonable upper bound (consistent with realistic run durations) may be enforced; the exact bound is an implementation detail and does not change behavior within range.
- **Most-recent-wins for live re-scheduling**: Issuing a new live schedule for a sequence supersedes any still-pending live schedule for that same sequence in the current run.
- **Server-local clock**: All time evaluation uses the server's local clock, with no per-entry timezone configuration, consistent with feature 053.
- **Scale**: Operator-scale consistent with existing queue features (up to ~50 templates, ~100 entries each); the number of concurrently pending live schedules is bounded by the number of sequences in the running queue.

## Out of Scope

- Recurring-interval timers (automatically firing every N minutes/seconds) — re-firing is done via repeated live calls, not an automatic recurrence.
- Persisting live relative schedules into the template or across service restarts (live schedules are ephemeral to the current run).
- Scheduling queue start/stop at a relative time (this feature schedules sequences within a running queue, not the queue itself).
- Changing the existing absolute time-of-day timer behavior, the "once-per-run" and "every-step" schedule types, or any internal sequence execution behavior (steps, conditions, loops, waits).
- Per-entry or per-template timezone configuration (server local clock only).
- Sub-iteration timing guarantees (relative timers fire at iteration boundaries, not at an exact sub-second instant).
- Pausing, editing, or cancelling a pending live schedule beyond replacing it with a new live call (no dedicated cancel operation in this iteration).
