# Feature Specification: Queue Sequence Scheduling

**Feature Branch**: `053-schedulable-sequences`  
**Created**: 2026-06-03  
**Status**: Implemented (iterated by 059, 060, 061)
**Input**: User description: "I want make a sequences in the queues schedulable. There should be following types of schedules: 1. Once per queue run (current implementation) 2. Every step - the sequences that are marked this way, will be executed after every step. These sequences do not count towards the steps needed to be performed to complete the queue execution run. If the last step of the queue is executed, these sequences are executed for the last time and the queue run ends. 3. Scheduled per timer - if a certain sequence is scheduled to run at a given time, it's execution should be done at the beginning of the queue, but only after the time has passed. If two sequences are scheduled to be executed at the same time, then the order is not important, so for example the last one to be scheduled will be executed first. These types of scheduling has to be configurable within template via API and UI."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set schedule types on template entries and observe different execution behaviors (Priority: P1)

An operator editing a queue template assigns one of three schedule types to each sequence entry: "once-per-run" (the default), "every-step", or "timer". When the queue runs, each sequence fires according to its type: once-per-run sequences execute in the normal order; every-step sequences execute automatically after each once-per-run sequence; timer sequences execute at the start of the next queue iteration once their scheduled time has passed. The operator can configure schedule types both in the queue template editor UI and through the API.

**Why this priority**: This is the foundation of the feature. Without schedule-type configuration being persisted and respected at runtime, none of the individual scheduling behaviors can be observed or tested independently.

**Independent Test**: Create a template with three entries — one "once-per-run", one "every-step", and one "timer" (set to fire immediately or in the past) — start the queue, and confirm the every-step sequence runs after the once-per-run step and the timer sequence runs before it at the start of the iteration.

**Acceptance Scenarios**:

1. **Given** a template entry with schedule type "once-per-run", **When** the queue runs, **Then** the sequence executes once per cycle in its normal position within the template order.
2. **Given** a template entry with schedule type "every-step", **When** the queue runs through its regular steps, **Then** that sequence executes after each once-per-run step, including after the final step.
3. **Given** a template entry with schedule type "timer" and a scheduled time that has already passed, **When** the queue starts a new iteration, **Then** the timer sequence executes at the beginning of that iteration before any once-per-run steps.
4. **Given** a template entry with no schedule type explicitly set, **When** the queue runs, **Then** the entry behaves as "once-per-run" (the default).
5. **Given** schedule types configured via the API on a template, **When** the queue editor is opened, **Then** the configured types are displayed correctly and can be changed through the UI.

---

### User Story 2 - "Every-step" sequences run after every regular step and on queue completion (Priority: P1)

An operator marks a monitoring or bookkeeping sequence as "every-step" so it runs automatically after each regular sequence in the queue. The every-step sequences do not count toward completing the queue run — the run ends when all once-per-run steps have executed — but they do execute one final time after the last step before the run finishes (or the next cycle begins).

**Why this priority**: This schedule type supports continuous background tasks (health checks, UI refreshes, resource monitors) that must run throughout the entire queue execution without the operator manually inserting them between every regular step.

**Independent Test**: Create a template with two "once-per-run" sequences (A, B) and one "every-step" sequence (C). Start the queue (no cycle). Observe that C executes after A, then C executes after B, and then the queue ends — C ran twice total, and the run is "completed full run" once A and B both finished.

**Acceptance Scenarios**:

1. **Given** a template with once-per-run steps A, B and an every-step sequence C, **When** the queue runs without cycle execution, **Then** the execution order is: A → C → B → C, and the run ends after the second C.
2. **Given** the queue from scenario 1, **When** the run ends, **Then** the run's stop reason is "completed full run" and C's executions are visible in the execution log but do not extend the run's required step count.
3. **Given** a template with no once-per-run steps and one every-step sequence, **When** the queue runs, **Then** the every-step sequence executes exactly once (it is the "last step") and the run ends — the system does not loop indefinitely.
4. **Given** multiple every-step sequences in the template, **When** the queue runs past one regular step, **Then** all every-step sequences execute after that step, in their template order.
5. **Given** cycle execution enabled and once-per-run steps A, B with every-step sequence C, **When** the queue cycles, **Then** each cycle follows A → C → B → C and starts again until stopped.

---

### User Story 3 - Timer-scheduled sequences fire at the start of the next iteration when their time is due (Priority: P1)

An operator schedules a sequence to run at a recurring interval or specific time. While the queue is running, the system checks timer-scheduled sequences at the beginning of each queue iteration. Any sequence whose timer has elapsed is executed before the regular steps of that iteration begin. If the queue is non-cyclic and the timer has not elapsed by the time the run completes, the timer sequence never fires during that run.

