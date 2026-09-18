# Feature Specification: Queue sessions survive idle gaps between scheduled runs

**Feature Branch**: `104-queue-session-idle-eviction`  
**Created**: 2026-09-18  
**Status**: Implemented  
**Input**: GitHub issue #217 (https://github.com/bbqf/GameBotAI/issues/217) — "B-018: a queue idle for ~an hour fails its next scheduled run with emulator connection lost, while ADB still has the device." Closes #217.

## Background

A running queue binds one emulator session when it starts and holds it for the whole run. The service
separately retires any session that has seen no activity for the configured idle timeout (30 minutes by
default). A queue that sits idle between self-booked firings — the pattern hourly and 8-hourly
scheduling was built for — touches nothing for that whole gap, so its own session is retired from under
it. At the next wake-up the queue checks its session before running anything, finds it gone, and fails
the whole run with `emulator connection lost mid-run ('<serial>')`. The queue stops, its pending bookings
are lost, and its before-each-run entries (the ones meant to wake a slept display or restart a stopped
instance) never get to run, because the check that fails sits in front of them.

Observed in production on 2026-09-17 (four failures, two emulators, two independent sessions) and
reproduced on throwaway queues with and without the idle pause: both failed identically at the first
wake-up after ~50 idle minutes, while ADB listed the device as connected the whole time with an unchanged
transport id. A 2-minute idle wakes correctly. The screenshot endpoint for the same serial answered 200 at
idle minutes 20 and 30 and 404 from minute 40 on — the exact footprint of a 30-minute session retirement.

**Terminology**: *idle retirement* in this spec is the session manager's idle **eviction** sweep
(`Service:Sessions:IdleTimeoutSeconds`); the plan and tasks use "eviction" for the same thing.

## Clarifications

### Session 2026-09-18

Resolved autonomously by the spec-kit pipeline (no manual review); each answer carries its rationale.

- Q: How is a session marked as queue-owned — inferred from its game label (`queue:<id>`) or set explicitly when the queue creates it? → A: Set explicitly at creation by the queue. Rationale: inferring ownership from a free-text label would let any ad-hoc session named `queue:…` escape retirement, and the label format is not a contract.
- Q: How many re-bind attempts does one pre-firing check make? → A: One. Rationale: session creation already validates the device against ADB; if it fails the device is genuinely unbindable at that instant, and a failed run is the existing, visible outcome — a retry loop would add latency and a new hang mode for no demonstrated benefit.
- Q: Does the idle-pause resume step (foregrounding the game before a due firing) also re-bind? → A: No; re-binding happens only at the pre-firing session checks. Rationale: with queue-owned sessions exempt from retirement the resume step's session is always present; the resume step is already best-effort, and the before-each-run entries that run after a re-bind exist precisely to wake the game.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A scheduled queue wakes up after a long idle and does its work (Priority: P1)

An operator starts a queue whose only work is a timer entry that re-books itself an hour (or eight hours)
ahead, plus before-each-run guard entries. The queue runs its start batch, sits idle, and at the booked
time runs its guard entries and then the task — however long the idle gap was — and books the next run.

**Why this priority**: Without it every production queue dies about an hour after it is started and does
nothing further until restarted by hand, which makes unattended operation impossible.

**Independent Test**: Start a queue with a self-rescheduling timer entry and a before-each-run entry,
let it idle past the session idle timeout (shortened in the test), and assert that at the wake-up both
entries run, the run does not fail, and the queue is still running afterwards.

**Acceptance Scenarios**:

1. **Given** a running queue whose next firing is booked further ahead than the session idle timeout,
   **When** the booked time arrives, **Then** the queue's before-each-run entries run, the booked
   sequence runs, and the queue keeps running.
2. **Given** a running queue that has been idle longer than the session idle timeout, **When** anything
   else in the service lists or looks up sessions during that idle gap, **Then** the queue's session is
   still present and still bound to the queue's emulator.
3. **Given** a queue that stops (operator stop, completed run, failure), **When** it stops, **Then** its
   session is released exactly as today.

---

### User Story 2 - A queue recovers when its session binding is gone but the device is still there (Priority: P2)

If, at a firing, a queue finds its session missing for any reason while its emulator is still connected,
it binds a fresh session to the same emulator and carries on, instead of failing the whole run.

**Why this priority**: Defense in depth. Story 1 removes the known cause; this keeps any other way of
losing the binding from silently killing a long-lived queue. It is secondary because it only matters if
the binding is lost some other way.

**Independent Test**: With a running queue, remove its session between firings (simulating eviction),
then let the next firing come due; assert that a new session is bound to the same emulator, the firing
and its before-each-run entries run, and the queue is still running.

**Acceptance Scenarios**:

1. **Given** a running queue whose session has disappeared between firings and whose emulator is still
   connected, **When** the next firing comes due, **Then** the queue binds a new session to the same
   emulator (with screen capture restarted for it), runs the firing, and keeps running.
