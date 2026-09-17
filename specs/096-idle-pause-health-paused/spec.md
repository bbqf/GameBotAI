# Feature Specification: Report Idle Pause in queue health

**Feature Branch**: `096-idle-pause-health-paused`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #199 (https://github.com/bbqf/GameBotAI/issues/199) — "B-015: health.paused stays false during an Idle Pause that /monitor reports". Closes #199.

## Background

A running queue can be paused in two different ways today:

- **Idle pause** (feature 073) — an opt-in, routine, self-releasing hold: when nothing is due for longer than the queue's idle threshold, the game is sent to the background and brought back when the next firing is due. The queue monitor (`GET /api/queues/{id}/monitor`) reports it as a current item with `scheduleKind: "IdlePause"`.
- **Failure-policy pause** (feature 087) — the run is parked after too many consecutive failed cycles and stays parked until an operator calls `POST /api/queues/{id}/resume`.

The queue detail's `health` block (`GET /api/queues/{id}`) has `paused`, `pausedAt` and `pauseReason`, but these reflect **only** the failure-policy pause. During an idle pause they read `paused: false, pausedAt: null, pauseReason: null`, while `/monitor` at the same moment reports `IdlePause`. The schema does not say that `paused` is limited to the failure policy, so consumers building an "is this farm correctly paused between runs" check read an idle-paused queue as not paused. Their workaround is to ignore `health.paused` and read `/monitor` instead.

## Clarifications

### Session 2026-09-17

- Q: Fix `health.paused` to include idle pauses, or only document it as failure-policy-only? → A: Include idle pauses, and add `pauseKind` to tell the two apart. Rationale: this is the issue's stated expected behaviour and removes the `/monitor` workaround. The existing failure-policy values are unchanged.
- Q: What exact text does `pauseReason` carry for an idle pause? → A: `idle pause: resumes at HH:mm` (service-local time of the current resume instant). Rationale: this follows the failure policy's `failure policy: …` prefix style, so a text check on the prefix works for both.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Health reports an idle pause (Priority: P1)

An operator (or an automated health check) polls a running queue's detail while the queue sits in an idle pause between scheduled firings. The `health` block says the queue is paused, since when, and why, agreeing with what the monitor shows.

**Why this priority**: This is the defect the issue reports; every health view built on `health` currently misreads an idle-paused queue.

**Independent Test**: Start a queue with idle pause enabled and a scheduled firing far enough in the future to trigger the pause; poll the queue detail while the monitor reports `IdlePause` and check the `health` pause fields.

**Acceptance Scenarios**:

1. **Given** a running queue in an idle pause, **When** the queue detail is read, **Then** `health.paused` is `true`, `health.pausedAt` is the instant the idle pause began, `health.pauseReason` is a non-empty text describing the idle pause, and `health.pauseKind` is `"idle"`.
2. **Given** a running queue in an idle pause whose resume time moves earlier (an earlier firing arrives), **When** the detail is read again, **Then** `health.pausedAt` is unchanged (still the start of the same pause).
3. **Given** a running queue whose idle pause has ended (the due firing is executing or the run moved on), **When** the queue detail is read, **Then** `health.paused` is `false` and `pausedAt`, `pauseReason` and `pauseKind` are `null`.
4. **Given** a running queue, **When** the monitor and the queue detail are read at the same moment, **Then** the monitor reports `IdlePause` exactly when `health.pauseKind` is `"idle"`.

---

### User Story 2 - The two pauses stay distinguishable (Priority: P2)

An operator who sees `health.paused: true` needs to know whether to act: a failure-policy pause needs a manual resume, an idle pause ends by itself.

**Why this priority**: Widening `paused` must not make a routine idle gap look like a failure-policy park (which would prompt a pointless resume or a false alarm).

**Independent Test**: Trip a `pause` failure policy on a running queue and read the detail; separately, read the detail of an idle-paused queue; compare `pauseKind` and `failurePolicyTripped`.

**Acceptance Scenarios**:

1. **Given** a run parked by a tripped `pause` failure policy, **When** the queue detail is read, **Then** `health.paused` is `true`, `pauseKind` is `"failurePolicy"`, and `pausedAt` and `pauseReason` are exactly what they are today.
2. **Given** a run in an idle pause, **When** the queue detail is read, **Then** `failurePolicyTripped` is unaffected by the idle pause and `pauseKind` is `"idle"`.
3. **Given** a run in an idle pause, **When** `POST /api/queues/{id}/resume` is called, **Then** it still reports `resumed: false` and the idle pause continues unchanged (resume releases only a failure-policy pause).

---