**Why this priority**: Timer scheduling enables periodic actions — collecting in-game resources, submitting daily tasks — without requiring the operator to structure the queue solely around timing. It depends only on schedule type storage (US1) and is independently valuable.

**Independent Test**: Set a timer sequence to a wall-clock time that is one minute from now (server local time). Start a cyclic queue with once-per-run steps that take a few seconds each per cycle. Observe that the timer sequence does not fire on the first iteration (time not yet passed), then fires at the start of the first iteration boundary after the scheduled time passes, and does not fire again during the same run.

**Acceptance Scenarios**:

1. **Given** a timer-scheduled sequence whose time has passed at the moment the queue starts a new iteration, **When** the iteration begins, **Then** the timer sequence executes before any once-per-run steps of that iteration.
2. **Given** a timer-scheduled sequence whose time has not yet passed, **When** the iteration begins, **Then** the timer sequence is skipped for that iteration and checked again at the start of the next one.
3. **Given** two timer sequences both due at the same time, **When** the iteration begins, **Then** both execute before the regular steps; their relative execution order is unspecified.
4. **Given** a non-cyclic queue with a timer sequence that has not elapsed by the time all once-per-run steps finish, **When** the run ends, **Then** the timer sequence was never executed during that run and the run ends normally.
5. **Given** timer sequences and every-step sequences both present, **When** an iteration begins, **Then** timer sequences fire first (at iteration start), then regular steps proceed, each followed by every-step sequences.

---

### User Story 4 - Configure schedule types via the API (Priority: P2)

A developer or automation script creates or updates a queue template through the API, setting the schedule type for each sequence entry (once-per-run, every-step, or timer with its schedule parameter). The schedule type and any associated schedule parameter are persisted as part of the template entry and are retrievable through the same API.

**Why this priority**: API configurability is required alongside UI configurability as an explicit feature requirement, and enables programmatic template management without a UI.

**Independent Test**: Create a template via API with one entry of each schedule type; retrieve the template via API and confirm each entry's schedule type and timer parameter (if applicable) are returned correctly.

**Acceptance Scenarios**:

1. **Given** a POST or PUT to the template entry endpoint with a `scheduleType` of "every-step", **When** the template is retrieved, **Then** the entry's schedule type is "every-step".
2. **Given** a POST or PUT with `scheduleType` of "timer" and a schedule parameter, **When** the template is retrieved, **Then** both the schedule type and parameter are returned unchanged.
3. **Given** a POST or PUT with no `scheduleType` field, **When** the template is retrieved, **Then** the entry defaults to "once-per-run".
4. **Given** an invalid `scheduleType` value, **When** the request is submitted, **Then** the API rejects it with a clear validation error.

---

### Edge Cases

