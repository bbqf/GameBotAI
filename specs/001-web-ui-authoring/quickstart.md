# Quickstart — Web UI Authoring (MVP)

## Prerequisites
- .NET 9 SDK installed
- Node.js (LTS) with npm installed

## Run the GameBot Service
```powershell
Push-Location C:\src\GameBot
dotnet run -c Debug --project src/GameBot.Service
Pop-Location
```

Or use VS Code: launch "GameBot Service (F5)" from the Run and Debug panel.

## Run the Web UI
```powershell
Push-Location C:\src\GameBot\src\web-ui
$env:Path = "C:\Program Files\nodejs;" + $env:Path
npm install
npm run dev
Pop-Location
```

Or use VS Code: launch "Web UI (Chrome)". To run both, use the compound "Service + Web UI".

## Use the UI
- Open http://localhost:5173
- Set API Base URL (e.g., http://localhost:5000)
- Paste token if service requires it (memory only unless "remember token" is checked)
- Create a sequence and view by ID

## Endpoints (MVP)
- Sequences: `POST /api/sequences`, `GET /api/sequences/{id}`, `PUT /api/sequences/{id}`, `POST /api/sequences/{id}/execute`
- Triggers: `GET /triggers`, `GET /triggers/{id}`, `POST /triggers`, `DELETE /triggers/{id}`
- Images: `GET /images`, `GET /images/{id}`, `POST /images`, `DELETE /images/{id}`

## VS Code Tasks
- Build: `build`
- Test: `test` or `verify`
- Service: `run-service` (background)
- Web UI: `run-web-ui` (dev server)
