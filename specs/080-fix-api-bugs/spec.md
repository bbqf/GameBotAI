# Feature Specification: Fix Sequence & Session-Input API Bugs

**Feature Branch**: `080-fix-api-bugs`
**Created**: 2026-09-11
**Status**: Draft
**Input**: User description: "I want you to fix and update all the bugs documented in C:\src\PNS\docs\api-bugs.md . Use usual development procedures, but don't update the api-bugs.md, it's own by another project and has to be retested there."

## Background

An external consumer of the GameBotAI platform API (the PNS project) maintains a bug
tracker at `C:\src\PNS\docs\api-bugs.md` listing platform behaviours that contradict
their own published contract. Of the seven entries, three describe genuine,
fixable defects in this platform's own request-handling code; the rest are either
informational notes about acceptable design limitations, or issues that live outside
this codebase (image-template scoring in the consuming project) and are out of scope
here.

This feature fixes the three in-scope defects:

- **Silent sequence misconfiguration** (tracker ID B-003): a sequence step that
  references a command by name instead of by id is accepted without complaint at
  creation time, and only fails much later, confusingly, when the sequence actually
  runs.
- **A safety gate that can never fire inside a loop or a conditional** (tracker ID
  B-005): a step nested inside a `Loop` or `If` body that is marked "this step must
  reach the device or the sequence should fail" silently loses that guarantee — the
  step, and the whole sequence, report success even though nothing was dispatched.
- **A misleading error on the ad-hoc session-input route** (tracker ID B-001): posting
  a swipe input in a shape the route doesn't recognize is reported as "the session
  isn't running," even though the session is healthy and other actions on it succeed.

The other four tracker entries (B-002, B-004, B-006, B-007) are informational or
external and receive no code change under this feature.

## Clarifications

### Session 2026-09-11

- Q: When posted session-input actions fail to dispatch due to malformed
  arguments (Story 3), what should the response contract be, given that a
  request can mix well-formed and malformed actions and some actions may
  already have taken effect on the device before a later one fails to parse? →
  A: Two-tier response — if **zero** actions in the request could be
  dispatched, return `400 Bad Request` identifying the failing action(s) and
  why (nothing happened, so this is a request-shape error, not a partial
  result). If **some but not all** actions dispatched, keep the existing
  `202 Accepted` shape (some side effects already occurred) but extend it with
  a per-action result identifying which action(s) failed and why, rather than
  silently reporting only the accepted count.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sequence authoring fails fast on a bad command reference (Priority: P1)

A person or tool authoring a sequence submits a step whose action payload names a
command by `commandName` (or omits `commandId` entirely) instead of supplying the
resolved `commandId`. Today the platform accepts the sequence, silently drops the
reference, and defaults the step's dispatch target to the step's own id — so the
sequence looks fine until it actually runs, where it fails with an opaque
"Command '<stepId>' was not found" error that gives no hint the real problem was a
malformed step payload submitted minutes or days earlier.

**Why this priority**: This is the defect with the widest blast radius — every
sequence author (human or automated tooling) that makes this one-letter-of-a-field
mistake pays for it with a confusing runtime failure instead of an immediate,
actionable error, and it has already cost real authoring time in a downstream
project.

**Independent Test**: Submit a sequence with one top-level `Command`-typed action
step whose payload has `commandName` but no `commandId`; the creation call must be
rejected with a clear validation error naming the offending step, and no sequence
must be persisted. Can be tested and delivers value entirely on its own, independent
of the other two stories.

**Acceptance Scenarios**:

1. **Given** a sequence-creation request with a top-level `Command`-typed step whose
   action payload has `commandName` set but no `commandId`, **When** the sequence is
   submitted, **Then** the platform rejects the request with a validation error that
   identifies the specific step and explains that a resolved `commandId` is required.
2. **Given** the same malformed payload on a step nested inside a `Loop` or `If`
   body, **When** the sequence is submitted, **Then** the platform rejects it the same
   way as for a top-level step (nesting must not create a validation blind spot).
3. **Given** a sequence-creation request where every `Command`-typed step supplies a
   valid `commandId`, **When** the sequence is submitted, **Then** creation succeeds
   exactly as it does today (no regression for the well-formed case).

---

