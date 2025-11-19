# Release Notes — Action/Command Refactor (001)

## Highlights
- Major domain refactor: Actions, Commands, Triggers as first-class resources
- OpenAPI regenerated for all new/updated endpoints
- Config precedence stabilized: Environment > Saved file > Defaults
- Full test suite green, including new metrics and cycle detection tests

## Breaking Changes
- Removed all legacy `/profiles` endpoints and nested trigger routes
- Replaced background trigger worker with explicit evaluation endpoints
- Deprecated legacy session execute path `/sessions/{id}/execute`

## New/Updated Endpoints
- `/actions` CRUD; `/sessions/{id}/execute-action`
- `/commands` CRUD; `/commands/{id}/evaluate-and-execute`; `/commands/{id}/force-execute`
- `/triggers` CRUD; `POST /triggers/{id}/test`; `POST /triggers/evaluate`
- `/metrics/domain` exposing counts of actions, commands, triggers

## Configuration
- Precedence enforced: Environment > Saved file > Defaults
- See `ENVIRONMENT.md` for variables and masking/redaction rules

## Migration
1. Backup `data/`
2. Run migration script:
   - Dry run: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun`
   - Convert and delete originals: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal`
3. Update clients to use `/actions`, `/triggers`, `/commands`

## Testing
- Added tests: command cycle detection; domain metrics; config precedence
- All suites pass in CI: unit, integration, contract

## Notes
- This release focuses on correctness and clarity; follow-ups will address performance tuning and extended OCR backends
