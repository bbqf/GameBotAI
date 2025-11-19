# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased] - 2025-11-19

### Added
- Actions, Commands, and Triggers as first-class resources with file-backed repositories.
- Endpoints:
  - `/actions` CRUD and `/sessions/{id}/execute-action` execution.
  - `/commands` CRUD, `/commands/{id}/evaluate-and-execute`, and `/commands/{id}/force-execute`.
  - `/triggers` CRUD, `POST /triggers/{id}/test`, and `POST /triggers/evaluate`.
- Metrics: `/metrics/domain` exposes counts for actions, commands, and triggers.
- Migration script: `scripts/migrate-profiles-to-actions.ps1` to convert legacy profiles to actions/triggers.

### Changed
- Configuration precedence clarified and enforced: Environment > Saved file > Defaults. See `ENVIRONMENT.md`.
- OCR: Improved preprocessing and dynamic environment-driven OCR stub; Tesseract integration remains optional but supported.
- README updated with new domain model and migration guidance.

### Removed (Breaking)
- All `/profiles` endpoints and nested trigger routes.
- Background trigger worker; evaluation is now explicit via endpoints or command gating.
- Legacy session execute path `/sessions/{id}/execute`.

### Migration
1. Backup the `data/` directory.
2. Run the migration script:
   - Dry run: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun`
   - Convert and delete originals: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal`
3. Update clients to use `/actions`, `/triggers`, and `/commands` endpoints.

### Notes
- OpenAPI spec regenerated for the new endpoints.
- Full test suite green after refactor with additional tests (cycle detection, metrics).
