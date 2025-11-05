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

Common flow (HTTP):
- Create a game: POST `/games` with `{ title, path, hash }`
- Create a profile: POST `/profiles` with `{ name, gameId, steps: [ { type: "tap", args: { x, y } } ] }`
- Start a session: POST `/sessions` with `{ gameId }`
- Execute a profile: POST `/sessions/{sessionId}/execute?profileId={profileId}` (returns `{ accepted }`)
- Grab a snapshot: GET `/sessions/{id}/snapshot` (image/png)
- Stop session: DELETE `/sessions/{id}`

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

## Development
Run tests:

```powershell
dotnet test -c Release
```

Notes:
- If `dotnet` isn't recognized in your shell, open a new terminal where the .NET SDK is on PATH, or use the absolute path (Get-Command dotnet | Select-Object -ExpandProperty Source).
- Warnings are treated as errors; analyzers are enabled across projects.
- Windows-only APIs are annotated; the emulator integration is designed for Windows hosts.
