# Changelog

All notable changes to this project will be documented in this file.

## [2025-11-26]

### Added
- OCR: Tesseract TSV integration
  - Pass `-c tessedit_create_tsv=1` to emit `output.tsv` alongside `output.txt`.
  - Parse TSV into word-level tokens (level=5) with floating-point confidences.
  - Reconstruct text from TSV tokens when `.txt` is not produced.
  - Force `en-US` numeric parsing to avoid locale-specific failures.
- Tests and fixtures
  - TSV fixtures under `tests/TestAssets/Ocr/tsv` and unit tests for header/rows/aggregation/malformed cases.
  - Updated `TesseractProcessOcr` tests to assert TSV args and behavior.
- Persistent reference image storage:
  - Disk-backed `ReferenceImageStore` under `data/images` with atomic PNG writes.
  - Endpoints: `POST /images`, `GET /images/{id}`, `DELETE /images/{id}`.
  - Integration test ensuring persistence across restart.

### Changed
- Confidence calculation now prefers TSV aggregate (scaled 0–1) and falls back to legacy text heuristic only when TSV is missing or invalid.
- Triggers: default `CooldownSeconds` is now 0 (was 60). Added a unit test to lock the default.
- Image trigger flow now supports persisted reference images across service restarts (no re-upload needed).

### Notes
- Backwards compatibility: existing triggers remain unchanged; the cooldown behavior only differs for newly created triggers relying on the default value.
- No persistence schema changes; ENV docs updated for OCR TSV usage.

## [Unreleased] - 2025-11-25

### Added
- Actions, Commands, and Triggers as first-class resources with file-backed repositories.
- Endpoints:
  - `/actions` CRUD and `/sessions/{id}/execute-action` execution.
  - `/commands` CRUD, `/commands/{id}/evaluate-and-execute`, and `/commands/{id}/force-execute`.
  - `/triggers` CRUD, `POST /triggers/{id}/test`, and `POST /triggers/evaluate`.
- Metrics: `/metrics/domain` exposes counts for actions, commands, and triggers.
- Migration script: `scripts/migrate-profiles-to-actions.ps1` to convert legacy profiles to actions/triggers.
- Structured Tesseract invocation logging (debug-level) capturing CLI, stdout/stderr with truncation guard, exit code, elapsed time, and correlation IDs.
- OCR coverage tooling: `tools/coverage/report.ps1` now emits Cobertura-derived summaries, writes `data/coverage/latest.json`, and enforces ≥70% coverage for `GameBot.Domain.Triggers.Evaluators.Tesseract*`.
- Coverage summary API surface: `GET /api/ocr/coverage` plus contract/integration tests so stakeholders can query latest coverage without parsing XML.
- 001-image-storage: Persistent reference image storage under `data/images` with atomic writes, structured LoggerMessage logging for `/images` endpoints, standardized error responses (`invalid_request`, `invalid_image`, `not_found`), and increased test coverage (evaluator edge cases and endpoint error paths). CI stability improvements: isolated storage via `Service__Storage__Root` + `GAMEBOT_DATA_DIR`, persistence test robustness, and evaluator GDI+ OOM fix using `Graphics.DrawImage`.

### Changed
- Configuration precedence clarified and enforced: Environment > Saved file > Defaults. See `ENVIRONMENT.md`.
- OCR: Improved preprocessing and dynamic environment-driven OCR stub; Tesseract integration remains optional but supported.
- README updated with new domain model and migration guidance.
- README + specs quickstart now document OCR logging toggles, coverage script usage, and `/api/ocr/coverage` flows (stale/missing behavior, bearer auth).
- `POST /commands/{id}/evaluate-and-execute` now surfaces `triggerStatus` + `message`, always persists trigger evaluation before dispatching actions, and emits structured telemetry (`TriggerExecuted`, `TriggerSkipped`, `TriggerBypassed`). Integration tests now assert both HTTP metadata and trigger repository state for satisfied, pending, cooldown, and disabled flows; quickstart instructions updated accordingly.

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
