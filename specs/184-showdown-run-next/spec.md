# Feature Specification: Run Next for Unscheduled Tasks

**Feature Branch**: `184-showdown-run-next`  
**Created**: 2026-11-09  
**Status**: Implemented  
**Input**: User description: "We need a better way to deal with the found showdown tasks. Currently the task reports a failure and stops. Reschedule is not possible without a queue restart. I want to be able to manually restart or schedule a task, once it has to run. This must work for any task that is active in the template but not scheduled to run. This must work from the mobile and the desktop UI. I can imagine a button like 'run next' next to the sequence in the template. Use icons like in the queues list for all buttons in the editor page, so that sequences do not use so many lines on the screen."

## Clarifications

### Session 2026-11-09

- Q: What does "showdown" mean? → A: It has no special meaning. The feature covers any entry that is enabled but has no pending run, for any cause.
- Q: When must a queued entry run? → A: Once, right after the running sequence ends, or as the next sequence if none runs.
- Q: What state has an at-queue-start, before-each-run, or every-step entry? → A: Idle after it ran once in the run.
- Q: What does the editor of a stopped queue show? → A: Run next, off, with the reason "Start the queue to use Run next".
- Q: How must the system record a Run next request? → A: A log line for the request with its time. The execution log gets no new label. The firing is a normal firing.
- Q: How must FR-008 treat the "Scheduled" text on the phone row? → A: "Scheduled" is one of the four states that the UI shows.
- Q: Must Run next work on a stopped queue? → A: No. It works only while the queue runs or is paused. A stopped queue uses its normal start.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run an unscheduled task next (Priority: P1)

A queue is running. One template entry is enabled, but it has no pending run. This can happen after
the entry failed and stopped, or after its timer was used and no new timer was booked. Today the
operator must restart the whole queue to run the entry again. The operator wants one control, next
to the entry, that puts the entry in the run list so that it runs next.

**Why this priority**: This is the main problem. Without it, one failed entry forces a queue restart.
A restart also loses the state of all other entries.

**Independent Test**: Start a queue with an enabled entry that has no pending run. Press **Run next**
for the entry. Check that the entry runs after the current sequence ends, and that no other entry
changes.

**Acceptance Scenarios**:

1. **Given** a running queue and an enabled entry with no pending run, **When** the operator presses **Run next**, **Then** the entry runs after the current sequence. The entry shows "Queued to run next".
2. **Given** an entry that is already queued to run next, **When** the operator presses **Run next** again, **Then** the entry does not run twice. The operator sees that the entry is already queued.
3. **Given** a paused queue, **When** the operator presses **Run next** for an entry, **Then** the entry is queued. It runs when the operator resumes the queue.
4. **Given** a queue that is not running, **When** the operator looks at the entries in the editor, **Then** **Run next** is off. It shows the reason "Start the queue to use Run next".

---

### User Story 2 - Use Run next from a phone (Priority: P1)

The operator is away from the desk. The operator opens the queue on a phone and sees that an entry
stopped. The operator must be able to run the entry again from the phone, with no desktop.

**Why this priority**: The request names the phone UI as a requirement. Phone use is the most common
case when the operator is away from the desk.

**Independent Test**: Open the queue detail at 375 px width. Find an enabled entry with no pending run.
Press **Run next**. Check that the entry is queued, and that the screen has no sideways scroll.

**Acceptance Scenarios**:

1. **Given** the phone queue detail of a running queue, **When** an entry is idle, **Then** the row shows a **Run next** control. The control is at least 44 x 44 px.
2. **Given** the phone queue detail, **When** the operator presses **Run next**, **Then** the entry is queued. The row shows "Queued to run next".
3. **Given** the connection is lost, **When** the operator looks at the control, **Then** the control is off. The UI does not send the action again after the connection returns.

---

### User Story 3 - Compact icon buttons in the editor (Priority: P2)

In the template editor, each sequence row has many text buttons and fields. Each row uses many lines
on the screen. The operator wants icon buttons, like the icon buttons in the queues list, so that
each sequence row is short.

**Why this priority**: This is a layout improvement. It makes **Run next** fit in the row, but the
operator can use Run next without it.

**Independent Test**: Open the template editor with ten sequences. Check that each row shows icon
buttons with a name and a tooltip, and that each row uses fewer lines than before.

**Acceptance Scenarios**:

