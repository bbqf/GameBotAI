# Quickstart: Background Screenshot Service

**Feature**: 034-background-screenshot-service

## Prerequisites

- .NET 9 SDK
- Node.js (for web UI)
- ADB available in PATH (or via `GAMEBOT_USE_ADB=false` for stub mode)
- Windows OS (System.Drawing dependency)

## Build & Run

```powershell
# Build backend
dotnet build -c Debug

# Run tests
dotnet test -c Debug

# Start backend service
dotnet run -c Debug --project src/GameBot.Service

# Start web UI (separate terminal)
cd src/web-ui
npm run dev
```

## Configuration

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| GAMEBOT_CAPTURE_INTERVAL_MS | 500 | Global capture interval in ms (min 50) |
| GAMEBOT_USE_ADB | true | Set to `false` to disable ADB and use stub mode |

## Verify Feature Works

1. Start the service and web UI.
2. Navigate to the Execution tab.
3. Select a connect-to-game action and start a session.
4. Observe the "Running sessions" section — a capture rate metric (e.g., "2.1 FPS") should appear within a few seconds.
5. Open browser DevTools Network tab, call `GET /api/sessions/running` — verify `captureRateFps` field is present.
6. Call `GET /api/emulator/screenshot` — verify it returns a PNG instantly (<50ms response time).
7. Stop the session — capture rate should disappear; capture loop should stop.

## Key Files

| File | Role |
|------|------|
| `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs` | Core capture loop service |
| `src/GameBot.Domain/Sessions/CaptureMetrics.cs` | Capture rate metric model |
| `src/GameBot.Service/Program.cs` | DI registration |
| `src/GameBot.Service/Services/SessionService.cs` | Lifecycle integration |
| `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs` | HTTP endpoint rerouting |
| `src/web-ui/src/pages/Execution.tsx` | UI display of FPS metric |
| `tests/unit/BackgroundScreenCaptureServiceTests.cs` | Unit tests |
