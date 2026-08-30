# Feature Specification: Concurrent Queue Execution

**Feature Branch**: `079-concurrent-queue-execution`
**Created**: 2026-08-30
**Status**: Draft
**Input**: User description: "I want to be able to run multiple queues at the same time. Check why these are interfereing each other now and implement a robust solution."

## Overview

Today an operator can press Start on two queues and both will report themselves as Running, but the
runs do not behave independently. The second run silently corrupts the first: screen observations are
read from whichever device happens to be listed first, several kinds of execution step start failing
outright with "no session available", and two queues bound to the same emulator physically fight over
the same screen. This feature makes concurrent queue runs a first-class, correct capability: every run
observes and acts on its own device only, and two runs may never share one device.

### Why runs interfere today (investigation result)

The investigation of the current implementation found five independent defects. They are recorded here
because the acceptance criteria below are written against them.

1. **Screen observation is not session-scoped.** The single shared screen provider answers every
   "what is on screen right now" question by picking the *first* running session it finds, regardless
   of which run is asking. Every image match, text/OCR match, wait-for-image, detect-and-tap and
   game-readiness probe therefore reads a possibly foreign device once a second run exists, and which
   device wins is not deterministic.
2. **"Exactly one running session" fallbacks.** Several execution steps (foreground-the-game,
   go-to-home-screen, primitive tap/swipe/key, force-execute-command, trigger-driven command
   execution) resolve their device by requiring that exactly one session exists, and hard-fail when
   the count is not one. Starting a second queue therefore breaks steps in the first.
3. **No exclusive claim on a device.** Nothing prevents two queues configured with the same emulator
   from running at once. Both drive the same physical screen, so navigation, back-outs and idle
   pauses from one run corrupt the other.
4. **A low, silent session capacity ceiling.** The number of simultaneously open device sessions is
   capped at a small fixed number; exceeding it fails a queue run with an opaque capacity error rather
   than an actionable message.
5. **Arbitrary device selection in operator tooling.** Screen capture and crop tooling picks an
   arbitrary running session, so with two runs active the operator can be shown, and can crop
   reference images from, the wrong device.

## Clarifications

### Session 2026-08-30

- Q: When a queue is started whose bound emulator is already claimed by a running queue, what should happen? → A: Refuse the start with an explanatory error; no auto-queueing, no waiting. (Rationale: two automations driving one screen cannot both be correct, and a silent wait would hide a misconfiguration.)
- Q: What default limit should apply to concurrently open device sessions? → A: 8, configurable. (Rationale: today's default of 3 is below a plausible emulator count and turns a configuration limit into a surprise run failure.)
- Q: How should a device-affecting step behave when its context carries no session and more than one session is active? → A: Fail the step with an explicit "specify a device; N sessions active" message. (Rationale: an arbitrary pick is exactly the current defect; failing loudly is diagnosable.)
- Q: How should operator screen-capture and crop tooling behave with no explicit selector while several sessions are active? → A: Return an explicit ambiguity error; single-session behavior is unchanged. (Rationale: authoring reference images from the wrong device silently poisons every sequence that uses them.)
- Q: What identifies a device for exclusive-claim purposes? → A: The queue's bound ADB emulator serial, trimmed and compared case-insensitively. (Rationale: the serial is already the immutable device identity on a queue, and instance name/index are optional cold-start hints rather than identity.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two queues on two emulators run independently (Priority: P1)

An operator has two emulator instances, each with its own queue and its own template. They start both
queues. Each run drives only its own emulator: every screen check, tap, swipe, key press and OCR read
in queue A is evaluated against emulator A's screen, and likewise for B. Neither run's outcome changes
because the other is running.

**Why this priority**: This is the feature. Without it, concurrency produces wrong actions on the wrong
device, which is worse than not being able to run concurrently at all.

**Independent Test**: Start two queues bound to two distinct emulator serials whose screens differ, and
have each run a sequence that waits for an image only present on its own device. Both runs succeed,
and no run reports a detection sourced from the other device.

**Acceptance Scenarios**:

1. **Given** queue A bound to device A and queue B bound to device B, both linked to templates whose
   sequences use image detection, **When** both queues are started and run concurrently, **Then** each
   run's image detections resolve against its own device's screen and both runs complete without
   device-related failures.
2. **Given** two queues running concurrently, **When** a sequence step that acts on the device
   (tap, swipe, key, foreground-the-game, go-to-home-screen) executes in either run, **Then** the step
   executes against that run's own device and never reports "no session available".
3. **Given** two queues running concurrently, **When** a sequence in run A performs an OCR or text
   match, **Then** the recognized text comes from device A's screen.
4. **Given** queue A is running, **When** queue B is started and later stopped, **Then** run A
   continues uninterrupted and its device session is untouched.

---

### User Story 2 - The same emulator cannot be claimed by two runs (Priority: P1)

An operator tries to start a second queue that is bound to an emulator already being driven by a
running queue. The system refuses the start and explains which queue currently holds that emulator,
instead of allowing both runs to fight over one screen.

**Why this priority**: Same-device concurrency cannot be made correct by scoping alone. Two runs
tapping one screen is inherently destructive, so the system must prevent it. It is equal in priority to
Story 1 because without it "run multiple queues" silently means "corrupt one game".

**Independent Test**: Configure two queues with the same emulator serial, start the first, then start
the second and observe the refusal and its message; stop the first and confirm the second then starts.

**Acceptance Scenarios**:

1. **Given** queue A is running on device X, **When** the operator starts queue B, also bound to device
   X, **Then** the start is refused with a message naming device X and the queue currently holding it,
   and queue B's status remains Stopped.
2. **Given** queue A is running on device X and the start of queue B was refused, **When** queue A
   stops, **Then** starting queue B succeeds.
3. **Given** queue A's run ends for any reason (completion, manual stop, failure, or service
   shutdown), **When** the run finishes, **Then** its claim on device X is released so another queue
   can claim it.