### User Story 2 - A required-dispatch step reliably fails the sequence when nested (Priority: P1)

A sequence author marks a step as "this step must actually reach the device, or the
sequence should be treated as failed" (`requireDispatch: true`). When that step is a
direct child of the sequence, this works. When the exact same step is nested one
level deeper — inside a `Loop` body or inside an `If` branch — the guarantee silently
evaporates: the step is reported as not having dispatched anything, yet the step and
the overall sequence both report success. This defeats the entire purpose of the
flag for any check that isn't a bare top-level step, which is exactly the "looks
successful but did nothing" failure mode this flag exists to catch.

**Why this priority**: Equal priority to Story 1 — this is a correctness guarantee
that currently fails silently in exactly the cases (loops, conditionals) where
authors are most likely to reach for it, and a silent false-success is strictly
worse than a fast, loud failure.

**Independent Test**: Submit and run a sequence containing a `Loop` (and separately,
an `If`) whose body has one step with `requireDispatch: true` that is set up so
nothing dispatches; the sequence execution must report the step, and the sequence,
as failed. Independently verifiable via a real end-to-end sequence run, with no
dependency on Story 1 or Story 3.

**Acceptance Scenarios**:

1. **Given** a sequence with a `Loop` body containing one step marked
   `requireDispatch: true`, **When** the sequence runs and that step dispatches
   nothing, **Then** the step outcome is reported as failed and the sequence run is
   reported as failed (not succeeded).
2. **Given** a sequence with an `If` body containing one step marked
   `requireDispatch: true`, **When** the sequence runs and that step dispatches
   nothing, **Then** the step outcome is reported as failed and the sequence run is
   reported as failed, matching the `Loop` behavior in Scenario 1.
3. **Given** a sequence with a nested step marked `requireDispatch: true` that *does*
   dispatch successfully, **When** the sequence runs, **Then** the step and sequence
   report success exactly as they do today (no regression for the working case).
4. **Given** a top-level (non-nested) step marked `requireDispatch: true`, **When**
   the sequence runs, **Then** behavior is unchanged from today for both the
   dispatch-succeeds and dispatch-fails cases.

---

### User Story 3 - Session-input errors describe the real problem (Priority: P3)

Someone posts an input action (tap, swipe, key, etc.) directly to a running
session's ad-hoc input route. If the action's arguments don't match the shape the
route expects, every action in the request silently fails to execute, and the route
reports this as `409 session not running` — even though the session is confirmed
running and healthy, and even though other, well-formed actions against the same
session succeed immediately before and after. The error actively misdirects
troubleshooting toward session/emulator health when the real problem is the request
body.

**Why this priority**: Lower priority than Stories 1-2 because a working, documented
workaround already exists for the one consumer who hit this (route the action
through command-step execution instead), and the impact is authoring-time friction
rather than a hidden runtime or safety failure. Still worth fixing because the
current error is actively misleading.

**Independent Test**: Post a request with one or more input actions to a session
that is confirmed running, where at least one action's arguments don't match the
expected shape for its type; the response must distinguish "some/all actions in this
request couldn't be dispatched" from "this session is not running," and must not
claim the session isn't running when it is. Fully testable on its own.

**Acceptance Scenarios**:

1. **Given** a session that is confirmed `Running`, **When** an input action is
   posted whose arguments don't match the shape expected for its declared type,
   **Then** the response does not report `not_running`/`409`, and instead reports
   that the action(s) could not be dispatched, distinguishing this from a session
   state problem.
2. **Given** a session id that does not correspond to any running session, **When**
   any input action is posted to it, **Then** the response continues to report
   `409 not_running` exactly as it does today (no regression for the genuine case).
3. **Given** a session that is confirmed `Running`, **When** a well-formed input
   action is posted, **Then** it continues to execute successfully exactly as it
   does today (no regression for the working case).

### Edge Cases

- A sequence step's payload supplies **both** `commandName` and `commandId`: the
  resolved `commandId` takes precedence (unchanged from today's behavior for that
  field), and no validation error is raised on this basis alone.
- A `requireDispatch: true` step is nested more than one level deep (e.g. a `Loop`
  whose body contains an `If`, whose body contains the step): the guarantee must
  still hold at every nesting depth reachable through `Loop`/`If` bodies.
