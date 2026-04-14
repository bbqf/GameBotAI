# Quickstart: UI Configuration Editor

**Feature**: 035-ui-config-editor
**Date**: 2026-04-14

## Prerequisites

- .NET 9 SDK
- Node.js (for web-ui)
- Running GameBot service (`dotnet run -c Debug --project src/GameBot.Service`)
- Running web-ui dev server (`npm run dev` in `src/web-ui`)

## Build & Verify

```powershell
# Build all projects
dotnet build -c Debug

# Run all tests (backend)
dotnet test -c Debug

# Run frontend tests
Set-Location src/web-ui
npx jest --passWithNoTests
```

## Quick Smoke Test

1. Start the service:
   ```powershell
   dotnet run -c Debug --project src/GameBot.Service
   ```

2. Verify existing GET config endpoint:
   ```powershell
   Invoke-RestMethod -Uri http://localhost:5081/api/config -Method GET | ConvertTo-Json -Depth 5
   ```

3. Test new PUT parameters endpoint (after implementation):
   ```powershell
   $body = @{ updates = @{ "Logging__LogLevel__Default" = "Debug" } } | ConvertTo-Json
   Invoke-RestMethod -Uri http://localhost:5081/api/config/parameters -Method PUT -Body $body -ContentType "application/json" | ConvertTo-Json -Depth 5
   ```

4. Test new PUT reorder endpoint (after implementation):
   ```powershell
   $body = @{ orderedKeys = @("Logging__LogLevel__Default", "GAMEBOT_USE_ADB", "GAMEBOT_TESSERACT_ENABLED") } | ConvertTo-Json
   Invoke-RestMethod -Uri http://localhost:5081/api/config/parameters/reorder -Method PUT -Body $body -ContentType "application/json" | ConvertTo-Json -Depth 5
   ```

5. Open the web UI at `http://localhost:5173`, navigate to the Configuration tab, and verify:
   - "Backend Connection" section is collapsed
   - Parameter list shows all backend parameters
   - Filter input narrows the list
   - Editing a parameter highlights the row (dirty state)
   - "Apply All" sends changes to the backend
   - Drag-and-drop reorders parameters and persists on drop

## Key Files

| Area | File | Purpose |
|------|------|---------|
| Backend endpoint | `src/GameBot.Service/Endpoints/ConfigEndpoints.cs` | PUT /parameters and /parameters/reorder |
| Backend models | `src/GameBot.Service/Models/Config.cs` | Request DTOs |
| Backend service | `src/GameBot.Service/Services/ConfigSnapshotService.cs` | Update + reorder logic |
| Frontend page | `src/web-ui/src/pages/Configuration.tsx` | Main configuration editor |
| Frontend components | `src/web-ui/src/components/ConfigParameterList.tsx` | Parameter list + filter + DnD |
| Frontend components | `src/web-ui/src/components/ConfigParameterRow.tsx` | Single parameter row |
| Frontend components | `src/web-ui/src/components/CollapsibleSection.tsx` | Collapsible wrapper |
| Frontend API | `src/web-ui/src/services/config.ts` | API client for config endpoints |
| Backend tests | `tests/unit/ConfigUpdateTests.cs` | Unit tests for update/reorder |
| Frontend tests | `src/web-ui/src/__tests__/configuration.spec.tsx` | Component tests |
