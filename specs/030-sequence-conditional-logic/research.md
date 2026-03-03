# Research: Visual Conditional Sequence Logic

## Decision 1: Canonical condition expression format

- Decision: Use a recursive expression tree with logical nodes (`and`, `or`, `not`) and operand leaves (`command-outcome`, `image-detection`), evaluated left-to-right per node.
- Rationale: Matches visual authoring structure, supports nesting naturally, and yields deterministic replay/testing behavior.
- Alternatives considered:
  - Infix string expression parsing (rejected: harder validation and poorer authoring diagnostics).
  - Flat rule lists (rejected: cannot express nested NOT/OR composition cleanly).

## Decision 2: Unevaluable condition handling

- Decision: Any condition evaluation that cannot complete (timeout, missing reference, evaluator failure) fails the condition step and stops the sequence immediately.
- Rationale: Prevents execution from following potentially unsafe branches with incomplete state.
- Alternatives considered:
  - Treat as `false` and continue (rejected: masks runtime faults).
  - Retry-until-success behavior (rejected: risks nondeterministic hangs).

## Decision 3: Image-detection truth rule

- Decision: Image operand evaluates `true` when at least one match meets/exceeds configured threshold.
- Rationale: Aligns with clarified spec and current detection semantics.
- Alternatives considered:
  - Require exactly one match (rejected: brittle in repeated UI elements).
  - Use highest match only (rejected: discards valid secondary detections).

## Decision 4: Cycle policy and iteration lifecycle

- Decision: Allow cycles only when each cycle has an explicit max iteration limit; runtime counters reset at the start of each sequence run.
- Rationale: Allows bounded retry loops while guaranteeing per-run termination.
- Alternatives considered:
  - Ban all cycles (rejected: removes practical retry/branching patterns).
  - Persist counters across runs (rejected: introduces hidden state and non-intuitive behavior).

## Decision 5: Iteration-limit exhaustion result

- Decision: On reaching iteration limit, mark current step failed and stop sequence.
- Rationale: Keeps failure explicit and operationally diagnosable.
- Alternatives considered:
  - Continue via fallback path (rejected: conceals retry-limit failures).

## Decision 6: Optimistic concurrency response contract

- Decision: Stale sequence save attempts return HTTP `409 Conflict` with payload containing `sequenceId` and `currentVersion`.
- Rationale: Standard optimistic-concurrency pattern with enough data for immediate client recovery.
- Alternatives considered:
  - `412 Precondition Failed` with no body (rejected: weaker client remediation path).
  - Generic `400` validation response (rejected: conflates conflicts with invalid payloads).

## Decision 7: Deep-link resolution behavior

- Decision: Logs include immutable IDs and readable labels; if target step no longer exists, UI opens sequence overview and displays a "referenced step missing" message.
- Rationale: Preserves historical trace usability while avoiding dead-end navigation.
- Alternatives considered:
  - Hard 404 page (rejected: poor operator recovery flow).
  - Remove historical links (rejected: loses audit utility).

## Decision 8: Deep-link authorization scope

- Decision: Do not add deep-link-specific authorization checks; rely on existing authoring UI routing behavior.
- Rationale: Matches clarified requirement and avoids duplicate enforcement layers.
- Alternatives considered:
  - Dedicated deep-link auth checks (rejected: out of scope and contradicts clarified behavior).

## Decision 9: Condition-trace observability schema

- Decision: Emit debug-level traces containing ordered operand outcomes, operator application steps, final boolean result, and selected branch.
- Rationale: Enables deterministic post-run explanation of branch decisions.
- Alternatives considered:
  - Final result only (rejected: insufficient for diagnosis).
  - Per-operand uncorrelated logs (rejected: weak trace correlation).

## Decision 10: Performance budget validation approach

- Decision: Validate conditional-step evaluation path with debug traces enabled against p95 ≤ 200 ms under normal load.
- Rationale: Directly measures the constrained hot path introduced by this feature.
- Alternatives considered:
  - End-to-end sequence-only latency metric (rejected: less sensitive to conditional evaluation regressions).
  - No explicit budget checks (rejected: violates constitution performance principle).
