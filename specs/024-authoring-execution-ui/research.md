# Research

## Findings

1) Running sessions sync strategy
- Decision: Poll running sessions endpoint every 2s from the execution UI.
- Rationale: Avoids introducing WebSockets/new infra while keeping session list fresh enough for operators.
- Alternatives considered: WebSocket push (rejected: new infra/deps), manual refresh only (rejected: stale lists and higher misclick risk).

2) Auto-stop failure handling
- Decision: If auto-stop of the prior session fails, drop it from the running list and continue with the new session (assume prior session already terminated).
- Rationale: Matches clarified requirement; prevents blocking operators while avoiding duplicate entries.
- Alternatives considered: Block new session until stop succeeds (rejected: adds friction), background retries (rejected: hides failure state and adds complexity).

3) Cached session defaulting
- Decision: Cache the latest started session ID (with game/emulator metadata) server-side and have the UI default to it when no explicit session ID is provided.
- Rationale: Minimizes user input and aligns with “start once, reuse” flow; server remains source of truth for cache validity.
- Alternatives considered: Require manual session ID entry each time (rejected: higher error rate), client-only cache (rejected: loses state on refresh and drifts from server reality).

4) Detection persistence
- Decision: Persist detection configuration in existing command JSON schema (data/commands) and ensure UI rehydrates the detection section from stored values on edit.
- Rationale: Reuses current storage and avoids schema drift; aligns with FR-008/FR-010 for lossless round-trips.
- Alternatives considered: New detection store (rejected: adds persistence complexity), transient in-memory only (rejected: loses data and violates persistence requirement).

5) Performance budgets
- Decision: Target p95 < 300 ms for running-sessions fetch, UI paint/update within 100 ms after data arrival, and detection save/reload within 500 ms p95.
- Rationale: Keeps execution UI responsive and authoring saves reliable without over-engineering; compatible with existing stack.
- Alternatives considered: No explicit budgets (rejected: risks regressions), more aggressive SLAs (rejected: unnecessary for current scope and infra).