2. **Given** a running queue whose session has disappeared and whose emulator is genuinely gone (it can
   no longer be bound), **When** the next firing comes due, **Then** the run fails with the existing
   `emulator connection lost mid-run ('<serial>')` reason, as today.

---

### User Story 3 - The screenshot endpoint tells the truth about an unbound device (Priority: P3)

An operator polling `GET /api/emulator/screenshot?serial=<serial>` for an idle queue's emulator keeps
getting screenshots. When no session is bound to a serial, the error says that no session is bound to
the device, not something that reads as "no such device".

**Why this priority**: Diagnostic quality. With Story 1 in place the reported 404 no longer happens for a
running queue; the remaining improvement is the wording of the genuine case.

**Independent Test**: Request a screenshot by a serial that has no bound session and assert the 404's
message says no session is bound to that device and how to get one.

**Acceptance Scenarios**:

1. **Given** a queue idle for longer than the session idle timeout, **When** a screenshot is requested
   for its emulator's serial, **Then** a screenshot is returned.
2. **Given** a serial with no session bound to it, **When** a screenshot is requested for it, **Then** the
   response is 404 with error `session_not_found` and a message stating that no running session is bound
   to that device and that one is created by starting a session or a queue on it.

### Edge Cases

- A queue whose session disappears in the middle of a sequence (not between firings) keeps today's
  behaviour for that sequence; the recovery applies at the pre-firing checks.
- Re-binding must not create a second session for the same queue: the old session id is gone, and the
  queue's handle and all later firings use the new session id.
- Re-binding counts against the session capacity limit like any session; if capacity is exhausted the
  re-bind fails and the run fails with the connection-lost reason (the device cannot be bound).
- Several firings due at the same wake-up re-bind at most once.
- Ad-hoc sessions created through the sessions API keep today's idle retirement, unchanged.
- The idle-pause hold (`pauseWhenIdle`) is unchanged; it simply no longer loses its session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A session created by a queue run MUST be exempt from idle retirement for as long as it
  exists; it ends only when the queue stops it (or an explicit stop removes it). The queue marks the
  session as queue-owned explicitly when it creates it (including on a re-bind); ownership is never
  inferred from the session's game label.
- **FR-002**: Sessions not owned by a queue (ad-hoc API sessions) MUST keep today's idle retirement,
  with the same timeout and the same triggers.
- **FR-003**: At every point where a queue checks its session before a firing, if the session is missing,
  the queue MUST attempt to bind a new session to the same emulator serial, restart screen capture for
  it, and continue with the new session for the rest of the run. Each check makes at most one re-bind
  attempt. The idle-pause resume step does not re-bind.
- **FR-004**: The queue MUST fail the run with the existing `emulator connection lost mid-run ('<serial>')`
  reason only when that re-bind fails (device not listed, no devices, or capacity exhausted).
- **FR-005**: A successful re-bind MUST be logged (queue id, serial, old and new session id) so an
  operator can see it happened.
- **FR-006**: Because the re-bind happens at the session check that precedes each before-each-run entry,
  before-each-run entries MUST run at a wake-up whose session had gone missing but could be re-bound.
- **FR-007**: The 404 from `GET /api/emulator/screenshot?serial=` for a serial with no bound session MUST
  keep error code `session_not_found` and state that no running session is bound to that device, and
  that starting a session or a queue on it creates one.
- **FR-008**: Existing behaviour MUST otherwise be unchanged: queue start, stop, capacity accounting,
  failure reasons for an emulator that cannot be reached at start, idle pause, and ad-hoc session
  lifecycle.

### Key Entities

- **Emulator session**: the service's binding of a game run to one emulator serial. Gains an ownership
  marker distinguishing queue-owned sessions (never idle-retired) from ad-hoc ones (idle-retired).
- **Queue run handle**: the running queue's state; its session id changes when a re-bind happens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A queue idle for any duration longer than the session idle timeout runs its next booked
  firing, including its before-each-run entries, in 100% of regression-test runs.
- **SC-002**: Zero queue runs end with "emulator connection lost" while the emulator is still connected
  and bindable (covered by the re-bind regression test).
- **SC-003**: A queue whose emulator is genuinely gone still fails with the existing message (no silent
  hang, no changed wording).
- **SC-004**: Ad-hoc sessions are still retired after the idle timeout (existing and new tests pass).

## Assumptions

- The idle-retirement timeout exists to reclaim abandoned ad-hoc sessions; a queue-owned session is never
  abandoned while its queue runs, because the queue always stops it in its teardown.
- "Genuinely gone" is decided by the same device check session creation already uses (the serial must be
  listed by ADB in `device` state); no new device probing is added.
- Surviving a genuine device loss without stopping the queue is out of scope (the issue lists it only as
  a fallback, and the re-bind covers the reported case).

## Non-Goals

- No change to the idle-pause (`pauseWhenIdle`) feature.
- No keep-alive entry workaround.
- No changes to PNS authoring data or sequences.
- No ADB server management.
- No change to the default idle timeout or to ad-hoc session retirement.
