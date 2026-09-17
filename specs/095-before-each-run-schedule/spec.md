# Feature Specification: Before-Each-Run Schedule Type

**Feature Branch**: `095-before-each-run-schedule`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #202 (https://github.com/bbqf/GameBotAI/issues/202) — FR-006: a queue-template schedule type that runs entries BEFORE every timed or self-rescheduled entry. Closes #202

## Background

Queue templates support four schedule types: OncePerRun, EveryStep (shown to operators as "After Every Step"), Timer and AtQueueStart. None of them runs an entry *before* a timed or self-booked firing: "`EveryStep` runs after every entry, and `AtQueueStart` runs once per queue start". No sequence step can run another sequence, and `reschedule-self` books only its own sequence.

Production queues are moving from continuous cycling to self-rescheduling tasks with an idle pause between runs. Every task must start from a known state (emulator attached and drawing; game on the city screen with popups cleared). Today the state-establishing sequences are After Every Step entries, so they run *after* each task. Anything that changes during the idle pause — for example a "Disconnected. Reconnect now?" popup, which caused a 44 h outage on 2026-09-14 — makes the next task refuse to act, and that run is lost. Copying the establishing steps into every task sequence duplicates the largest sequence and risks the ~240 s sequence time limit (#182).

## Clarifications

### Session 2026-09-17

- Q: Which firings trigger a Before Each Run pass — only timers, or every self-rescheduled firing too? → A: Time-of-day timers (and their daily retries), relative timers, live schedules, and self-reschedule firings booked as Timer, AtQueueStart or OncePerRun; not template OncePerRun/AtQueueStart steps and not After Every Step (template or injected). *Rationale: matches the issue's "timer-fired, live-scheduled or self-rescheduled entry"; After Every Step is infrastructure, not a task.*
- Q: If a Before Each Run entry fails, does the triggering task still run? → A: Yes — failure is recorded and non-fatal, the task runs. *Rationale: mirrors After Every Step accounting; the task sequences already guard themselves and refuse to act on a wrong screen, so skipping would only lose more runs.*
- Q: If more firings become due while the pass or the first task is running (same wake-up), is the pass repeated? → A: No — at most once per scheduler-loop iteration, before its first triggering firing. *Rationale: the issue says "once per wake-up when several entries are due together"; the next iteration gets its own pass.*
- Q: How does the web editor expose the type? → A: As a new "Before each run" scheduling area alongside the existing areas, with a "Before Each Run" badge/label. *Rationale: the editor already assigns schedule types by area; a new area is the consistent affordance.*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish a known state before each scheduled task (Priority: P1)

An operator marks the state-establishing sequences in a queue template as "Before Each Run". Whenever the queue wakes up to run a timed, live-scheduled or self-rescheduled task, those sequences run first, in template order, and only then does the task run — so a popup that appeared during the idle pause is cleared before the task looks at the screen.

**Why this priority**: This is the whole value of the issue: tasks stop being lost to state that changed while the queue was idle.

**Independent Test**: Build a template with one Before Each Run entry and one Timer entry; run the queue; observe that the Before Each Run sequence executes immediately before the timer sequence, every time the timer fires.

**Acceptance Scenarios**:

1. **Given** a running queue whose template has a Before Each Run entry B and a time-of-day Timer entry T, **When** T becomes due, **Then** B runs and completes, and T runs immediately after it.
2. **Given** a running queue with Before Each Run entry B and a sequence that self-reschedules with a Timer option, **When** the self-rescheduled firing becomes due, **Then** B runs immediately before it.
3. **Given** a running queue with Before Each Run entry B and a relative-offset Timer entry or a live schedule, **When** that firing becomes due, **Then** B runs immediately before it.
4. **Given** a template with Before Each Run entries B1 then B2 (template order), **When** a timed firing becomes due, **Then** B1 runs, then B2, then the timed firing.
5. **Given** a queue with pause-when-idle enabled that has backed the game out during a gap, **When** the next timed firing becomes due and the game is brought back, **Then** the Before Each Run entries run before that firing.

---

### User Story 2 - Run the establishers once per wake-up, not once per due task (Priority: P1)

When several scheduled tasks become due at the same wake-up, the Before Each Run entries run once, before the first of them, rather than once before each.

**Why this priority**: The establishers are the largest sequences; repeating them for every co-due task would multiply run time for no benefit and was explicitly ruled out by the issue.

**Independent Test**: Make two Timer entries due at the same time with one Before Each Run entry; observe exactly one Before Each Run execution before the first timer firing and none between the two.

**Acceptance Scenarios**:

1. **Given** two timed firings T1 and T2 due at the same wake-up and a Before Each Run entry B, **When** the queue wakes up, **Then** the execution order is B, T1, (After Every Step entries, if any), T2, (After Every Step entries, if any) — B runs exactly once.
2. **Given** a wake-up in which no timed, live or self-rescheduled firing is due, **When** the queue iterates, **Then** no Before Each Run entry runs.

---

### User Story 3 - Configure and see the new schedule type (Priority: P2)

An operator can choose "Before Each Run" for a template entry when editing a queue template, save it, see it again when the template is reloaded, and see it listed in the queue monitor alongside the other scheduled items.

**Why this priority**: Required to use the feature without hand-editing data, but the engine behaviour (Stories 1–2) delivers the value.

**Independent Test**: Save a template with an entry whose schedule type is Before Each Run through the template API and the web UI; reload it; confirm the type round-trips and the queue monitor lists the entry with a "Before Each Run" label.

**Acceptance Scenarios**:

1. **Given** the template save API, **When** an entry is saved with schedule type `BeforeEachRun`, **Then** the save succeeds and the template detail returns `BeforeEachRun` for that entry.
2. **Given** the template save API, **When** an entry is saved with an unknown schedule type, **Then** the error message lists `BeforeEachRun` among the accepted values.
3. **Given** the queue template editor, **When** the operator places a sequence in the Before Each Run area and saves, **Then** reopening the template shows the sequence in that area.
4. **Given** a running queue with a Before Each Run entry, **When** the operator opens the queue monitor, **Then** the entry is listed once with the label "Before Each Run".
5. **Given** the published API documentation, **When** a client reads the schedule type field, **Then** `BeforeEachRun` is documented as an accepted value with its meaning.

### Edge Cases

- A Before Each Run entry fails: the failure is recorded as a failed execution, the run continues, and the scheduled firing that triggered the pass still runs.
- A Before Each Run entry is disabled: it is skipped like any other disabled entry.
- A template has only Before Each Run entries (nothing that can trigger them): they never run, and they do not keep the run alive or make an otherwise idle cycle count as work.
- A Before Each Run sequence itself calls `reschedule-self`: the booking is honoured with its normal semantics; the pass never re-triggers itself and never triggers an After Every Step pass.
- Template OncePerRun steps, template AtQueueStart entries, After Every Step entries and After Every Step self-reschedule injections do not trigger a Before Each Run pass.
- A daily retry of a failed time-of-day Timer firing is a timed firing and triggers the pass like the original firing.
- The run is stopped while a Before Each Run entry is executing: the stop takes effect as for any other firing and the triggering firing does not run.
- The emulator connection is lost before a Before Each Run entry: the run ends with the existing connection-lost failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support a fifth queue-template schedule type with the stored/wire identifier `BeforeEachRun`, shown to operators as "Before Each Run".
- **FR-002**: Before Each Run entries MUST run immediately before each *triggering firing*. Triggering firings are: time-of-day Timer firings, daily retries of those firings, relative-offset Timer firings, live-schedule firings, and self-reschedule firings booked with the Timer, AtQueueStart or OncePerRun option.
- **FR-003**: Within one scheduler wake-up (one pass of the run's scheduling loop), the Before Each Run entries MUST run at most once, immediately before the first triggering firing of that wake-up; later triggering firings in the same wake-up MUST NOT repeat them.
- **FR-004**: Before Each Run entries MUST run in template order.
- **FR-005**: Template OncePerRun steps, template AtQueueStart entries, After Every Step entries and After Every Step self-reschedule injections MUST NOT trigger a Before Each Run pass.
- **FR-006**: Before Each Run executions MUST NOT count toward the run's executed total; a failed execution MUST count toward the failed total, MUST be non-fatal, and MUST NOT prevent the triggering firing from running.
- **FR-007**: A Before Each Run pass MUST NOT trigger an After Every Step pass, and MUST NOT trigger another Before Each Run pass.
- **FR-008**: Disabled Before Each Run entries MUST be skipped.
- **FR-009**: Before Each Run entries MUST NOT, on their own, keep a non-cycling run alive, make a cycling iteration count as a cycle, or otherwise change when the run completes.
- **FR-010**: The template save endpoint MUST accept `BeforeEachRun` (case-insensitively, like the existing types), persist it, and return it from the template detail endpoint; its invalid-type error message MUST list it among the accepted values. Timer fields are not required for it.
- **FR-011**: The queue monitor MUST list each enabled Before Each Run entry once, labelled "Before Each Run", in the same manner After Every Step entries are listed.
- **FR-012**: The web UI queue template editor MUST offer a "Before each run" scheduling area in which an operator can place sequences, and MUST display the "Before Each Run" label wherever schedule types are shown.
- **FR-013**: The API contract documentation MUST describe `BeforeEachRun` wherever the other schedule type values are described.
- **FR-014**: Existing schedule types (OncePerRun, EveryStep, Timer, AtQueueStart) and `reschedule-self` booking semantics MUST behave exactly as before; a template without Before Each Run entries MUST execute identically to today.
- **FR-015**: Each Before Each Run execution MUST appear in the run's execution log and cycle ledger like other entry executions.

### Out of scope

- A new primitive step that runs another sequence (run-sequence, call-sequence, include).
- A `BeforeEachRun` option for `reschedule-self`, or any other change to its booking semantics.

### Key Entities

- **Queue template entry**: a sequence reference with a schedule type; gains the new `BeforeEachRun` value.
- **Before Each Run pass**: the ordered execution of all enabled Before Each Run entries, performed at most once per scheduler wake-up ahead of the first triggering firing.
- **Triggering firing**: a timed, live-scheduled or self-rescheduled firing (as listed in FR-002) that causes the pass.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of wake-ups that run at least one triggering firing, the configured Before Each Run entries run before the first such firing.
- **SC-002**: When N triggering firings are due at one wake-up, the Before Each Run entries run exactly once (not N times).
- **SC-003**: A task scheduled after an idle pause during which a blocking popup appeared runs against a cleared screen, provided the Before Each Run sequence handles that popup — no scheduled run is lost to state that changed during the pause.
- **SC-004**: All existing queue scheduling tests pass unchanged.
- **SC-005**: An operator can configure a Before Each Run entry end-to-end (edit, save, reload, see it in the monitor) without editing stored data by hand.

## Assumptions

- "Wake-up" means one iteration of the run's scheduling loop; an idle pause ends with the next iteration, so establishers run after the game is brought back.
- The pass is lazy: it runs only when a triggering firing is actually about to run, never speculatively.
- Before Each Run executions are infrastructure like After Every Step executions, so they share its accounting (not executed, but failures counted).
- A self-reschedule firing booked with the AtQueueStart or OncePerRun option is a "self-rescheduled entry" in the issue's sense and therefore triggers the pass.
