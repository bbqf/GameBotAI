# Feature Specification: Live Queue Monitor View

**Feature Branch**: `072-queue-monitor`
**Created**: 2026-07-23
**Status**: Implemented
**Input**: User description: "I need to be able to monitor the Queue when it runs: what are the next steps, in which order will they be executed and for what time are the sequence scheduled. Think of it like a playlist in music player. I don't need controls (yet), but the list showing what the queue is doing and what are the next steps. This should be dynamic, as the queue is dynamic in run-time. I want to see it when I open the running queue in the UI. Maybe instead of editing the queue when it's running (which doesn't make sense anyway), display different pages for stopped queue - edit page, and for running queue - monitor page."

## Clarifications

### Session 2026-07-23

- Q: How often should the monitor update while open (live-update cadence)? → A: Auto-refresh on a short fixed interval (~2–3 seconds) while the monitor is open.
- Q: How far ahead should the up-next list extend for a cycling / long-running queue? → A: Show the upcoming cycle's steps once, marked as repeating, plus all currently-pending timed/live firings (no projected infinite timeline).
- Q: Does the monitor show already-completed steps, or only the current + upcoming steps? → A: Now + upcoming only; completed steps drop off the list (full run history remains in the Execution Logs).
- Q: At what granularity does the "now" indicator identify current work — sequence or step within it? → A: Sequence-level (highlight the currently-running sequence); per-step detail stays in the Execution Logs.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Watch a running queue like a playlist (Priority: P1)

An operator opens a queue that is currently running. Instead of the entry-editing form,
they see a live "now-and-next" view: what the queue is doing right now and the ordered
list of sequences coming up, each annotated with when it is expected to run and why it is
scheduled (e.g. runs once at start, runs every cycle, fires at a time of day, fires after
an offset, or was scheduled live). The list updates on its own as the run progresses —
finished steps drop off the top, and newly scheduled work appears — without the operator
having to reload the page.

**Why this priority**: This is the core of the request. A queue's run-time behavior is
currently invisible: the operator can only infer what happened after the fact from the
Execution Logs. Seeing the upcoming plan while it runs is the entire value of the feature,
and it delivers a usable result on its own even without the page-switching of Story 2.

**Independent Test**: Start a queue with a mix of at-start, once-per-run, every-step, and
timed entries, open it, and confirm the monitor lists the upcoming sequences in the order
they will execute with a schedule label and expected time for each, and that the list
visibly changes as steps complete — all without manual refresh.

**Acceptance Scenarios**:

1. **Given** a running queue with several scheduled sequences, **When** the operator opens
   it, **Then** they see an ordered list of the upcoming sequences with, for each, the
   sequence name, why it is scheduled (its schedule type), and the time it is expected to
   run (or "next" / "now" for the imminent one).
2. **Given** the monitor is open on a running queue, **When** a step finishes and the run
   moves on, **Then** the view updates on its own so the completed step is no longer shown
   as upcoming and the currently-executing step is reflected, without the operator
   reloading the page.
3. **Given** a running queue with a sequence scheduled for a specific time of day, **When**
   the operator views the monitor, **Then** that sequence appears with its scheduled time
   so the operator can see when it will fire.
4. **Given** a sequence is scheduled live against the running queue (or a sequence
   reschedules itself), **When** that firing is registered, **Then** it appears in the
   upcoming list at its expected time without the operator reloading the page.

---

### User Story 2 - Separate monitor page for running, edit page for stopped (Priority: P1)

Opening a queue shows a different page depending on its state. A stopped queue opens the
existing editor (entries, schedule types, templates, game/emulator links). A running queue
opens the read-only monitor from Story 1 instead of the editor, because editing a queue's
entries while it is executing does not make sense and is already disallowed.

**Why this priority**: The user explicitly asked for this split, and it removes a
confusing state — today opening a running queue shows an editor whose controls are all
disabled. Routing running queues to the monitor and stopped queues to the editor makes the
distinction obvious and is a prerequisite for the monitor to be the thing an operator sees
when they "open the running queue."

**Independent Test**: Open a stopped queue and confirm the editor appears; start it (or
open an already-running queue) and confirm the monitor appears instead of the editor;
stop it and confirm opening it returns to the editor.

**Acceptance Scenarios**:

1. **Given** a stopped queue, **When** the operator opens it, **Then** the entry/template
   editor is shown as it is today.
2. **Given** a running queue, **When** the operator opens it, **Then** the monitor view is
   shown and the entry-editing controls are not presented.
