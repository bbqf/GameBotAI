# Feature Specification: Device-Scoped Image Detection

**Feature Branch**: `085-device-scoped-detect`
**Created**: 2026-09-14
**Status**: Implemented
**Input**: GitHub issue #176 — "B-009: POST /api/images/detect returns an empty match array for every template whenever more than one session is running" (https://github.com/bbqf/GameBotAI/issues/176)

## Overview

The single-template detection request answers "how well does this reference image match what is on screen right now". Today it has no way for the caller to say **which** screen. When exactly one emulator session is running, the system infers the screen correctly. When two or more are running, the system correctly recognises that it cannot tell which screen was meant — and then reports that ambiguity as **"nothing matched"**: an ordinary, successful, empty result.

That answer is indistinguishable from a genuine "this image is absent from the screen". Callers that use detection as an *absence* probe therefore read a safety-critical measurement as a confident zero when in fact no measurement was taken at all. This feature makes the detection request device-scoped: the caller can name the screen to measure, and when no screen can be determined the system says so explicitly instead of fabricating an empty measurement.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ambiguity is reported, never disguised as a negative result (Priority: P1)

An automation author runs a watchdog that repeatedly asks "is the quit dialog present on screen?" while several emulators are running. Today the watchdog silently receives "absent" for every probe. They need the system to refuse the question rather than answer it wrongly.

**Why this priority**: This is the safety defect. Until the system stops disguising "I don't know" as "no", every caller's absence test is unsound, and the failure is invisible — it requires no change on the caller's side to trigger, and produces no error, log, or artifact the caller can notice. Shipping only this story already removes the silent failure, even before any caller is updated.

**Independent Test**: Start two sessions, issue a detection request that names no target, and confirm the response is an explicit, identifiable error rather than a successful empty result.

**Acceptance Scenarios**:

1. **Given** two or more sessions are running and no ambient run context identifies one of them, **When** a caller requests detection without naming a target screen, **Then** the system returns an explicit error that identifies the cause as an unresolved/ambiguous target and states that the request must name one.
2. **Given** the same conditions, **When** the caller inspects the response, **Then** the response is distinguishable from a successful detection that found no matches, without the caller needing to know how many sessions are running.
3. **Given** no session is running at all, **When** a caller requests detection without naming a target screen, **Then** the system returns an explicit error rather than a successful empty result.

---

### User Story 2 - A caller can name the screen to measure (Priority: P1)

An automation author supervising one specific emulator, while other emulators run alongside it, needs their detection probes to keep working — scoped to their own device and honouring their own match threshold.

**Why this priority**: Without this, Story 1 converts a silent wrong answer into a hard failure with no way forward: the multi-emulator setup that exposed the bug would have no working absence probe at all. The two stories together are the fix; this one restores the capability.

**Independent Test**: With two sessions running, issue a detection request naming one specific target and confirm it returns real match scores for that device's screen.

**Acceptance Scenarios**:

1. **Given** two or more sessions are running, **When** a caller requests detection naming a specific target screen, **Then** the system measures against that screen and returns match results, regardless of how many other sessions are running.
2. **Given** a caller names a target screen, **When** they also supply a match threshold, **Then** that threshold is honoured, so a deliberately low threshold reports a low-confidence score rather than suppressing it — letting the caller distinguish "absent" from "present but below the gate".
3. **Given** a caller names a target screen, **When** they also supply result-count and overlap preferences, **Then** those are honoured exactly as they are on the existing unnamed path.
4. **Given** a caller names a target screen that does not exist or is no longer available, **When** the request is made, **Then** the system returns an explicit not-found error naming the unknown target, rather than an empty result or a measurement against some other screen.

---

### User Story 3 - Single-emulator callers are unaffected (Priority: P2)

Every existing automation sequence, trigger and script that calls detection today runs against a single emulator and names no target. They must keep working unchanged after this feature ships.

**Why this priority**: This is a compatibility guarantee rather than new capability, but a regression here would break the entire existing automation library at once. It is tested rather than built.

**Independent Test**: With exactly one session running, issue the same detection requests used before the change and confirm identical behaviour.

**Acceptance Scenarios**:

1. **Given** exactly one session is running, **When** a caller requests detection without naming a target, **Then** the system measures against that session's screen and returns the same results it returned before this feature.
2. **Given** exactly one session is running, **When** a caller supplies threshold, result-count or overlap preferences, **Then** they are honoured exactly as before.
3. **Given** a caller is executing inside a run that is already bound to a particular device, **When** they request detection without naming a target, **Then** the system measures against that run's device — as it does today — even if other sessions are running concurrently.

