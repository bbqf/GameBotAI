# Research: Randomized Sequence Step Delays

## Decision 1: Delay semantics are inter-step and post-step

- Decision: Apply randomized delay between consecutive executed steps (after one step completes and before the next step starts).
- Rationale: The feature explicitly asks for delays "in between Sequence execution steps" and avoids adding delay after the final step.
- Alternatives considered:
  - Pre-step-only delays (rejected: does not match "between steps" semantics).
  - Add delay after final step (rejected: no subsequent step boundary exists).

## Decision 2: Preserve existing per-step delay behavior and add sequence-level pacing

- Decision: Keep current per-step delay behavior intact and introduce sequence-level inter-step delay range as an additional pacing layer.
- Rationale: Existing step-level delay/range fields are already part of the domain model and may be used by existing sequences; preserving them prevents regressions.
- Alternatives considered:
  - Replace per-step delay with sequence-level delay (rejected: breaking behavior change for existing authored sequences).
  - Ignore existing per-step delay fields for this feature (rejected: causes inconsistent runtime behavior).

## Decision 3: Sampling strategy is uniform and inclusive

- Decision: Use uniform random sampling over integer milliseconds with inclusive bounds (`min <= sampled <= max`).
- Rationale: This was clarified in spec and is straightforward to explain, test, and validate statistically.
- Alternatives considered:
  - Exclusive upper bound sampling (rejected: contradicts clarified requirement).
  - Biased distribution (rejected: not requested and harder to reason about).

## Decision 4: Range validation policy

- Decision: Validate sequence-level delay range as integer milliseconds with `min >= 0`, `max` integer, and `min <= max`, with no explicit upper bound.
- Rationale: Matches clarified requirements and avoids introducing arbitrary caps that were explicitly declined.
- Alternatives considered:
  - Fixed upper bound (rejected: contradicts clarification).
  - Decimal duration support (rejected: contradicts integer-only clarification).

## Decision 5: Backward compatibility and defaults

- Decision: Existing sequences without sequence-level delay configuration use default range `100..300` ms.
- Rationale: Required by spec and preserves behavior for legacy persisted sequence payloads without migration blockers.
- Alternatives considered:
  - Require explicit migration for all sequences (rejected: unnecessary operational friction).
  - Default to no inter-step delay when field is absent (rejected: violates FR-003/FR-009 intent).

## Decision 6: Persistence and contract extension pattern

- Decision: Add an optional sequence-level object (for example `interStepDelayRangeMs`) in sequence persistence and API DTOs.
- Rationale: Sequence-level configurability belongs on `CommandSequence` rather than individual steps.
- Alternatives considered:
  - Add a per-step override only (rejected: does not satisfy per-sequence configuration requirement).
  - Store as global app config (rejected: cannot express per-sequence customization).

## Decision 7: Integration points for runtime execution

- Decision: Integrate inter-step delay in both linear and flow-graph execution paths, only when another step is going to run.
- Rationale: SequenceRunner currently has multiple execution paths (`Steps`, `FlowSteps`, loop bodies); all paths need consistent pacing semantics.
- Alternatives considered:
  - Implement for linear steps only (rejected: inconsistent behavior for flow-graph sequences).
  - Apply delay only around command/action steps and not condition/loop transitions (rejected: violates uniform "between executed steps" expectation).

## Decision 8: Test strategy for determinism and quality gates

- Decision: Add unit tests for range validation/defaulting/sampling boundaries and integration tests for execution-path behavior in linear and flow modes.
- Rationale: Constitution testing standards require deterministic coverage of touched behavior; this feature changes runtime timing semantics.
- Alternatives considered:
  - Manual verification only (rejected: insufficient for regression safety).
  - Performance-only tests without correctness assertions (rejected: misses boundary correctness and validation rules).
