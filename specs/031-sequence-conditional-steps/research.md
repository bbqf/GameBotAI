# Research

## Decision 1: Keep v1 conditional semantics linear (no branch graph)
- Decision: `stepType=conditional` only controls execute-vs-skip for its own action; flow always continues to the next ordered step.
- Rationale: Matches the requested A/B-location-check use case with the smallest model/runtime change.
- Alternatives considered:
  - True/false target branch graph (rejected: out of scope and substantially higher authoring/runtime complexity).

## Decision 2: Limit condition model to `imageVisible` in v1
- Decision: Support exactly one condition type: `imageVisible` with required `imageId` and optional `minSimilarity`.
- Rationale: Reuses existing detection stack and keeps both API and UI minimal.
- Alternatives considered:
  - Introduce command-outcome or composite logic now (rejected: not required for MVP behavior).

## Decision 3: Support generic action payloads in both step types
- Decision: `stepType=action` and `stepType=conditional.action` accept any currently supported action payload type.
- Rationale: Matches current action execution capabilities while keeping the sequence-step extension minimal.
- Alternatives considered:
  - Restrict v1 to `primitiveTap` only (rejected: unnecessarily narrows initial value and conflicts with clarified scope).

## Decision 4: Runtime decision policy is execute/skip/fail-stop
- Decision: Condition true => execute action; false => skip action and continue; evaluator error => fail step and stop sequence.
- Rationale: Separates expected negative conditions from runtime/system faults.
- Alternatives considered:
  - False as failure (rejected: breaks intended conditional flow).
  - Evaluation errors as skip (rejected: masks faults and complicates diagnosis).

## Decision 5: Clean-slate schema only
- Decision: Feature scope assumes no pre-existing sequence/action/command data; persisted sequence steps use only explicit v1 `stepType` schema.
- Rationale: Simplifies implementation and removes dual-path runtime overhead.
- Alternatives considered:
  - Dual-shape read support (rejected: unnecessary complexity for clean start).
  - Data conversion workflow (rejected: not needed for empty baseline).

## Decision 6: Keep explicit conditional step execution logging
- Decision: Per-step logs include `stepType`, condition summary, evaluation result (`true`/`false`/`error`), and action outcome (`executed`/`skipped`).
- Rationale: Required for diagnosability and acceptance testing.
- Alternatives considered:
  - No additional log fields (rejected: insufficient observability).

## Decision 7: Performance validation profile is single-run (no concurrency)
- Decision: Measure p95 conditional-evaluation latency with one active sequence execution, 30 total steps including 10 conditional steps, over 15 minutes.
- Rationale: Matches explicit product direction to avoid concurrency complexity in this feature.
- Alternatives considered:
  - Multi-run concurrent profile (rejected by clarification decision).
