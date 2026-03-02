# Research

## Decision 1: Persist execution logs as append-oriented JSON records with repository abstraction
- Decision: Use a dedicated execution log repository backed by JSON files under `data/execution-logs`, following existing file-repository patterns.
- Rationale: Aligns with current architecture, keeps operational model simple, and avoids introducing new storage dependencies.
- Alternatives considered:
  - Reusing existing command/sequence files (rejected: mixes runtime telemetry with authoring data).
  - New database dependency (rejected: out of scope and operationally heavier).

## Decision 2: Capture command and sequence runs as correlated log entries with parent-child linkage
- Decision: Write one top-level entry per command/sequence execution plus step-level outcome details, preserving parent-child references for nested runs.
- Rationale: Supports both high-level success/failure review and hierarchy-aware troubleshooting from persisted data.
- Alternatives considered:
  - Flat, uncorrelated entries only (rejected: weak sequence diagnostics).
  - Aggregate-only sequence result (rejected: loses failing-node visibility).

## Decision 3: Keep final status separate from step execution outcome
- Decision: Use final status values `success|failure` and separate per-step outcome flag `executed|not_executed`.
- Rationale: Preserves user-requested status semantics while clearly representing skipped/blocked steps.
- Alternatives considered:
  - Three-state status including `not_executed` (rejected per clarification).
  - Encode skip only in reason text (rejected: ambiguous for filtering/tests).

## Decision 4: Represent navigation context as host-agnostic relative routes
- Decision: Persist relative paths for direct object navigation and parent hierarchy context (for nested executions).
- Rationale: Works across environments without binding host/port and supports future web-ui deep links.
- Alternatives considered:
  - Absolute URLs (rejected: environment-coupled and brittle).
  - IDs only with no route hints (rejected: poorer UX and slower troubleshooting).

## Decision 5: Make retention configurable with asynchronous cleanup
- Decision: Expose configurable retention duration and perform expiry cleanup outside the execution request critical path.
- Rationale: Meets configurability requirement and avoids latency regressions in command execution.
- Alternatives considered:
  - Fixed retention period (rejected by clarification).
  - Cleanup only at read-time (rejected: stale data growth and unpredictable query cost).

## Decision 6: Apply sensitive-value masking/redaction before persistence
- Decision: Execution details are normalized and masked/redacted before writing to durable storage.
- Rationale: Protects sensitive values while keeping enough context for user troubleshooting.
- Alternatives considered:
  - Store raw details (rejected: privacy risk).
  - Remove all parameter details (rejected: insufficient diagnostics).

## Decision 7: Introduce retrieval endpoints with filtering and pagination
- Decision: Add list/detail endpoints for execution logs, with filters on status, object type/id, and time range; include pagination.
- Rationale: Enables immediate backend consumption and future web-ui integration while controlling query cost.
- Alternatives considered:
  - No retrieval API yet (rejected: limits validation and near-term usability).
  - Bulk-only export endpoint (rejected: poor interactive diagnostics).