- A session-input request mixes one well-formed action with one malformed action in
  the same call: the response must make clear that only some actions dispatched,
  rather than reporting a blanket session-state error.
- An input action's arguments are well-formed but the underlying device/transport
  genuinely fails to execute them (a true dispatch failure, not a shape mismatch):
  this must remain distinguishable from both the "not running" case and the "bad
  request shape" case introduced by this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST reject, at sequence-creation time, any `Command`-typed
  action step (top-level or nested inside a `Loop`/`If` body) whose payload does not
  contain a non-empty `commandId`, with a validation error that identifies the
  offending step.
- **FR-002**: The platform MUST apply the validation in FR-001 identically regardless
  of nesting depth inside `Loop` and `If` bodies — there must be no path through
  sequence creation that accepts an unresolved command reference.
- **FR-003**: The platform MUST NOT persist a sequence that fails the validation in
  FR-001.
- **FR-004**: The platform MUST continue to accept sequence-creation requests where
  every `Command`-typed step supplies a valid `commandId`, with no behavior change
  from today.
- **FR-005**: The sequence execution engine MUST honor `requireDispatch: true` on a
  step nested inside a `Loop` body: if that step's action does not dispatch, the step
  and the overall sequence run MUST be reported as failed.
- **FR-006**: The sequence execution engine MUST honor `requireDispatch: true` on a
  step nested inside an `If` body (either branch), with the same failure semantics as
  FR-005.
- **FR-007**: The `requireDispatch` behavior in FR-005/FR-006 MUST apply uniformly
  regardless of nesting depth (a step nested inside a `Loop` inside an `If`, or vice
  versa, must behave the same as a directly-nested step).
- **FR-008**: The platform MUST continue to report success for a nested
  `requireDispatch: true` step whose action does dispatch successfully, and MUST
  continue existing behavior for top-level `requireDispatch` steps, with no
  regression in either case.
- **FR-009**: The session-input route MUST NOT report a `409`/`not_running` response
  for a session that is confirmed running, solely because one or more posted input
  actions could not be parsed or dispatched.
- **FR-010**: The session-input route MUST continue to report `409`/`not_running` when
  the target session genuinely does not exist or is not running, with no change from
  today's behavior for that case.
- **FR-011**: When **none** of the posted input actions against a running session
  could be parsed or dispatched, the route MUST report `400 Bad Request` (not `409`)
  identifying which action(s) failed and why.
- **FR-012**: When **some but not all** posted input actions against a running
  session could be dispatched, the route MUST keep reporting `202 Accepted` (since
  some actions already took effect) but MUST extend that response with a per-action
  result identifying which action(s) failed and why, rather than only an accepted
  count.
- **FR-013**: The session-input route MUST continue to execute and report success for
  well-formed input actions against a running session, with no regression from
  today's behavior.

### Key Entities

- **Sequence step**: An authored unit of a sequence (top-level or nested inside a
  `Loop`/`If` body) that may carry an action payload naming a command to dispatch,
  and may carry a `requireDispatch` flag governing whether a failed dispatch fails
  the step.
- **Session input action**: An ad-hoc, one-off action (tap, swipe, key, etc.) posted
  directly against a running session, outside of any sequence or command.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sequence-creation requests containing an unresolved command
  reference (`commandName` without `commandId`), at any nesting depth, are rejected
  at creation time with an error identifying the offending step — zero such
  sequences can be persisted.
- **SC-002**: 100% of sequence runs containing a nested (`Loop`/`If`-body)
  `requireDispatch: true` step whose action fails to dispatch are reported as failed
  — zero such runs can report overall success.
- **SC-003**: Session-input requests against a confirmed-running session with
  malformed action arguments no longer produce a `409 not_running` response in any
  of the automated regression tests added for this feature.
- **SC-004**: All pre-existing automated tests covering sequence creation, sequence
  execution, and session-input handling continue to pass unmodified in their
  well-formed-input assertions (no regression to the documented working cases).

## Requirement Cross-Reference

Renumbered during `/speckit-analyze`: the response-contract requirement is
`FR-012` (previously drafted as `FR-011a`), and the well-formed-input regression
requirement is `FR-013` (previously `FR-012`), keeping the `FR-NNN` sequence
strictly ordinal.
