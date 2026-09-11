# Feature Specification: Dry-Run / Validate-Only Sequence Mode

**Feature Branch**: `082-dry-run-sequences`
**Created**: 2026-09-11
**Status**: Draft
**Input**: User description: "Implement FR-002 from C:\src\PNS\docs\api-feature-requests.md: a dry-run / validate-only mode for sequence authoring and execution — resolve every commandId/image reference and check structural constraints without dispatching any input to the emulator."

## Background

An external consumer of the GameBotAI platform API (the PNS project) maintains a
feature-request tracker at `C:\src\PNS\docs\api-feature-requests.md`. Its second
entry, FR-002, documents a real, paid authoring cost: diagnosing why a sequence's
structural design was wrong (Loop nesting, condition placement, a bad `commandId`
or image reference) required creating and executing roughly ten throwaway
sequences against a live emulator session — one real device run per hypothesis —
even though most of those hypotheses were purely structural and did not need the
emulator at all.

This feature closes that gap with an additive `dryRun` option on both halves of
the sequence API that can incur that cost:

1. **Create** (`POST /api/sequences`) gains a `dryRun: true` mode that performs
   the exact same validation as a real create — reference resolution and every
   structural constraint (no nested `Loop`, no nested `If`, `Break` placement,
   `requireDispatch` placement, etc.) — but never persists a sequence.
2. **Execute** (`POST /api/sequences/{id}/execute`) gains a `dryRun: true` mode
   that walks an already-persisted sequence's real step tree — so `Loop`
   iteration/exit-reason mechanics, `If`/`Break` branch selection, and step
   visitation order are genuinely exercised — but every step that would dispatch
   input to the emulator, start or use a session, or read live screen-capture
   state is skipped and reported with a `skipped_dry_run` outcome instead.

Neither mode changes the meaning or result of any sequence create/execute call
that does not opt in; `dryRun` defaults to `false` everywhere it is added.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate a candidate sequence body without creating it (Priority: P1)

A sequence author has a candidate sequence body — new steps, a reshuffled
`Loop`/`If` nesting, a condition referencing a different step — and wants to know
whether it is structurally valid (every `commandId`/image reference resolves,
no illegal nesting, `Break`/`requireDispatch` placement rules are respected)
without creating a real, persisted sequence for every variant they try.

**Why this priority**: This is the specific, evidenced cost from FR-002 — roughly
ten throwaway sequences created and run just to isolate a structural question.
It delivers value entirely on its own, before the execute-side dry-run exists:
every one of those ten iterations could have been a single dry-run create call
instead of a create-then-execute-then-inspect-then-delete cycle.

**Independent Test**: Submit a `POST /api/sequences` body with `dryRun: true`
that is structurally valid; confirm the call succeeds and that no sequence with
a matching name/shape appears in a subsequent list of sequences. Submit a second
body with `dryRun: true` that violates a structural rule (e.g. a `Loop` nested
inside another `Loop`); confirm it fails with the same error that a non-dry-run
create would produce. Fully testable without touching the execute endpoint.

**Acceptance Scenarios**:

1. **Given** a structurally valid sequence body with every `commandId` and image
   reference resolvable, **When** it is submitted to create with `dryRun: true`,
   **Then** the response reports success and no new sequence is persisted.
2. **Given** a sequence body containing a `Loop` nested inside another `Loop`'s
   body, **When** it is submitted to create with `dryRun: true`, **Then** the
   response reports the same structural error a non-dry-run create would report,
   and nothing is persisted.
3. **Given** a sequence body whose `commandId` does not resolve to an existing
   command, **When** it is submitted to create with `dryRun: true`, **Then** the
   response reports the same reference-resolution error a non-dry-run create
   would report, and nothing is persisted.
4. **Given** a sequence body that is valid under every rule already enforced by
   today's create endpoint, **When** it is submitted with `dryRun` omitted or set
   to `false`, **Then** behavior is unchanged from today — a real sequence is
   created.

---

### User Story 2 - Exercise an existing sequence's structure without touching the emulator (Priority: P2)

