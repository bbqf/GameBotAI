# Feature Specification: Queue Failure Policy and Outbound Notification

**Feature Branch**: `087-queue-failure-policy`
**Created**: 2026-09-14
**Status**: Implemented (iterated by 096)
**Input**: GitHub issue [#181](https://github.com/bbqf/GameBotAI/issues/181) — "FR-004: no queue-level failure policy and no outbound notification - a failing queue cycles forever, silently"

## Context

Feature 086 (issue #180) made a failing queue *visible* to anyone who asks: a running queue now
publishes `cyclesCompleted`, `lastCycleStatus` and `consecutiveFailedCycles` while it runs. It
deliberately stopped there — "`consecutiveFailedCycles` is exposed; nothing acts on it."

This feature closes the other half. Today a queue whose every entry fails cycles forever and tells
nobody. During the 2026-09-14 outage the per-entry guards in two production rosters worked exactly
as designed — they detected a bad screen and correctly refused to act on it, every cycle, for 44
hours. It changed nothing, because a guard can protect the account but cannot escalate. Failing
correctly in a place nobody reads is indistinguishable from failing silently.

The operator's own withdrawn workaround (an external script that counted bad observations and called
`stop`) is instructive about what *not* to build: a cycling roster can self-heal — during that same
outage it relaunched the game on its own after the network returned — so an unreliable watchdog
holding the authority to halt production was a liability, not a guardrail. **Notification is the
primary outcome of this feature; stopping is one selectable option among several.**

## Clarifications

### Session 2026-09-14

- Q: How does a notification receiver verify the caller is this service? → A: An optional
  operator-configured static auth header (name + value) on the destination. Absent by default,
  because the common destination is a local receiver; required in practice for a remote one.
- Q: What is the delivery budget for one notification? → A: A 5-second timeout per attempt, at most
  2 attempts (one retry after a short fixed backoff), then abandon and record the failure.
- Q: What does a paused run do with timers that come due while it is paused? → A: It fires nothing
  while paused. On resume, due-ness is re-evaluated normally — a time-of-day slot not yet fired
  today fires, and an elapsed relative or live schedule fires immediately. No firings are recorded
  as skipped; the pause holds the loop *before* its timer evaluation rather than suppressing it.
- Q: Where is an undeliverable notification recorded? → A: Both the structured application log and a
  last-notification block on the running queue's live health, so an operator can see the delivery
  failure through the API without reading log files. Deliberately NOT the execution log, which a
  cycling run does not write until the run ends.
- Q: Is the notification payload a stable contract? → A: Yes — a fixed schema carrying an explicit
  `schemaVersion` starting at 1, with stable field names, so a receiver can be written against it
  and a future change is detectable rather than silent.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A failing queue raises an alert on its own (Priority: P1)

An operator configures a queue with a failure policy: after N consecutive failed cycles, send a
notification. The queue then runs unattended. When a real outage begins — a network-loss modal, a
game update, a changed screen — the queue's cycles start failing. On the Nth consecutive failed
cycle the service delivers an outbound notification describing which queue, which entry and which
sequence failed, and how many cycles have failed in a row. The queue keeps running, because it may
still self-heal.

**Why this priority**: This is the entire point of the issue and the only part that changes the
44-hour outage into a minutes-long one. Every other story is a refinement of what happens *after*
the alert. Without it, the platform still has no way to get a signal off the machine.

**Independent Test**: Configure a queue with `{ count: 3, action: "notify" }` pointed at a local
receiver, make every cycle fail, and observe exactly one notification arriving on the third failed
cycle carrying the queue, entry, sequence, failure detail and count — with the queue still Running
afterwards.

**Acceptance Scenarios**:

1. **Given** a running queue with a notify policy of count 3, **When** three cycles fail in a row,
   **Then** one notification is delivered after the third failed cycle and the queue remains Running.
2. **Given** that same queue after two failed cycles, **When** the next cycle succeeds, **Then** the
   consecutive count resets to zero and no notification is sent.
3. **Given** a queue whose policy has already tripped and notified, **When** further cycles keep
   failing, **Then** the operator is not notified again for every subsequent cycle (no alert storm).
4. **Given** a tripped-and-notified queue, **When** a cycle succeeds and later cycles fail again to
   the threshold, **Then** a fresh notification is delivered for the new episode.
5. **Given** a queue with no failure policy configured, **When** its cycles fail indefinitely,
   **Then** behaviour is exactly as it is today — no notification, no state change.

---

### User Story 2 - The operator chooses what a tripped policy does (Priority: P2)

Some rosters should keep trying; some should stop touching the device the moment they are clearly
broken. The operator selects the action the policy takes when it trips: notify only, stop the run,
pause the run so it can be resumed without re-establishing everything, or notify *and* stop.

**Why this priority**: Selectability is what makes the policy safe to adopt at all — the issue is
explicit that a bare "stop after N failures" is a liability on a roster that can self-heal. It is
P2 rather than P1 because an operator who is merely *notified* can already act; this story removes
the need for them to be awake to do so.

**Independent Test**: Run the same failing queue four times with each action, and confirm: `notify`
leaves it Running; `stop` ends the run with a distinguishable stop reason; `pause` leaves the run
alive but idle and resumable; `notifyAndStop` does both.

**Acceptance Scenarios**:

1. **Given** a policy with action `stop`, **When** the threshold is reached, **Then** the run
   terminates and its execution-log record attributes the stop to the failure policy rather than to
   an operator or a connection loss.
2. **Given** a policy with action `pause`, **When** the threshold is reached, **Then** the run stops
   firing sequences but stays alive, reports itself as paused with the reason and the instant, and
   fires nothing further until it is resumed.
3. **Given** a paused run, **When** the operator resumes it, **Then** it continues executing, its
   consecutive-failure count is cleared, and the policy can trip again later.
4. **Given** a policy with action `notifyAndStop`, **When** the threshold is reached, **Then** a
   notification is delivered *and* the run terminates, and the notification is delivered even if the
   run terminates immediately after.
5. **Given** any tripped action, **When** the notification endpoint is unreachable, misconfigured or
   slow, **Then** the configured non-notify part of the action still happens and the run is never
   crashed or blocked by the delivery attempt.

---

### User Story 3 - An authored sequence raises its own alert (Priority: P3)

A sequence author adds an explicit "notify" step to a sequence, so the escalation lives inside the
committed, reviewable sequence artifact instead of in service configuration. A guard sequence that
detects an unrecognised screen can then say so out loud, at the exact point it gives up, with a
message the author wrote.

**Why this priority**: The filing repository calls this "the most attractive of the three" shapes
because it keeps escalation in version control next to the logic that decides to escalate. It is
P3 because it is additive: the policy in Stories 1 and 2 already covers the unattended-outage case
this issue was filed about, and this story is valuable mainly to authors who want finer control.

**Independent Test**: Author a sequence containing a notify step with a custom message, run it, and
observe the notification arrive with that message and the originating sequence identified — then
confirm a failed delivery leaves the step (and the sequence) unaffected.

**Acceptance Scenarios**:

1. **Given** a sequence containing a notify step, **When** the sequence runs, **Then** a
   notification carrying the author's message and the originating sequence is delivered.
2. **Given** a notify step whose delivery fails, **When** the sequence runs, **Then** the step does
   not fail the sequence and execution continues to the next step.
3. **Given** a sequence saved with a malformed notify step, **When** it is saved, **Then** it is
   rejected at save time with an actionable message rather than failing at run time.

---

### Edge Cases

- **Threshold of zero or negative**: a policy configured with a non-positive count is invalid and is
  rejected when the queue is saved, rather than tripping on every cycle.
- **A policy attached to a non-cycling queue**: a non-cycling run completes at most one cycle, so a
  threshold above one can never trip. This is allowed (it is not an error) but the policy simply
  never fires; the feature does not invent cycles that the engine does not count.
- **An empty roster**: the engine counts an empty template as one completed, *successful* cycle, so
  an idle-but-alive queue never accumulates failures and never trips a policy.
- **A cycle interrupted by a stop or a lost connection**: it is never completed, so it is neither a
  success nor a failure and does not move the counter in either direction.
- **The notification endpoint hangs**: each attempt is abandoned after its 5-second timeout and the
  notification after its second attempt; the run loop never waits on it indefinitely.
- **A timer comes due while the run is paused**: nothing fires during the pause. On resume the
  firing is evaluated as normally due — a time-of-day slot not yet fired today fires, an elapsed
  relative or live schedule fires at once. Nothing is recorded as a skipped firing.
- **A paused run is left paused for days**: it holds its device session and performs no work; the
  operator ends it with the existing stop action, exactly as for any other run.
- **The notification endpoint returns an error status**: the failure is recorded for the operator
  but is not retried indefinitely and does not change the queue's execution outcome.
- **A queue restarted after a policy stopped it**: the new run starts from zero — no failure history
  survives a run, consistent with all other run state.
- **The policy trips on the same cycle the run is stopping anyway**: at most one policy trip is
  evaluated per completed cycle, and a run already terminating is not stopped twice.
- **Two queues failing at once**: each queue evaluates and notifies independently; one queue's
  notification cannot delay or suppress another's.

## Requirements *(mandatory)*

### Functional Requirements

**Failure policy configuration**

- **FR-001**: A queue MUST be able to carry an optional failure policy consisting of a consecutive
  failed-cycle threshold and an action to take when that threshold is reached.
- **FR-002**: The policy's action MUST be selectable from: notify only, stop the run, pause the run,
  or notify and stop.
- **FR-003**: The policy MUST be persisted with the queue's configuration and MUST survive a service
  restart, unlike the run-time failure counts it is evaluated against.
- **FR-004**: A queue with no policy configured MUST behave exactly as it does today: no evaluation,
  no notification, no change to when or whether sequences run.
- **FR-005**: A policy with a non-positive threshold MUST be rejected when the queue is saved, with
  an actionable message naming the offending value.
- **FR-006**: A policy whose action can notify MUST resolve a destination for the notification; if
  neither the policy nor the service-wide configuration supplies one, saving the policy MUST be
  rejected with an actionable message rather than silently never notifying.

**Policy evaluation**

- **FR-007**: The policy MUST be evaluated against the count of consecutive failed cycles already
  maintained for the run, at the point a cycle completes — not against individual sequence failures.
- **FR-008**: A successful cycle MUST reset the consecutive-failure count to zero, and MUST re-arm a
  policy that has already tripped so a later episode notifies again.
- **FR-009**: A policy MUST trip at most once per failure episode: reaching the threshold notifies
  once, and continued failures past the threshold MUST NOT produce a notification per cycle.
- **FR-010**: Evaluation MUST NOT change which sequences run, in what order, or when, beyond the
  stop or pause that the operator explicitly selected.
- **FR-011**: A failure in evaluation or delivery MUST NOT terminate the run, throw out of the run
  loop, or block it for longer than a bounded delivery timeout.

**Outbound notification**

- **FR-012**: When a notifying action trips, the system MUST deliver an outbound notification to the
  configured destination.
- **FR-013**: The notification MUST carry at minimum: the queue's identifier and display name, the
  roster entry and sequence that failed, the failure status or message, and the consecutive-failure
  count that tripped the policy.
- **FR-014**: The notification MUST also identify the event's cause (a tripped policy versus an
  author's explicit alert) and the instant it was raised, so a receiver can distinguish and order
  events without parsing prose.
- **FR-014a**: The notification payload MUST be a stable, documented contract carrying an explicit
  schema version, so a receiver can be written against it and a future change to it is detectable
  rather than silent.
- **FR-015**: Delivery MUST be bounded in time and MUST NOT be retried indefinitely: at most one
  retry after a short fixed backoff, and each attempt abandoned after a bounded timeout well shorter
  than a typical cycle.
- **FR-015a**: An undeliverable notification MUST be recorded in two places an operator can reach:
  the structured application log, and a last-notification block on the running queue's live health
  so the failure is visible through the API without reading log files.
- **FR-016**: Notification destinations MUST be operator-configured only — never derived from a
  sequence's runtime data or from a game screen — and MUST be rejected at configuration time unless
  they are well-formed absolute destinations.
- **FR-016a**: A destination MUST optionally accept an operator-configured static authentication
  header, so a receiver outside the local machine can verify the caller. Absent by default; when
  configured it MUST be sent with every notification to that destination and MUST NOT be echoed back
  in any health, log, or API response.

**Stop and pause actions**

- **FR-017**: A tripped stop action MUST terminate the run and MUST record a stop reason that is
  distinguishable from an operator stop, a completed run, and a connection failure.
- **FR-018**: A tripped pause action MUST leave the run alive but idle: no further sequence firings,
  no scheduled work performed, and the run still reported as the queue's current run.
- **FR-018a**: A paused run MUST hold *before* evaluating which firings are due, not suppress them
  individually. On resume, due-ness is therefore re-evaluated normally: a time-of-day slot not yet
  fired today fires, and an elapsed relative or live schedule fires immediately. No firing is
  recorded as skipped, and no new skip-bookkeeping is introduced.
- **FR-019**: A paused run MUST report that it is paused, when it was paused, and why.
- **FR-020**: A paused run MUST be resumable by explicit operator action, and resuming MUST clear the
  consecutive-failure count so the policy evaluates the resumed run afresh.
- **FR-021**: A paused run MUST still be stoppable by the existing stop action, and stopping it MUST
  behave as stopping any other run.

**Author-raised alerts**

- **FR-022**: A sequence MUST be able to contain a step whose effect is to raise a notification with
  an author-supplied message.
- **FR-023**: A notify step MUST be validated when the sequence is saved, so a malformed one is
  rejected at authoring time rather than at run time.
- **FR-024**: A notify step whose delivery fails MUST NOT fail the step or the enclosing sequence.

**Visibility**

- **FR-025**: The live health already published for a running queue MUST additionally report whether
  a policy is configured, whether it has currently tripped, and the paused state when paused — so an
  operator can see the policy's state without waiting for it to fire.
- **FR-026**: The live health MUST report the outcome of the most recent notification attempt for
  the run — when it was attempted, whether it succeeded, and the failure detail when it did not —
  without ever exposing a configured authentication header value.

### Key Entities

- **Failure policy**: An optional part of a queue's persisted configuration. Holds the consecutive
  failed-cycle threshold, the selected action, and optionally the notification destination that
  overrides the service-wide default.
- **Notification destination**: An operator-configured absolute endpoint, plus an optional static
  authentication header (name and value) sent with every notification to it. Configured
  service-wide as a default and optionally overridden per policy. The header value is secret: it is
  never returned by any API response, health block, or log line.
- **Notification event**: The payload that leaves the service. A stable, versioned contract
  identifying the queue, the failing entry and sequence, the failure detail, the consecutive-failure
  count, the cause, and the instant it was raised.
- **Policy state**: Per-run, in-memory: whether the policy has already tripped for the current
  failure episode, whether the run is paused (and when and why), and the outcome of the most recent
  notification attempt. Discarded with the run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator learns that a production roster has stopped working within the time it
  takes the configured number of cycles to fail, instead of when a person happens to look at a
  device — reducing the 2026-09-14 outage's 44-hour detection gap to under 15 minutes for a roster
  whose cycles take a few minutes.
- **SC-002**: The alert identifies the failing roster, entry and sequence precisely enough that the
  operator knows what broke without first opening the machine.
- **SC-003**: A sustained outage produces one alert per failure episode, not one per cycle, so an
  overnight outage does not bury the signal it was meant to raise.
- **SC-004**: A queue with no policy configured shows no observable change in behaviour, timing, or
  output compared with today.
- **SC-005**: An unreachable, slow, or misbehaving notification destination never stops, stalls, or
  crashes a production run; a run whose destination is a black hole completes its cycles on the same
  schedule as one with no policy at all.
- **SC-006**: An operator can choose, per roster, whether a persistent failure keeps trying, halts,
  or parks the device — and a parked roster can be put back to work without reconfiguring it.
- **SC-007**: A receiver written once against the published payload keeps working across service
  upgrades, or is told by an explicit version change that the contract moved.
- **SC-008**: When a notification cannot be delivered, an operator can establish that fact — and the
  reason — from the running queue's own status, without opening a log file on the host.

## Assumptions

- **A-001**: "Consecutive failed cycles" reuses the count feature 086 already maintains: a cycle
  fails when at least one entry in it failed, and only *completed* cycles count. No second notion of
  a cycle is introduced.
- **A-002**: The notification destination is an operator-configured HTTP endpoint. A service-wide
  default destination serves every queue; a queue's policy may name its own to override it. This is
  the shape the issue proposed ("`POST <configured url>`") and needs no new delivery infrastructure.
- **A-003**: Notification delivery is fire-and-forget from the run loop's perspective — the run does
  not await a receiver's success. Each attempt times out after 5 seconds, and at most 2 attempts are
  made (one retry after a short fixed backoff) before the notification is abandoned and recorded as
  undeliverable.
- **A-004**: Policy state (tripped / paused) is per-run and in-memory, like all other run state, and
  does not survive a service restart. Only the policy *configuration* is persisted.
- **A-005**: The set of actions is exactly the four the issue named. A "notify and pause" combination
  is not introduced, because the issue did not ask for it.
- **A-006**: The failing entry and sequence reported in a notification are taken from the failed
  entries of the cycle that tripped the policy; when several failed, the notification identifies the
  first failure and the total count of failed entries in that cycle.

## Dependencies

- Feature 086 (`specs/086-queue-cycle-observability/`) — supplies the per-run cycle ledger, the
  consecutive-failure count, and the live health block this feature extends. This feature is not
  implementable without it and must not duplicate it.

## Out of Scope

- Changing per-sequence failure semantics: a failed sequence still does not abort the rest of its
  cycle. The policy operates at cycle granularity only.
- The run-detail `/subtree` projection, the queue *list* response, and the web UI.
- The cancelled-versus-failed distinction for sequence outcomes.
- Re-implementing per-cycle observability, which feature 086 shipped.
- Persisting run health or cycle history across a service restart.
- Notification transports other than an outbound HTTP POST (no email, chat-app, or SMS integrations).
- Rate limiting or aggregating notifications across multiple queues.
