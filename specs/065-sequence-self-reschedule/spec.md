# Feature Specification: Sequence Self-Rescheduling into the Originating Queue Run

**Feature Branch**: `065-sequence-self-reschedule`  
**Created**: 2026-06-22  
**Status**: Implemented  
**Input**: User description: "As an author of a sequence I want to be able for a sequence to reschedule itself at the queue it was started from. I want to do it from UI, based on the if conditions during the sequence runs. I want to be able to use all the possible options when rescheduling the sequence. This rescheduling should only apply to the current queue run and must not be persisted, however it should be reflected in the execution logs later on. If the sequence wasn't started from a queue, this should be a no-op with a successful exit code."

## Context

Queue runs already support scheduling a sequence into a running queue ephemerally, only for the current run, without persisting to the template — this was established for relative/timer schedules in feature 059 (Relative-Time Sequence Scheduling) and built on the four schedule options surfaced everywhere scheduling is configured: **At Queue Start**, **Once Per Run**, **Timer** (time-of-day or relative offset), and **After Every Step** (features 053, 060, 061).

Separately, sequences support visual **IF-style conditional logic** (features 031–033): an author can branch a sequence's flow based on command success/failure and image detections.

This feature lets a sequence **reschedule itself** into the queue run it was started from, as an authored action placed inside the sequence and gated by the sequence's existing conditional logic. The decision is made *during the run*, based on live conditions, using any of the available schedule options. The reschedule affects only the current queue run, is never written back to the queue's template, and is reflected in the execution logs. When the sequence was not started from a queue (e.g., run standalone from the authoring UI), the action does nothing and reports success.

This is the inverse of feature 059's *external* live-scheduling call: here the running sequence schedules *itself* from *within* its own flow.

## Clarifications

### Session 2026-06-22

- Q: How should repeated self-rescheduling be bounded against runaway loops? → A: No built-in cap — purely author-controlled via the IF condition; the loop risk is the author's responsibility.
- Q: What does the "At Queue Start" option mean mid-run for a self-reschedule? → A: Cycling run → fire at the start of the next cycle; non-cycling run → fire at the next iteration boundary.
- Q: Where in the run does a "Once Per Run" self-reschedule fire? → A: Appended after the remaining Once-Per-Run steps of the current cycle (runs again before this cycle ends).
- Q: Is the self-reschedule action exposed via the API as well as the UI? → A: In scope on both — the action is part of the persisted sequence definition and round-trips through the authoring API like any other action.
- Q: Does a nested/child sequence inherit the originating queue run of its queue-driven parent? → A: Yes — origin propagates through nesting; the child reschedules itself into that run.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a self-reschedule action gated by an IF condition (Priority: P1)

As a sequence author, I want to add a "reschedule this sequence" action inside a sequence and place it behind an IF condition, so that the sequence decides at runtime — based on what it observes (a command result or an image detection) — whether to schedule another firing of itself into the queue run it is part of.

**Why this priority**: This is the core of the request: an author-controlled, condition-driven, in-sequence reschedule. Without the authored action and its conditional gating, none of the rescheduling behavior can be expressed or observed. It is the smallest slice that delivers value on its own.

**Independent Test**: In the sequence editor, add the self-reschedule action under an IF branch whose condition is forced true; run a queue that includes the sequence; confirm the sequence fires a second time during the same run as a direct result of the action, and that forcing the condition false in another run produces no additional firing.

**Acceptance Scenarios**:

1. **Given** a sequence containing a self-reschedule action behind an IF condition that evaluates true, **When** the sequence runs as part of a queue, **Then** the action executes and an additional firing of that same sequence is scheduled into the current run.
2. **Given** the same sequence where the IF condition evaluates false, **When** it runs as part of a queue, **Then** the self-reschedule action is not reached and no additional firing is scheduled.
3. **Given** a sequence with the self-reschedule action, **When** the author opens the sequence editor, **Then** the action is visible, configurable, and can be placed inside conditional branches like other sequence actions.
4. **Given** the self-reschedule action executes successfully, **When** the sequence continues, **Then** the rest of the sequence's steps still run normally (the action does not, by itself, terminate the sequence).

---

### User Story 2 - Choose any schedule option when rescheduling (Priority: P1)