3. **Given** the operator has the monitor open, **When** the queue stops (manually,
   completed, or failed), **Then** the view reflects that the queue is no longer running
   and the operator can return to the editor.
4. **Given** the operator has the editor open on a stopped queue, **When** the queue is
   started, **Then** the operator can reach the monitor view for that queue.

---

### User Story 3 - Understand a queue that has nothing imminent (Priority: P2)

Some running queues spend long stretches idle — e.g. a non-cycling queue that has finished
its once-per-run work and is only waiting for a timed firing much later, or a cycling queue
paused between cycles. The monitor makes this legible: it shows that the queue is alive but
waiting, and what it is waiting for, rather than looking empty or broken.

**Why this priority**: Without it, an idle-but-healthy queue is indistinguishable from a
stuck or empty one, which erodes trust in the view. It builds on Story 1 but is not
required for the first useful version.

**Acceptance Scenarios**:

1. **Given** a running queue that is between scheduled firings, **When** the operator views
   the monitor, **Then** it clearly indicates the queue is running and waiting, and shows
   the next thing it will do and when.
2. **Given** a running queue whose only remaining work is a timer far in the future, **When**
   the operator views the monitor, **Then** that pending firing is shown with its expected
   time so the operator understands why the queue is still running.

---

### Edge Cases

- **Cycling queue (repeats indefinitely)**: The once-per-run steps repeat every cycle with
  no natural end. The monitor presents the upcoming cycle's steps once, marked as repeating,
  alongside any currently-pending timed/live firings — rather than implying a finite end or
  attempting to list infinite future firings.
- **Queue stops while the monitor is open**: The monitor stops showing upcoming work and
  indicates the run has ended (with its outcome), then the operator can return to the
  editor. It must not keep showing stale "upcoming" steps as if the queue were still live.
- **Queue with no linked template / no entries**: The monitor shows that there is nothing
  scheduled, rather than an error or a blank area.
- **Time-of-day timer that already fired today**: A once-per-day timer that has already
  fired should not be shown as imminent; it is either omitted or shown as scheduled for its
  next eligible day, consistent with how the run actually treats it.
- **A step is currently executing and taking a long time**: The monitor shows it as the
  active/now-running step; it should not disappear or be shown as "next" while still running.
- **Rapid changes**: Live schedules, self-reschedules, and completing steps can change the
  plan quickly; the view must converge to the current truth without flicker that makes it
  unreadable.
- **Two people watching the same running queue**: Each sees a consistent live view; the
  monitor is read-only so there is no conflicting edit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a queue is running, the system MUST present a monitor view for that
  queue instead of the entry-editing form.
- **FR-002**: When a queue is stopped, the system MUST present the existing entry/template
  editor for that queue.
- **FR-003**: The system MUST route to the correct view based on the queue's current
  running state at the time it is opened, and MUST let the operator reach the monitor for a
  queue that is running and the editor for a queue that is stopped.
- **FR-004**: The monitor MUST display the ordered list of upcoming sequences the running
  queue is expected to execute, in the order they will be executed. The list is
  forward-looking: it shows the currently-executing step and upcoming steps only, and a
  completed step drops off as the run advances. The full run history is not duplicated in
  the monitor (it remains available in the Execution Logs).
- **FR-005**: For each upcoming sequence, the monitor MUST show the sequence's name (or a
  clear identifier), the reason it is scheduled (its schedule type — e.g. runs once at
  start, once per cycle, after every step, at a time of day, after an offset, or scheduled
  live), and the time it is expected to run where a time is known.
- **FR-006**: The monitor MUST distinguish the sequence that is currently executing (the
  "now" item) from the sequences that are still upcoming (the "next" items). The "now"
  indicator is sequence-level (which sequence is running); it does not track which internal
  step of that sequence is executing — per-step detail remains in the Execution Logs.
- **FR-007**: The monitor MUST reflect changes to the running queue's plan over time —
  steps completing, new live or self-rescheduled firings appearing, and timed firings
  becoming imminent — without requiring the operator to manually reload the page. It MUST
  do so by refreshing on its own on a short fixed interval of roughly 2–3 seconds while
  open.
- **FR-008**: The monitor MUST convey that a cycling queue repeats. For the repeating work
  it MUST show the upcoming cycle's steps once, marked as repeating, rather than projecting
  an unbounded timeline; alongside these it MUST show all currently-pending timed and live
  firings. It MUST NOT imply a false finite end for a cycling queue.
