# Spec: Action/Command Refactor (001)

## Summary
This PR completes the architectural migration from a legacy execution concept to standalone Actions, Commands, and Triggers.

Key outcomes:
1. All `/profiles` endpoints fully removed (no feature flag retained).
2. Actions are first-class (`/actions`) with file-backed persistence.
3. Triggers are fully decoupled and stored independently (`/triggers` CRUD + test + batch evaluate).
4. Commands compose Actions (and nested Commands) with cycle detection and optional trigger gating via `evaluate-and-execute`.
5. Force execution path bypasses trigger state (`/commands/{id}/force-execute`).
6. Background trigger evaluation eliminated; evaluation is now explicit (test endpoint, command gating, `/triggers/evaluate`).

Spec: `specs/001-action-command-refactor/spec.md`  
Checklist: `specs/001-action-command-refactor/checklists/requirements.md`

## Final Status (2025-11-19)
All implementation steps completed on branch `001-action-command-refactor` (post-regeneration of OpenAPI spec):

- Domain:
	- Removed legacy types (`AutomationProfile`, `IProfileRepository`, execution helpers) and related DTOs.
	- Added `StandaloneTriggersEndpoints` with create, get, update (PATCH), delete, test, evaluate.
	- Refactored `TriggerEvaluationCoordinator` to operate on `ITriggerRepository` (no profile traversal).
- Endpoints:
	- Removed `/sessions/{id}/execute` (legacy-based); retained `/sessions/{id}/execute-action`.
	- Added `/triggers/*` standalone endpoints; legacy nested trigger routes removed.
	- Actions & Commands CRUD stable; command execution endpoints respect trigger status (Satisfied only) for evaluate-and-execute.
- Infrastructure:
	- Feature flag for legacy endpoints removed—migration considered complete.
	- Background worker for trigger evaluation removed; metrics surface unchanged.
- Persistence:
	- File repositories for `actions/`, `triggers/`, `commands/` active; legacy data requires one-time migration (script provided).
- Testing:
	- All unit, integration, and contract tests pass (full suite 54 total, 0 failed).
	- Updated tests to use `/triggers` directly (removed intermediary creation blocks).
	- OpenAPI spec (`specs/openapi.json`) regenerated after green test run.

## Breaking Changes
| Area | Change | Migration Action |
|------|--------|------------------|
| HTTP API | Removed all `/profiles` endpoints | Switch to `/actions` and `/triggers` |
| Session Exec | `/sessions/{id}/execute` removed | Use `/sessions/{id}/execute-action` |
| Trigger Paths | Nested trigger routes removed | Use `/triggers/*` |
| Data Layout | Legacy data folder no longer read | Run migration script `scripts/migrate-profiles-to-actions.ps1` |
| Background Eval | Automatic trigger evaluation removed | Call `/triggers/evaluate` or rely on command gating |

## Migration Instructions
1. Backup existing `data/` directory.
2. Run migration script (optionally dry-run first):
	 ```powershell
	 pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun
	 pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal
	 ```
3. Update any client code:
	 - Use action creation endpoints instead of legacy equivalents.
	 - Create triggers directly under `/triggers`.
	 - Replace execute calls with `/commands/{id}/evaluate-and-execute` (with trigger gating) or `/commands/{id}/force-execute`.
4. Remove use of `GAMEBOT_ENABLE_PROFILE_ENDPOINTS` env var (no longer recognized).

## Evaluation Model (Post-Refactor)
- Explicit test: `POST /triggers/{id}/test` updates timestamps and potential cooldown state.
- Batch evaluate: `POST /triggers/evaluate` processes only enabled triggers.
- Command gating: `evaluate-and-execute` runs Actions only if associated trigger status resolves to `Satisfied` (otherwise returns accepted = 0).

## Rationale for Removing Background Evaluation
Eliminating automatic polling reduces:
- Unnecessary file IO churn.
- Ambiguous timing interactions in cooldown tests.
- Complexity around partial failures (now explicit endpoints allow clearer error surfaces).

## Security & Observability
- Logging retains trigger evaluation detail (debug-level for text-match evaluators).
- Metrics surface unchanged; triggers can still be aggregated externally after calling manual evaluation endpoints.

## Future Enhancements (Out of Scope for 001)
1. Add image reference upload & management improvements (bulk, checksum validation).
2. Pluggable OCR/image similarity backends selection endpoint.
3. Scheduled command orchestration (cron-style) leveraging existing schedule trigger type.
4. WebSocket push for trigger status changes.

## Verification Snapshot
- Commit: current head `2ff675d`.
- Tests: full suite 54 succeeded (integration, unit, contract); coordinator tests reflect trigger-centric flow.
- No remaining references to legacy endpoint names in codebase (verified post-OpenAPI regeneration).

## Rollback Strategy
To revert temporarily:
1. Reintroduce legacy endpoints by checking out pre-refactor commit `7e7f758`.
2. Restore `profiles/` data from backup.
3. Redeploy service.

## Completion Criteria (Met)
- Legacy execution concept removed from API & domain.
- Standalone trigger CRUD + evaluation endpoints live.
- Commands support trigger gating & force execution.
- All tests green after migration.

---
Please review the breaking changes and migration steps. Once approved, we can merge and tag a release noting the completion of the migration.
