# Research: Connect to game action

## Decisions

- Decision: Scope cached sessionId to game + adbSerial
  - Rationale: Prevents reusing sessions for the wrong title/device and aligns with user clarification.
  - Alternatives considered: Global sessionId cache; per-game only cache.

- Decision: Enforce 30s synchronous session request timeout
  - Rationale: Matches spec and avoids hanging command chains; keeps UX predictable.
  - Alternatives considered: Shorter timeout (risking premature failure); longer/async (risks blocking flows and violating requirement).

- Decision: Store sessionId client-side in localStorage keyed by game + adbSerial
  - Rationale: Survives page refreshes within the same browser; keeps reuse predictable per scoped key.
  - Alternatives considered: In-memory only (lost on refresh); cookie (less explicit scoping, potential CS concerns).

- Decision: Allow manual adbSerial entry even when /api/adb/devices fails
  - Rationale: Keeps authoring unblocked when device discovery is empty/unavailable.
  - Alternatives considered: Block authoring on discovery failure (hurts availability).

- Decision: Treat sessionId as optional on command execution APIs and auto-inject when absent and cached
  - Rationale: Simplifies sequences after the initial connect step while keeping semantic requirement satisfied.
  - Alternatives considered: Require explicit sessionId on every call (more friction, contradicts spec intent).