- **All entries are "every-step"**: No once-per-run steps exist. The every-step sequences execute exactly once (there is one implicit "completion point" with zero regular steps before it) and the run ends without looping.
- **All entries are "timer"**: No once-per-run or every-step sequences. Timer sequences fire if due at the start of the first (and only) iteration; the run ends immediately after because there are no regular steps to execute.
- **Timer fires during a running step**: Timer sequences are only checked at iteration boundaries (beginning of each cycle), never mid-step. A timer that becomes due while a regular step is executing waits until the next iteration boundary.
- **Every-step sequence fails**: Per the existing failure policy (FR-008 of spec 051), every-step sequence failures are non-fatal. The run records the failure in the nested entry and continues with the next step.
- **Timer-scheduled sequence fires when the queue is about to end**: If the last iteration's regular steps complete and there are due timer sequences, the timer sequences still fire before the run closes (at the iteration start of the final cycle). However, if the queue is non-cyclic and ends, timer sequences that became due after the last regular step started do not fire mid-step.
- **Very frequent timer (shorter interval than a single step's duration)**: The timer sequence fires at most once per iteration boundary, even if the interval has elapsed multiple times during a long step. It fires exactly once per due period, not once per missed interval.
- **Cycle execution off, timer not yet due**: A non-cyclic queue with a timer sequence completes without ever executing the timer sequence if the timer does not elapse before all regular steps finish.
- **Template has no entries**: The queue run completes immediately ("completed full run") with no sequences executed, consistent with existing empty-template behavior.
- **Reordering entries changes every-step execution order**: Moving an every-step entry up or down in the template changes the order it executes relative to other every-step entries (they all fire after each regular step, in their template order).

## Requirements *(mandatory)*

### Functional Requirements

#### Schedule types

- **FR-001**: System MUST support three schedule types for each sequence entry in a queue template: **"once-per-run"** (default), **"every-step"**, and **"timer"**.
- **FR-002**: System MUST default to "once-per-run" when no schedule type is specified for a template entry, preserving existing behavior.
- **FR-003**: A "timer"-type entry MUST carry a schedule parameter defining a wall-clock time-of-day (e.g., 15:30) in server local time at which the sequence is due. The sequence fires at most once per calendar day during the run: if the queue is running and an iteration boundary occurs after that time has passed today (server local clock), the sequence executes. The "already fired" state is tracked per calendar date and is per-run only — it resets when the queue is restarted; if the queue is stopped and restarted on the same day, the timer may fire again in the new run. Recurring-interval and elapsed-duration timer types are out of scope for this iteration.
- **FR-004**: System MUST persist the schedule type and any associated timer parameter as part of the template entry, so that the configuration survives service restarts and is shared across all queues that load the template.

#### Execution of "once-per-run" sequences

- **FR-005**: "Once-per-run" sequences MUST execute in their template order, one after another, as they do today. This type represents the normal queue step and defines what counts as a "step" for the purposes of "every-step" sequencing.

#### Execution of "every-step" sequences

- **FR-006**: "Every-step" sequences MUST execute after every "once-per-run" sequence completes, in their template order among other every-step entries, before the next once-per-run step begins.
- **FR-007**: "Every-step" sequences MUST also execute after the final once-per-run step, before the run ends or the next cycle begins.
- **FR-008**: "Every-step" sequences MUST NOT count toward the queue run's completion. The run is considered complete (or a cycle complete) when all once-per-run sequences have executed, regardless of how many times every-step sequences ran.
- **FR-009**: If there are no once-per-run entries in the template, every-step sequences MUST execute exactly once (as if the zero regular steps have "all" been completed), and the run ends.
- **FR-010**: Every-step sequence failures MUST be non-fatal to the queue run, consistent with the existing per-sequence failure policy (non-fatal, recorded in nested execution log, run continues).

#### Execution of "timer" sequences

- **FR-011**: Timer-scheduled sequences MUST be evaluated at the beginning of each queue iteration (before any once-per-run step in that iteration is executed).
- **FR-012**: A timer sequence MUST execute in that iteration if and only if its scheduled time has passed by the time the iteration begins AND the sequence has not already fired on today's calendar date during this run (per FR-003). If not yet due, or already fired today in this run, the sequence is skipped for that iteration and re-evaluated at the start of the next.
- **FR-013**: When multiple timer sequences are due at the start of the same iteration, all of them MUST execute before any once-per-run step, and their relative order among themselves is unspecified (the system may execute them in any order).
- **FR-014**: Timer sequences MUST NOT interrupt a currently executing step; they are only checked at iteration boundaries.
- **FR-015**: Timer sequence failures MUST be non-fatal to the queue run, consistent with the existing per-sequence failure policy.

#### Interaction between schedule types in a single iteration

- **FR-016**: Within each iteration, the execution order MUST be: (1) due timer sequences (in any order), (2) the next once-per-run sequence, (3) all every-step sequences (in template order). This group of (2)+(3) repeats for each once-per-run step, with timer sequences evaluated exactly once at the iteration start and never again between subsequent once-per-run or every-step executions within that iteration.

*(FR-017 merged into FR-016 during spec clarification — no gap in coverage.)*

#### Configuration via API

- **FR-018**: The template entry API (create and update) MUST accept a `scheduleType` field with values "once-per-run", "every-step", and "timer".
- **FR-019**: For "timer" entries, the API MUST accept and validate the associated schedule parameter alongside the entry.
- **FR-020**: The template entry API (read) MUST return the `scheduleType` and schedule parameter for each entry.
- **FR-021**: The API MUST reject invalid schedule type values with a descriptive validation error.

#### Configuration via UI

- **FR-022**: The queue template editor UI MUST allow the operator to set the schedule type for each sequence entry.
- **FR-023**: For "timer" schedule type, the UI MUST provide input for the associated schedule parameter.
- **FR-024**: The UI MUST visually distinguish entries of different schedule types (e.g., a label, icon, or badge) so the operator can see at a glance which entries are "every-step" or "timer" without opening each entry.

### Key Entities *(include if feature involves data)*

- **Template Entry (updated)**: An ordered reference to a sequence within a queue template, extended with a `scheduleType` field ("once-per-run" | "every-step" | "timer") and, for timer entries, an associated schedule parameter. Other template entry attributes are unchanged.
- **Queue Run Iteration**: One pass through the template's once-per-run steps within a queue execution. Timer sequences are evaluated once per iteration start; every-step sequences execute once per regular step within the iteration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every "every-step" sequence in a template executes exactly once after each "once-per-run" step and exactly once after the final step in 100% of queue runs, regardless of whether cycle execution is on or off.
- **SC-002**: Every-step sequences never appear in the queue run's step-completion count: a run with N once-per-run sequences and M every-step sequences reports completing N steps, not N×(M+1) or any other inflated count.
- **SC-003**: A timer-scheduled sequence whose time is past executes at the start of the next iteration in 100% of runs that include a new iteration after the time elapses.
- **SC-004**: A timer-scheduled sequence whose time has not elapsed during a non-cyclic run is never executed — 0 executions — in 100% of such runs.
- **SC-005**: When multiple timer sequences are simultaneously due, all of them execute before any regular step in the same iteration in 100% of such cases.
- **SC-006**: Schedule types configured via the API are reflected correctly in the UI editor, and schedule types set in the UI are persisted and returned correctly by the API, in 100% of cases.
- **SC-007**: Operators can configure schedule types for all template entries, view their current types at a glance, and save the configuration — completing the full workflow in the template editor in under 2 minutes for a template with up to 20 entries.
- **SC-008**: The introduction of schedule types does not change the behavior of existing templates: all entries that had no explicit schedule type continue to execute as "once-per-run" in 100% of existing queues.

## Assumptions

- **"Step" defined as once-per-run execution**: In the context of "every-step" scheduling, a "step" is one execution of a once-per-run sequence. Every-step sequences themselves do not trigger additional every-step executions (no cascading).
- **Iteration boundary = cycle boundary**: For cyclic queues, "beginning of the queue" means the start of each cycle. For non-cyclic queues, there is one iteration (and one iteration boundary at the very start).
- **Timer check is once per iteration start**: Timers are evaluated only at the iteration boundary, never mid-step. A timer that becomes due while a step is executing fires at the next iteration boundary.
- **Timer parameter is a wall-clock time-of-day in server local time**: The timer schedule parameter specifies a time-of-day (hours and minutes) evaluated against the server's local clock. The sequence fires at most once per calendar day during the run: when an iteration boundary occurs after that time has passed today (server local time) and the sequence has not already fired on that calendar date in this run, the sequence executes. The "already fired" state is tracked per calendar date and is per-run only — it resets when the queue is restarted; if the queue is stopped and restarted on the same day, the timer may fire again in the new run.
- **Every-step sequences in template order**: When multiple every-step sequences exist, they execute in the order they appear in the template (relative to each other), after each regular step.
- **Failure policy unchanged**: Every-step and timer sequence failures follow the existing non-fatal policy (FR-008 of spec 051): recorded in nested execution log entries, run continues.
- **No cascading every-step triggers**: An every-step sequence completing does not trigger other every-step sequences; every-step sequences fire only after once-per-run sequences.
- **Templates are the authoring point**: Schedule type is a property of the template entry. When a template is loaded into a queue, the schedule types are copied as part of the template's entries and behave accordingly at runtime.
- **Backward compatibility**: Existing templates and queue entries without a schedule type field are treated as "once-per-run", preserving current behavior without requiring migration.
- **Scale**: Operator-scale consistent with existing queue features: up to ~50 templates, ~100 entries each, ~10 every-step sequences per template.

## Clarifications

### Session 2026-06-03

- Q: What is the timer schedule parameter — recurring interval, wall-clock time-of-day, or elapsed duration from queue start? → A: Wall-clock time-of-day (e.g., 15:30) is the MVP timer type. Recurring intervals and elapsed-duration timers are deferred to future iterations.
- Q: What timezone is used when evaluating the wall-clock timer? → A: Server local time (machine clock timezone); no per-entry or per-template timezone configuration.
- Q: If a queue is stopped and restarted on the same day after a timer sequence has already fired, does the timer fire again? → A: Yes — "fired today" state is per-run only and resets on restart; the timer may fire again in the new run on the same calendar day.

## Out of Scope

- Schedule types other than "once-per-run", "every-step", and "timer" (e.g., "on-event", "conditional", "random").
- Recurring-interval timer scheduling (e.g., every N seconds/minutes) — deferred to a future iteration.
- Elapsed-duration timer scheduling (e.g., fire N seconds after the queue run starts) — deferred to a future iteration.
- Scheduling at the queue level (e.g., automatically starting a queue at a given time); this feature schedules sequences within a running queue, not queue start/stop.
- Priority ordering among every-step sequences beyond their template position (no per-sequence priority field).
- Pausing or skipping a timer sequence without removing it from the template.
- Making the timer parameter dynamic at runtime (e.g., an operator cannot change the timer's schedule while the queue is running without editing the template).
- Per-sequence retry or backoff policies for every-step or timer sequences.
- Granular execution-log filtering by schedule type (logs record all executions consistently; filtering is out of scope for this feature).
- Any change to how individual sequences execute internally (steps, conditions, loops, waits); only the scheduling/ordering of sequences within a queue run is in scope.
