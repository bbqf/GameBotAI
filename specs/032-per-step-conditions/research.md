# Research

## Decision 1: Replace branch graph authoring with per-step optional conditions
- Decision: Represent sequence authoring as a linear ordered step list where each step has an optional condition field.
- Rationale: Aligns with user mental model ("for each step, optionally check condition") and removes entry-step/branch-link complexity.
- Alternatives considered:
  - Keep entry-step plus branch links (rejected: confusing in practical authoring use cases).
  - Keep both models in authoring UI (rejected: dual-mode UX complexity and validation overhead).

## Decision 2: Keep runtime semantics linear execute/skip/fail-stop
- Decision: Evaluate condition immediately before each step. `true` executes step, `false` skips step, evaluator errors fail-stop sequence.
- Rationale: Deterministic linear behavior matches existing sequence execution expectations and simplifies debugging.
- Alternatives considered:
  - Treat false as failure (rejected: violates intended conditional semantics).
  - Treat evaluator errors as skip (rejected: hides runtime faults).

## Decision 3: Support exactly two condition types in v1
- Decision: v1 condition types are `imageVisible` and `commandOutcome`.
- Rationale: Covers current real workflows (screen-state checks and prior-step gating) while keeping schema bounded.
- Alternatives considered:
  - `imageVisible` only (rejected: insufficient for stateful step dependencies).
  - Pluggable arbitrary condition registry (rejected: unnecessary for first release scope).

## Decision 4: Constrain commandOutcome references to prior steps
- Decision: `commandOutcome` may reference only earlier steps in the same sequence order.
- Rationale: Prevents forward/circular dependencies and preserves deterministic single-pass execution.
- Alternatives considered:
  - Any-step reference (rejected: introduces unresolved-forward-reference behavior).
  - Only immediately previous step (rejected: too restrictive for common flows).

## Decision 5: CommandOutcome expected states in v1
- Decision: Allowed expected states are `success`, `failed`, and `skipped`.
- Rationale: Explicitly models key observed outcomes in step-level runtime logs and enables practical dependent-step logic.
- Alternatives considered:
  - `success` only (rejected: cannot branch on fallback outcomes).
  - `success` and `failed` only (rejected: omits skipped-based logic).

## Decision 6: Clean-slate scope only
- Decision: Ignore legacy branch-mode sequence migration and assume only new per-step sequences exist.
- Rationale: User explicitly scoped legacy handling out; this removes migration complexity from implementation.
- Alternatives considered:
  - Auto-migration (rejected: unnecessary under clean-slate assumption).
  - Read-only legacy support (rejected: out of scope).

## Decision 7: Preserve existing repository and API integration surfaces
- Decision: Continue using current file-backed repositories and sequence endpoints; evolve payload contracts to per-step optional condition fields without introducing new storage systems.
- Rationale: Minimizes integration risk and leverages current test harnesses.
- Alternatives considered:
  - New endpoint family for per-step model (rejected: duplicate API surface and migration burden).
