# Feature Specification: Distinct reporting of sequence time-limit cancellation

**Feature Branch**: `094-sequence-time-limit-reporting`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #182 (https://github.com/bbqf/GameBotAI/issues/182) — "FR-005: a sequence cancelled at the ~240s cap is indistinguishable from one that failed, and the cap is set nowhere". Closes #182.

## Background

A queue-driven sequence firing that runs longer than its time bound (the platform default of 4 minutes,
or the sequence's own `watchdogTimeoutMs` override) is cancelled by the platform. Today that firing's
execution-log entry is closed as a plain `failure`, with only a free-text summary that cannot tell "the
bound fired" from "the queue run was stopped". A run stopped by the clock and a run that reached its own
failure assertion therefore look the same to any consumer that reads the status.

The bound itself is settable per sequence (`watchdogTimeoutMs`, >0 and ≤30 minutes) but this is
undiscoverable: it is not in the published API document, and reading a sequence that has no override
returns nothing, so authors pace their sequences against an observed, undocumented number.

Real cost (2026-09-14): a defensive recovery sequence spent 180 s in a wait loop and was cancelled at the
bound before its confirmation step and its three failure assertions could run. The execution log could
not distinguish "ran out of time" from "concluded the screen is bad".

## Clarifications

### Session 2026-09-17

- Q: Should the distinction be a new `Cancelled` status or a reason field alongside `failure`? → A: Reason field alongside `failure` (rationale: status filters, live monitor "last outcome", and daily-retry logic all key off non-success; a new status would silently change them).
- Q: Which entries must carry the reason? → A: Only the queue firing's sequence entry. A firing's children are command entries (a sequence never runs another sequence at run time), and they keep their own outcomes (rationale: the verdict that needs disambiguating is the sequence's; a command cut short is already reported by its own step outcome).
- Q: If a step swallows the cancellation and the firing ends normally as a failure after the bound fired, is it a time-limit cancellation? → A: Yes — whenever the bound has fired by the time the firing ends without success, it is labelled (rationale: the verdict was produced under a cancelled budget, so it is not a trustworthy verdict).
- Q: What does a user stop of the queue record? → A: Unchanged from today — no cancellation reason (rationale: issue only requires it not be mislabelled; adding a stop reason is out of scope).
- Q: Should a firing that the bound cuts off after it already succeeded be labelled? → A: No — a `success` entry never carries a cancellation reason.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tell a timeout from a verdict in the execution log (Priority: P1)

An automation author investigating a failed queue firing reads the sequence's execution-log entry and can
see, from structured fields rather than prose, that the firing was cut off by the platform time bound,
and which bound applied.

**Why this priority**: This is the gap that let a real outage evade every assertion built to catch it.
Without it, no downstream tooling can separate timeouts from genuine failures.

**Independent Test**: Run a queue whose sequence deliberately outlasts a short per-sequence bound; read
the execution-log entry for that firing and confirm it carries the time-limit cancellation reason and the
applied bound, while a sequence that fails on its own terms in the same run does not.

**Acceptance Scenarios**:

1. **Given** a queue firing whose sequence exceeds its time bound, **When** the bound fires, **Then** the
   sequence's execution-log entry is closed with status `failure` and a structured cancellation reason
   `sequence_time_limit`, plus the effective bound in milliseconds that applied.
2. **Given** a sequence that finishes with its own failure (e.g. a failed step or assertion) inside the
   bound, **When** its entry is read, **Then** it carries no cancellation reason and no applied bound.
3. **Given** a queue run that is stopped by the user while a sequence is mid-flight, **When** the
   sequence's entry is read, **Then** it carries no cancellation reason (as today).
4. **Given** a sequence that succeeds, **When** its entry is read, **Then** it carries no cancellation
   reason.
5. **Given** a time-limit cancellation, **When** the queue continues, **Then** the queue run carries on
   with its next entry exactly as before (the timeout remains non-fatal to the run).

---

### User Story 2 - Read the effective time bound of a sequence (Priority: P2)

An author reading a sequence through the API sees the time bound that will actually apply when it fires
from a queue — the override when one is set, otherwise the platform default — so they can budget loops
and waits against a published number.

**Why this priority**: Removes the guesswork that forces authors to reverse-engineer the bound; secondary
to being able to diagnose timeouts after the fact.

**Independent Test**: Read one sequence with no override and one with an override; confirm the first
reports 240000 ms as effective and null as the stored override, the second reports its override for both.

**Acceptance Scenarios**:

1. **Given** a sequence with no stored override, **When** it is read, **Then** the response reports the
   stored override as absent/null and the effective bound as 240000 ms.
2. **Given** a sequence with an override of 1200000 ms, **When** it is read, **Then** both the stored
   override and the effective bound are 1200000 ms.
3. **Given** a sequence is updated without mentioning the override, **When** it is read back, **Then** the
   stored override is unchanged (the effective value is read-only and is never persisted as an override).

---

### User Story 3 - Discover the bound and the new fields from the API document (Priority: P3)

An integrator reading the published API document finds the per-sequence time-bound setting (with its
default and maximum), the effective-bound read-out, and the cancellation reporting fields, without
observing runs.

**Why this priority**: Documentation completes the fix but has no runtime effect.

**Independent Test**: Fetch the published API document and confirm the sequence schema describes the
override (default 240000 ms, maximum 1800000 ms) and the effective read-out, and the execution-log entry
schema describes the cancellation reason and applied bound.

**Acceptance Scenarios**:

1. **Given** the service is running, **When** the API document is fetched, **Then** the sequence
   read/write schemas include the time-bound override with its default and maximum stated.
2. **Given** the service is running, **When** the API document is fetched, **Then** the execution-log
   entry schema includes the cancellation reason (with its possible values) and the applied bound.

### Edge Cases

- The bound fires while the firing is still in the pre-sequence foreground check (before any sequence
  entry exists): no sequence entry exists to label; behaviour is unchanged (the existing service log line
  still records it).
- The bound fires while a child command of the firing is running: the firing's sequence entry MUST carry the
  reason; child command entries are unchanged.
- The per-sequence override lookup fails and the default is used: the applied bound recorded is the one
  actually used (the default).
- A step catches cancellation internally and returns a failure instead of unwinding: the firing is still
  reported as a time-limit cancellation, because the bound fired.
- Existing log entries written before this change have no cancellation reason and must still read and
  list correctly.
- Filtering/sorting the execution-log list by status continues to work; the status vocabulary
  (`success`/`running`/`failure`) is unchanged.
- Ad-hoc (non-queue) sequence executions have no platform time bound; their entries never carry the
  reason.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a queue sequence firing is terminated because its time bound elapsed, the system MUST
  record on that firing's execution-log entry a structured cancellation reason of `sequence_time_limit`.
- **FR-002**: The same entry MUST record the effective time bound, in milliseconds, that was applied to the
  firing.
- **FR-003**: The entry's status MUST remain `failure` so existing status filters, retry and scheduling
  treatment, and consumers that only read status keep their current behaviour.
- **FR-004**: A firing that fails on its own terms before the bound fires, succeeds, or is ended by a user
  stop of the queue run MUST NOT carry a cancellation reason.
- **FR-004a**: A firing that ends without success after its bound has fired (whether by unwinding or by a
  step converting the cancellation into an ordinary failure) MUST carry `sequence_time_limit`.
- **FR-004b**: Child command entries of a time-limited firing MUST NOT be altered; only the firing's sequence
  entry carries the reason and the applied bound.
- **FR-005**: The cancellation reason and applied bound MUST be returned wherever execution-log entries are
  returned by the API (entry list, entry detail and execution tree node).
- **FR-006**: A time-limit cancellation MUST remain non-fatal to the queue run; the run continues with its
  next entry as it does today.
- **FR-007**: Reading a sequence MUST expose the effective time bound in milliseconds: the stored override
  when set, otherwise the platform default of 240000 ms.
- **FR-008**: The effective bound MUST be read-only: it is never persisted, and supplying it on a write
  MUST NOT change the stored override.
- **FR-009**: The published API document MUST describe the per-sequence time-bound override (default
  240000 ms, must be greater than 0, maximum 1800000 ms), the effective bound read-out, and the
  execution-log cancellation reason (values) and applied bound.
- **FR-010**: Execution-log entries written before this change MUST continue to load, list and filter
  correctly, reporting no cancellation reason.

### Key Entities

- **Execution-log entry (sequence)**: gains an optional cancellation reason (`sequence_time_limit` when the
  platform time bound ended the firing; absent otherwise) and an optional applied time bound (ms).
- **Sequence**: keeps its optional stored time-bound override; gains a read-only effective time bound.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of queue firings ended by the time bound are identifiable as time-limit cancellations
  from structured execution-log fields alone, with zero false positives among ordinary failures and user
  stops in automated tests.
- **SC-002**: An author can determine the time bound that will apply to any sequence with a single read of
  that sequence, without consulting source code or observing runs.
- **SC-003**: The time-bound setting, its default and maximum, and the cancellation fields are all present
  in the published API document.
- **SC-004**: No change in queue run continuation, retry or scheduling outcomes for timed-out firings
  (existing queue tests continue to pass).

## Assumptions

- The status vocabulary stays `success`/`running`/`failure`; distinction is carried by an added reason
  field rather than a new `Cancelled` status, so that existing filters, the live monitor and retry logic
  (which treat non-success as failure) are unaffected. The issue explicitly allows this shape.
- Field names follow the existing camelCase API style: `cancellationReason`, `timeLimitMs` on log entries;
  `effectiveWatchdogTimeoutMs` on sequences (the existing `watchdogTimeoutMs` name is kept).
- A user stop keeps its current representation (no cancellation reason).
- Only queue firings have a platform time bound today; ad-hoc executions are unchanged.

## Non-Goals

- No change to the default 4-minute bound or the 30-minute maximum.
- No per-step time limits.
- No renaming of `watchdogTimeoutMs` and no alias such as `timeLimitSeconds`.
- No changes to PNS sequence data.
- No web-UI changes.
