# Feature Specification: Loop Exit Reason & Nested Step-Outcome References

**Feature Branch**: `081-loop-exit-reason-and-nested-steprefs`
**Created**: 2026-09-11
**Status**: Implemented
**Input**: User description: "Implement FR-001 from C:\src\PNS\docs\api-feature-requests.md: two related, additive changes to the sequence execution/authoring API — expose why a Loop step ended (brokeVia vs. exhaustedMaxIterations), and let commandOutcome/stepRef reference a step nested inside a Loop/If body, not only a top-level sibling."

## Background

An external consumer of the GameBotAI platform API (the PNS project) maintains a
feature-request tracker at `C:\src\PNS\docs\api-feature-requests.md`. Its first
entry, FR-001, documents a real, paid authoring cost: because a `Loop` step's
result never reveals *why* it stopped iterating, and because a condition can only
reference a step that is its own immediate sibling, two downstream features had to
fall back on weaker designs —

- `PNS.CollectResources` shipped without a hard "cluster not found" failure gate,
  because there was no structural way to ask "did the exhaustion-detection loop's
  Break fire?" from outside that loop.
- The alliance-donation feature's exhaustion gate had to guess whether a rejection
  had occurred using a fragile ~2.0-3.3 second screenshot-timing window instead of
  a provable check, and that heuristic is documented as unreliable and
  best-effort-only.

This feature closes that gap with two additive changes to sequence authoring and
execution, plus the minimal supporting plumbing needed for the second change to
actually be usable for its stated purpose (querying a `Break` step's fired/not-fired
outcome):

1. A `Loop` step's execution result reports a structured reason it stopped
   iterating: which `Break` step fired (if any), or whether it ran out of
   iterations.
2. A condition elsewhere in the sequence can reference any step's outcome by id,
   regardless of which `Loop`/`If` body that step lives in — not only a step in its
   own immediate scope — including asking whether a specific `Break` step fired.

Neither change alters the meaning or result of any sequence or condition that is
already valid today.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A Loop's result reveals why it stopped (Priority: P1)

A sequence author (or the tooling that inspects a sequence run's result) looks at a
`Loop` step's own execution result and needs to know, structurally, whether the loop
stopped because a specific `Break` fired, because it ran out of iterations, or
because it simply finished its body/condition normally — instead of having to infer
this from timing or from the coarse pass/fail status alone.

**Why this priority**: This is useful on its own even before any condition can
reference it — a caller inspecting a completed run's JSON result already gains a
provable signal, cutting the exact "can't tell if my safety loop actually broke"
cost documented in FR-001. It requires no other part of this feature to deliver
value.

**Independent Test**: Run a sequence containing a `Loop` whose body has a
conditional `Break`, once with an input that makes the `Break` fire and once with an
input that makes the loop exhaust its configured max iterations; inspect the `Loop`
step's own result in both runs and confirm the reported reason matches which path
actually happened. Fully testable without any condition/stepRef change.

**Acceptance Scenarios**:

1. **Given** a `Loop` whose body contains a `Break` step with a condition that
   evaluates true on some iteration, **When** the sequence runs and the `Break`
   fires, **Then** the `Loop` step's result reports the firing `Break` step's id and
   reports that max iterations was not exhausted.
2. **Given** the same `Loop`, **When** the sequence runs with an input that never
   satisfies the `Break` condition and the loop reaches its configured maximum
   iteration count, **Then** the `Loop` step's result reports no firing `Break` step
   and reports that max iterations was exhausted.
3. **Given** a `Break` step nested inside an `If` body that is itself nested inside
   the `Loop` body, **When** that `Break` fires, **Then** the `Loop` step's result
   reports the `Break` step's own id (not the enclosing `If` step's id).
4. **Given** a `Loop` that completes without exhausting its max iterations and
   without any `Break` ever firing (e.g. a body with no `Break` step, or a
   while/repeat-until loop whose own condition ends the loop), **When** the sequence
   runs, **Then** the `Loop` step's result reports no firing `Break` step and reports
   that max iterations was not exhausted (neither state applies).
5. **Given** any sequence run that existed and passed before this feature, **When**
   it is run again unmodified, **Then** its existing status, message, and iteration
   fields are unchanged — the new information is additive.

---

### User Story 2 - A condition can ask "did that Break fire?" from anywhere in the sequence (Priority: P1)

