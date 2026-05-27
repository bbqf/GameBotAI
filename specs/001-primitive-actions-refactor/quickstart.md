# Quickstart: Primitive Actions Data Model Refactor

## 1. Start service in primitive-only mode
1. Ensure `GAMEBOT_AUTH_TOKEN` is set.
2. Start service from repository root:
   - `dotnet run -c Debug --project src/GameBot.Service`
3. Confirm swagger is reachable at `/swagger/v1/swagger.json`.

## 2. Verify backend API behavior
1. Confirm removed routes are absent from OpenAPI (`/api/actions`, `/api/action-types`).
2. Confirm command and sequence contracts expose inline `primitiveAction` payloads.
3. Confirm session start requires connect primitive payload (`primitiveAction.type=connect-to-game`).

## 3. Verify authoring flows
1. Create/update command with inline `PrimitiveTap` step.
2. Create/update sequence with inline primitive step payloads.
3. Reload saved entities and verify primitive payloads persist by value.

## 4. Verify frontend authoring and execution behavior
1. In web UI, create command and sequence using primitive selectors.
2. In execution tab, start a session and verify selected game/device are submitted.
3. Confirm command execution can reuse a running session without manual session id.

## 5. Regression checks
1. Run build and tests:
   - dotnet build -c Debug
   - dotnet test -c Debug
2. Run web UI tests relevant to actions/commands/sequences/execution.
3. Validate OpenAPI snapshot refresh in `specs/openapi.json`.

## 6. Acceptance checklist
1. No authored Action CRUD surface remains in backend/frontend flows.
2. Commands/sequences persist primitive selections inline by value with typed discriminator payload.
3. Connect-to-game execution flow remains functionally equivalent with required parameter entry.
4. Full backend/frontend suites pass after refactor.

## 7. Final implementation checkpoint (2026-05-27)
1. Added primitive contract/integration/UI coverage for inline primitive authoring and connect-session start/reuse.
2. OpenAPI snapshot refreshed from live swagger into `specs/openapi.json`.
3. Full backend and frontend regressions re-run after final cleanup:
   - `dotnet test -c Debug`: PASS (517 passed, 0 failed)
   - `npx jest --coverage --json --outputFile jest-results.json --runInBand --silent`: PASS (52 suites, 188 tests)
