# Research — Sequence Logic Blocks

## Decisions

- Nested Blocks: Allow unlimited nesting (author guidance to keep shallow where possible)
  - Rationale: Enables expressive flows without arbitrary limits; safeguards avoid runaway execution
  - Alternatives: Max depth 2 or 3 (simpler mental model), Disallow nesting (split across sequences)

- Loop Control: Support `break` and `continue`
  - Rationale: Precise control during iteration; common patterns for retries and skip conditions
  - Alternatives: Break-only (less flexible), No control (rely solely on count/conditions)

- Condition Sources: Include `triggerId` status alongside image/text
  - Rationale: Leverages existing trigger evaluation; richer branching without new primitives
  - Alternatives: Image/text only (focus), Variables/state (more complexity and authoring semantics)

## Best Practices

- Safeguards: Require `timeoutMs` or `maxIterations` for loops; enforce cadence bounds (50–5000ms)
- Telemetry: Record iterations, evaluations, branch decisions, durations, applied delays per block
- Logging: Use `LoggerMessage` for structured, analyzer-compliant logging
- Validation: Clear error codes/messages for invalid configs (missing safeguards, out-of-range cadence, unknown condition targets)

## Patterns

- Polling: Backoff cadence optionally configurable; default 100ms
- Detection: Reuse image/text detection and trigger evaluation; confidence thresholds documented
- Compatibility: Sequences without blocks execute unchanged; blocks nest within step groups deterministically