4. **Given** two queues bound to different devices, **When** both are started, **Then** neither start
   is refused.

---

### User Story 3 - Concurrency is visible and diagnosable (Priority: P2)

An operator running several queues can see, at a glance, which queues are running, which device each
one holds, and what each is doing right now. When a start is refused or a run fails for a
concurrency-related reason, the reason is stated in plain language in both the response and the
execution log.

**Why this priority**: Correct isolation without visibility leaves the operator unable to tell whether
concurrency is working. Valuable, but the runs are already correct without it.

**Independent Test**: Start two queues, read the monitor view, and confirm each running queue shows its
own device and its own current sequence; then attempt a same-device start and confirm the refusal is
both returned to the caller and recorded.

**Acceptance Scenarios**:

1. **Given** two queues running concurrently, **When** the operator views queue monitoring, **Then**
   each running queue reports its own bound device and its own currently-executing sequence, with no
   cross-contamination between the two.
2. **Given** a start refused because the device is already claimed, **When** the operator inspects the
   result, **Then** the refusal reason distinguishes "this queue is already running" from "this queue's
   device is in use by another queue".
3. **Given** a run that cannot start because the concurrent-session limit is reached, **When** the run
   is finalized, **Then** the recorded failure names the limit and the number of runs currently active
   rather than an opaque error code.
4. **Given** a start refused because the device is claimed, **When** the operator checks the service's
   application log, **Then** an entry names the refused queue, the device and the holding queue.

---

### User Story 4 - Operator tooling targets a chosen device (Priority: P3)

While multiple queues are running, an operator using the live screen view and the crop/reference-image
tooling can specify which device to look at, and the tooling never silently substitutes another.

**Why this priority**: This is authoring convenience, not run correctness. Runs are already isolated
without it, but authoring against the wrong device produces bad reference images.

**Independent Test**: With two runs active, request a screen capture for a named device and confirm the
returned image is that device's; request one without naming a device and confirm the response is
unambiguous rather than an arbitrary pick.

**Acceptance Scenarios**:

1. **Given** two running device sessions, **When** the operator requests a screen capture for an
   explicitly named session or device, **Then** the returned image is from that device.
2. **Given** two running device sessions, **When** the operator requests a screen capture without
   naming one, **Then** the system responds with an explicit "which device?" style error rather than
   arbitrarily choosing one.
