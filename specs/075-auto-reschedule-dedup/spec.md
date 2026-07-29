# Feature Specification: Deduplicate Self-Rescheduled Sequence Firings

**Feature Branch**: `075-auto-reschedule-dedup`
**Created**: 2026-07-29
**Status**: Implemented
**Input**: User description: "We need to make sure auto-rescheduling of the sequences doesn't create duplicates: i.e. if a sequence wants to reschedule itself and there's another auto-rescheduled entry in the queue already, the old one should be deleted and the new one scheduled. This behaviour does not affect other types of scheduled sequences."

## Clarifications

### Session 2026-07-29

- Q: Which self-reschedule kinds are treated as "auto-rescheduled" and subject to dedup? → A: Only future-timed self-reschedule firings (the kind that request the sequence run again at a specific later time). Rationale: the request describes an entry already "in the queue" awaiting a future point; the immediate/cycle-bound self-reschedule kinds (run again this cycle, at next cycle start, after every step) are the "other types" the request explicitly excludes, and after-every-step is already deduplicated per sequence by design.
- Q: What identifies a duplicate for the dedup? → A: The sequence id, scoped to the current queue run. A sequence re-arming itself is matched by its own sequence id; two different sequences never collide.
- Q: Does a self-rescheduled future firing also replace a template-defined timer (or live schedule) for the same sequence? → A: No. Dedup is only among self-rescheduled future firings; template-defined timers, once-per-run/after-every-step/next-cycle-start registrations, and externally requested live schedules are left entirely unchanged (FR-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A self-rescheduling sequence never stacks duplicate future firings (Priority: P1)

Many daily automation sequences reschedule themselves to run again later — for example, a task that reads an in-game cooldown and asks to run again once the cooldown expires, or a periodic "collect" task that re-arms itself for a fixed interval. When such a sequence runs, completes, and requests a future firing of itself, the queue should hold **exactly one** pending self-rescheduled firing for that sequence: the newest request. If a self-rescheduled firing for that same sequence was already pending, it is discarded in favor of the new one.

Without this, a sequence that runs more than once before its earlier self-rescheduled firing comes due (because the queue was restarted, the sequence was also triggered by another schedule, or the same task self-reschedules on more than one path) accumulates several pending firings and then executes redundantly — wasting run time and, in the worst case, spending in-game resources more often than intended.

**Why this priority**: This is the entire point of the feature. It directly prevents redundant and resource-wasting duplicate executions of automated daily tasks, which is the observed problem.

**Independent Test**: Run a sequence configured to self-reschedule for a future time twice in succession within the same queue run, then inspect the queue's pending future firings. Exactly one pending firing for that sequence must remain, targeting the most recently requested time. Delivers value on its own: duplicate future firings can no longer form.

**Acceptance Scenarios**:

1. **Given** a running queue with no pending self-rescheduled firing for sequence S, **When** sequence S runs and requests a future self-reschedule, **Then** the queue holds exactly one pending self-rescheduled firing for S at the requested time.
2. **Given** a running queue that already holds one pending self-rescheduled firing for sequence S at time T1, **When** sequence S runs again and requests a future self-reschedule for time T2, **Then** the earlier firing at T1 is removed and the queue holds exactly one pending self-rescheduled firing for S, at T2.
3. **Given** a running queue holding a pending self-rescheduled firing for sequence S, **When** a *different* sequence D requests a future self-reschedule, **Then** the firing for S is left untouched and a separate pending firing for D is added.

### User Story 2 - Other scheduling mechanisms keep their existing behavior (Priority: P1)

The queue schedules sequences in several ways besides a sequence rescheduling *itself* for a future time: template-defined timers, per-cycle ("once per run") steps, after-every-step registrations, next-cycle-start firings, and externally requested live schedules. The deduplication described in User Story 1 must apply **only** to future self-rescheduled firings and must not change how any of these other mechanisms behave.

**Why this priority**: The change is explicitly scoped by the request ("does not affect other types of scheduled sequences"). Silently altering the other mechanisms would regress established, working automations.

**Independent Test**: Exercise each of the other scheduling mechanisms for a sequence that also has a pending self-rescheduled firing, and confirm each still produces the same firings it did before this change, with the self-reschedule dedup affecting only the future self-rescheduled register.

**Acceptance Scenarios**:

1. **Given** a sequence registered to run once per cycle (or after every step, or at next cycle start) that also has a pending future self-rescheduled firing, **When** those other mechanisms fire, **Then** they fire exactly as they did before this change, unaffected by the dedup.
2. **Given** a sequence with a template-defined timer entry, **When** a self-rescheduled firing for the same sequence is added or replaced, **Then** the template timer entry is unaffected and still fires on its own schedule.
3. **Given** an externally requested live schedule for a sequence, **When** a self-rescheduled firing for the same sequence is added or replaced, **Then** the live schedule is unaffected.

### Edge Cases

- **Two future self-reschedules of the same sequence in one run**: the second replaces the first; only the later-requested firing survives (US1 scenario 2).
- **Self-reschedule requested when the run is no longer active**: behavior is unchanged from today — it is a logged no-op, and no firing is created to deduplicate.
- **Non-future self-reschedule options** (run again this cycle / at next cycle start / after every step): these are not future-timed firings and are out of scope for the dedup; their current behavior is preserved (after-every-step is already deduplicated per sequence by its existing design).
- **Replacement timing**: replacing an earlier pending firing must not itself trigger an extra execution — it only changes which single future firing is pending.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a sequence requests a future self-rescheduled firing of itself, the system MUST ensure at most one pending future self-rescheduled firing exists for that sequence within the current queue run.
- **FR-002**: When a future self-rescheduled firing for a sequence is requested and a pending future self-rescheduled firing for the **same** sequence already exists in the same queue run, the system MUST remove the pre-existing firing and retain only the newly requested one (most-recent-wins).
- **FR-003**: Deduplication MUST be scoped per sequence: a self-rescheduled firing for one sequence MUST NOT remove or alter a pending self-rescheduled firing for any different sequence.
- **FR-004**: Deduplication MUST be scoped to future self-rescheduled firings only and MUST NOT change the behavior of any other scheduling mechanism, including template-defined timers, once-per-run steps, after-every-step registrations, next-cycle-start firings, and externally requested live schedules.
- **FR-005**: Replacing a pre-existing pending firing MUST NOT cause an additional execution of the sequence; it only changes which single firing is pending.
- **FR-006**: The retained firing MUST target the time requested by the most recent self-reschedule.
- **FR-007**: When no pending future self-rescheduled firing exists for the sequence, a self-reschedule request MUST create exactly one pending firing (behavior unchanged from today for the first request).
- **FR-008**: Deduplication MUST remain scoped to a single queue run; self-rescheduled firings are not shared or persisted across runs, so the dedup only ever considers the current run's pending firings.

### Key Entities *(include if feature involves data)*

- **Pending self-rescheduled firing**: an in-memory, run-scoped record that a sequence has asked to run again at a specific future time. Identified for deduplication by the sequence it will run. Discarded when it fires or when the run ends.
- **Queue run**: one active execution of a queue that owns the set of pending self-rescheduled firings for its sequences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a self-rescheduling sequence runs any number of times within a single queue run, the queue holds no more than one pending future self-rescheduled firing for that sequence.
- **SC-002**: A self-rescheduling sequence that would previously have produced N pending future firings (N > 1) for itself now produces exactly 1, eliminating the redundant executions.
- **SC-003**: 100% of the other scheduling mechanisms (template timers, once-per-run, after-every-step, next-cycle-start, live schedules) produce the same firings after this change as before it, as verified by their existing tests continuing to pass unchanged.
- **SC-004**: The retained firing fires at the most-recently-requested time in 100% of cases where a replacement occurred.
