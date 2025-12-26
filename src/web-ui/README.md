# GameBot Web UI (MVP)

Browser-based UI to author and manage GameBot sequences.

## Features
- Token-gated API client (memory by default; optional remember)
- Configurable base URL; defaults to same-origin
- Create sequence (POST /api/sequences)
- View sequence by ID (GET /api/sequences/{id})
- Responsive layout (≥375px)

## Getting Started

1. Start the GameBot Service:
   - Use the existing VS Code task: `run-service`
   - Or run manually:

```powershell
Push-Location C:\src\GameBot; dotnet run -c Debug --project src/GameBot.Service; Pop-Location
```

2. Run the Web UI (requires Node.js >= 18):

```powershell
Push-Location C:\src\GameBot\src\web-ui
npm install
npm run dev
Pop-Location
```

3. Open http://localhost:5173 and set API Base URL (e.g., http://localhost:5081) and token if required.

## Notes
- Sequences list/delete endpoints are not yet exposed by the service; this MVP focuses on create and fetch by ID.
- Validation errors (HTTP 400 with `errors[]`) are surfaced in the UI when returned by the service.
