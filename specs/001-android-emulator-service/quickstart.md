# Quickstart: GameBot Android Emulator Service

Date: 2025-11-05
Branch: 001-android-emulator-service

## Prerequisites
- Windows 10/11 with virtualization enabled
- LDPlayer installed (preferred). If not using LDPlayer, install Android SDK (Emulator + platform-tools) and ensure `adb` is on PATH.
- Sufficient hardware for concurrent emulator sessions

## Setup (service only)
- Configure a token for API access (env var or config file)
- Ensure game artifact directories are accessible to the service
- Optional: Set explicit adb path via env `GAMEBOT_ADB_PATH` or config `Service:Emulator:AdbPath`. Otherwise, the service auto-detects LDPlayer and uses its bundled adb.
- Optional: Choose data directory for games/profiles via env `GAMEBOT_DATA_DIR` or config `Service:Storage:Root`. Defaults to `<app>/data`.

## Typical Flow
1. Register a game artifact via REST (title + path + checksum)
2. (Optional) Create an automation profile (ordered input steps)
3. Start a session referencing the game (and profile if desired)
4. Send control inputs via REST, or execute a stored profile against a session
5. Fetch periodic snapshots to render in a separate UI
6. Stop the session; verify resources are released

## Notes
- UI is a separate deployment consuming the documented REST API.
- Streaming visuals are not included in MVP; periodic snapshots are supported.

## Examples (HTTP)

All requests (except `/health`) require a bearer token header:

Authorization: Bearer <token>

- Create game
	- POST `/games`
	- Body: `{ "title": "Game A", "path": "C:/roms/game-a.rom", "hash": "abc123" }`
- Create profile
	- POST `/profiles`
	- Body: `{ "name": "P1", "gameId": "<id>", "steps": [ { "type": "tap", "args": { "x":10, "y":10 } } ] }`
- Start session
	- POST `/sessions`
	- Body: `{ "gameId": "<id>" }`
- Execute profile against session (MVP: batched execution)
	- POST `/sessions/{sessionId}/execute?profileId=<id>`
	- Response: `202 Accepted { "accepted": 2 }`