As a sequence author, I want the self-reschedule action to offer every schedule option the queue supports — At Queue Start, Once Per Run, Timer (time-of-day and relative offset), and After Every Step — with the same configuration choices I get when scheduling a sequence in a template, so I can control *when* within the run the re-firing happens.

**Why this priority**: The user explicitly asked to "use all the possible options." Option parity is what makes the action general-purpose rather than a single hard-coded re-fire. It is independently demonstrable by configuring each option and observing the corresponding timing.

**Independent Test**: For each schedule option, configure the action with that option (and its parameters where applicable), run the queue, and confirm the re-firing occurs at the moment dictated by that option (immediately/next normal step for Once Per Run; at the configured/elapsed time for Timer; after each subsequent normal step for After Every Step; at the start of the next cycle for At Queue Start).

**Acceptance Scenarios**:

1. **Given** the action configured as **Once Per Run**, **When** it executes, **Then** the sequence is scheduled as an additional normal step for the current run (fired again within the run rather than at a timed boundary).
2. **Given** the action configured as **Timer** with a relative offset (e.g., 10 min 0 sec), **When** it executes, **Then** the offset is measured from the moment of execution and the sequence fires once at the first iteration boundary at or after that instant — identical to a live relative schedule (feature 059).
3. **Given** the action configured as **Timer** with a time-of-day, **When** it executes, **Then** the sequence fires once at the first iteration boundary at or after that wall-clock time during the current run.
4. **Given** the action configured as **After Every Step**, **When** it executes, **Then** the sequence is registered to fire after each subsequent normal step for the remainder of the current run, and it does not retrigger the self-reschedule action in a way that creates an endless loop (loop-safety as established in feature 060).
5. **Given** the action configured as **At Queue Start**, **When** it executes during a cycling run, **Then** the sequence is scheduled to fire at the start of the next cycle; **and** during a non-cycling run where no further start boundary exists, the action falls back to firing at the next iteration boundary.
6. **Given** any option that has additional parameters (timer time/offset), **When** the author selects that option in the editor, **Then** the relevant parameter inputs appear, matching the inputs used when scheduling a sequence in a template.

---

### User Story 3 - No-op with success when not started from a queue (Priority: P1)

As a sequence author, I want a sequence containing the self-reschedule action to still run correctly when it is executed outside any queue (e.g., standalone from the authoring UI or as a nested sequence not driven by a queue run), with the action simply doing nothing and reporting success, so that the same sequence is safe to reuse in both contexts.

**Why this priority**: Sequences are reused across contexts. If the action failed or errored when there is no originating queue, authors could not safely place it in shared sequences. Making it a successful no-op is an explicit requirement and is independently testable.

**Independent Test**: Run a sequence containing the self-reschedule action standalone (not from a queue); confirm the action is recorded as executed-but-no-op with a success outcome, the sequence completes normally, and nothing is scheduled anywhere.

**Acceptance Scenarios**:

1. **Given** a sequence with the self-reschedule action, **When** it runs standalone (not started from a queue), **Then** the action performs no scheduling and reports success.
2. **Given** the standalone run from scenario 1, **When** the execution log is inspected, **Then** the action appears with a success status and a clear note that there was no originating queue, so no reschedule was performed.
3. **Given** the standalone run, **When** the sequence continues after the action, **Then** the remaining steps execute normally and the sequence's overall outcome is unaffected by the no-op.

---

### User Story 4 - See the reschedule reflected in the execution logs (Priority: P2)

As an operator reviewing a run afterward, I want each self-reschedule decision and the firing it produced to appear in the execution logs, so I can understand why the sequence ran more times than the template alone would explain.

**Why this priority**: Observability of an otherwise-invisible runtime decision. The feature still works without rich logging, but the user explicitly asked that the reschedule "be reflected in the execution logs later on," and operators need to reconcile run behavior with the static template.

**Independent Test**: Trigger a self-reschedule during a run, let the rescheduled firing occur, then open the execution logs and confirm both the reschedule action (with its chosen option and resolved timing) and the resulting additional firing are visible and attributable to the originating sequence.

**Acceptance Scenarios**:

