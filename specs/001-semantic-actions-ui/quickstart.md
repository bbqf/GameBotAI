# Quickstart: Semantic Actions UI

**Branch**: 001-semantic-actions-ui  
**Spec**: [specs/001-semantic-actions-ui/spec.md](specs/001-semantic-actions-ui/spec.md)

## Prerequisites
- Node.js 18+
- .NET SDK 9 (for the existing GameBot.Service backend)
- npm (bundled with Node)

## Run the backend (action definitions + persistence)
```powershell
# From repo root
 dotnet run -c Debug --project src/GameBot.Service
```
- Ensure the service exposes the action-type catalog endpoint (per contracts) for the UI to render forms.

## Run the web UI
```powershell
# From repo root
 cd src/web-ui
 npm install
 npm run dev
```
- The UI should fetch action types from the backend on load; confirm ports match the service configuration.

## Validate the flow
1. Load the UI, create an action by selecting a type and filling required attributes. Saving should succeed only when validation passes.
2. Edit an existing action: change type, observe the confirmation before incompatible attributes are dropped.
3. Duplicate an action and save the duplicate after validation.
4. Confirm validation feedback appears immediately for bad inputs (ranges, formats, enums, required fields).

## Testing
```powershell
# Frontend tests (Vitest / React Testing Library if configured in package.json)
 cd src/web-ui
 npm test

# Backend tests
 dotnet test -c Debug
```
- Target coverage: ≥80% line, ≥70% branch for touched areas per constitution.

## Artifacts
- Plan: [specs/001-semantic-actions-ui/plan.md](specs/001-semantic-actions-ui/plan.md)
- Research: [specs/001-semantic-actions-ui/research.md](specs/001-semantic-actions-ui/research.md)
- Data model: [specs/001-semantic-actions-ui/data-model.md](specs/001-semantic-actions-ui/data-model.md)
- Contracts: [specs/001-semantic-actions-ui/contracts/actions.openapi.yaml](specs/001-semantic-actions-ui/contracts/actions.openapi.yaml)