- **FR-009**: The monitor MUST clearly indicate when a running queue is alive but idle
  (waiting for a future firing), including what it is waiting for and when, so an idle
  healthy queue is distinguishable from a stuck or empty one.
- **FR-010**: When the queue is not running (stopped, completed, or failed), the monitor
  MUST indicate the run has ended rather than continuing to show upcoming steps, and MUST
  surface the run's final outcome where available.
- **FR-011**: The monitor MUST handle a running queue that currently has nothing scheduled
  (no template/entries) by clearly showing that nothing is scheduled, without error.
- **FR-012**: The monitor MUST be read-only — it presents information only and MUST NOT
  offer entry editing or, for this feature, run controls (start/stop/schedule).
- **FR-013**: The information the monitor shows MUST correspond to what the running queue
  will actually do (the same schedule types, order, and timing the run uses), so the
  operator can trust the view as an accurate preview of the run.
- **FR-014**: Times shown to the operator MUST be presented consistently and
  unambiguously (so, for example, a time-of-day firing is not misread against the wrong
  clock/zone).

### Key Entities *(include if data involved)*

- **Running queue state**: The live status of a queue's current run — whether it is
  running, which sequence it is currently running, and its start time. Basis for choosing
  monitor vs. editor and for the sequence-level "now" indicator.
- **Upcoming step (schedule item)**: A single planned firing of a sequence within the run —
  the sequence being fired, its schedule reason/type, its expected run time (absolute time,
  relative offset, "next", or "repeats each cycle"), and its position in the execution
  order. The collection of these, ordered, is the "playlist."
- **Schedule type**: The classification that explains why and when a sequence fires
  (at-queue-start, once-per-run, every-step, timer by time-of-day, timer by relative
  offset, live/ad-hoc schedule, self-reschedule). Drives the label and the expected time
  shown for each upcoming step.
- **Run outcome**: The terminal result of a run (completed / stopped / failed, with a
  summary) shown when the monitored queue is no longer running.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator opening a running queue can, within a few seconds and without any
  manual refresh, tell what the queue is doing now and see the ordered list of what it will
  do next with times — without consulting the Execution Logs.
- **SC-002**: 100% of the time, opening a running queue shows the monitor and opening a
  stopped queue shows the editor; the two are never confused.
- **SC-003**: As a run progresses, the monitor reflects a completed step and a newly
  scheduled firing on its own within roughly one refresh interval (~2–3 seconds), so the
  displayed plan matches the run's actual next actions.
- **SC-004**: For every schedule type a queue can use, the monitor shows a correct,
  human-readable reason and expected time (or an explicit "next"/"repeats"/"waiting"
  indicator) — verified across at-start, once-per-run, every-step, time-of-day, relative,
  and live/self-rescheduled entries.
- **SC-005**: A running queue that is idle-but-waiting is never presented as empty, broken,
  or finished; the operator can always tell it is alive and what it is waiting for.

## Assumptions

- **Read-only, no controls now**: Per the request, this feature adds monitoring only. Run
  controls (start/stop/schedule) remain where they are today (the queues overview) and are
  explicitly out of scope for the monitor page. This can be revisited later.
- **View selection follows existing running/stopped status**: The system already tracks
  whether a queue is Running vs. Stopped and already disables entry editing while running.
  The monitor/editor split reuses that same status signal rather than introducing a new
  concept of "monitor mode."
- **The monitor reflects the actual run plan, best-effort for future times**: The running
  queue evaluates timers at run-time boundaries rather than precomputing a fixed schedule.
  The monitor is expected to present the best available view of upcoming work and expected
  times (including "next", "repeats each cycle", and "waiting until <time>"), not a
  guaranteed-to-the-second future timeline. Ordering and schedule reasons are exact; far-
  future timer instants may be shown as their next eligible time.
- **Live updating cadence**: "Dynamic / updates on its own" is satisfied by the view
  refreshing on a short fixed interval of roughly 2–3 seconds (clarified) rather than an
  instantaneous push; the exact request mechanism is an implementation choice for planning.
- **Scope is the web UI**: The monitor is a web-UI concern surfaced when opening a running
  queue. Backend support to expose the running queue's live plan (order, schedule reasons,
  expected times, current step) is in scope as the data source for the view; automation
  behavior of the run itself is unchanged.
- **Time presentation**: Times are shown in a single, clearly indicated frame of reference
  to avoid the known ambiguity between the service's local clock and other zones.