1. **Given** a self-reschedule action that executed and scheduled a firing, **When** the execution log is viewed, **Then** the action entry records the chosen schedule option, the resolved timing (e.g., target instant for a timer), and that the schedule applies to the current run only.
2. **Given** the rescheduled firing later occurs, **When** the execution log is viewed, **Then** the additional firing appears as an executed sequence in the run, consistent with how other scheduled/live firings are logged (features 059, 063).
3. **Given** a reschedule that could not be applied (e.g., the run ended before the firing was due), **When** the execution log is viewed, **Then** the action entry indicates the reschedule was accepted but did not fire within the run, without marking the run a failure.
4. **Given** the no-op case (no originating queue), **When** the execution log is viewed, **Then** the action entry clearly distinguishes "no-op, not started from a queue" from "scheduled."

---

### Edge Cases

- **No originating queue**: Sequence run standalone or nested outside a queue run → action is a successful no-op (US3).
- **Repeated rescheduling / loops**: The action can be reached on the rescheduled firing too. Because it is gated by the author's IF condition and (per Assumptions) schedules at most one additional firing per execution, runaway looping is the author's responsibility — the same model as in-sequence cycles (feature 031, which requires explicit max-iteration limits) and re-issued live schedules (feature 059).
- **Run is ending / last step**: A timer or At-Queue-Start reschedule whose due moment never arrives before the run completes does not fire and does not make the run a failure (consistent with feature 053 non-cyclic timer behavior).
- **Relative offset of 0 / past time-of-day**: The sequence fires at the next iteration boundary.
- **Multiple self-reschedules in one run**: Each accepted reschedule is an independent firing; the run's executed-step total reflects each, consistent with features 059/060.
- **Manual stop mid-run**: A pending self-rescheduled firing that has not yet occurred is abandoned when the run is stopped/aborted (consistent with feature 051 abort behavior).
- **Sequence appears multiple times in the template**: The reschedule targets the sequence as run within the current queue run; it adds an additional firing rather than altering existing template entries.
- **After Every Step self-trigger**: An After-Every-Step reschedule must not cause the self-reschedule action to fire itself in an unbounded chain (loop-safety, feature 060).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a sequence action, configurable in the sequence authoring UI, that reschedules the currently running sequence into the queue run it was started from.
- **FR-001a**: The self-reschedule action MUST be part of the persisted sequence definition and round-trip through the authoring API (create/read/update) like any other sequence action, preserving UI/API parity.
- **FR-002**: The self-reschedule action MUST be placeable within the sequence's existing IF/conditional structure, so its execution is gated by the same condition logic available to other actions (features 031–033).
- **FR-003**: When executed within a queue run, the action MUST schedule one additional firing of the same sequence into the **current** queue run.
- **FR-004**: The action MUST offer all schedule options available when scheduling a sequence in a queue: At Queue Start, Once Per Run, Timer (time-of-day and relative offset), and After Every Step, with the same parameter inputs used elsewhere for those options.
- **FR-005**: For the **Timer / relative offset** option, the offset MUST be measured from the moment the action executes ("from now") and resolve to a single target instant for the current run, matching live relative scheduling (feature 059).
- **FR-006**: For the **Timer / time-of-day** option, the firing MUST occur at the first iteration boundary at or after that wall-clock time during the current run.
- **FR-007**: For the **Once Per Run** option, the rescheduled firing MUST be appended after the remaining Once-Per-Run steps of the current cycle, so it runs again before that cycle ends.
- **FR-008**: For the **After Every Step** option, the sequence MUST be registered to fire after each subsequent normal step for the remainder of the run, and this MUST be loop-safe (it does not retrigger the self-reschedule action into an endless chain), consistent with feature 060.
- **FR-009**: For the **At Queue Start** option in a cycling run, the rescheduled firing MUST occur at the start of the next cycle; in a non-cycling run with no further start boundary, it MUST fall back to firing at the next iteration boundary.
- **FR-010**: The reschedule MUST apply only to the current queue run and MUST NOT be persisted to the queue template or any saved configuration.
- **FR-011**: When the sequence was not started from a queue, the action MUST perform no scheduling and MUST report a successful outcome (no-op success).
- **FR-012**: The action's execution MUST NOT, by itself, terminate the sequence; remaining steps continue regardless of whether a reschedule was performed or was a no-op.
- **FR-013**: The execution log MUST record each self-reschedule action with: its chosen schedule option, the resolved timing (where applicable), and whether it scheduled a firing or was a no-op (with the reason for the no-op).
- **FR-014**: The execution log MUST show the resulting rescheduled firing as an executed sequence within the run, consistent with how other live/scheduled firings are logged (features 059, 063), and attributable to the originating sequence.
- **FR-015**: A self-reschedule that is accepted but whose firing never becomes due before the run ends MUST NOT mark the run as failed; the log MUST indicate it did not fire within the run.
- **FR-016**: Rescheduled firings MUST count toward the run's executed-sequence total/metrics consistently with other scheduled/live firings (features 059, 060), while run termination remains governed by the once-per-run steps.
- **FR-017**: A pending self-rescheduled firing that has not yet occurred MUST be abandoned if the run is stopped or aborted (feature 051).
- **FR-018**: The originating queue run MUST propagate through sequence nesting: a sequence executed as a nested/child of a queue-driven run MUST treat that run as its originating queue run, and the self-reschedule MUST schedule an additional firing of that child sequence into the run.

