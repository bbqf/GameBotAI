# Feature Specification: Smart Scheduling

**Feature Branch**: `189-smart-scheduling`  
**Created**: 2026-11-13  
**Status**: Implemented  
**Input**: User description: "Let's implement smart scheduling. Smart scheduling should minimize the number of wakeups of an idling queue and at the same time provide a reasonable balance between maximizing the free rewards and longer idle times. On the queue level it should be possible to activate smart scheduling (default off). What it means is that when a sequence reschedules itself for a given time, scheduler should check if there's already something scheduled within certain time from the desired, I.e. at or later, it is allowed to postpone the scheduled time. Default time window for smart scheduling should be 15 min, but check the logs if another window make more sense. This time should be also configurable on the queue level when smart scheduling is enabled. Sequences should be able to be configured to ignore smart scheduling, this should be off by default, but set on a sequence level, if a certain action is time critical, e.g. setting a new shield as close as possible to the time the existing one expires."

## Background

A sequence that runs in a queue can book its next firing with the `reschedule-self` action (Timer option). Each sequence books its own time. The times of different sequences do not align. Thus an idle queue wakes up many times, and each wake-up does little work.

The execution logs show the cost. The logs of 2026-11-06 to 2026-11-13 have 15 queue runs of approximately one day each, on four production queues (14.3 queue-days, 1 220 wake-ups). The numbers are:

- A queue wakes up 85 times each day (mean). The median wake-up runs 3 sequences.
- 55% of the idle gaps between two wake-ups are 10 minutes or less. 65% are 15 minutes or less.
- Each wake-up brings the game back to the foreground and runs the Before Each Run entries. Short idle gaps also give the idle pause little time.

A simulation on the same logs shows the effect of a window. The simulation merges wake-ups that start in one window. It is an estimate, because a moved firing also moves the bookings that follow it.

| Window | Wake-ups for each queue-day | Decrease |
|--------|-----------------------------|----------|
| Off    | 85                          | 0%       |
| 5 min  | 72                          | 15%      |
| 10 min | 58                          | 32%      |
| 15 min | 47                          | 44%      |
| 20 min | 41                          | 52%      |
| 30 min | 32                          | 63%      |
| 60 min | 19                          | 77%      |

The curve has no sharp bend. A window of 15 minutes removes almost half of the wake-ups, and the maximum delay of a reward stays at 15 minutes. A window of 20 minutes adds only 8 points for 5 more minutes of delay. Many sequences already use a retry time of 15 minutes. Thus the logs confirm 15 minutes as the default window.

## Clarifications

### Session 2026-11-13

- Q: Does a new booking also move the bookings that are already pending? → A: No. Only the new booking moves. The result can depend on the order of the bookings. *Rationale: the rule stays simple, and a booked time never changes after the booking.*
- Q: When does a change of the queue settings take effect on a queue that runs? → A: Immediately. Each booking that is made after the save uses the new settings. The bookings that are already made keep their times. *Rationale: a queue restart drops all bookings, so the operator must be able to tune the window with no restart.*
- Q: Does the scheduler also postpone template Timer entries with a relative offset? → A: No. Only `reschedule-self` Timer bookings move. Template Timer entries (relative offset and time of day) and live schedules stay on their times. They are targets only. *Rationale: a template time is a time that the operator set, and a postponement of a repeating entry would move all its later firings.*
- Q: What happens to the "ignore smart scheduling" setting when a full sequence update does not have it? → A: The sequence keeps the stored value. *Rationale: a client that does not know the setting must not remove the mark of a time-critical sequence.*
- Q: Does the phone view show that smart scheduling postponed a firing? → A: Yes. The schedule row shows the booked time and the text "postponed from HH:mm", on the phone and on the large screen.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Merge near wake-ups of a queue (Priority: P1)

An operator turns on smart scheduling for a queue. When a sequence books its next firing for a time T, the scheduler looks for a firing that is already pending in the same queue run at T or later, not later than T plus the window. If there is one, the scheduler postpones the new booking to the time of that firing. The two firings then run in one wake-up. If there is none, the booking keeps the time T.

**Why this priority**: This is the full value of the feature. It decreases the number of wake-ups and gives the queue longer idle times.

**Independent Test**: Turn on smart scheduling for a queue with the default window. Let sequence A have a pending firing at 12:10. Let sequence B book itself for 12:00. Make sure that the booking of B is at 12:10 and that A and B run in the same wake-up.

**Acceptance Scenarios**:

1. **Given** a queue with smart scheduling on and a window of 15 minutes, and a pending firing of sequence A at 12:10, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:10.
2. **Given** the same queue, and a pending firing of sequence A at 12:20, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:00, because 12:20 is not in the window.
3. **Given** the same queue, and a pending firing of sequence A at 11:55, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:00. The scheduler never moves a booking to an earlier time.
4. **Given** the same queue, and pending firings at 12:04 and at 12:12, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:04 (the earliest firing in the window).
5. **Given** the same queue, and a pending firing at exactly 12:15, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:15. The end of the window is included.
6. **Given** the same queue, and no other pending firing, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:00.
7. **Given** the same queue, and a template Timer entry with its next firing at 12:08, **When** sequence B books itself for 12:00, **Then** the booking of B has the time 12:08.
8. **Given** a queue with smart scheduling off, **When** a sequence books itself for a time, **Then** the booking has exactly that time, as before this feature.

---

### User Story 2 - Configure smart scheduling on the queue (Priority: P1)

An operator turns smart scheduling on or off for each queue. When it is on, the operator can set the window. A new queue and each existing queue have smart scheduling off.

**Why this priority**: Without the queue setting, the operator cannot use the feature. The default must not change the behaviour of the existing queues.

**Independent Test**: Open the settings of a queue. Turn on smart scheduling. Make sure that the window shows 15 minutes. Change the window to 20 minutes, save, and load the queue again. Make sure that the two values are stored.

**Acceptance Scenarios**:

1. **Given** a queue that was saved before this feature, **When** the operator opens its settings, **Then** smart scheduling is off.
2. **Given** a queue with smart scheduling off, **When** the operator turns it on and does not set a window, **Then** the window is 15 minutes.
3. **Given** a queue with smart scheduling on, **When** the operator sets the window to 20 minutes and saves, **Then** the queue keeps 20 minutes. If the queue runs, each booking that is made after the save uses 20 minutes, and the bookings that are already made keep their times.
4. **Given** a queue with smart scheduling on, **When** the operator sets a window that is out of the permitted range, **Then** the save is rejected with a message that gives the permitted range.
5. **Given** a queue with smart scheduling on and a window of 20 minutes, **When** the operator duplicates the queue, **Then** the copy has smart scheduling on and a window of 20 minutes.
6. **Given** a queue with smart scheduling on and a window of 20 minutes, **When** the operator turns it off and then on again, **Then** the window is 20 minutes.

---

### User Story 3 - Keep a time-critical sequence on its exact time (Priority: P2)

A sequence author marks a sequence as time-critical, for example a sequence that sets a new shield immediately when the old shield expires. The scheduler never postpones a booking of this sequence. The mark is off by default.

**Why this priority**: Without this mark, an operator cannot turn on smart scheduling on a queue that has one time-critical task. The feature is still of value without it on queues that have no such task.

**Independent Test**: Mark sequence S to ignore smart scheduling. Turn on smart scheduling for the queue. Let sequence A have a pending firing at 12:10. Let S book itself for 12:00. Make sure that the booking of S is at 12:00.

**Acceptance Scenarios**:

1. **Given** a queue with smart scheduling on, a pending firing at 12:10, and a sequence S that ignores smart scheduling, **When** S books itself for 12:00, **Then** the booking of S has the time 12:00.
2. **Given** the same queue, and a pending firing of S at 12:10, **When** sequence B (no mark) books itself for 12:00, **Then** the booking of B has the time 12:10. A firing of a time-critical sequence is a valid target for other bookings.
3. **Given** a sequence that was saved before this feature, **When** the author opens it, **Then** the mark is off.
4. **Given** a sequence with the mark on, **When** it runs in a queue with smart scheduling off, or runs with no queue, **Then** the mark has no effect.

---

### User Story 4 - See what the scheduler did (Priority: P3)

An operator can see when the scheduler postponed a booking. The execution log of the booking step gives the requested time, the booked time, and the target firing. The queue monitor shows the booked time.

**Why this priority**: The operator must be able to explain why a task ran later than its own timer. The feature works without this, but a problem is then hard to find.

**Independent Test**: Cause a postponed booking as in User Story 1. Open the execution log of the booking step. Make sure that it shows the requested time, the booked time, and the name of the target sequence. Open the queue monitor. Make sure that the up-next list shows the booked time.

**Acceptance Scenarios**:

1. **Given** a booking that the scheduler postponed from 12:00 to 12:10 to the firing of sequence A, **When** the operator opens the execution log of the booking step, **Then** the log shows the requested time 12:00, the booked time 12:10, and sequence A as the target.
2. **Given** the same booking, **When** the operator opens the queue monitor, **Then** the up-next list shows the firing at 12:10 and shows that smart scheduling postponed it.
3. **Given** a booking that the scheduler did not postpone, **When** the operator opens the execution log of the booking step, **Then** the log is the same as before this feature.

