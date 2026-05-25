# Quickstart: Primitive Actions Data Model Refactor

## 1. Run migration preview and report
1. Prepare a copy of persisted data under data/.
2. Execute migration tooling in dry-run mode to emit mapping and blocking diagnostics.
3. Confirm there are zero unresolved legacy Action references.

## 2. Run cutover validation gate
1. Start service in validation mode.
2. Verify startup/readiness succeeds only when no blocking legacy references remain.
3. Verify deterministic diagnostics are emitted when intentionally reintroducing a legacy reference.

## 3. Verify backend API behavior
1. Confirm Action CRUD routes are not available for authored flows.
2. Confirm primitive-action-oriented contracts are available (catalog + inline payload models in command/sequence APIs).
3. Create/update command and sequence payloads using inline discriminated primitive selections.

## 4. Verify frontend authoring and execution behavior
1. In web UI, ensure users select primitive actions directly where Action selection existed before.
2. Confirm command and sequence authoring persists inline primitive selections.
3. In execution tab, select connect-to-game primitive and verify required parameters are displayed and validated.

## 5. Regression checks
1. Run build and tests:
   - dotnet build -c Debug
   - dotnet test -c Debug
2. Run web UI tests relevant to actions/commands/sequences/execution.
3. Validate no >2% p95 execution regression in unchanged command/sequence scenarios.

## 6. Acceptance checklist
1. No authored Action entity remains in backend/frontend/test flows.
2. Legacy references block startup/readiness until migrated.
3. Commands/sequences persist primitive selections inline by value with typed discriminator payload.
4. Connect-to-game execution flow remains functionally equivalent with required parameter entry.