3. **Given** exactly one running device session, **When** the operator requests a screen capture
   without naming one, **Then** the existing single-device behavior is preserved and the capture
   succeeds.

---

### Edge Cases

- **A run's device disappears mid-run** (emulator closed, ADB drops the device): only that run fails,
  with a device-lost reason; other concurrent runs are unaffected and keep running.
- **Two queues bound to the same device are started at the same instant**: exactly one wins the claim;
  the other is refused. There is no window in which both hold the device.
- **A run fails or throws before it finishes starting**: its device claim is released, so a retry or a
  different queue can claim the device immediately.
- **The service shuts down with several runs active**: every run is cancelled and every claim released;
  no claim survives a restart.
- **More queues are started than the concurrent-session limit allows**: the runs that fit start
  normally; each run beyond the limit fails with a message naming the limit, and the successful runs
  are unaffected.
- **A queue's bound device is blank or unknown to ADB**: that run fails with the existing
  device-not-reachable reason; it does not consume or block another queue's claim.
- **One run enters an idle pause and backs its game out** while another run is active: the back-out
  affects only the pausing run's own device.
- **A sequence started manually (outside any queue) while queues are running**: it must either be given
  an explicit device or, when exactly one device session exists, keep today's behavior; it must never
  silently borrow a queue run's device.
- **A run's device session is evicted for idleness while the run is still alive**: the run detects the
  loss and fails with the existing connection-lost reason rather than silently acting on another
  device.

## Requirements *(mandatory)*

### Functional Requirements

#### Device-scoped observation

- **FR-001**: Every screen observation made on behalf of a queue run (image match, text/OCR match,
  wait-for-image, detect-and-tap, template detection, game-readiness probe) MUST be evaluated against
  the device bound to that run, and MUST NOT be able to return another run's device screen.
- **FR-002**: When an execution context provides a device session, the system MUST use that session for
  screen observation; it MUST NOT fall back to "the first running session".
- **FR-003**: When a screen observation is requested for a session that has no cached frame yet or is
  no longer running, the system MUST report that condition to the caller (no frame available) rather
  than substituting another session's frame.
- **FR-004**: The existing behavior for a single running session, and for stub/test mode where no real
  device exists, MUST be preserved.

#### Device-scoped action

- **FR-005**: Every device-affecting sequence step (primitive tap/swipe/key, foreground-the-game,
  go-to-home-screen, command execution, emulator lifecycle actions) MUST act on the device session
  supplied by its execution context.
- **FR-006**: The system MUST remove the requirement that exactly one device session exists in order to
  resolve a device for a step whose context already carries one. Steps invoked with an explicit session
  MUST succeed regardless of how many other sessions are active.
- **FR-007**: When a step has no session in its context, the system MUST resolve a device only if
  exactly one device session is active; when more than one is active it MUST fail the step with an
  explicit message naming the number of active sessions and stating that a device must be specified,
  and MUST NOT select one arbitrarily.

#### Exclusive device claim

- **FR-008**: A device MUST be held by at most one queue run at a time. A device's identity for
  claiming purposes is the queue's bound ADB emulator serial, trimmed of surrounding whitespace and
  compared case-insensitively; the optional emulator instance name/index are cold-start hints and MUST
  NOT take part in device identity.
- **FR-009**: Starting a queue whose device is already claimed by another running queue MUST be refused
  immediately without starting a run, without altering the claiming run, and without changing the
  refused queue's status. The system MUST NOT wait for, auto-queue, or retry the refused start.
- **FR-010**: The refusal MUST be distinguishable from "this same queue is already running" and MUST
  identify the device and the queue that currently holds it.
- **FR-011**: A run MUST release its device claim when it ends for any reason: normal completion,
  manual stop, failure, cancellation, or host shutdown.
- **FR-012**: Claim acquisition MUST be atomic, so that two simultaneous starts for the same device
  cannot both succeed.
- **FR-013**: Device claims MUST be in-memory only and MUST NOT survive a service restart.
- **FR-014**: Queues bound to different devices MUST NOT block one another.

#### Concurrency capacity and diagnostics