1. **Given** the template editor, **When** the operator looks at a sequence row, **Then** each action shows as an icon button. Examples are Parameters, Remove, and Run next. The style is the same as the queues list.
2. **Given** an icon button, **When** the operator points at it or uses a screen reader, **Then** the button gives a text name. The name says what the button does.
3. **Given** a row at desktop width, **When** the row shows all its controls, **Then** the row fits on one line. It never uses more lines than before this change.

---

### Edge Cases

- The entry is disabled in the template: **Run next** is not available for it.
- The entry is running now: **Run next** is not available. The row shows "Running".
- The entry is already scheduled (it has a pending timer): **Run next** is not available. The operator can use the timer instead.
- The queue stops before the queued entry runs: the queued request ends with the queue. The request does not remain for the next start.
- Two operators press **Run next** for the same entry at the same time: the entry runs once.
- The entry needs parameters: the entry runs with the same parameter values that the template sets for it.
- The entry fails again: the failure shows as before, and the entry can be queued again.
- The template has a change that is not yet applied: **Run next** uses the entry as the running queue has it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let the operator queue one run of a template entry to run next. This works while the queue is running or paused. It needs no queue restart.
- **FR-002**: **Run next** MUST be available for each entry that is enabled in the template and has no pending run.
- **FR-003**: **Run next** MUST NOT be available when the entry is disabled, runs now, or has a pending run. It MUST NOT be available when the queue is not running. The UI MUST show the reason as text. A tooltip alone is not enough.
- **FR-004**: A queued entry MUST run once, right after the sequence that runs now ends. If no sequence runs, the entry MUST run as the next sequence. A second request for the same queued entry MUST NOT add a second run.
- **FR-005**: An entry that runs because of **Run next** MUST use the same parameter values as a normal run of the entry.
- **FR-006**: The run of a queued entry MUST NOT change the schedule of other entries, and MUST NOT reset the state of the queue.
- **FR-007**: A queued request MUST end when the queue stops. It MUST NOT remain for a later start.
- **FR-008**: The UI MUST show the state of each entry: "Queued to run next", "Running", "Scheduled", or no mark.
- **FR-009**: **Run next** MUST work in the desktop UI and in the phone UI.
- **FR-010**: The system MUST write a log line for each accepted **Run next** request. The line has the queue, the entry, and the request time.
- **FR-011**: All action buttons in the template editor MUST show as icon buttons in the same style as the queues list. Each icon button MUST have an accessible text name and a tooltip.
- **FR-012**: The change of the editor buttons MUST NOT remove any function that the editor has today.
- **FR-013** *(web UI change)*: The feature MUST state its phone support level and follow docs/mobile-ui-principles.md. The phone support level of **Run next** is `full`. The template editor stays `desktop-only`.
- **FR-014**: All new UI text MUST be in Simplified Technical English.

### Key Entities

- **Template entry**: A sequence in the queue template, with an enabled state, a schedule, and parameter values.
- **Run-next request**: A one-time request to run one template entry after the current sequence. It belongs to one running queue. It ends when the entry runs or when the queue stops.
- **Entry state**: The state of an entry in a running queue: running, queued to run next, scheduled, idle (enabled with no pending run), or disabled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can run a stopped entry again in 2 taps or clicks, with no queue restart.
- **SC-002**: After a **Run next** request, the entry starts when the running sequence ends, or at once if none runs. The entry runs once in 100% of tests.
- **SC-003**: The phone queue detail shows the control at 320 px to 768 px width. The page has no sideways scroll. Each control is at least 44 x 44 px.
- **SC-004**: In the template editor at 1024 px width, a sequence row uses one line. Its height is at most half of the old height. A browser test measures the height.
- **SC-005**: Each icon button in the editor has a text name for screen readers, in 100% of buttons.
- **SC-006**: No editor function is lost: all existing editor tests pass without change to what they check.

## Assumptions

- The feature covers any entry that is enabled in the template but not scheduled to run. The cause can be a failure, a used timer, or other. The cause does not change the feature.
- "Run next" means the entry runs after the sequence that runs now. It does not interrupt the running sequence.
- An at-queue-start, before-each-run, or every-step entry is idle after it ran once in the run. Before its first run, it is scheduled.
- The control is available only while the queue is running or paused. A stopped queue starts all enabled entries by its normal start.
- One queued run for each entry at a time is enough. The operator can press the control again after the entry ran.
- The "queues list" icon buttons are the reference style. The feature reuses their look, size, and tooltip rules.
- The phone UI has no template editor. On a phone, **Run next** shows in the queue detail schedule list, next to each template entry.
