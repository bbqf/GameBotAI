# Research: Visual Conditional Sequence Logic

## Decision 1: Canonical condition expression model

- Decision: Represent conditions as a recursive expression tree with typed operands (`command-outcome`, `image-detection`) and logical nodes (`and`, `or`, `not`), evaluated left-to-right within each node.
- Rationale: A tree model maps directly to visual authoring, supports nested logic naturally, and keeps serialization deterministic for tests and diffing.
- Alternatives considered:
  - Flat infix string expressions (rejected: harder to validate and localize authoring errors).
  - Rule-list only model without nesting (rejected: insufficient for required AND/OR/NOT composition).

## Decision 2: Unevaluable condition behavior

- Decision: If any required operand cannot be evaluated (timeout, missing reference, evaluator error), mark the condition step failed and stop sequence execution immediately.
- Rationale: This matches the accepted clarification and prevents accidental branch execution with unreliable inputs.
- Alternatives considered:
  - Fallback to false branch (rejected: can mask evaluator faults and execute unsafe paths).
  - Retry indefinitely (rejected: risks hangs and nondeterministic outcomes).

## Decision 3: Image condition truth semantics

- Decision: Image-detection operand evaluates true when at least one match meets or exceeds threshold.
- Rationale: Aligns with binary IF expectations and existing detection semantics centered on match confidence.
- Alternatives considered:
  - Exactly one match required (rejected: fragile for real-world UI duplicates).
  - Top-match only (rejected: can ignore valid secondary detections in some layouts).

## Decision 4: Cycle handling strategy

- Decision: Cycles are allowed only when every detected cycle path declares an explicit maximum iteration limit; validation fails otherwise.
- Rationale: Supports practical retry loops while guaranteeing bounded execution and preventing infinite loops.
- Alternatives considered:
  - Disallow all cycles (rejected: blocks useful retry patterns).
  - Allow cycles with runtime timeout only (rejected: weak predictability and operator diagnostics).

## Decision 5: Iteration-limit exhaustion behavior

- Decision: When a cycle reaches its configured maximum iteration limit, mark the current step failed and stop the sequence.
- Rationale: Preserves deterministic termination and makes limit exhaustion explicit for operators and alerts.
- Alternatives considered:
  - Continue to first non-cyclic path (rejected: can hide failed retry intent).
  - Per-cycle configurable continuation policy (rejected: increases complexity and ambiguity for first release).

## Decision 6: Deep-link and log identity contract

- Decision: Include immutable sequence/step IDs for navigation plus readable sequence/step labels in every step-level execution log entry.
- Rationale: IDs ensure durable links after rename/reorder; labels preserve operator readability.
- Alternatives considered:
  - Labels or indexes only (rejected: brittle under edits).
  - IDs only (rejected: lowers troubleshooting usability).

## Decision 7: Debug-level condition trace schema

- Decision: Emit one debug trace envelope per condition step with ordered operand results, operator applications, final boolean outcome, and chosen branch.
- Rationale: Provides reproducible evaluation reasoning without requiring code-level introspection.
- Alternatives considered:
  - Log only final result (rejected: insufficient for root-cause analysis).
  - Emit per-operand logs without envelope (rejected: fragmented and harder to correlate).
