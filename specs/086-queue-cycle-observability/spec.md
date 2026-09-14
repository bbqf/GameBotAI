# Feature Specification: Queue Cycle Observability

**Feature Branch**: `086-queue-cycle-observability`
**Created**: 2026-09-14
**Status**: Draft
**Issue**: [#180](https://github.com/bbqf/GameBotAI/issues/180) (FR-003)
**Input**: User description: "Make a running queue expose evidence of the work it is doing, so that
'cycling healthily' can be told apart from 'failing on every cycle' without stopping the queue."

## Overview

A queue started with cycle execution enabled runs its roster of sequences over and over, indefinitely.
From the outside, such a queue currently offers exactly one signal: `status: Running`. That is a
start/stop flag — it says a run loop was started and has not exited. It says nothing about whether
that loop is doing any work.

The consequence is not hypothetical. On 2026-09-12 a blocking modal appeared on two production
devices. Both queues kept reporting `Running` and performed no useful work for 44 hours, until a
person happened to look at a device screen. Every signal the platform offered said green, because the
platform had no signal that could have said otherwise.

Three separate gaps combine to produce that blindness:

1. **Execution-log records are written per *run*, not per *cycle*.** A cycling run writes its root
   record at start and its terminating record at stop. A queue that has cycled for 44 hours has
   written nothing in those 44 hours.
2. **The queue resource exposes no progress.** There is no cycle count, no last-cycle timestamp, no
   last-cycle outcome, and no consecutive-failure count anywhere on the queue representation.
3. **The per-run detail view is only populated after the run ends.** The one place that would show
   per-sequence outcomes becomes readable only once the run is over — so inspecting a cycling queue
   requires stopping it, which destroys the condition being investigated.

This feature closes all three: a cycling run records each cycle as it completes, keeps a live health
summary on the queue, and makes both readable *while the queue is still running*.

Deciding what to *do* about an unhealthy queue — stopping it automatically, alerting anyone — is
deliberately not part of this feature (see Out of Scope).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator can tell a working queue from a stuck one (Priority: P1)

An operator checks on a queue that has been running for two days. Without stopping it, they read the
queue and see when its last cycle completed, whether that cycle succeeded, how many cycles have
completed in total, and how many cycles in a row have failed. A queue that is cycling healthily and a
queue that has failed every cycle since yesterday now look different.

**Why this priority**: This is the failure that cost 44 hours of production time. It is the smallest
change that makes the condition *detectable*, and it delivers value even if nothing else ships.

**Independent Test**: Start a cycling queue, read the queue while it runs, and confirm the health
values advance as cycles complete. Then make every cycle fail and confirm the consecutive-failure
count climbs while the last-cycle outcome reports failure — all without stopping the queue.

**Acceptance Scenarios**:

1. **Given** a cycling queue that has completed three cycles, **When** an operator reads the queue
   while it is still running, **Then** the response reports three completed cycles, the start and
   completion instants of the most recent one, and its outcome.
2. **Given** a cycling queue whose every sequence has failed for the last five cycles, **When** an
   operator reads the queue, **Then** the consecutive-failed-cycle count is five and the last-cycle
   outcome is a failure — distinguishable from a queue with the same `Running` status and zero failures.
3. **Given** a cycling queue that is part-way through a cycle, **When** an operator reads the queue,
   **Then** the response identifies the entry the run is currently on.
4. **Given** a queue that has never been started, **When** an operator reads it, **Then** the health
   values are reported as absent/zero rather than as misleading defaults.
5. **Given** a cycling queue that recovers — four failing cycles followed by a successful one —
   **When** an operator reads the queue, **Then** the consecutive-failed-cycle count has reset to zero.

---

### User Story 2 - An operator can diagnose *why* cycles are failing, without stopping the queue (Priority: P1)

Having seen that cycles are failing, the operator asks for the recent cycles of that queue and gets,
for each one, when it ran, how it ended, and the per-entry outcomes within it. They can see that the
same sequence fails first in every cycle — enough to identify the cause — while production keeps
running.

**Why this priority**: Detection without diagnosis means the only remedy is still "stop it and look",
which is what the operator cannot afford to do. Detection alone (Story 1) is a viable MVP, so this is
a separate slice, but it is the half that makes the signal actionable.

**Independent Test**: Start a cycling queue, let several cycles complete, and request its recent
cycles while it runs. Confirm each completed cycle appears with its per-entry outcomes, newest first,
and that the count returned honours a caller-supplied limit.

**Acceptance Scenarios**:

1. **Given** a cycling queue that has completed several cycles, **When** an operator requests its
   recent cycles while it is running, **Then** each completed cycle is listed with its start instant,
   completion instant, outcome, and the outcome of each entry executed within it.
2. **Given** an operator who asks for the most recent N cycles, **When** more than N have completed,
   **Then** exactly N are returned, newest first.
3. **Given** a queue that has been cycling for days, **When** an operator requests its recent cycles,
   **Then** the response is bounded to a recent window rather than the entire history of the run.
4. **Given** a queue that has never run, **When** an operator requests its cycles, **Then** an empty
   list is returned rather than an error.
5. **Given** a queue id that does not exist, **When** an operator requests its cycles, **Then** the
   request is refused as "not found".

---

### User Story 3 - Non-cycling queues and existing consumers are unaffected (Priority: P2)

A queue that runs its roster once still behaves exactly as before, and every existing consumer of the
queue representation and the execution log keeps working without change.

**Why this priority**: Regression protection for the live installation and the web UI. It constrains
the other two stories rather than delivering new value on its own.

**Independent Test**: Run the existing queue and execution-log test suites unchanged, and confirm a
single-pass queue produces the same run-level records it produced before.

**Acceptance Scenarios**:

1. **Given** a non-cycling queue, **When** it completes its single pass, **Then** its run-level
   execution records are unchanged from current behaviour, and it reports exactly one completed cycle.
2. **Given** an existing consumer reading the queue representation, **When** the new health values are
   added, **Then** every field it already relied on keeps its current name and meaning.
3. **Given** a run that ends, **When** the terminating run record is written, **Then** its existing
   summary and status are unchanged.

---

### Edge Cases

- **A cycle is interrupted mid-flight by a stop.** The partially-executed cycle is not reported as a
  completed cycle; the health values continue to describe the last cycle that actually completed.
- **A run ends and the queue is later restarted.** Health values describe the *current* run. A fresh
  run starts from zero completed cycles rather than accumulating across runs, and the previous run's
  per-cycle records are not presented as belonging to the new one.
- **The service restarts while a queue is running.** Live run state does not survive a restart; after
  a restart the queue is not running and reports no current-run health.
- **A cycle executes no sequences at all** (empty roster, or every entry filtered out). It still
  counts as a completed cycle and is recorded, so an idle-but-alive queue is distinguishable from a
  stalled one.
- **A cycle contains both successful and failed entries.** The cycle's outcome reflects that at least
  one entry failed, while the per-entry outcomes preserve which ones.
- **A queue cycles very fast.** The number of retained per-cycle records is bounded, so a long-running
  queue cannot grow unboundedly; the oldest records are discarded first.
- **Two queues run concurrently on different devices.** Each reports its own health and its own
  cycles; neither observes the other's.
- **A caller supplies an out-of-range limit** when requesting cycles (zero, negative, or very large).
  The request is handled predictably — clamped to the supported range — rather than failing.

## Requirements *(mandatory)*

### Functional Requirements

**Queue health**

- **FR-001**: The system MUST expose, on the single-queue read, a health summary describing the
  queue's current run, including the instant that run started.
- **FR-002**: The health summary MUST include the number of cycles completed in the current run.
- **FR-003**: The health summary MUST include the instant the most recent cycle started and the
  instant it completed.
- **FR-004**: The health summary MUST include the outcome of the most recent completed cycle,
  distinguishing at minimum "all entries succeeded" from "at least one entry failed".
- **FR-005**: The health summary MUST include the number of consecutive most-recent cycles that ended
  in failure, and this count MUST reset to zero when a cycle succeeds.
- **FR-006**: The health summary MUST identify the entry the run is currently executing — both its
  position in the roster and the sequence it refers to — and MUST report it as absent between entries.
- **FR-007**: The health summary MUST be readable while the queue is running, without stopping it and
  without altering the run's behaviour.
- **FR-008**: When a queue is not running, the health summary MUST report the absence of a current run
  unambiguously, rather than reporting stale or zeroed values that could be mistaken for a live run.
- **FR-008a**: A single response MUST NOT contradict itself: a health summary MUST be present exactly
  when the same response reports the queue as running, and the two read paths MUST agree with each
  other about whether a run is in progress.
- **FR-009**: Health values MUST be scoped to the current run: a newly started run begins with zero
  completed cycles and no last-cycle values.

**Per-cycle records**

- **FR-010**: The system MUST record each completed cycle of a run as its own record, rather than only
  recording the run as a whole.
- **FR-011**: Each cycle record MUST include the cycle's ordinal within the run, its start instant,
  its completion instant, and its outcome.
- **FR-012**: Each cycle record MUST include the outcome of each entry executed within that cycle,
  identifying the sequence and whether it succeeded.
- **FR-013**: The system MUST provide a way to read the recent cycle records of a queue *while that
  queue is running*.
- **FR-014**: The cycle read MUST accept a caller-supplied maximum number of cycles to return, MUST
  return them newest-first, and MUST clamp an out-of-range value to the supported range rather than
  failing.
- **FR-015**: The number of cycle records retained for a run MUST be bounded, discarding the oldest
  first, so that an indefinitely-cycling queue does not accumulate records without limit.
- **FR-016**: Requesting the cycles of a queue that does not exist MUST be refused as "not found";
  requesting the cycles of a queue that has not run MUST return an empty list, not an error.

**Compatibility**

- **FR-017**: All fields currently present on the queue representation MUST keep their existing names
  and meanings; the health summary is additive.
- **FR-018**: The existing run-level execution records — the root record written at run start and the
  terminating record written at run end, including its summary text and status — MUST be unchanged.
- **FR-019**: A non-cycling queue MUST retain its current behaviour, reporting exactly one completed
  cycle for its single pass.
- **FR-020**: Recording cycles and maintaining health values MUST NOT change which sequences run, in
  what order, or when.

### Key Entities

- **Queue health summary**: A description of what a queue's current run is doing — completed cycle
  count, last cycle's start/completion instants and outcome, consecutive failed-cycle count, and the
  entry currently executing. Describes the current run only; absent when no run is in progress.
- **Cycle record**: One completed pass of a cycling run over its roster. Carries its ordinal within
  the run, its start and completion instants, its outcome, and the outcomes of the entries executed
  within it.
- **Cycle entry outcome**: The result of executing one roster entry inside a cycle — which sequence
  ran and whether it succeeded.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can determine whether a running queue is doing useful work using a single
  read of that queue, without stopping it and without inspecting a device screen.
- **SC-002**: A queue that has failed every cycle for an hour is distinguishable from a healthy one by
  at least two independent values (last-cycle outcome and consecutive-failure count).
- **SC-003**: The 2026-09-12 condition — a queue reporting `Running` while doing no successful work —
  becomes detectable within one cycle duration of its onset, rather than remaining undetectable
  indefinitely.
- **SC-004**: An operator can identify which roster entry is failing in a repeatedly-failing queue
  from the recorded cycles alone, without stopping the queue.
- **SC-005**: A queue cycling continuously for a week retains a bounded, constant-size set of cycle
  records.
- **SC-006**: 100% of existing queue and execution-log behaviour verified by the current test suite
  continues to pass unchanged.
- **SC-007**: Reading a running queue's health or its recent cycles has no observable effect on which
  sequences that queue runs, or when.

## Clarifications

### Session 2026-09-14

Answered autonomously during an unattended pipeline run. Each answer is the option most consistent
with how the existing run engine already works, and with the constraints the issue states.

- **Q: Should per-cycle records be written into the execution log (one record per cycle instead of
  per run), or held as live run state and exposed through a dedicated read?**
  **A: Live run state, exposed through a dedicated read.**
  *Rationale*: The issue accepts either. FR-013 requires the records to be readable *while the run is
  in progress*, which is precisely what the execution log's run detail cannot do today. Every other
  piece of live run state — the current sequence, the schedule, the idle-pause hold — already lives on
  the run handle and is projected read-only, so this follows an established pattern rather than
  inventing one. It also avoids multiplying writes into the file-backed execution log, which carries
  retention and rotation behaviour of its own that this feature is not meant to disturb (FR-018).

- **Q: How many cycle records should a run retain, and what limit should the read accept?**
  **A: Retain the 50 most recent cycles; the read accepts a limit of 1–50, defaulting to 20, with
  out-of-range values clamped.**
  *Rationale*: FR-015 requires a bound; 50 is enough to see a failure pattern across a meaningful
  window while staying trivially small in memory. Clamping rather than rejecting satisfies FR-014 and
  keeps a careless caller working.

- **Q: What vocabulary should a cycle's outcome use?**
  **A: Two values — succeeded, or failed (at least one entry in the cycle failed).**
  *Rationale*: FR-004 asks for that distinction as a minimum, and it is the distinction the engine
  already makes per sequence. A cycle interrupted by a stop is never recorded as completed (see Edge
  Cases), so no third "aborted" value is needed. Distinguishing cancelled from failed is explicitly
  out of scope.

- **Q: Should the health summary appear on the queue list as well as the single-queue read?**
  **A: On the single-queue read only.**
  *Rationale*: This is what the issue asks for, and it keeps the list response — fetched routinely by
  the web UI — unchanged in shape and cost (FR-017).

- **Q: What does "the entry the run is currently executing" mean when a sequence fires from a timer or
  a self-reschedule rather than from the roster pass?**
  **A: The position refers to the roster's once-per-run entries; it is reported as absent for firings
  that do not come from the roster pass, while the currently-executing sequence is still reported.**
  *Rationale*: Consistent with the Assumptions entry on cycle membership — the position is only
  meaningful within the roster pass, but the operator's "what is running right now" question has an
  answer either way.

## Assumptions

- "Cycle" means one pass of the run loop over the roster's once-per-run entries — the same boundary
  the run already counts internally to report "across N cycles" in its terminating summary. Adopting
  the existing boundary keeps the new counts consistent with the summary already produced.
- A non-cycling run performs exactly one such pass, so it reports one completed cycle. This follows
  from using the existing boundary and keeps the two queue kinds described uniformly.
- Health values and cycle records describe the current run and are not required to survive a service
  restart. Run state is already in-memory and run-scoped; persisting it is a larger change than this
  issue asks for, and the restart case is not the failure being addressed.
- A cycle's outcome is derived from the entries executed within it: failure if any entry failed.
  Nothing in the issue suggests a richer per-cycle status vocabulary is needed to tell the two
  conditions apart.
- Sequences that fire outside the roster pass (timer firings, self-reschedules, after-every-step
  entries) still count toward the cycle they execute within; the operator's question is "did this
  cycle do its work", not "which scheduler asked for it".

## Out of Scope

- **Any failure policy.** Automatically stopping, pausing, or restarting a queue after N failed
  cycles is issue #181 and is explicitly excluded. Exposing the consecutive-failure count is in scope;
  acting on it is not.
- **Any outbound notification.** No alerts, webhooks, emails, or push notifications. Also #181.
- **Distinguishing a cancelled sequence from a failed one.** Tracked as a separate companion request;
  this feature uses the success/failure distinction the engine already makes.
- **OS-level schedulers, operator polling scripts, or device-screen classification.** The consuming
  project's constitution rules these out; the whole point is that the platform answers the question
  itself.
- **Changing when the per-run detail view becomes populated.** This feature adds a way to read cycles
  during a run; it does not alter the existing after-the-fact run detail view.
- **Persisting run health across a service restart.**
- **Web UI changes.** The API is the deliverable; surfacing health in the UI is separate work.
