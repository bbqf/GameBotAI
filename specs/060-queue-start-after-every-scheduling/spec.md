# Feature Specification: Queue-Start and After-Every-Step Scheduling

**Feature Branch**: `060-queue-start-after-every-scheduling`  
**Created**: 2026-06-18  
**Status**: Draft  
**Input**: User description: "I need two more scheduling options: at queue start and after every sequence. It should work like this: all the sequences that are scheduled to be started at queue start, must be executed in the order of appearing in the template at queue start, before any evaluation of execution timers take place. the 'after every sequence' means that after every sequence step, one or many 'after every sequence' sequences will be executed. 'after every' in this respect doesn't apply to such sequences, so that they don't create endless loops. The functionality has to be enabled in the UI and API."

## Clarifications

### Session 2026-06-18

- Q: How should "after every sequence" relate to the existing "Every Step" option? → A: It is the **same narrow behavior** as today's "Every Step" (fires after each normal "Once Per Run" step only), and it **supersedes** "Every Step" — renamed to **"After Every Step"**. The broad "fires after every executed sequence (start/timer/etc.)" interpretation is **out of scope**.
- Q: Does "At Queue Start" count toward the run's executed-sequence total? → A: **Yes** — "At Queue Start" executions count toward the executed total (treated as deliberate scheduled work), like "Once Per Run" and relative/live timer firings.
- Q: For the "Every Step" → "After Every Step" rename, does the API/stored identifier change? → A: **No** — change the **display label only**; keep the existing API/stored identifier (`EveryStep`). Fully backward compatible, no migration.

## User Scenarios & Testing *(mandatory)*

A queue template is an ordered list of sequence references, each tagged with a **schedule option** that controls when, during a run, that sequence executes. Today the available options are: run once per run/cycle as a normal step ("Once Per Run"), run after each normal step (currently labeled "Every Step"), and run on a timer (time-of-day or relative offset). This feature:

1. Adds one genuinely new option, **At Queue Start**, that runs entries in template order at the very start of a run, before any timer is evaluated.
2. **Renames the existing "Every Step" option to "After Every Step"** for clarity, keeping its current behavior (it fires after each normal step) and its loop-safe property (it never triggers itself).

Both options are surfaced everywhere schedule options are surfaced today: the template editor UI and the template/queue API.

### User Story 1 - Run setup sequences at queue start (Priority: P1)

As an operator, I want to mark one or more sequences in a template as "At Queue Start" so that they run, in template order, the moment the queue begins — before any timer-scheduled sequences are evaluated — so I can reliably perform startup/setup work (e.g. open the game, dismiss daily pop-ups, navigate to a known screen) before the rest of the run proceeds.

**Why this priority**: Startup/setup work is a precondition for the correctness of everything else in the run. Without a guaranteed-first execution slot, operators currently rely on ordering tricks that break as soon as timers or other schedule types are involved. This is the highest-value, independently demonstrable slice and the only genuinely new run behavior in this feature.

**Independent Test**: Create a template with two "At Queue Start" entries and a couple of normal entries, start the queue, and confirm the two start entries execute first, in their template order, before any timer evaluation or normal steps.

**Acceptance Scenarios**:

1. **Given** a template with sequences A and B marked "At Queue Start" (A before B in the template) plus normal step C, **When** the queue starts, **Then** A executes, then B executes, then the run proceeds to evaluate timers and run C.
2. **Given** a template containing both "At Queue Start" entries and timer entries (time-of-day, relative offset), **When** the queue starts, **Then** all "At Queue Start" entries run to completion before any timer is evaluated for the first time.
3. **Given** a template with no "At Queue Start" entries, **When** the queue starts, **Then** the run behaves exactly as it does today (no change).
4. **Given** a cycling queue with "At Queue Start" entries, **When** the queue runs multiple cycles, **Then** the "At Queue Start" entries run once at the beginning of the run and are not repeated at the start of each subsequent cycle.

---

### User Story 2 - Rename "Every Step" to "After Every Step" (Priority: P2)

As an operator, I want the existing per-step option to be labeled "After Every Step" (instead of "Every Step") in the UI and API so its meaning — "run this after every normal step" — is self-evident, while its behavior stays exactly as it is today.

**Why this priority**: This is a clarity/usability improvement to an existing capability, not new run behavior. It is independently shippable (a label/identifier change) but lower value than the new "At Queue Start" slot.

**Independent Test**: In the template editor, the per-step option appears as "After Every Step"; selecting it for an entry and running the queue produces the same after-each-normal-step execution as the current "Every Step" option does.

