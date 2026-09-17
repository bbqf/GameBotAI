# Feature Specification: Condition-Model Ceiling Retest and Documented Outcome

**Feature Branch**: `103-condition-model-retest-docs`
**Created**: 2026-09-17
**Status**: Draft
**Input**: GitHub issue [#193](https://github.com/bbqf/GameBotAI/issues/193) — "B-006: condition-model ceiling on nested stepRef and loop exit reason - retest owed, not confirmed closed" (`documentation`, `P3`). Closes #193.

## Context

Issue #193 is not a feature request. It reports that a previously observed
limitation in the sequence per-step condition model was **reported** fixed by an
earlier feature (FR-001, delivered 2026-09-12, spec directory
`specs/081-loop-exit-reason-and-nested-steprefs`) but was never **re-measured**.
The issue's own words: *"'Reported fixed' and 'observed fixed' are different
claims."*

The originally observed ceiling had two halves:

1. A `commandOutcome` condition's `stepRef` could only name a **top-level** prior
   step; naming a step nested inside a `Loop`/`If` body was rejected with
   `400 "references unknown prior step"`. `expectedState` accepted only
   `success|failed|skipped`, with no way to ask whether a specific nested `Break`
   had fired.
2. A top-level `Loop`'s own status was `Succeeded` whether it exited via `Break`
   or by exhausting `maxIterations` — the two were indistinguishable.

What the issue asks for: *"Either a confirmation that the whole ceiling is gone —
nested `stepRef` in every condition variant, and a readable loop exit reason — or
a statement of what remains, so the local row can be closed on evidence."*

This feature therefore delivers **measurement first, and whatever the
measurement justifies second**: durable automated evidence covering both halves
across every condition variant, the closure of whichever gaps that evidence
exposes and that are fixable within the already-delivered design, and a written
statement of anything that genuinely remains.

## Clarifications

### Session 2026-09-17

Answered autonomously (no manual review in this run); each answer states the
reasoning that selected it.

- Q: Is exposing a loop's exit reason to a caller of the service (FR-011) a fix
  this feature makes, or a limitation it merely records? → A: **A fix, narrowly
  scoped.** The exit reason is already returned on the synchronous run response,
  so the issue's second half is genuinely delivered for a caller who runs a
  sequence directly. It is *not* recorded in the persisted run log, which is the
  only record a queue-driven run leaves behind — and queue-driven runs are how the
  downstream consumer actually executes sequences. So the value is computed,
  correct, and already published on one surface; carrying it to the persisted
  record is additive plumbing of an existing value, placing it on the "bring into
  line with the delivered design" side of A-002.
- Q: Which surface carries it? → A: **The loop step's entry in the persisted run
  log**, alongside the iteration count already recorded there. The synchronous run
  response needs no change. Rejected also adding it to the run-tree node view:
  that is a second, independently-consumed surface with its own published-schema
  concerns, and widening scope to it would add risk without adding evidence for
  issue #193.
- Q: Is the web authoring UI's divergence from the service (FR-012) a fix or a
  recorded limitation? → A: **Split.** Two stale client-side rules — resolving a
  reference against top-level steps only, and restricting outcome states to the
  pre-FR-001 set — are fixed: they reject exactly what the service accepts, and
  they are the original ceiling still standing in the UI. The absence of composite
  conditions from the UI's own condition editor is **recorded as remaining**, not
  fixed: that is missing capability rather than a rule contradicting the service,
  and building it is new scope this issue did not ask for.
- Q: When a `Break` fires in the same iteration that exhausts the iteration
  ceiling, which cause is reported? → A: **The break, identified by name, with
  exhaustion reported as false.** The break is the author's own explicit exit and
  is what actually ended that iteration; the ceiling was merely also reached. This
  is what the delivered implementation already does, so the requirement pins
  existing behaviour rather than changing it — which is the correct posture for a
  retest.
- Q: Where is the retest conclusion written? → A: **In the published interface
  description, plus the existing architecture documentation.** This matches how
  the four most recent documentation issues in this repository were closed, so a
  reader looking for condition-model semantics finds them where the neighbouring
  answers already live. Rejected a new standalone document, which would add a
  place to look without adding a reader.
- Q: How does a save-time rejection of a reference nested inside a composite
  identify itself? → A: **Reusing the position-path convention the composite
  validator's other messages already use**, so an author reads one message format
  regardless of which rule caught the problem, and FR-010's "find it in a large
  sequence" is satisfied without inventing a second idiom.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An automation author gets a trustworthy, non-rotting answer (Priority: P1)

An author maintaining sequences against this service needs to know, with
certainty, whether a condition may reference a step nested inside a loop or an
`if` branch — in every place a condition can be written, not just the simplest
one — and whether a loop's exit can be told apart from a loop's exhaustion. Today
that answer exists only as a claim in an issue thread. The author needs it as
evidence that re-runs on every build, so the answer cannot silently rot.

**Why this priority**: This is the whole of what the issue asks for. Without the
measurement, nothing else in this feature is justified, and the issue's central
complaint — that the retest was never run — stands unaddressed.

**Independent Test**: Run the repository's automated test suite. Every claim
about nested references and loop exit reasons is asserted by a named test that
fails loudly if the behaviour regresses. Delivers value on its own even if no gap
is found: the answer becomes observed rather than assumed.

**Acceptance Scenarios**:

1. **Given** a sequence whose `commandOutcome` condition names a step nested
   inside a different, earlier `Loop` body, **When** the sequence is saved,
   **Then** the save succeeds rather than being rejected with "references unknown
   prior step", and an automated test asserts this.
2. **Given** the same reference written inside each composite condition variant
   (`all`, `any`, `none`), and inside a composite nested within a composite,
   **When** the sequence is saved, **Then** the reference is subject to the same
   resolution and ordering rules as the same reference written directly, and an
   automated test asserts this for each variant.
3. **Given** a `commandOutcome` condition asking whether a specific nested
   `Break` fired, **When** the sequence runs and that `Break` fires, **Then** the
   condition distinguishes fired from not-fired, and an automated test asserts
   both directions.
4. **Given** a loop that exits because a `Break` fired and a loop that exits
   because it exhausted its iteration ceiling, **When** a caller inspects each
   loop's recorded result, **Then** the two exits are distinguishable and the
   fired `Break` is identified, and an automated test asserts this.
5. **Given** a reference that names a step that does not exist anywhere in the
   sequence, or that names a structurally later step, **When** the sequence is
   saved, **Then** it is still rejected — proving the rules were widened, not
   removed — and an automated test asserts this for every condition variant.

---

### User Story 2 - The gaps the measurement exposes are closed, not papered over (Priority: P2)

Where the retest shows part of the ceiling still standing, and closing it is a
matter of applying the already-delivered design consistently rather than
designing something new, it is closed. Where closing it would mean building new
capability, it is written down instead of being quietly fixed or quietly ignored.

**Why this priority**: The issue explicitly accepts "a statement of what remains"
as a valid outcome, so fixing is not strictly required to close the issue. But
leaving a known, cheap inconsistency in place after measuring it would recreate
exactly the situation the issue complains about.

**Independent Test**: For each gap the retest identifies, a test written against
the gap fails before the corresponding change and passes after it. Testable
independently of the documentation in User Story 3.

**Acceptance Scenarios**:

1. **Given** a `commandOutcome` condition nested inside a composite whose
   `stepRef` names a step that exists nowhere in the sequence, **When** the
   sequence is saved, **Then** it is rejected at save time with a message
   identifying the offending condition, rather than being accepted and failing
   later at run time.
2. **Given** a `commandOutcome` condition nested inside a composite whose
   `stepRef` names a structurally later step, **When** the sequence is saved,
   **Then** it is rejected with the same ordering error the equivalent
   directly-written condition produces.
3. **Given** a completed sequence run containing a loop, **When** a caller reads
   that run's recorded result through the service's own interface, **Then** the
   loop's exit reason is readable as data — which `Break` fired, or that the
   iteration ceiling was exhausted — not only as prose in a message.
4. **Given** an author using the web authoring UI, **When** they write a
   condition that the service accepts — a reference to a nested step, or a
   fired/not-fired `Break` check — **Then** the UI does not block it with a
   stale client-side objection the service itself would not raise.

---

### User Story 3 - The outcome is written down where the next reader will find it (Priority: P3)

Anyone who later asks the question issue #193 asks should find the answer in the
project's own published interface description and documentation, rather than
having to re-derive it from tests or re-open the issue.

**Why this priority**: The issue's stated closure condition is evidence plus a
statement. The evidence (US1) is the harder half and the tests are the durable
artifact; the written statement makes the result discoverable, which is why it
follows rather than leads.

**Independent Test**: Read the published interface description and the
architecture documentation without reading any test code, and the supported
reference scope, the accepted outcome states, the loop exit-reason shape, and any
remaining limitation are all stated there.

**Acceptance Scenarios**:

1. **Given** the published interface description, **When** a reader looks up a
   step condition, **Then** it states which steps a reference may name (scope and
   ordering), and every outcome state that is accepted.
2. **Given** the published interface description, **When** a reader looks up a
   loop's recorded result, **Then** the exit-reason shape and the meaning of each
   of its values is stated, including the case where neither a break nor
   exhaustion occurred.
3. **Given** the project documentation, **When** a reader looks for the status of
   the ceiling this issue describes, **Then** they find the retest's conclusion:
   which parts are confirmed gone, and precisely what remains and under which
   condition variant.

---

### Edge Cases

- A reference whose target is reachable in the sequence tree but structurally
  **after** the referencing condition: must stay rejected, in every variant,
  including inside a composite. Widening scope must not become abandoning order.
- A reference nested at the maximum permitted condition depth: the same rules
  apply there as at depth one, and exceeding the depth limit is still rejected.
- A composite with a mixture of variant children, where only one child carries a
  reference: only the offending child is reported, and it is identified by its
  position within the condition.
- A loop that exits normally — its body/condition simply finished, with neither a
  `Break` firing nor the iteration ceiling being reached: the exit reason must be
  readable as "neither", not silently rendered as one of the two.
- A loop whose iteration ceiling is reached in the same iteration a `Break` fires:
  the exit reason names the break, reports exhaustion as false, and this choice is
  asserted rather than left incidental (FR-012a).
- A nested `Break` that never fires: a condition asking whether it fired must
  answer "did not fire" rather than becoming unanswerable.
- A condition that genuinely cannot be answered at run time must remain
  distinguishable from a condition that answered "false", so a broken setup does
  not silently present as a skipped step.

## Requirements *(mandatory)*

### Functional Requirements

**Measurement (User Story 1)**

- **FR-001**: The repository MUST contain automated tests that assert a
  `commandOutcome` reference naming a step nested inside a `Loop` body or an `If`
  branch — other than the condition's own sibling list — is accepted at save
  time.
- **FR-002**: Those tests MUST cover the reference written directly as a step's
  condition **and** written as a child of each composite variant (`all`, `any`,
  `none`), and as a child of a composite nested inside another composite.
- **FR-003**: The tests MUST cover every slot in which a condition is validated —
  a step's own guard, a loop's continuation guard, a break guard, and an `if`'s
  branch guard — so "every condition variant" is measured in every position, not
  only in the most convenient one.
- **FR-003a**: Where an earlier delivery deliberately excluded a slot from
  validation, that exclusion MUST be asserted as intended behaviour rather than
  removed. This feature measures the delivered design; it does not reverse earlier
  decisions. (Stating the inherited boundary in the documentation is FR-016's duty,
  not repeated here.)
- **FR-004**: The tests MUST assert that a fired nested `Break` and a
  not-fired nested `Break` are distinguishable by a condition at run time.
- **FR-005**: The tests MUST assert that a loop exiting via `Break` is
  distinguishable from a loop exiting by exhausting its iteration ceiling, that
  the fired `Break` is identified, and that a loop which did neither is reported
  as having done neither.
- **FR-006**: The tests MUST assert that a reference to a step absent from the
  whole sequence, and a reference to a structurally later step, are each still
  rejected — in every condition variant covered by FR-002.
- **FR-007**: Every assertion this feature relies on MUST live in the automated
  suite that runs on every build. A result recorded only in prose, or produced by
  a one-off manual probe, does not satisfy any requirement in this section.

**Gap closure (User Story 2)**

- **FR-008**: A `commandOutcome` condition nested inside a composite MUST be
  subject to the same reference-resolution rule as one written directly: a
  reference to a step absent from the whole sequence MUST be rejected at save
  time.
- **FR-009**: Such a nested condition MUST also be subject to the same ordering
  rule: a reference to a structurally later step MUST be rejected at save time.
- **FR-010**: A save-time rejection under FR-008 or FR-009 MUST identify both the
  offending step and the offending condition's position within the condition
  tree, so an author can find it in a large sequence. It MUST use the same
  position-path convention the other composite-condition messages already use,
  rather than a second message idiom.
- **FR-011**: A loop's exit reason MUST be readable as structured data — the
  identifier of the `Break` that fired, or an indication that the iteration ceiling
  was exhausted — from the **persisted run log** of a completed run, not only from
  the response to a directly-invoked run. It MUST be carried on the loop step's
  entry in that log, beside the iteration count already recorded there, and not
  only as prose inside a human-readable message.
- **FR-011a**: The exit reason already returned on a directly-invoked run's
  response MUST keep its present shape and field names. This feature adds a second
  place to read it; it does not move or rename the first.
- **FR-012**: The web authoring UI MUST NOT reject a condition that the service
  accepts. Specifically it MUST accept a reference to a step nested inside a loop
  body or an `if` branch, and MUST accept every outcome state the service
  accepts, including the fired/not-fired `Break` states.
- **FR-012a**: A loop that exits because a `Break` fired in the same iteration
  that also reached the iteration ceiling MUST report the break — named — as the
  cause, and MUST report exhaustion as not having occurred. Exactly one cause is
  reported.
- **FR-013**: No new condition variant, and no new outcome state, may be
  introduced by this feature. The permitted set is exactly what the earlier
  delivery already established.

**Written outcome (User Story 3)**

- **FR-014**: The published interface description MUST state, for a step
  condition, the scope within which a reference is resolved and the ordering
  constraint applied to it, and MUST list every accepted outcome state.
- **FR-015**: The published interface description MUST state a loop result's
  exit-reason shape, the meaning of each value, and the mutual exclusivity of
  breaking and exhausting.
- **FR-016**: The project's existing architecture documentation MUST record the
  retest's conclusion: for each half of the ceiling, whether it is confirmed gone,
  and for anything remaining, precisely what remains and in which condition
  variant or component. No new standalone document is created for this.
- **FR-016a**: If the retest confirms the web authoring UI offers no way to author
  a composite condition, that MUST be recorded under FR-016 as a remaining
  limitation, with the note that it is absent capability rather than a rule
  contradicting the service.
- **FR-017**: The recorded conclusion MUST cite the automated tests that
  establish it, so a later reader can re-run the evidence rather than trusting the
  prose.

### Key Entities

- **Step condition**: a guard attached to a sequence step, a loop, a break or an
  `if` branch. Exists in leaf forms (screen-image visibility, prior-step outcome)
  and composite forms that combine children under an all/any/none rule.
- **Step reference**: the identifier, carried by a prior-step-outcome condition,
  naming the step whose outcome is being asked about. Subject to two rules —
  the reference must resolve to a step in the sequence, and that step must come
  before the asking condition in authored order.
- **Outcome state**: the value a prior-step-outcome condition compares against,
  covering ordinary step results and the fired/not-fired states of a break.
- **Loop exit reason**: the structural record of why a loop stopped iterating —
  a fired break identified by name, an exhausted iteration ceiling, or neither.
- **Retest conclusion**: the written finding this feature produces, pairing each
  half of the reported ceiling with its present status and the evidence for it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every condition variant enumerated in FR-002, and every validated
  slot enumerated in FR-003, is covered by at least one passing automated assertion
  about nested reference resolution — a reader can count the variants, count the
  slots, and find a test for each. The two axes are covered independently; a full
  variant-by-slot cross-product is explicitly not required.
- **SC-002**: Both halves of the reported ceiling have a recorded status of either
  "confirmed gone, by these tests" or "remains, precisely here" — zero halves left
  in the "reported but not observed" state that caused the issue to be filed.
- **SC-003**: A reference that names a nonexistent step, and a reference that
  names a later step, are each rejected at save time in every variant from
  FR-002 — measured as zero variants in which such a reference is accepted.
- **SC-004**: A caller can determine, from the persisted run log of a completed run
  and without reading log prose, which of the three loop exits occurred, for 100% of
  loops in that run.
- **SC-005**: There is no condition the service accepts that the web authoring UI
  rejects, across the reference scopes and outcome states covered by this feature
  — measured as zero such divergences.
- **SC-006**: A reader who consults only the published interface description and
  the project documentation can answer the question issue #193 asks, without
  reading test code or reopening the issue.
- **SC-007**: The whole automated suite passes, with no pre-existing assertion
  about condition validation, loop behaviour or `if` behaviour weakened or removed
  to accommodate this feature.

## Assumptions

- **A-001**: "Every condition variant" is read as the variants the condition model
  actually offers — the two leaf forms and the three composite forms — in every
  slot a condition may occupy. (FR-013 holds the normative rule against adding
  any.)
- **A-002**: Where the retest finds a component behaving inconsistently with the
  already-delivered design, bringing it into line is treated as closing the
  measured gap, not as new scope. Where matching the design would require building
  a capability that does not exist anywhere yet, it is recorded as remaining. The
  boundary between these two cases is settled in Clarifications.
- **A-003**: The evidence artifact is the automated suite. Documentation records
  the conclusion and points at the tests; it is not itself the evidence.
- **A-004**: The issue's cited source rows (`docs/api-bugs.md` B-006 and FR-001 in
  `docs/api-feature-requests.md`) live in a separate downstream repository. They
  are context only. Nothing in this feature creates or edits them here.
