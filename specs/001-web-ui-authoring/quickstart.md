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

## Run the Web UI
```powershell
Push-Location C:\src\GameBot\src\web-ui
$env:Path = "C:\Program Files\nodejs;" + $env:Path
npm install
npm run dev
Pop-Location
```

## Use the UI
- Open http://localhost:5173
- Set API Base URL (e.g., http://localhost:5000)
- Paste token if service requires it (memory only unless "remember token" is checked)
- Create a sequence and view by ID
