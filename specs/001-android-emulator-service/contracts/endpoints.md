# API Contracts: GameBot Android Emulator Service (MVP)

Date: 2025-11-05

## Authentication
- Header: `Authorization: Bearer <token>` (required on all non-health endpoints)

## Health
- GET `/health`
  - 200 OK `{ status: "ok" }`

## Games
- POST `/games`
  - Request: `{ title, path, hash, region?, notes?, complianceAttestation }`
  - Responses: `201 Created { id, ... }`, `400`, `409`
- GET `/games`
  - 200 OK `[ { id, title, region }, ... ]`
- GET `/games/{id}`
  - 200 OK `{ id, title, path?, hash, region, notes }`, `404`

## Profiles
- POST `/profiles`
  - Request: `{ name, gameId, steps: [InputAction], checkpoints?: [string] }`
  - 201 Created `{ id, ... }`
- GET `/profiles?gameId=`
  - 200 OK `[ { id, name }, ... ]`

## Sessions
- POST `/sessions`
  - Request: `{ gameId | gamePath, profileId? }`
  - 201 Created `{ id, status: "running", gameId }`
- GET `/sessions/{id}`
  - 200 OK `{ id, status, uptime, health, gameId }`, `404`
- POST `/sessions/{id}/inputs`
  - Request: `{ actions: [InputAction] }`
  - 202 Accepted `{ accepted: n }`, `409` if not running
- GET `/sessions/{id}/snapshot`
  - 200 OK image/png (binary)
- POST `/sessions/{id}/execute?profileId=...`
  - 202 Accepted `{ accepted: n }`
  - 404 if session or profile not found
  - 409 if session not running
- DELETE `/sessions/{id}`
  - 202 Accepted `{ status: "stopping" }`, `404`

## Types
- `InputAction`:
  - `{ type: "key", args: { keyCode: number }, delayMs?: number }`
  - `{ type: "tap", args: { x: number, y: number }, delayMs?: number }`
  - `{ type: "Swipe", args: { x1: number, y1: number, x2: number, y2: number, durationMs?: number }, delayMs?: number }`

## Errors
- Unified error: `{ error: { code, message, hint? } }`
- No sensitive data in messages.