- **FR-015**: The number of concurrently open device sessions MUST be configurable and MUST default to
  8 (raised from today's 3), so the configured limit is not the practical ceiling on concurrent queue
  runs for a realistic number of emulators.
- **FR-016**: When a run cannot start because the session capacity is reached, the failure recorded and
  returned MUST name the configured limit and the number of sessions currently open.
- **FR-017**: Concurrency-related run **failures** (device claim lost, capacity reached, device
  unreachable) MUST appear in the affected queue's execution log in plain language. A refused **start**
  produces no run and therefore no execution-log entry; it MUST instead be reported in plain language
  in the response to the caller (FR-010) and recorded in the service's application log naming the
  queue, the device and the holding queue.
- **FR-018**: Queue monitoring MUST report, per running queue, its own bound device and its own current
  activity, with no values derived from another run.

#### Isolation of run state

- **FR-019**: Per-run state (schedule registers, self-reschedule registers, idle-pause state, current
  sequence indicator, live schedules) MUST remain isolated per run and MUST be safe to read and write
  concurrently across runs.
- **FR-020**: A failure, cancellation or device loss in one run MUST NOT change the status, schedule or
  outcome of any other run.
- **FR-021**: Execution-log writes from concurrent runs MUST not interleave into one another's entries;
  each run's entries MUST remain attributable to that run.

#### Operator tooling

- **FR-022**: Screen-capture and crop tooling MUST accept an explicit device or session selector and
  MUST use it when supplied.
- **FR-023**: When no selector is supplied and more than one device session is active, the tooling MUST
  return an explicit ambiguity error instead of choosing arbitrarily.
- **FR-024**: When no selector is supplied and exactly one device session is active, the tooling MUST
  behave as it does today.

### Key Entities

- **Queue run**: One in-flight execution of one queue. Owns exactly one device session, one device
  claim, one schedule state and one set of ephemeral registers, for the lifetime of the run.
- **Device session**: The live binding between a run and one emulator device serial, through which all
  observation and input for that run flows.
- **Device claim**: The exclusive, in-memory reservation of one emulator serial by one queue run.
  Created when a run starts, released when the run ends.
- **Screen observation context**: The device-scoped means by which a step obtains the current screen.
  Always derived from the run's own device session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two queues bound to two different emulators can run to completion at the same time, and
  every screen observation in each run resolves against that run's own device (0 cross-device
  observations).
- **SC-002**: With two or more runs active, no device-affecting step fails with a "no session
  available" or "missing session context" reason when its run holds a healthy device session.
- **SC-003**: 100% of attempts to start a second queue on an already-claimed device are refused, with a
  message that names the device and the holding queue.
- **SC-004**: After any run ends (completion, stop, failure, or shutdown), its device becomes claimable
  again within one start attempt.
- **SC-005**: Stopping or failing one run leaves every other concurrent run's status and remaining
  schedule unchanged.
- **SC-006**: The number of queues that can run at once is limited only by the number of distinct
  emulators the operator has configured, up to the configured session limit, which defaults to at least
  eight.
- **SC-007**: Queue monitoring shows a correct, per-run device and current activity for every running
  queue simultaneously.
- **SC-008**: Operator screen capture returns the requested device's screen 100% of the time when a
  device is named, and never silently returns a different device when one is not.

## Assumptions

- "Multiple queues at the same time" means one queue run per emulator, with several emulators running
  in parallel. Two runs sharing a single physical device is treated as an error to prevent, not a
  scenario to support, because two automations driving one screen cannot both be correct.
- Emulator serials are the identity of a device for claiming purposes; a queue's bound serial is
  already immutable after creation.
- The existing single-queue behavior, including feature 059/060/065/072/073/074/075/077/078 semantics,
  is preserved unchanged; this feature only removes cross-run coupling.
- Persistence is not required for claims: a service restart ends all runs, so all claims are naturally
  void.
- The background trigger worker and other non-queue consumers keep their current behavior except where
  it depends on "the only running session"; those paths follow FR-007.

## Out of Scope

- Allowing two queue runs to time-share one emulator (interleaved or cooperative multitasking on one
  device).
- Automatically queueing or retrying a start that was refused because the device is claimed.
- Persisting run state or claims across service restarts.
- Multi-machine or distributed queue execution.
- Changing the scheduling semantics of any existing schedule type.