A sequence author builds an `If` (or another `Loop`'s `Break`) condition that needs
to check whether a specific `Break` step — nested inside some `Loop` or `If` body
elsewhere in the sequence, not necessarily the condition's own immediate body —
fired during this run, so they can gate later steps on that structural fact instead
of a screenshot-timing guess.

**Why this priority**: This is the specific, evidenced use case that forced the
alliance-donation feature's timing-based workaround. Equal priority to Story 1
because together they are what actually lets that workaround be replaced.

**Independent Test**: Author a sequence with a `Break` step nested inside one
`Loop`/`If` body, and a separate `If` step elsewhere in the sequence (not a sibling
of that `Break`) whose condition references the `Break` step's id and checks whether
it fired. Run the sequence once with an input that fires the `Break` and once with
an input that does not; confirm the referencing `If` step's condition result differs
correctly between the two runs. Independently testable once Story 1's execution
plumbing exists, without needing the general non-Break stepRef case from Story 3.

**Acceptance Scenarios**:

1. **Given** a sequence-creation request whose `If` condition's `stepRef` names a
   step nested inside a `Loop`/`If` body that is not the condition's own sibling,
   **When** the sequence is submitted, **Then** creation succeeds (today it is
   rejected with `400 "references unknown prior step"`).
2. **Given** that sequence, **When** it runs and the referenced `Break` step fires,
   **Then** a condition checking for the `break` outcome evaluates true (and a
   condition checking for `no_break` evaluates false).
3. **Given** the same sequence, **When** it runs and the referenced `Break` step
   does not fire, **Then** a condition checking for `no_break` evaluates true (and
   one checking for `break` evaluates false).
4. **Given** a `stepRef` naming a step that appears later in the sequence's authored
   structure than the referencing condition, **When** the sequence is submitted,
   **Then** creation is still rejected as referencing a step that is not prior —
   relaxing scope does not relax ordering.

---

### User Story 3 - A condition can reference any nested step's outcome, not just Break (Priority: P2)

A sequence author builds a condition that checks the success/failed/skipped outcome
of an ordinary (non-`Break`) step nested inside a different `Loop`/`If` body than
the condition's own, to combine several nested checks into one gate.

**Why this priority**: Lower priority than Stories 1-2 because the specifically
evidenced, paid cost (the donation-exhaustion gate) is a `Break`-detection use case,
but the platform's own condition contract makes no distinction between referencing a
`Break` step and referencing any other step type, so the relaxed resolution
inherently covers this too and it should be verified explicitly.

**Independent Test**: Author a sequence with an ordinary `Command`-typed step nested
inside one `Loop` body and a separate `If` step elsewhere whose condition references
that step's id and expected `success`/`failed`/`skipped` outcome; confirm it
resolves correctly. Testable independently of the `Break`-specific scenarios in
Story 2.

**Acceptance Scenarios**:

1. **Given** a `commandOutcome` condition elsewhere in the sequence that references
   a non-`Break` step nested inside a `Loop`/`If` body, **When** the sequence is
   submitted and run, **Then** the condition resolves against that step's actual
   outcome exactly as it would for a top-level reference today.
2. **Given** a `commandOutcome` condition whose `stepRef` names a step that is
   structurally prior and reachable but did not actually execute during this run
   (e.g. it lives in an `If` branch that was not taken this run), **When** the
   sequence runs, **Then** the referencing step and the sequence are reported as
   failed with an "unavailable reference" error, exactly as an unresolvable
   reference is reported today — no new silent-success or silent-skip behavior is
   introduced by widening scope.

### Edge Cases

- A `Loop` reaches its configured max iterations with `ExitOnMaxIterations` set to
  `false` (today reported as an overall `"Failed"` loop status rather than
  `"exhausted"`): `exitReason.exhaustedMaxIterations` still reports `true`, because
  it reports the structural fact independent of whether that fact is also
  configured to fail the loop.
- A `while`/`repeat-until` loop whose condition is false on the very first check
  (the body never executes): `exitReason` reports no firing `Break` and
  `exhaustedMaxIterations: false` — it simply never had a chance to do either.
- A `commandOutcome` condition inside a `Loop` body references a `Break` step that
  is its own sibling in the same body (already structurally permitted before this
  feature, but never actually resolvable at runtime because a `Break` step's
  fired/not-fired outcome was never recorded for lookup): this now resolves
  correctly, since recording that outcome is required plumbing for Story 2 and
  applies uniformly regardless of scope.
- Two `Break` steps share the same `stepId` string in different, unrelated loop
  bodies: existing sequence-wide `stepId` uniqueness validation already prevents
  this; nested-reference resolution does not need to disambiguate duplicates.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A `Loop` step's execution result MUST include a structured exit
  reason with two fields: the id of the `Break` step that fired during this run (or
  no value, if none fired), and whether the loop's configured maximum iteration
  count was reached without any `Break` firing.
- **FR-002**: The exit reason in FR-001 MUST be populated consistently across every
  loop kind the platform supports today (count-based, while, and repeat-until).
- **FR-003**: When the firing `Break` step is nested inside an `If` body that is
  itself nested inside the `Loop` body, the exit reason MUST report the `Break`
  step's own id, not an enclosing step's id.
- **FR-004**: The "max iterations exhausted" fact in FR-001 MUST reflect whether the
  loop actually ran its full configured iteration count without a `Break` firing,
  regardless of whether the loop is configured to fail or succeed when that happens.
- **FR-005**: When a loop finishes without any `Break` firing and without exhausting
  its configured max iterations, the exit reason MUST report neither condition as
  true (no firing `Break` id, `exhaustedMaxIterations: false`).
- **FR-006**: The platform MUST accept a `commandOutcome` condition's `stepRef`
  naming any step reachable from the sequence root — including a step nested inside
  a `Loop`/`If` body other than the condition's own — not only a step within the
  condition's immediate sibling list.
- **FR-007**: The platform MUST continue to reject a `commandOutcome` `stepRef` that
  names a step which is not structurally prior to the referencing condition (using
  the sequence's authored structure to determine "prior," now spanning the whole
  sequence rather than only the immediate sibling list).
- **FR-008**: The platform MUST accept `break` and `no_break` as valid
  `commandOutcome` `expectedState` values, in addition to the existing
  `success`/`failed`/`skipped`.
- **FR-009**: The sequence execution engine MUST record whether each `Break` step
  fired or did not fire, keyed by that step's id, so that any in-scope
  `commandOutcome` condition referencing it (at any nesting depth permitted by
  FR-006) resolves to `break`/`no_break` rather than failing as an unavailable
  reference.
- **FR-010**: The platform MUST continue to accept and resolve a `commandOutcome`
  condition referencing a non-`Break` step nested inside a `Loop`/`If` body other
  than the condition's own, using the same `success`/`failed`/`skipped` semantics
  that already apply to a top-level reference today.
- **FR-011**: When a `commandOutcome` `stepRef` names a step that is structurally
  reachable and prior but did not execute during a given run (for example, a step in
  an `If` branch that was not taken), the platform MUST continue to fail the
  referencing step and the sequence run with the existing "reference unavailable"
  error — widening reference scope MUST NOT introduce a new silent-success or
  silent-skip outcome for this case.
- **FR-012**: The platform MUST continue to accept and execute, unchanged, every
  sequence and condition that is valid under today's rules (top-level references,
  same-body sibling references, `success`/`failed`/`skipped` expected states, and
  existing `Loop`/`Break` status/message reporting) — this feature is additive only.

