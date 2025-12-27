# GameBot Web UI

Browser-based UI to author and manage GameBot actions using the backend action-type catalog.

## Features
- Token-gated API client (memory by default; optional remember)
- Configurable base URL; defaults to same-origin
- Create, edit, and duplicate actions with form-based validation (no JSON editing)
- Browse actions with filters for game and type; duplicate directly from the list
- Dynamic fields sourced from backend action-type definitions (tap, swipe, key) with client-side validation
- Required game selection for every action; type-change confirmation when incompatible fields would be dropped

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

3. Configure API base URL:
   - Option A: Copy `.env.local.example` to `.env.local` and set `VITE_API_BASE_URL` (e.g., http://localhost:5081).
   - Option B: Use the in-app Base URL field (persists in local storage).

4. Open http://localhost:5173, set token if required, and navigate to Actions.

5. Author actions:
   - Select a **Game** (required) and an **Action Type** (tap, swipe, key). Fields render from the backend definitions.
   - Save new actions or edit existing ones; changing type prompts before incompatible fields are cleared.
   - Browse the Actions list and filter by game or type; duplicate an action from the list and adjust as needed.

## Notes
- Validation errors (HTTP 400 with `errors[]`) are surfaced in the UI when returned by the service.
- Perf probes: the browser console logs `[perf] action-types.fetch` and `[perf] action-form.render` to track definition fetch and form render timings.