---

### Edge Cases

- **Named target that has expired**: a target that was valid earlier but has since aged out must produce the same explicit not-found error as one that never existed — never a silent empty result.
- **Ambient run context plus an explicitly named target**: the explicitly named target wins, because the caller stated it deliberately. See Assumptions.
- **Two different kinds of target named in one request**: the request is rejected as malformed rather than the system choosing one. See Assumptions.
- **A named target that is syntactically present but empty or blank**: treated as "no target named", falling back to the unnamed resolution path.
- **The screen image for a named target cannot be decoded**: reported as a distinct failure, not as an empty match set.
- **No screen capability is available at all** (an environment with no emulator support wired up): reported as the same service-unavailable condition as "no session running" — see FR-007 and Clarifications Q2.
- **A session is running but no frame has been captured for it yet**: the caller must be able to tell this apart from "nothing matched". See FR-008.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The detection request MUST accept an optional caller-supplied target identifying which screen to measure against.
- **FR-002**: The target MUST be expressible as a previously taken screen capture, using the same form of identifier the existing detect-all request already accepts, so callers can measure the exact frame they inspected rather than a later one.
- **FR-003**: The target MUST also be expressible as a running session, so a caller that simply wants "the current screen of that emulator" need not first take and manage a capture.
- **FR-004**: When a target is named, the system MUST measure against that target's screen regardless of how many sessions are running and regardless of any ambient run context.
- **FR-005**: When a target is named but cannot be resolved — unknown, expired, or not running — the system MUST return an explicit not-found error that identifies the unresolved target, and MUST NOT fall back to any other screen and MUST NOT return a successful empty result.
- **FR-006**: When no target is named, the system MUST resolve the screen using the existing precedence: the ambient device context of the run in progress first, then the single running session when exactly one exists.
- **FR-007**: When no target is named and the screen cannot be resolved, the system MUST return an explicit error and MUST NOT return a successful empty result. The error MUST distinguish the two causes: *several sessions running with none named* (a conflict the caller can resolve by naming one) from *no screen available at all* — no session running, or no screen capability present in this environment (a service-state condition the caller cannot resolve by changing the request).
- **FR-008**: Every failure to *take* a measurement MUST be reported as a failure and MUST be distinguishable, by the caller, from a successful measurement that found no matches. This includes an unresolvable target, an undecodable screen image, and a resolved session with no captured frame yet available.
- **FR-009**: The system MUST NOT report a successful empty result in any circumstance other than a real measurement having been taken against a determinate screen and having found no match at or above the requested threshold.
- **FR-010**: Caller-supplied threshold, maximum result count and overlap preferences MUST be honoured identically on the named-target path and the unnamed path.
- **FR-011**: Error responses MUST follow the same shape and conventions as the existing errors on this capability, so callers parse them with existing logic.
- **FR-012**: Error responses MUST carry distinct, machine-readable codes for the distinct causes — ambiguous screen, no screen available, and named target not found — so a caller can react programmatically without parsing prose. These codes MUST reuse the vocabulary the system already uses for the same causes on the screenshot capability rather than introducing new names.
- **FR-016**: The unresolved-screen condition MUST be detected from the screen resolver reporting that it could not determine a screen, and MUST NOT be detected by counting running sessions before a screen is requested. Session state may be consulted only after the resolver has reported failure, and only to classify which cause to report. This preserves environments that supply a fixed screen without any running session, which would otherwise begin failing despite being unaffected by the defect.
- **FR-013**: Existing callers that name no target and run against a single session MUST observe no change in behaviour, response shape, or result values.
- **FR-014**: The published API documentation MUST describe the new request fields and every error response this capability can now return.
- **FR-015**: The change MUST be covered by automated tests for: a named target succeeding while several sessions run; an unnamed request while several sessions run returning the explicit ambiguity error; the single-session unnamed path returning unchanged results; an unknown or expired named target returning the not-found error; and a low threshold on the named-target path returning a graded below-default-gate score rather than an empty match set (the measurement SC-006 protects).

### Key Entities