**Acceptance Scenarios**:

1. **Given** a template with normal steps C and D and one "After Every Step" entry X, **When** the queue runs, **Then** X executes after C and after D (and once more after the final normal step), exactly as "Every Step" does today.
2. **Given** two "After Every Step" entries X and Y (X before Y in the template), **When** a normal step completes, **Then** X executes, then Y executes — and neither X nor Y triggers a further round of "After Every Step" execution (no endless loop).
3. **Given** "At Queue Start" entry A and "After Every Step" entry X, **When** the queue starts, **Then** X does NOT run after A — "After Every Step" fires only after normal ("Once Per Run") steps, not after queue-start or timer firings.
4. **Given** a template that previously used the "Every Step" option, **When** it is loaded after this change, **Then** the same entries run with identical behavior under the new "After Every Step" label (no behavioral migration required).

---

### User Story 3 - Configure the options in UI and API (Priority: P3)

As an operator, I want to select "At Queue Start" and "After Every Step" from the same schedule-type control I already use for each template entry — in the web UI and via the API — so the options are discoverable and scriptable alongside the existing ones.

**Why this priority**: The scheduling behavior (Stories 1–2) is the core value; surfacing it is what makes it usable. It is listed separately to make the cross-surface enablement explicit and independently testable.

**Independent Test**: In the template editor, change an entry's schedule type to "At Queue Start" and save; reload and confirm the choice persisted. Via the API, save a template whose entries use the options and read them back, confirming the values round-trip.

**Acceptance Scenarios**:

1. **Given** the template editor, **When** the operator opens an entry's schedule-type selector, **Then** "At Queue Start" and "After Every Step" appear as choices alongside "Once Per Run" and the timer options.
2. **Given** an entry set to "At Queue Start", **When** the operator saves and reloads the template, **Then** the entry still shows "At Queue Start" and a clear badge/label indicating it.
3. **Given** an API client, **When** it saves a template entry with the new schedule option and then reads the template, **Then** the schedule option round-trips unchanged.
4. **Given** an API client, **When** it sends an unrecognized or invalid schedule option, **Then** the request is rejected with a clear validation error and the template is not modified.

---

### Edge Cases

