# Contracts: Connect to game action

## POST /api/sessions (existing)
- Purpose: Start a session for a game on a specific device.
- Request body: `{ "gameId": string, "adbSerial": string }` (both required).
- Behavior: Synchronous; must return within 30s.
- Success response: `200` `{ "sessionId": string }`.
- Failure: Non-200 with error message; on timeout, return 504.

## POST /api/commands/{id}/force-execute
- Purpose: Execute a command immediately.
- Input: optional `sessionId` in body or query.
- Behavior change: If `sessionId` omitted, server auto-injects the latest cached sessionId matching the command’s gameId + adbSerial; if none available, respond 400 with guidance to run connect action first.

## POST /api/commands/{id}/evaluate-and-execute
- Purpose: Evaluate triggers and execute command.
- Input: optional `sessionId` in body or query.
- Behavior change: Same optional handling and auto-injection as force-execute.

## Client Session Cache Contract
- Scope: cache sessionId per (gameId, adbSerial).
- Store: localStorage key format `session:{gameId}:{adbSerial}` (example; exact keying may be adjusted in implementation while preserving scope).
- Lifecycle: overwritten on successful connect action; cleared/ignored on failure.

## Device Discovery
- Endpoint: `/api/adb/devices` (existing)
- Use: populate selectable adbSerial suggestions during action authoring; author can still input manual value.

## Validation/Error States
- Missing gameId/adbSerial when saving action → 400 validation error.
- Missing cached sessionId when command call omits sessionId → 400 with message to run connect action first.
- Timeout on /api/sessions → surface to caller; do not cache sessionId.