### Edge Cases

- The target firing goes away after the postponement (its sequence books a new time, a Cancel removes it, or a live template change removes it): the postponed booking keeps its booked time. The scheduler does not calculate the booking again.
- A sequence books a new time while its own booking is pending: its own pending booking is not a target. The new booking replaces it, as today.
- A sequence has a template Timer entry or a live schedule of its own in the window: these are not targets. A sequence never moves to a firing of itself.
- A booking has `keep: earliest`: the scheduler first applies smart scheduling, then compares the booked times.
- A booking is the only pending firing of the run: it keeps its requested time.
- A booking is made with the option At Queue Start, Once Per Run, After Every Step, or Cancel: these options have no time. Smart scheduling does not apply.
- A booking uses a time of day or an OCR offset: the scheduler first calculates the requested time as today, then applies smart scheduling to that time.
- Two sequences book in the same wake-up, B for 12:00 and then C for 12:05, and no other firing is pending: B stays at 12:00, C stays at 12:05 (the booking of B is earlier than 12:05). If C books first, B moves to 12:05. The result depends on the order of the bookings. This is permitted.
- A postponed booking is a target for a later booking of a different sequence. The later booking moves a maximum of one window from its own requested time.
- The window crosses local midnight or a change of daylight saving time: the scheduler compares absolute instants. The window is a real duration.
- The queue is paused (operator pause or failure policy pause) at the booked time: the firing stays due and runs after the resume, as today.
- The operator changes the queue settings while the queue runs: the bookings that are already made keep their times. Each later booking uses the new settings, with no queue restart.
- A sequence runs with no queue: the booking is a no-op with success, as today.

## Requirements *(mandatory)*

### Functional Requirements

**Queue settings**

- **FR-001**: Each queue MUST have a smart scheduling setting that is on or off. The default MUST be off, for new queues and for queues that were saved before this feature.
- **FR-002**: Each queue MUST have a smart scheduling window. The default MUST be 15 minutes. The window MUST be a whole number of minutes from 1 to 120.
- **FR-003**: The system MUST reject a queue save with a window out of the permitted range, with a message that gives the range.
- **FR-004**: The system MUST keep the window value when smart scheduling is off. The window has an effect only when smart scheduling is on.
- **FR-005**: The operator MUST be able to read and change the two settings in the same places as the other queue settings (the queue settings in the web UI and the queue interface for external tools).
- **FR-006**: A duplicate of a queue MUST have the smart scheduling settings of its source queue.
- **FR-007**: A change of the settings MUST apply immediately to a queue that runs, with no queue restart. Each booking that is made after the save MUST use the new settings. A change MUST NOT move a booking that is already made.
- **FR-007a**: The edit page of a running queue shows the note "Settings apply at the next start." (feature 173, FR-019). This page MUST also say that the smart scheduling settings apply to the next booking.

**Sequence setting**

- **FR-008**: Each sequence MUST have an "ignore smart scheduling" setting that is on or off. The default MUST be off, for new sequences and for sequences that were saved before this feature.
- **FR-009**: The author MUST be able to read and change this setting in the same places as the other sequence settings. The setting MUST stay with the sequence when the sequence is exported or imported. A sequence update that does not have the setting MUST keep the stored value.

**Scheduling rule**

- **FR-010**: The rule applies when a sequence books a timed firing of itself in a queue run (the Timer option of `reschedule-self`), the queue has smart scheduling on, and the sequence does not ignore smart scheduling. The system first calculates the requested time T as today.
- **FR-011**: The system MUST look for the pending firings of the same queue run that have a due time from T to T plus the window (the two ends included). If there is one or more, the system MUST book the firing at the due time of the earliest of them. If there is none, the system MUST book the firing at T.
- **FR-012**: The pending firings that are valid targets MUST be: the timed bookings of other sequences (postponed or not, time-critical or not), the next firing of each enabled template Timer entry (time of day, daily retry, relative offset), and the pending live schedules.
- **FR-013**: These MUST NOT be targets: each pending firing of the same sequence (its booking, its template Timer entry, and its live schedule), the Before Each Run entries, the After Every Step entries, the entries with no time (At Queue Start, Once Per Run), and the firings of other queues.
- **FR-014**: The booked time MUST NOT be earlier than T and MUST NOT be later than T plus the window.
- **FR-015**: The system MUST apply the rule one time, when the booking is made. The system MUST NOT move the booking later when the target changes or goes away.
- **FR-016**: The system MUST NOT move the target firing. Only the new booking moves.
- **FR-017**: When the queue has smart scheduling off, or the sequence ignores smart scheduling, the booking MUST have exactly the time T, as before this feature.
- **FR-018**: Smart scheduling MUST NOT change the template Timer entries, the live schedules, the run-next requests, and the bookings with the options At Queue Start, Once Per Run, After Every Step, and Cancel.
- **FR-019**: The other booking rules MUST stay as today: the last booking of a sequence wins, and `keep: earliest` compares the booked times after the smart scheduling rule.