- **At Queue Start with timers**: "At Queue Start" entries always precede the first timer evaluation; timers (time-of-day, relative offset, live schedule) continue to be evaluated only at iteration boundaries as today.
- **After Every Step excludes itself**: an "After Every Step" execution never counts as a "normal step" that triggers another round — preserving today's loop-safe behavior even with multiple "After Every Step" entries (one completed normal step triggers exactly one pass through all "After Every Step" entries, not a cascading chain).
- **Empty buckets**: a template with no "At Queue Start" entries (or no "After Every Step" entries) behaves exactly as today for the unaffected paths.
- **Cycling vs non-cycling**: "At Queue Start" runs once per run regardless of cycling. "After Every Step" runs after each normal step in every cycle, as today.
- **No normal steps present**: if a template has only "At Queue Start" and/or "After Every Step" entries and no "Once Per Run" steps, "After Every Step" entries behave exactly as the current "Every Step" option does in that situation (no behavior change from today).
- **Failure of an "At Queue Start" sequence**: a failure is non-fatal — it is recorded and the run continues, consistent with how per-sequence failures are handled today.
- **Stop mid-start**: if the queue is stopped while "At Queue Start" entries are still running, the run stops promptly and does not proceed to the main loop.
- **Stale/unresolved sequence reference** in an "At Queue Start" entry: treated as a non-fatal per-sequence failure, same as for existing schedule types.
- **Ordering among the same type**: "At Queue Start" entries always execute in their order of appearance in the template.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new schedule option, "At Queue Start", for queue-template entries, in addition to the existing options.
- **FR-002**: The system MUST rename the existing per-step schedule option's **display label** from "Every Step" to "After Every Step" in operator-facing surfaces (primarily the UI), without changing its run behavior and without changing its underlying API/stored identifier (`EveryStep`), so existing templates and API/script clients keep working unchanged.
- **FR-003**: The system MUST execute all "At Queue Start" entries once at the beginning of a run, in their order of appearance in the template, before any timer (time-of-day, relative-offset, or live) is evaluated and before the first normal step runs.
- **FR-004**: The system MUST run "At Queue Start" entries exactly once per run, not once per cycle, for cycling queues.
- **FR-005**: "After Every Step" entries MUST continue to execute after each normal ("Once Per Run") step (and once more after the final normal step), in their order of appearance in the template — identical to the current "Every Step" behavior. "After Every Step" MUST NOT fire after "At Queue Start" executions or after timer/relative/live firings.
- **FR-006**: An "After Every Step" execution MUST NOT itself trigger another round of "After Every Step" executions; this loop-prevention guarantee MUST hold for any number of "After Every Step" entries (preserving today's behavior).
- **FR-007**: A failure in an "At Queue Start" sequence MUST be non-fatal: the failure is recorded and the run continues, consistent with existing per-sequence failure handling.
- **FR-008**: The system MUST allow a template to contain zero, one, or many "At Queue Start" entries, and MUST allow "At Queue Start" to coexist with all other schedule options in the same template.
- **FR-009**: The system MUST persist an entry's chosen schedule option (including "At Queue Start") and return it unchanged when the template is read back.
- **FR-010**: Templates that previously used the "Every Step" option MUST continue to run with identical behavior under the "After Every Step" label, with no data migration required and no change to stored templates' run outcomes.
- **FR-011**: Existing behavior for "Once Per Run" and the timer modes MUST remain unchanged; "At Queue Start" is purely additive and the default schedule option for entries without an explicit choice MUST be unchanged.
- **FR-012**: The web UI template editor MUST let the operator select "At Queue Start" (and the renamed "After Every Step") from the same per-entry schedule-type control used for existing options, and MUST clearly indicate (e.g. via a badge/label) when an entry uses "At Queue Start".
- **FR-013**: The API MUST accept "At Queue Start" on template-entry save and return it on template read, continue to accept and return the existing per-step identifier (`EveryStep`) unchanged, and reject an unrecognized/invalid schedule option with a clear validation error without modifying the template.
- **FR-014**: "At Queue Start" entries MUST be evaluated/executed only at the start of the run (before the first iteration's timer evaluation), with no new background timers or polling introduced beyond what already exists.
- **FR-015**: "At Queue Start" executions MUST count toward the run's executed-sequence total (like "Once Per Run" and relative/live firings); "After Every Step" executions MUST NOT count, unchanged from today's "Every Step".

### Key Entities *(include if feature involves data)*

- **Queue Template Entry**: a positional reference to a sequence within a template, carrying a schedule option. This feature adds one possible schedule option ("At Queue Start") and renames one existing option ("Every Step" → "After Every Step"); no other attributes of the entry change.
- **Schedule Option**: the enumerated choice that governs when an entry's sequence executes during a run. After this feature the set is: Once Per Run, After Every Step (formerly "Every Step"), Timer (time-of-day / relative offset), and At Queue Start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a template with "At Queue Start" entries, 100% of runs execute those entries first, in template order, before any timer evaluation or normal step — verifiable from the run's execution-log ordering.
- **SC-002**: For a template with an "After Every Step" entry, the entry executes after each normal step and once after the final normal step, with zero self-triggered executions and no executions after queue-start or timer firings — identical to current "Every Step" behavior across a cycling and a non-cycling run.
- **SC-003**: A run that includes "At Queue Start" and "After Every Step" entries always terminates (no endless loops), completing within the expected, bounded number of executions.
- **SC-004**: "At Queue Start" is selectable in the template editor and round-trips through the API: an entry saved with it reads back with the same option in 100% of cases.
- **SC-005**: Existing templates and the (renamed) per-step behavior, "Once Per Run", and timer behaviors produce identical run ordering and outcomes before and after this feature (no regressions), verified by the existing scheduling test suite continuing to pass (updated only for the rename).
- **SC-006**: An operator can configure "At Queue Start" for a template entry without consulting documentation, completing the change in under 1 minute in the editor.

## Assumptions

- **Counting toward run totals**: "At Queue Start" executions are deliberate, scheduled work and count toward the run's executed-sequence total (confirmed); "After Every Step" executions remain interstitial and do NOT count toward the executed total (unchanged from today's "Every Step"). Neither option, by itself, defines run completion — completion continues to be governed by the "Once Per Run" steps.
- **Rename mechanics**: "After Every Step" is a **display-label change only** (confirmed). The underlying persisted/API identifier stays `EveryStep`, so previously-saved templates and existing API/script clients keep working with no migration (FR-002/FR-010/FR-013).
- **No new persistence format**: the schedule option continues to be stored on the existing template-entry record using the existing storage mechanism; "At Queue Start" is an additive enumeration value and is backward compatible.
- **No new external dependencies or background services**: the behavior is implemented within the existing run loop.
- **Naming**: the operator-facing labels are "At Queue Start" and "After Every Step"; exact wording may be refined during design but the meaning is as specified here.