- **Detection request**: a caller's question "does this reference image appear on screen, and how strongly". Carries the reference image, the match tuning preferences (threshold, maximum results, overlap), and — new in this feature — an optional target naming the screen to measure.
- **Target**: the caller's statement of which screen to measure. Either a previously taken screen capture, or a running session. Optional; when absent the system falls back to ambient and single-session resolution.
- **Detection outcome**: either a measurement (a possibly-empty set of matches, which now always means a real measurement was taken) or an explicit failure carrying a machine-readable cause.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With two or more emulators running, a caller that names its target gets the same match scores it would get with only that one emulator running — in 100% of cases, for both present and absent reference images.
- **SC-002**: The rate at which the system reports a successful empty result while no measurement was actually taken drops to zero. Every such case is instead reported as an explicit failure.
- **SC-003**: A caller can determine, from a single response and with no knowledge of how many emulators are running, whether its probe was measured or refused — in 100% of cases.
- **SC-004**: Adding a second or subsequent emulator to a working single-emulator setup no longer changes the answers that setup's existing detection probes receive: they either keep working or fail loudly, and never silently invert from "present" to "absent".
- **SC-005**: All existing automation that calls detection against a single emulator continues to pass its current tests with no edits to the callers.
- **SC-006**: A low-threshold absence probe reports a graded confidence score rather than a suppressed one, so "absent" and "present but weak" remain distinguishable on the named-target path.

## Clarifications

### Session 2026-09-14

Resolved during `/speckit-clarify`. Both questions were settled by an existing precedent in this
codebase rather than by inventing a new convention: the screenshot capability already faces exactly
this "which device did you mean" problem and already answers it explicitly.

**Q1 — What kind of failure is an ambiguous/unresolved screen?**

**Answer**: Mirror the established convention for this exact situation, which distinguishes two
causes rather than collapsing them:

- *Several sessions running, none named, no ambient context* → a **conflict**, machine-readable code
  `ambiguous_session`, with a message stating how many sessions are active and that the request must
  name one. The caller has a concrete remedy, so the failure names it.
- *No session running at all* → a **service-unavailable** condition, code `emulator_unavailable`,
  with a hint to start the emulator and retry. The caller cannot fix this by changing the request.

**Rationale**: this is the precedent already set by the emulator screenshot capability for the same
ambiguity, introduced by the same earlier feature that taught the screen resolver to refuse to
guess. Reusing it means callers that already handle these codes on one capability handle them on
this one unchanged, and no new vocabulary enters the API. Reflected in FR-007 and FR-012.

**Q2 — Does the "no screen capability available at all" path also become an explicit error?**

**Answer**: Yes — reported as the same `emulator_unavailable` service-unavailable condition. This is
required for FR-009 to hold without exception, and it is safe: the capability is present in every
environment where this endpoint can do real work, so no supported configuration starts failing.

**Rationale and the constraint it imposes**: the compatibility risk here is not the no-capability
path itself but *how the ambiguity is detected*. The check MUST be driven by the screen resolver
reporting that it could not determine a screen — the existing behaviour described in FR-006 — and
MUST NOT be driven by independently counting running sessions before asking for the screen. In
stub/test environments a fixed screen is always available even with zero sessions running, so a
pre-emptive session count would turn every existing passing test into a failure while changing
nothing about the real defect. Session state may be consulted **only after** the resolver has
already reported "cannot determine", and then only to classify which of the two causes to report.
This constraint is what makes FR-013 achievable alongside FR-007, and it is carried into the plan.

## Assumptions

- **Precedence when both a target and an ambient run context exist**: the explicitly named target wins. Rationale: the caller named it deliberately and specifically, whereas ambient context is implicit.
- **Naming two kinds of target at once** (both a capture and a session) is treated as a malformed request and rejected, rather than the system silently preferring one. Rationale: consistent with FR-009's principle of never guessing.
- **Blank or whitespace-only target values** are treated as "not named" rather than as a malformed request, matching how the existing request already treats blank optional values.
- **The existing detect-all capability is unchanged** by this feature, including its lack of a threshold parameter.
- **The resolution order used when no target is named is not changed** — ambient run context first, then the sole running session. Only the handling of its "cannot determine" outcome changes, at the boundary where the answer is returned to the caller.
- **Reference image lookup failures keep their current behaviour** (an explicit not-found), which already complies with FR-009.
- **"Session" as a target** is identified the same way sessions are identified everywhere else in the system; no new identifier scheme is introduced.

## Out of Scope

- Adding a threshold parameter to the detect-all capability.
- Changing how trigger evaluators, condition adapters or the standalone trigger worker resolve their screens.
- Changing the resolution order that decides which session an unnamed request refers to.
- Changing how background screen capture works, including its cadence or caching.
- Changing the match/scoring algorithm or the shape of a successful match result.
- Migrating existing callers to name targets; they may continue to rely on single-session resolution.