- **A-005**: A downstream consumer already depends on the loop exit reason and on
  nested references, so any change this feature makes must be additive: widening
  what is accepted, or exposing what was already computed. No currently accepted
  sequence may become rejected, except where it was accepted only because a rule
  was missing (FR-008, FR-009) — which is a deliberate, stated correction.

## Out of Scope

- Redesigning or extending the condition model: no new condition variants, no new
  outcome states, no new reference syntax.
- Any change to how conditions are evaluated against the screen, or to image
  matching.
- Building composite-condition authoring into the web UI. Per Clarifications, its
  absence is recorded under FR-016a as remaining, not fixed here: it is missing
  capability rather than a client rule contradicting the service.
- Exposing the loop exit reason on the execution-tree node surface. FR-011 covers
  the loop step's own recorded run detail only; the tree node is a separate
  published surface and widening to it adds risk without adding evidence.
- Editing the downstream repository's bug or feature-request rows.
- Closing issue #193 by hand. The pull request's closing reference does that on
  merge.

## Dependencies

- The condition model, its save-time validation, its run-time evaluation, and the
  loop exit reason as delivered by the earlier feature in
  `specs/081-loop-exit-reason-and-nested-steprefs`, and the composite conditions
  delivered in `specs/088-composite-image-conditions`. This feature measures and
  documents those; it does not replace them.
- The existing automated suites covering sequence validation, loop behaviour, `if`
  behaviour and composite conditions, which this feature extends rather than
  duplicates.
