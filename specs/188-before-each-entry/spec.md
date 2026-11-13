# Feature Specification: Before Each Run Before Every Firing

**Feature Branch**: `188-before-each-entry`  
**Created**: 2026-11-13  
**Status**: Implemented  
**Input**: GitHub issue #393 (https://github.com/bbqf/GameBotAI/issues/393). Make `BeforeEachRun` run before every task entry that fires, also when several entries are due in the same wake-up. No new schedule type and no new flag: the existing `BeforeEachRun` changes its behaviour. Closes #393

## Background

Feature 095 (#202, #208) added the `BeforeEachRun` schedule type. Today the Before Each Run pass runs at most one time in each wake-up of the scheduler loop. It runs immediately before the first triggering firing of that wake-up. When several triggering firings are due in one wake-up, only the first firing gets a pass.

The establisher of the gamepack brings the game to a known start screen. Each task starts from that screen. When one task ends on a different screen, each later task of the same wake-up fails at its start guard. The next wake-up recovers.

The execution logs show the cost. From 2026-11-04 to 2026-11-11, about 17 runs of one task ended with a named failure on a wrong screen, on four queues. After almost each of these runs, 4 to 10 later tasks of the same wake-up failed. On 2026-11-11, three different tasks caused four more cases on three queues, with 3 to 10 failed tasks each. After a queue restart, the full roster of about 20 entries is due in one wake-up. Thus one bad end can fail most of the roster.

The workaround is a return chain in each task, for each failure path. This workaround is fragile. A watchdog cancel, a popup, or a slow screen can stop a run at any step, and each chain is new code.

## Clarifications

### Session 2026-11-13

- Q: Does a pass run before a triggering firing also when the previous firing of the same wake-up ended with success? → A: Yes. The pass runs before each triggering firing, with no condition on the result of the previous firing. *Rationale: the issue shows that a successful task can also end on a wrong screen (a last BACK press that opened a dialog). The system cannot know the screen state.*
- Q: Which firings get a pass? → A: The same set as today: Timer firings (time of day, daily retry, relative offset), live schedules, self-rescheduled firings, next-cycle-start firings, and run-next requests. Only the "once per wake-up" limit goes away. *Rationale: the issue asks for a change of frequency, not of scope. Feature 184 already gives run-next requests a pass.*
- Q: Does the queue monitor show the Before Each Run entry one time, or one time for each due firing? → A: One time, as today. *Rationale: the monitor lists entries, not executions. The issue asks for no monitor change.*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish the start screen before each due task (Priority: P1)

An operator has a queue template with Before Each Run entries (the establisher) and several timed or self-rescheduled tasks. When several tasks are due in one wake-up, the Before Each Run entries run again before each task. Thus a task that ends on a wrong screen does not cause the next task to fail.

**Why this priority**: This is the full value of the issue. It removes the chain of failures after one bad task end.

**Independent Test**: Make a template with one Before Each Run entry B and three Timer entries T1, T2, T3 that are co-due firings. Run the queue. Make sure that the execution order is B, T1, B, T2, B, T3.

**Acceptance Scenarios**:

1. **Given** a queue that runs, with Before Each Run entry B and three triggering firings T1, T2, T3 that are due in one wake-up, **When** the queue wakes up, **Then** the execution order is B, T1, B, T2, B, T3. B runs three times.
2. **Given** a template with Before Each Run entries B1 then B2 (template order) and two triggering firings T1 and T2 that are due in one wake-up, **When** the queue wakes up, **Then** the execution order is B1, B2, T1, B1, B2, T2.
3. **Given** a template with Before Each Run entry B, an After Every Step entry E, and two triggering firings T1 and T2 that are due in one wake-up, **When** the queue wakes up, **Then** the execution order is B, T1, E, B, T2, E.
4. **Given** a wake-up with only one triggering firing T, **When** the queue wakes up, **Then** the execution order is B, T, as before this feature.
5. **Given** a wake-up that has a mix of triggering firings (a time-of-day Timer, a relative Timer, a live schedule, and a self-rescheduled firing), **When** these firings are due in one wake-up, **Then** a full Before Each Run pass runs immediately before each of them.

---

### User Story 2 - Keep the failure accounting and the run log as today (Priority: P1)

A failure of a Before Each Run entry stays counted and non-fatal (#208). The per-step run log shows each pass as its own sequence node, as today.

**Why this priority**: Operators read the counts and the run log to find problems. More passes must not change what a count or a log node means.

**Independent Test**: Make a Before Each Run entry that fails, and two triggering firings that are due in one wake-up. Run the queue. Make sure that the failed total increases by two, that the run continues, that both firings run, and that the run log shows two Before Each Run sequence nodes.

**Acceptance Scenarios**:

1. **Given** a Before Each Run entry B that fails and two triggering firings T1 and T2 in one wake-up, **When** the queue wakes up, **Then** B fails two times, the failed total increases by two, the run continues, and T1 and T2 both run.
2. **Given** two triggering firings in one wake-up, **When** the queue wakes up, **Then** the executed total counts T1 and T2 only. Before Each Run executions do not count toward the executed total.
3. **Given** two triggering firings in one wake-up, **When** the operator opens the per-step run log, **Then** the log shows each Before Each Run execution as its own sequence node, immediately before the node of its triggering firing.

---

### User Story 3 - Existing templates work with no change (Priority: P2)

An operator does not edit a template to get the new behaviour. Templates with no Before Each Run entry run exactly as before.

**Why this priority**: The issue forbids a new flag and a template change. The behaviour change applies to all queues at once.

**Independent Test**: Load a template that was saved before this feature. Run it. Make sure that the template loads with no change and that the new behaviour applies.

**Acceptance Scenarios**:

1. **Given** a template that was saved before this feature, **When** the queue runs, **Then** the template loads with no change, and the Before Each Run entries run before each triggering firing.
2. **Given** a template with no Before Each Run entry, **When** the queue runs, **Then** the execution order is the same as before this feature.

### Edge Cases

- The liveness gate holds a triggering firing: no Before Each Run pass runs for the held firing, as today. A later firing of the same wake-up that is held in hold-only mode also gets no pass.
- An operator pause request arrives between two firings: the loop stops at the operator gate before the next pass. After the resume, the next pass and its triggering firing run.
- A live template change is applied while the loop waits at the operator gate: the rest of the wake-up runs no firing and no pass, as today.
- The run is stopped while a Before Each Run entry runs: the stop takes effect as for each other firing, and the triggering firing does not run.
- Template OncePerRun steps, template AtQueueStart entries, After Every Step entries, and After Every Step self-reschedule injections do not trigger a pass, as today.
- A Before Each Run sequence calls `reschedule-self`: the booking has its normal semantics. A pass never triggers another pass and never triggers an After Every Step pass.
- A disabled Before Each Run entry does not run, as today.
- A template with Before Each Run entries but no triggering firing in a wake-up: no pass runs in that wake-up.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST run the full Before Each Run pass immediately before each triggering firing that runs. This rule applies also when several triggering firings are due in one wake-up. This requirement replaces FR-003 of feature 095 ("at most once per wake-up").
- **FR-002**: The set of triggering firings MUST stay as in feature 095 and feature 184: time-of-day Timer firings, daily retries of those firings, relative-offset Timer firings, live-schedule firings, self-rescheduled firings that are booked with the Timer, AtQueueStart, or OncePerRun option, firings from the next-cycle-start register, and run-next requests.
- **FR-002a**: The pass MUST run before each triggering firing, with no condition on the result or the end screen of the previous firing.
- **FR-003**: Each pass MUST run the enabled Before Each Run entries in template order.
- **FR-004**: When an After Every Step pass follows a triggering firing, that After Every Step pass MUST run before the Before Each Run pass of the next triggering firing.
- **FR-005**: A Before Each Run execution MUST NOT count toward the executed total of the run. A failed execution MUST count toward the failed total, MUST be non-fatal, and MUST NOT stop the triggering firing (#208). Each execution of each pass MUST be counted separately.
- **FR-006**: The per-step run log and the cycle ledger MUST show each Before Each Run execution as its own sequence node, as today.
- **FR-007**: A firing that the liveness gate holds MUST NOT get a Before Each Run pass, as today.
- **FR-007a**: The queue monitor MUST list each enabled Before Each Run entry one time, as today.
- **FR-008**: The system MUST NOT add a new schedule type, a new flag, or a new template option. Stored templates MUST load and run with no change.
- **FR-009**: Templates with no Before Each Run entry MUST run exactly as before this feature. All other schedule types and the `reschedule-self` booking semantics MUST behave as before.
- **FR-010**: Text that describes the Before Each Run behaviour MUST describe the new behaviour. This includes the API contract descriptions, the code comments, the queue monitor text, and the docs. This text MUST NOT say "at most once per wake-up".

### Key Entities

- **Before Each Run pass**: the ordered execution of all enabled Before Each Run entries. With this feature, one pass runs immediately before each triggering firing that runs.
- **Triggering firing**: a timed, live-scheduled, self-rescheduled, next-cycle-start, or run-next firing, as FR-002 lists.
- **Wake-up**: one iteration of the scheduler loop of a run.
- **Co-due firings**: two or more triggering firings that are due in the same wake-up.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When N triggering firings run in one wake-up, the Before Each Run pass runs exactly N times, one time immediately before each firing.
- **SC-002**: A task that ends on a wrong screen causes 0 start-guard failures in the later tasks of the same wake-up, if the establisher can bring the game back to the start screen.
- **SC-003**: A wake-up with one triggering firing has the same execution order and the same counts as before this feature.
- **SC-004**: 0 existing templates need a change to get the new behaviour.
- **SC-005**: All existing queue scheduling tests pass, except the tests that check the old "once per wake-up" rule. These tests change to check the new rule.

## Assumptions

- The issue accepts the cost: about 10 s for each additional pass, and about 3 minutes more for a full roster of about 20 entries after a queue restart.
- "Wake-up" has the same meaning as in feature 095: one iteration of the scheduler loop.
- The set of triggering firings does not change. Only the "once per wake-up" limit goes away.
- The web UI shows only the label "Before each run" and no text about the once-per-wake-up limit. Thus the web UI needs no change, and the feature has no phone support impact.