A sequence author has already created a sequence (or is iterating on one they
created via Story 1's dry-run create, then created for real once satisfied) and
wants to confirm its runtime branching behavior — which `Break` fires, whether a
`Loop` exhausts its iterations, which `If` branch is taken, the order steps are
visited in — without dispatching taps/swipes/keys to a real emulator or needing
a device session to be running at all.

**Why this priority**: Lower priority than Story 1 because it depends on a
sequence already existing (Story 1 or a normal create), and because its
guarantee is inherently partial — branching driven by live device state (e.g. an
`imageVisible` condition) cannot be meaningfully exercised without a real
screen, so this story's value is scoped to structural/control-flow mechanics
that do not depend on live capture state. It is still worth shipping alongside
Story 1 because FR-002 explicitly asks for it and it removes emulator
dependency for the mechanics it can cover.

**Independent Test**: Execute an existing sequence containing a `Loop` with a
conditional `Break` and at least one primitive tap step, once with
`dryRun: true` and no session available. Confirm the call succeeds, the tap
step's outcome is `skipped_dry_run`, and the `Loop`'s exit reason still
correctly reflects whether the `Break` fired. Independently testable once a
sequence exists, without needing Story 1.

**Acceptance Scenarios**:

1. **Given** an existing sequence containing a primitive tap step, **When** it
   is executed with `dryRun: true` and no `sessionId` is provided, **Then** the
   call succeeds, the tap step's outcome is `skipped_dry_run`, and no session is
   started or resolved.
2. **Given** an existing sequence containing a `Loop` with a `Break` whose
   condition depends only on sequence parameters (not live device state),
   **When** it is executed with `dryRun: true` using inputs that make the
   `Break` fire, **Then** the `Loop` step's exit reason correctly reports the
   firing `Break`'s id, exactly as a real run would.
3. **Given** the same sequence executed with `dryRun: true` using inputs that
   never satisfy the `Break` condition and exhaust the loop's configured max
   iterations, **Then** the `Loop` step's exit reason correctly reports
   `exhaustedMaxIterations: true`, exactly as a real run would.
4. **Given** an existing sequence containing a step that references a
   `commandId` which no longer resolves (e.g. the referenced command was
   deleted after the sequence was created), **When** it is executed with
   `dryRun: true`, **Then** the call reports the same reference-resolution
   error a non-dry-run execution would report.
5. **Given** the same existing sequence, **When** it is executed with `dryRun`
   omitted or set to `false`, **Then** behavior is unchanged from today — real
   dispatch occurs.

---

### Edge Cases

- A `commandOutcome` condition references a step whose own outcome was
  `skipped_dry_run` (because dry-run skipped it): the condition resolves
  against that outcome like any other non-matching outcome — it does not equal
  `success`/`failed`/`skipped`/`break`/`no_break`, so the condition evaluates
  false. This is a documented, inherent limitation of dry-run branching, not a
  defect.
- A sequence-referenced `Command`'s own inner steps (dispatched via the
  command-execution path, not the sequence-execution path directly) are also
  skipped under `dryRun: true` — dry-run coverage is not limited to steps
  authored directly on the sequence.
- A self-reschedule step is skipped and reported `skipped_dry_run` under
  `dryRun: true`, even though it does not dispatch input to the emulator,
  because it is a persistent side effect (mutating queue schedule state) that a
  validate-only run should not perform.
- A queue-scheduled (non-ad-hoc) sequence run is never affected by this
  feature: `dryRun` is only reachable through the direct create/execute API
  surface, never through scheduled queue execution.
- A step gated by a per-step `imageVisible` condition (distinct from the
  dedicated `waitForImage` action step) is executed with `dryRun: true` and no
  session running: the condition resolves to false (live capture is never
  read), so the gated step is skipped like any other false-evaluated
  condition — the call still succeeds rather than failing for lack of a
  session.
- A step references an image (via `waitForImage` or an `imageVisible`
  condition) that no longer exists (e.g. deleted after the sequence was
  created): unlike a stale `commandId` (FR-010), this is **not** specially
  detected under `dryRun` — it resolves the same neutral way any
  false-evaluated image condition does, since checking image existence
  without also touching the live-capture path is not worth the added
  complexity for a case FR-002 did not evidence a cost for. A non-dry-run
  execution's existing "image reference unavailable" error is unaffected.
- Executing with `dryRun: true` against a sequence that no longer exists (bad
  id) still reports the existing "not found" error — dry-run does not change
  existence checks.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `POST /api/sequences` (create) MUST accept an optional `dryRun`
  boolean field, defaulting to `false` when omitted.
- **FR-002**: When `dryRun` is `true` on create, the platform MUST perform the
  same command-reference enrichment and per-step validation that a real create
  performs today — every structural constraint (no nested `Loop`, no nested
  `If`, `Break` placement, `requireDispatch` placement) and every
  `commandId`/image reference resolution — and MUST NOT persist a new sequence.
- **FR-003**: When `dryRun` is `true` on create and validation succeeds, the
  platform MUST respond with a success result indicating the body would have
  been created, without assigning or returning a persisted sequence id.
