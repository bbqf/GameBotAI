# Quickstart: Connect to game action

## Prereqs
- Branch: 020-connect-game-action
- Backend: .NET 9 SDK; frontend: Node 18+.
- Ensure adb devices are discoverable for suggestion testing.

## Steps
1) Backend
- Add action type "connect-to-game" to domain and JSON repository models (gameId required, adbSerial required).
- Extend POST /api/sessions handler to enforce 30s timeout if not already.
- Make sessionId optional on force-execute and evaluate-and-execute; auto-inject latest cached sessionId matching gameId + adbSerial; return 400 if none.
- Expose session caching keyed by gameId + adbSerial; avoid reuse across mismatched pairs.
- Update logging to include gameId/adbSerial on session creation and command calls (no secrets).

2) Frontend
- Authoring UI: add action type option; require selecting game from list; adbSerial suggestions from /api/adb/devices with manual override; persist values on save/edit.
- Execution UI: after connect action success, show sessionId to user and cache in localStorage keyed by gameId + adbSerial; clear/ignore on failure.
- Command execution flows: if sessionId not provided, pull from cache using gameId + adbSerial; display guidance when missing.
- Handle empty device list gracefully while keeping adbSerial editable.

3) Tests
- Backend: unit/integration tests for session creation timeout, optional sessionId handling, and cache scoping.
- Frontend: RTL tests for authoring form (required fields, suggestions fallback); execution flow test for caching and auto-injection; Playwright e2e happy/timeout paths.

4) Run
- dotnet build -c Debug
- dotnet test -c Debug
- npm install (if needed) then npm test in src/web-ui; run Playwright suite if available.

## Performance Notes
- /api/sessions must respond within 30s; target typical success <5s.
- Device suggestions should load within 2s; do not block manual input.
