# GameBot

A .NET 8 (C#) minimal API service that controls Android emulator sessions on Windows, exposing a REST API for games, profiles, and session automation (snapshots + input execution). UI is a separate deployment.

## Specs and contracts
- Spec, plans, and research: `specs/001-android-emulator-service/`
- API endpoints: `specs/001-android-emulator-service/contracts/endpoints.md`

## Requirements
- Windows 10/11
- .NET SDK 8.x
- LDPlayer installed is preferred (ADB autodetection). Otherwise ensure `adb` is available on PATH.

## Quick start

Set a bearer token (required for all non-health endpoints) and optionally a data directory for file-backed repositories:

```powershell
# From repository root
$env:GAMEBOT_AUTH_TOKEN = "test-token"
# Optional: choose where games/profiles JSON files are stored
$env:GAMEBOT_DATA_DIR = "C:\\data\\gamebot"
```

Run the service:

```powershell
# Build and run in Release
dotnet build -c Release
dotnet run -c Release --project src/GameBot.Service
```

Health check:

```powershell
# Should return { "status": "ok" }
Invoke-RestMethod -Uri http://localhost:5000/health -Method GET
```

Notes:
- The service binds to http://localhost:5000 by default (via launchSettings.json). If you prefer a different port, set it before running:

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5080"
dotnet run -c Release --project src/GameBot.Service
Invoke-RestMethod -Uri http://localhost:5080/health -Method GET
```

## Resources and domain

### Games
- Create: `POST /games`
  - Body: `{ "name": "Game A", "description": "optional" }`
  - Response: `{ id, name, description }`
- Get: `GET /games/{id}` → `{ id, name, description }`
- List: `GET /games` → `[{ id, name, description }]`

Example create:

```json
{
  "name": "Game A",
  "description": "My ROM"
}
```

### Profiles
A profile is a reusable, named script of input actions for a specific game.

- Create: `POST /profiles`
  - Body: `{ name, gameId, steps: InputAction[], checkpoints?: string[] }`
  - Response: `{ id, name, gameId, steps, checkpoints }`
- Get: `GET /profiles/{id}`
- List: `GET /profiles?gameId={gameId}` (filter by game)

Minimal example:

```json
{
  "name": "TutorialSkip",
  "gameId": "<game-id>",
  "steps": [
    { "type": "tap", "args": { "x": 320, "y": 640 }, "delayMs": 250 },
    { "type": "key", "args": { "key": "ESCAPE" } }
  ],
  "checkpoints": []
}
```

### Sessions
Run-time context used to execute inputs and take snapshots.

- Create: `POST /sessions`
  - Body: `{ gameId?: string, gamePath?: string, profileId?: string, adbSerial?: string }`
    - Provide `gameId` (preferred) or `gamePath`.
    - Optional `adbSerial` to bind a specific device/emulator; if omitted and ADB is enabled, the first available device is selected; if none, the API returns 404.
  - Response: `{ id, status, gameId }`
- Get: `GET /sessions/{id}` → `{ id, status, uptime, health, gameId }`
- Device info: `GET /sessions/{id}/device` → `{ id, deviceSerial, mode: "ADB"|"STUB" }`
- Health: `GET /sessions/{id}/health` → ADB connectivity details when applicable
- Snapshot: `GET /sessions/{id}/snapshot` → `image/png`
- Send inputs: `POST /sessions/{id}/inputs` → `{ accepted }`
- Execute profile: `POST /sessions/{id}/execute?profileId={profileId}` → `{ accepted }`
- Stop: `DELETE /sessions/{id}` → `{ status: "stopping" }`

### Input actions
Supported `type` values (case-insensitive). Numeric args may be provided as numbers or numeric strings (e.g., "50").

- tap
  - args: `x`, `y`
  - Example:
    ```json
    { "actions": [ { "type": "tap", "args": { "x": 50, "y": 50 } } ] }
    ```

- swipe
  - args: `x1`, `y1`, `x2`, `y2`
  - optional: `durationMs` on the action
  - Example:
    ```json
    {
      "actions": [
        {
          "type": "swipe",
          "args": { "x1": 0, "y1": 0, "x2": 200, "y2": 200 },
          "durationMs": 300
        }
      ]
    }
    ```

- key
  - args: either `keyCode` (Android key code) or `key` (symbolic name)
  - Examples:
    ```json
    { "actions": [ { "type": "key", "args": { "keyCode": 29 } } ] }
    { "actions": [ { "type": "key", "args": { "key": "ESCAPE" } } ] }
    ```
  - Common key names: ESCAPE(111), BACK(4), HOME(3), ENTER(66), SPACE(62), TAB(61), DEL/DELETE(67), UP(19), DOWN(20), LEFT(21), RIGHT(22), VOLUME_UP(24), VOLUME_DOWN(25), POWER(26), A–Z(29–54).

- Timing
  - `delayMs` per action waits after the action finishes before the next action.
  - `durationMs` is used by long-running actions like `swipe`.

## Common flow (HTTP)
- Create a game → `POST /games` with `{ name, description }`
- Create a profile → `POST /profiles` with `{ name, gameId, steps: [...] }`
- Start a session → `POST /sessions` with `{ gameId, adbSerial? }`
- Execute a profile → `POST /sessions/{sessionId}/execute?profileId={profileId}` (returns `{ accepted }`)
- Or send ad-hoc inputs → `POST /sessions/{id}/inputs` with `{ actions: [...] }`
- Grab a snapshot → `GET /sessions/{id}/snapshot` (image/png)
- Stop session → `DELETE /sessions/{id}`

See full request/response shapes in the contracts doc linked above.

## Configuration
- Authentication token:
  - Environment: `GAMEBOT_AUTH_TOKEN`
  - Config: `Service:Auth:Token`
- Storage root for file repositories:
  - Environment: `GAMEBOT_DATA_DIR`
  - Config: `Service:Storage:Root` (default: `<app>/data` under the running process)
- Session settings (Options): `Service:Sessions`
  - `MaxConcurrentSessions` (default 3)
  - `IdleTimeoutSeconds` (default 1800)

- Trigger evaluation worker (Options): `Service:Triggers:Worker`
  - `IntervalSeconds` (default 2): base cadence for evaluating triggers.
  - `GameFilter` (optional): limit evaluation to a specific `gameId`.
  - `SkipWhenNoSessions` (default true): if there are no active sessions, skip evaluation to save CPU.
  - `IdleBackoffSeconds` (default 5): delay used when idle due to no active sessions.

Notes:
- You can still force ADB-less screen evaluation in tests via `GAMEBOT_TEST_SCREEN_IMAGE_B64` (base64 PNG). When set and `GAMEBOT_USE_ADB=false`, the image-match evaluator compares against this image.

- ADB behavior
  - `GAMEBOT_USE_ADB`: set to `false` to disable ADB integration (useful in tests/CI). By default on Windows, ADB is enabled.
  - `GAMEBOT_ADB_PATH`: optional override path to `adb.exe`.
  - `GAMEBOT_ADB_RETRIES`, `GAMEBOT_ADB_RETRY_DELAY_MS`: control retry count and delay (ms) for ADB actions.
  - Diagnostics endpoints: `GET /adb/version`, `GET /adb/devices`.

- Dynamic port (tests/CI)
  - `GAMEBOT_DYNAMIC_PORT=true` binds to port 0 to avoid conflicts.

- Logging
  - Console logs include timestamps and scopes; ADB operations are logged at Debug.
  - Enable verbose logs: `$env:Logging__LogLevel__Default = 'Debug'`
  - Correlation ID: send `X-Correlation-ID` header; responses include it and it is added to log scopes.

## Development
Run tests:

```powershell
dotnet test -c Release
```

Notes:
- If `dotnet` isn't recognized in your shell, open a new terminal where the .NET SDK is on PATH, or use the absolute path (Get-Command dotnet | Select-Object -ExpandProperty Source).
- Warnings are treated as errors; analyzers are enabled across projects.
- Windows-only APIs are annotated; the emulator integration is designed for Windows hosts.

