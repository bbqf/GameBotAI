# GameBot

A .NET 8 (C#) minimal API service that controls Android emulator sessions on Windows, exposing a REST API for games, actions, commands, triggers, and session automation (snapshots + input execution). UI is a separate deployment.

## Specs and contracts
- Spec, plans, and research: `specs/001-android-emulator-service/`
- API endpoints: `specs/001-android-emulator-service/contracts/endpoints.md`
- Save Configuration spec: `specs/001-save-config/`

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

## OCR Logging & Coverage

Tesseract invocations now emit structured logs and have a coverage gate so operators can diagnose OCR issues quickly.

1. **Enable detailed OCR logging** when investigating:
  ```powershell
  $env:GAMEBOT_LOG_LEVEL__GameBot__Domain__Triggers__Evaluators__TesseractProcessOcr = "Debug"
  $env:GAMEBOT_TESSERACT_PATH = "C:\\Program Files\\Tesseract-OCR\\tesseract.exe"
  ```
  Each OCR run generates a single `TesseractInvocationLogger` entry with sanitized CLI args, stdout/stderr (8 KB cap + truncation flag), correlation ID, duration, and exit code. Leave this log level at Info in production unless actively debugging.
2. **Generate the OCR coverage report** using the bundled script (writes `data/coverage/latest.json` and a timestamped history file):
  ```powershell
  pwsh tools/coverage/report.ps1 `
    -Project tests/integration/GameBot.IntegrationTests.csproj `
    -NamespaceFilter "[GameBot.Domain]GameBot.Domain.Triggers.Evaluators.Tesseract*" `
    -TargetPercent 70 `
    -DataDirectory (Join-Path $PWD 'data')
  ```
  The command runs `dotnet test` with coverlet, prints pass/fail status, and exits non-zero if coverage drops below the target.
3. **Serve the summary via API** by pointing the service at the same data directory:
  ```powershell
  $env:GAMEBOT_DATA_DIR = Join-Path $PWD 'data'
  dotnet run -c Release --project src/GameBot.Service
  curl https://localhost:5001/api/ocr/coverage -H "Authorization: Bearer dev-token" | jq
  ```
  On success the endpoint returns JSON containing the generated timestamp, coverage %, target, uncovered scenarios, and optional report URL. If the summary is missing or >24h old, the endpoint returns HTTP 503 instructing you to rerun the script.

See `specs/001-tesseract-logging/quickstart.md` for the full workflow, including setting a custom `ReportUrl` and troubleshooting stale summaries.

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

### Domain Concepts (Post-Refactor)

Profiles have been removed. Three first-class concepts now drive automation:

#### Actions
Atomic, file-backed executable units (e.g., a tap sequence or composite of low-level input actions).
- Create: `POST /actions` → body includes a name and action definition (input steps).
- Execute against a session: `POST /sessions/{id}/execute-action?actionId={actionId}` → `{ accepted }`.

Example action definition (simplified):
```json
{
  "name": "OpenMenu",
  "steps": [
    { "type": "tap", "args": { "x": 50, "y": 50 }, "delayMs": 100 },
    { "type": "key", "args": { "key": "ESCAPE" } }
  ]
}
```

#### Triggers
Standalone evaluators that determine readiness (Satisfied vs NotSatisfied) based on screen/image/text/schedule/delay.
- CRUD: `POST /triggers`, `GET /triggers/{id}`, `PATCH /triggers/{id}`, `DELETE /triggers/{id}`.
- Test single trigger (updates internal timestamps/cooldowns): `POST /triggers/{id}/test`.
- Batch evaluate all enabled triggers: `POST /triggers/evaluate`.

Example text-match trigger:
```json
{
  "name": "ReadyBanner",
  "type": "text-match",
  "enabled": true,
  "params": {
    "text": "READY",
    "confidenceThreshold": 0.75
  }
}
```

#### Reference Images (Persistent)
Reference images used by `image-match` triggers can be persisted under the service storage root (`GAMEBOT_DATA_DIR` or `Service:Storage:Root`). Files are saved as PNG in `data/images`.

Endpoints:
- `POST /images` → `{ id }` (upload or overwrite). Body: `{ "id": "Home", "data": "<base64-png-or-data-url>" }`.
- `GET /images/{id}` → `200 OK` if persisted; `404` if missing.
- `DELETE /images/{id}` → removes the file.

Workflow:
1. Upload once via `POST /images`.
2. Create an `image-match` trigger referencing `referenceImageId`.
3. After service restart the image remains available (no re-upload required).

Example image-match trigger referencing a persisted image:
```json
{
  "type": "image-match",
  "enabled": true,
  "params": {
    "referenceImageId": "Home",
    "region": { "x": 0, "y": 0, "width": 1, "height": 1 },
    "similarityThreshold": 0.90
  }
}
```

Validation rules:
- `id` must match `^[A-Za-z0-9_-]{1,128}$`.
- Image must decode successfully (PNG/JPEG); invalid data returns `400` with `invalid_image`.

Overwrite: re-upload with same `id` atomically replaces the file.
Delete: `DELETE /images/{id}`; subsequent evaluations treat the reference as missing until replaced.

#### Commands
Composable orchestration objects referencing Actions (and optionally nested Commands) with cycle detection and optional trigger gating.
- CRUD: `POST /commands`, `GET /commands/{id}`, `PATCH /commands/{id}`, `DELETE /commands/{id}`.
- Evaluate triggers then execute eligible actions: `POST /commands/{id}/evaluate-and-execute` → returns counts of accepted steps.
- Force execution (skip trigger gating): `POST /commands/{id}/force-execute`.

Example command referencing an action and gating on a trigger:
```json
{
  "name": "StartLoop",
  "steps": [
    {
      "type": "action",
      "refId": "<action-id>",
      "gateTriggerIds": ["<trigger-id>"]
    }
  ]
}
```

Common flow now:
1. Create an Action (`/actions`).
2. Create a Trigger (`/triggers`).
3. Create a Command that references the Action and lists the Trigger as a gate.
4. Run `POST /commands/{id}/evaluate-and-execute` until the trigger becomes `Satisfied` and the Action executes.
5. Inspect trigger status via `POST /triggers/{id}/test` or batch `POST /triggers/evaluate`.

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
- Create an action → `POST /actions` with action steps
- (Optional) Create a trigger → `POST /triggers`
- (Optional) Create a command → `POST /commands` referencing the action and trigger
- Start a session → `POST /sessions` with `{ gameId, adbSerial? }`
- Execute an action directly → `POST /sessions/{sessionId}/execute-action?actionId={actionId}` (returns `{ accepted }`)
- Or run a command with gating → `POST /commands/{id}/evaluate-and-execute`
- Send ad-hoc inputs → `POST /sessions/{id}/inputs` with `{ actions: [...] }`
- Grab a snapshot → `GET /sessions/{id}/snapshot` (image/png)
- Evaluate triggers → `POST /triggers/evaluate`
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
- Text OCR (text-match triggers)
  - Enable Tesseract backend: `$env:GAMEBOT_TESSERACT_ENABLED = 'true'`
  - Optional path: `$env:GAMEBOT_TESSERACT_PATH = 'C:\\Program Files\\Tesseract-OCR\\tesseract.exe'`
  - Optional language: `$env:GAMEBOT_TESSERACT_LANG = 'eng'` (can be overridden per-trigger via `params.language`)
  - Fallback (when disabled): Env-based OCR reads `$env:GAMEBOT_TEST_OCR_TEXT` and `$env:GAMEBOT_TEST_OCR_CONF` for deterministic tests.

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

### Configuration Snapshot (Saved Config)
The service maintains an "effective configuration" snapshot for auditability and diagnostics.

- File: `data/config/config.json` under the storage root (defaults to `<app>/data`; override with `GAMEBOT_DATA_DIR`).
- Precedence: Environment > Saved file > Other files > Defaults.
- Secrets: Keys containing `TOKEN`, `SECRET`, `PASSWORD`, or `KEY` (case-insensitive) are fully redacted as `***` in the snapshot and API output.
- Startup: On start, if a saved file exists it is loaded, env overrides are applied, defaults fill gaps, and the result is persisted. Malformed/missing file is ignored with a log; service continues.
- Writes: Atomic (temp + move) to avoid partial/corrupt JSON.

Endpoints (auth required when `GAMEBOT_AUTH_TOKEN` is set):
- GET `/config/` → returns the current effective configuration JSON (generated lazily if missing)
- POST `/config/refresh` → regenerates and persists the snapshot

Examples (PowerShell):

```powershell
# Optional: set token used by the service
$env:GAMEBOT_AUTH_TOKEN = "test-token"

# Read current snapshot
Invoke-RestMethod -Uri http://localhost:5000/config/ -Headers @{ Authorization = "Bearer $env:GAMEBOT_AUTH_TOKEN" }

# Regenerate snapshot
Invoke-RestMethod -Uri http://localhost:5000/config/refresh -Method POST -Headers @{ Authorization = "Bearer $env:GAMEBOT_AUTH_TOKEN" }
```

## Development
Run tests:

```powershell
dotnet test -c Release
```

Notes:
- If `dotnet` isn't recognized in your shell, open a new terminal where the .NET SDK is on PATH, or use the absolute path (Get-Command dotnet | Select-Object -ExpandProperty Source).
- Warnings are treated as errors; analyzers are enabled across projects.
- Windows-only APIs are annotated; the emulator integration is designed for Windows hosts.

## Migration: Legacy Profiles → Actions & Triggers

Use the PowerShell script `scripts/migrate-profiles-to-actions.ps1` to convert each legacy profile into:
- An Action JSON (`data/actions/<profileId>.json`)
- Individual Trigger JSON files (`data/triggers/<triggerId>.json`)

Parameters:
- `DataDir`: Root data directory (defaults to `$env:GAMEBOT_DATA_DIR` or `./data`).
- `-DryRun`: Print intended operations without writing files.
- `-DeleteOriginal`: Remove original profile JSONs after successful migration.

Examples:
```powershell
pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun
pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal
```

Sample dry-run output (abridged):
```text
[DRY-RUN] Would create action: C:\src\GameBot\data\actions\d6bfccf5....json
[DRY-RUN] Would create trigger: C:\src\GameBot\data\triggers\d21c45c1....json
Migration summary:
  Actions created: 1
  Triggers created: 1
  Errors: 0
  (Dry run: no files written)
```

Post-merge deprecation: after migrating and validating, remove the `data/profiles` folder from deployments and eliminate any automation referencing `/profiles` endpoints (now deleted). The script can be retained for archival conversions but should be marked deprecated in the next release.