**Visibility**

- **FR-020**: When the system postpones a booking, the execution log of the booking step MUST show the requested time, the booked time, and the target firing (the name of its sequence or entry). A booking with a target at exactly the requested time does not move. It is not a postponement, and its log is the same as before this feature.
- **FR-021**: The queue monitor MUST show the booked time of a postponed firing and MUST show that smart scheduling postponed it. The schedule rows of the queue detail MUST show the same, on the large screen and on the phone.
- **FR-022**: The queue monitor MUST show if smart scheduling is on for the queue that runs, and its window.
- **FR-023**: The user documentation MUST describe the queue settings, the sequence setting, the rule, and the maximum delay.

**Web UI**

- **FR-024**: The phone support level of this feature is `reduced`, the level of the Queues area, and the feature MUST follow docs/mobile-ui-principles.md. The phone view MUST show the booked time of each pending firing and the text "postponed from HH:mm" for a postponed firing. The operator edits the queue settings on a large screen, as for the other queue settings. The sequence setting is in the Authoring area, which is `desktop-only`.

### Key Entities

- **Queue smart scheduling settings**: two values of a queue: on or off (default off), and the window in minutes (default 15).
- **Sequence "ignore smart scheduling" setting**: one value of a sequence: on or off (default off). When on, the bookings of this sequence always keep the requested time.
- **Booking**: one pending timed firing that a sequence made for itself in the current queue run. A booking has a requested time and a booked time. The two are different only when smart scheduling postponed the booking.
- **Target firing**: a pending firing of the same queue run to which a booking is postponed.
- **Window**: the maximum time that the scheduler can postpone a booking.
- **Wake-up**: in this spec, a period in which a queue runs one or more sequences after an idle gap of more than 30 seconds. This is the unit that the log analysis counts. Feature 188 uses the same word for one iteration of the scheduler loop.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a production queue with smart scheduling on and a window of 15 minutes, the number of wake-ups for each day decreases by a minimum of 30% compared with the same queue before (85 wake-ups for each day in the logs of 2026-11-06 to 2026-11-13).
- **SC-002**: 100% of the postponed bookings have a booked time that is not earlier than the requested time and not later than the requested time plus the window.
- **SC-003**: 100% of the bookings of a sequence that ignores smart scheduling have a booked time equal to the requested time.
- **SC-004**: A queue with smart scheduling off has the same booked times, the same execution order, and the same execution log content as before this feature. 0 existing queues and 0 existing sequences need a change.
- **SC-005**: An operator can turn on smart scheduling for a queue and set the window in less than 1 minute on a large screen.
- **SC-006**: For each postponed booking, an operator can find the requested time, the booked time, and the target in the execution log, with no other tool.
- **SC-007**: The share of idle gaps of 10 minutes or less between two wake-ups decreases from 55% to less than 35% on a production queue with the default window.

## Assumptions

- "A sequence reschedules itself" means a `reschedule-self` action with the Timer option. Template Timer entries and live schedules have times that the operator set. Thus they are targets, but the scheduler does not postpone them (confirmed in the Clarifications section).
- The earliest firing in the window is the target. A later target gives the same number of wake-ups and a longer delay. Thus the earliest target gives the best balance between rewards and idle time.
- The rule runs one time for each booking. This keeps the maximum delay at one window and keeps the behaviour easy to explain. A booking can stay alone when its target goes away. This is accepted.
- The permitted range of 1 to 120 minutes is a sanity limit. The logs show that a window of 60 minutes already removes 77% of the wake-ups.
- The "ignore smart scheduling" setting is a property of the sequence, as the user description says. It is not a property of one `reschedule-self` step or of one template entry.
- The simulation in the Background section is an estimate. SC-001 thus uses 30%, not the simulated 44%.
- Bookings stay ephemeral, as today. A booking and its postponement do not stay after the queue run ends or the service restarts.
- The idle pause, the liveness gate, the Before Each Run pass (feature 188), and the failure policy do not change. Fewer wake-ups give the idle pause longer holds with no change of its rules.
