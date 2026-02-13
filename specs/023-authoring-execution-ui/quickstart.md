# Quickstart

1) Restore and build backend
```powershell
cd C:/src/GameBot
dotnet build -c Debug
```

2) Run backend service (new terminal)
```powershell
dotnet run -c Debug --project src/GameBot.Service
```

3) Install and run web UI (new terminal)
```powershell
cd C:/src/GameBot/src/web-ui
npm install
npm run dev
```

4) Validate flows
- Open the execution UI and verify the session banner renders when a session is cached and stops correctly.
- Start multiple sessions across game/emulator pairs; confirm running list shows each and auto-replaces the prior pair entry on new start.
- Stop sessions from the list and verify removal.
- Create/edit a command, set detection target/parameters, save, reopen, and confirm values persist.

Session endpoints reference:
- `GET /api/sessions/running` → list running sessions
- `POST /api/sessions/start` → `{ "gameId": "...", "emulatorId": "..." }`
- `POST /api/sessions/stop` → `{ "sessionId": "..." }`

5) Run automated tests
```powershell
cd C:/src/GameBot
dotnet test -c Debug
cd src/web-ui
npm test
npm run test:e2e  # Playwright if configured
```
