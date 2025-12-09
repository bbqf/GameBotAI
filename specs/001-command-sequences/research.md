# Command Sequences — Phase 0 Research

## Clarifications
- Delay precedence: A. Range overrides fixed value when both are present.

## Open Questions
- Error handling on mid-sequence failures: stop vs. continue with policy?
- Per-step timeouts vs. sequence-level timeout interaction.
- Retries: per-step configuration and max attempts.

## Constraints
- .NET 8 Service, .NET 9 Domain alignment.
- No new persistence stores; reuse `data/commands` JSON.
- Use existing detection pipeline; no new external packages.

## Risks
- Non-determinism in image detection affecting sequence flow.
- Long-running sequences impacting service throughput; need cancellation.

## Decisions
- Sequence execution model: synchronous API call with cancellable background token.
- Validation: enforce either `delayMs` or `delayRangeMs` with precedence rule above.
- Result schema to include per-step outcomes and overall status.
