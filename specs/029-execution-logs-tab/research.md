# Research: Execution Logs Tab

## Decision 1: List query shape and semantics

- Decision: Use a single list query that supports combined sorting and per-column free-text filtering, with default sort `timestamp desc`, default page size `50`, and case-insensitive contains filtering.
- Rationale: This directly matches clarified requirements and minimizes UI/backend mismatch by making filter+sort behavior explicit in one request/response contract.
- Alternatives considered:
  - Separate filter endpoint + sort endpoint (rejected: increases race conditions and complexity).
  - Client-only filtering/sorting after bulk load (rejected: weakens backend performance guarantees and scales poorly).

## Decision 2: Concurrency handling for rapid list changes

- Decision: Adopt latest-request-wins semantics so stale responses are ignored and only the newest query result is rendered.
- Rationale: Prevents stale table state during rapid typing/clicking and aligns with clarified UX behavior.
- Alternatives considered:
  - Queue all responses in order (rejected: stale UI flicker, poor UX).
  - Block new requests until current request finishes (rejected: input lag and poor responsiveness).

## Decision 3: Timestamp presentation model

- Decision: Display exact local timestamp by default, with user-controlled switch to relative display.
- Rationale: Exact timestamps preserve audit precision; relative mode supports quick scanning.
- Alternatives considered:
  - Relative-only (rejected: insufficient precision for troubleshooting).
  - Exact-only (rejected: less scannable for recency checks).

## Decision 4: Non-technical details rendering

- Decision: Map structured execution detail data into labeled, user-facing sections (summary, related objects, optional snapshot, step outcomes) and never render raw JSON blocks.
- Rationale: Meets explicit non-technical usability requirement while preserving necessary information.
- Alternatives considered:
  - Show expandable JSON raw payload (rejected: violates requirement and raises cognitive load).

## Decision 5: Responsive layout strategy

- Decision: Implement two layout variants: desktop split-pane (list + details visible) and phone drill-down (list first, detail view second) with state preservation.
- Rationale: Delivers device-appropriate UX while preserving core workflows.
- Alternatives considered:
  - Single unified layout for all sizes (rejected: degrades either desktop efficiency or phone usability).

## Decision 6: Performance validation strategy

- Decision: Enforce p95 local targets (`<100ms` first open, `<300ms` filter/sort update at 1,000 logs) and relaxed CI p95 thresholds (`<200ms`, `<450ms`).
- Rationale: Maintains strict local confidence while accounting for CI environment variability.
- Alternatives considered:
  - Same strict thresholds in CI (rejected: brittle due to shared-runner variability).
  - No CI performance gate (rejected: allows regressions to slip through).