### Key Entities

- **Self-Reschedule Action**: An authorable sequence action carrying the chosen schedule option and its parameters (timer time-of-day / relative offset where applicable). Lives inside a sequence definition and may sit under conditional branches.
- **Ephemeral Run Schedule Entry**: A run-scoped, non-persisted record that a sequence is to fire again in the current run, with a resolved option/timing. Exists only for the lifetime of the run.
- **Execution Log Entry (reschedule)**: A log record capturing the action's decision (option, resolved timing, scheduled vs. no-op + reason) and, separately, the resulting additional firing of the sequence.
- **Originating Queue Run Context**: The information that tells a running sequence which queue run (if any) started it — propagated through sequence nesting — used to decide between "schedule into this run" and "no-op success."

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can add and configure the self-reschedule action — including selecting any of the available schedule options — entirely from the sequence editor, without editing files or calling the API directly.
- **SC-002**: When a gated self-reschedule action's condition is true during a queue run, the sequence fires again within that same run 100% of the time, at the timing dictated by the chosen option.
- **SC-003**: The same sequence containing the action runs to a successful completion when executed standalone, with zero scheduling side effects, 100% of the time.
- **SC-004**: After any run that triggered a self-reschedule, an operator can identify, from the execution logs alone, that a reschedule occurred, which option was used, and that the extra firing came from the self-reschedule (not the static template).
- **SC-005**: No self-reschedule is ever observable in the queue's saved template or configuration after the run ends (0 persisted reschedules across runs).
- **SC-006**: The reschedule decision adds no more than a negligible delay to sequence execution (consistent with the conditional-step budget of p95 ≤ 200 ms established in feature 031).

## Assumptions

- **One firing per execution**: Each execution of the action schedules at most one additional firing. Repeated re-firing is achieved only by the action being reached again (on a later step or a rescheduled firing) with its condition still true — there is no built-in automatic recurrence and no built-in recurrence cap; loop control is the author's responsibility via the IF condition, mirroring features 059 (re-issued live schedules) and 031 (cycles need explicit limits).
- **Option semantics reuse existing behavior**: Each schedule option behaves for the current run exactly as that option behaves when configured live/in a template (features 053, 059, 060), applied to the originating sequence. The only novel interpretation is **At Queue Start mid-run**, defined in FR-009.
- **Counting**: Rescheduled firings count toward the run's executed-sequence totals like other scheduled/live firings (features 059, 060); they do not change the run's stop condition.
- **Configuration surface**: The primary authoring surface is the sequence editor ("from UI"). Per clarification, the action is also part of the persisted sequence definition and round-trips through the authoring API with full UI/API parity (FR-001a).
- **Target identity**: "Reschedule itself" means the same sequence definition currently executing; the reschedule adds a firing to the current run and does not modify or depend on whether that sequence is already an entry in the running template.
- **No-op breadth**: "Not started from a queue" covers any execution context lacking an originating queue run (standalone authoring runs, or nested executions whose top-level run is not a queue run). A sequence nested under a queue-driven run **does** have an originating queue run (origin propagates through nesting) and reschedules itself into that run (FR-018).