- **FR-004**: When `dryRun` is `true` on create and validation fails, the
  platform MUST respond with the identical error status and shape a
  non-dry-run create would produce for the same body.
- **FR-005**: `POST /api/sequences/{id}/execute` MUST accept an optional
  `dryRun` boolean field, defaulting to `false` when omitted.
- **FR-006**: When `dryRun` is `true` on execute, every step that would
  otherwise dispatch input to the emulator, start or use an emulator session,
  or read live screen-capture state — primitive tap/swipe/key input,
  connect-to-game, ensure-game-running, ensure-emulator-running,
  go-to-home-screen, wait-for-image, and the inner dispatch of any
  command-referencing step — MUST be skipped without performing that action,
  and MUST report a `skipped_dry_run` outcome.
- **FR-007**: When `dryRun` is `true` on execute, a self-reschedule step MUST
  also be skipped and reported `skipped_dry_run`, since it produces a
  persistent side effect unrelated to verifying structural/behavioral
  correctness.
- **FR-008**: When `dryRun` is `true` on execute, `Loop`/`If`/`Break`
  control-flow evaluation — iteration counting, exit-reason determination,
  condition evaluation, and branch selection — MUST execute exactly as in a
  real run, for every part of that evaluation that does not itself require
  live emulator/session state.
- **FR-009**: When `dryRun` is `true` on execute, the call MUST NOT require a
  session to be provided or resolved, and MUST succeed even when no emulator
  session exists.
- **FR-010**: When `dryRun` is `true` on execute and a step references a
  `commandId` that cannot be resolved to an existing command, the platform
  MUST still report that as a real error, identical to a non-dry-run
  execution — checking a `commandId`'s existence costs nothing in emulator
  terms and preserves genuine value. This check MUST NOT itself dispatch to
  the referenced command (only confirm it exists).
- **FR-011**: `dryRun` MUST default to `false` wherever it is added to a
  request contract, and a call that omits it MUST behave identically to
  today's create/execute behavior.
- **FR-012**: Queue-scheduled sequence execution MUST always run with `dryRun`
  effectively `false`; the option MUST only be reachable through the direct
  create and execute API surface, never through scheduled queue runs.
- **FR-013**: The execute response for a `dryRun: true` call MUST use the same
  result shape (overall status, per-step results, block results, condition
  traces) as a real execution response, so a client parses both identically.
- **FR-014**: The platform MUST continue to accept and execute, unchanged,
  every sequence create/execute call that omits `dryRun` or sets it to
  `false` — this feature is strictly additive.
- **FR-015**: When `dryRun` is `true` on execute, any condition evaluation
  that would otherwise read live screen-capture state — including a per-step
  `imageVisible` gating condition, not only the dedicated `waitForImage`
  action step — MUST NOT read live capture state, and MUST resolve the same
  way an ordinary false-evaluated condition resolves today (the gated step is
  skipped, not failed, and not blocked on a session existing).

### Key Entities

- **Sequence create request**: gains an optional `dryRun` flag; when set,
  validates a candidate body exactly as a real create would, without
  persisting anything.
- **Sequence execute request**: gains an optional `dryRun` flag; when set,
  walks an existing sequence's real step tree without dispatching to the
  emulator, starting a session, or reading live capture state.
- **`skipped_dry_run` step outcome**: a new canonical outcome value reported
  for any step a dry-run execution intentionally did not dispatch, so callers
  can distinguish "this would have dispatched but didn't" from every other
  existing outcome (`success`/`failed`/`skipped`/`break`/`no_break`/etc.).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A sequence author can determine whether a candidate sequence
  body is structurally valid — nested-block rules, `Break`/`requireDispatch`
  placement, `commandId`/image reference resolution — with a single dry-run
  create call, with zero sequences persisted and zero emulator/session
  activity, replacing what previously required a full create-run-inspect-delete
  cycle per hypothesis.
- **SC-002**: A sequence author can dry-run execute an existing sequence and
  observe the same `Loop` exit-reason, branch-selection, and step-visitation
  structure a real run would produce for every part of that structure not
  dependent on live device state, with zero calls reaching emulator input
  dispatch or session start.
- **SC-003**: 100% of automated tests covering existing (non-dry-run) sequence
  creation and execution behavior continue to pass unmodified.
- **SC-004**: Every dry-run-skipped step in an execute response is
  distinguishable from a real dispatch outcome by one documented outcome
  value (`skipped_dry_run`), consistently across every dispatch-capable step
  type (primitive input, connect/ensure/home actions, wait-for-image,
  command-referenced inner steps, self-reschedule).