### Key Entities

- **Loop step result**: The per-run outcome record for a `Loop` step, already
  carrying a status and per-iteration detail; gains a structured exit reason
  describing which `Break` fired (if any) and whether max iterations was exhausted.
- **Break step**: A step nested inside a `Loop` body (directly, or inside an `If`
  body within that `Loop`) that conditionally or unconditionally ends the loop's
  iteration; gains a recorded fired/not-fired outcome resolvable by id from
  elsewhere in the sequence.
- **`commandOutcome` condition**: A condition (on an `If` or `Break` step) that
  gates on a named prior step's recorded outcome; its `stepRef` resolution scope
  widens from "immediate siblings only" to "anywhere reachable and prior in the
  sequence," and its accepted outcome vocabulary gains `break`/`no_break`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of automated tests covering count/while/repeat-until `Loop`
  execution assert a correct, structured exit reason (firing `Break` id, or
  max-iterations-exhausted, or neither) for every tested termination path.
- **SC-002**: A `commandOutcome` condition can reference a `Break` step nested
  anywhere else in the sequence and correctly distinguish `break` from `no_break`
  in 100% of the automated regression tests added for this feature.
- **SC-003**: A sequence-creation request that today is rejected with `400
  "references unknown prior step"` solely because its `stepRef` names a step outside
  its immediate sibling list, but which does name a structurally prior, reachable
  step, is accepted after this feature ships.
- **SC-004**: All pre-existing automated tests covering sequence validation, `Loop`
  execution, and condition evaluation continue to pass unmodified in their
  well-formed-input assertions (no regression to any documented working case).