### User Story 3 - The schema says what the pause fields mean (Priority: P3)

A client author reading the published API description learns that `paused` covers both kinds of pause, what `pausedAt` and `pauseReason` hold for each, and that `pauseKind` tells them apart.

**Why this priority**: The issue explicitly asks for the meaning to be documented; the ambiguity is what produced the misreading.

**Independent Test**: Fetch the published API description and read the descriptions of the `health` pause fields.

**Acceptance Scenarios**:

1. **Given** the published API description, **When** the queue health schema is inspected, **Then** `paused`, `pausedAt`, `pauseReason` and `pauseKind` each carry a description covering both idle and failure-policy pauses, and `pauseKind` lists its allowed values.

---

### Edge Cases

- **Both pauses at once**: a run parked by the failure policy does not enter an idle pause, but if both were ever in force at the same moment the failure-policy pause is reported (`pauseKind: "failurePolicy"`, its own `pausedAt`/`pauseReason`), because it is the one that needs operator action.
- **Queue with idle pause disabled**: never reports `pauseKind: "idle"`; its health output is unchanged from today.
- **Stopped queue**: `health` stays `null`, as today.
- **Stop during an idle pause**: once the run is stopped, `health` is `null`; no stale pause is reported on a later run.
- **Idle pause extended or shortened**: `pausedAt` keeps the start of the continuous pause; only a new pause after the hold has ended gets a new `pausedAt`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While a running queue is in an idle pause, the queue detail's `health.paused` MUST be `true`.
- **FR-002**: While a running queue is in an idle pause, `health.pausedAt` MUST be the instant that continuous idle pause began, and MUST NOT change when the pause's resume time is re-computed.
- **FR-003**: While a running queue is in an idle pause, `health.pauseReason` MUST be `idle pause: resumes at HH:mm`, where `HH:mm` is the service-local time of the pause's current resume instant.
- **FR-004**: `health` MUST expose a new `pauseKind` field: `"idle"` for an idle pause, `"failurePolicy"` for a failure-policy pause, `null` when not paused.
- **FR-005**: For a failure-policy pause, `paused`, `pausedAt` and `pauseReason` MUST keep their current values; only `pauseKind: "failurePolicy"` is added.
- **FR-006**: When both pauses are in force, the failure-policy pause MUST be reported.
- **FR-007**: When no pause is in force, `paused` MUST be `false` and `pausedAt`, `pauseReason` and `pauseKind` MUST be `null`.
- **FR-008**: Whether the queue detail reports an idle pause MUST be derived from the same run state the monitor uses to report `IdlePause`, so the two cannot disagree.
- **FR-009**: The published API description MUST document `paused`, `pausedAt`, `pauseReason` and `pauseKind` as covering both kinds of pause, including `pauseKind`'s allowed values.
- **FR-010**: `failurePolicyTripped`, the resume endpoint's behaviour, idle-pause timing and the monitor's response MUST be unchanged.
- **FR-011**: An automated contract/integration test MUST cover the idle-pause case of `health`, and the existing failure-policy pause tests MUST continue to pass.

### Key Entities

- **Queue health (`health` block)**: live state of a running queue. Pause-related attributes: `paused` (any pause in force), `pausedAt` (start of the reported pause), `pauseReason` (human-readable why), `pauseKind` (which pause: idle / failure policy).
- **Run pause state**: the running queue's in-memory record of an idle pause (start instant, resume instant) and of a failure-policy pause (start instant, reason). Not persisted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of samples taken while the monitor reports an idle pause, the queue detail reports `paused: true` with a non-null `pausedAt`, `pauseReason` and `pauseKind: "idle"`.
- **SC-002**: In 100% of samples taken while neither pause is in force, the queue detail reports `paused: false` and null pause fields.
- **SC-003**: A consumer can tell an idle pause from a failure-policy pause from the queue detail alone, without calling the monitor.
- **SC-004**: All existing failure-policy pause tests pass unchanged.

## Assumptions

- Widening `paused` (rather than only documenting it as failure-policy-only) is the chosen fix, because it is the issue's stated expected behaviour and removes the need for the `/monitor` workaround; `pauseKind` keeps the two pauses distinguishable as the issue requires.
- No known client treats `health.paused: true` as "call resume"; the web UI does not read `health.paused`.
- Adding one field (`pauseKind`) to an existing response object is additive and is not a new endpoint.
- `pausedAt` uses the service's local clock, like the other `health` timestamps.

## Out of Scope

- Changing when an idle pause starts or ends.
- Changing the `/monitor` response shape.
- New endpoints, or making `POST /resume` end an idle pause.
- Web UI changes.
