# Release 2025-11-28: Persistent Reference Images, Logging, and Tests

## Highlights
- Persistent reference image storage under `data/images` with atomic writes
- Structured LoggerMessage logging for `/images` endpoints
- Standardized error responses: `invalid_request`, `invalid_image`, `not_found`
- Increased coverage: evaluator edge cases and endpoint error paths
- CI stability fixes: storage isolation via `Service__Storage__Root` + `GAMEBOT_DATA_DIR`, persistence test robustness
- Evaluator stability: replace GDI+ `Bitmap.Clone` with `Graphics.DrawImage`

## Details
- Domain
  - `ImageMatchEvaluator`: 24bpp conversion via draw, avoid intermittent GDI+ OOM
  - `ReferenceImageStore`: PNG persistence with atomic replace; `Exists/Delete` support
- Service
  - `Program`: registers disk-backed `IReferenceImageStore` at `Path.Combine(storageRoot, "images")`
  - `ImageReferencesEndpoints`: POST/GET/DELETE with structured logs and standardized errors; POST returns `overwrite` flag
- Tests
  - Integration: persistence across restart; error paths for `/images`
  - Unit: evaluator edge cases (missing ref, null screen, template > region, constant equal/different)
  - Test infra: `TestEnvironment` sets both `GAMEBOT_DATA_DIR` and `Service__Storage__Root`

## How to Validate
- Upload a 1x1 PNG to `/images`; GET should return 200 before and after restart
- POST same id twice; second response includes `overwrite: true`
- Error paths:
  - POST with empty fields → 400 `invalid_request`
  - POST invalid base64 → 400 `invalid_image`
  - GET/DELETE missing → 404 `not_found`

## Links
- PR: #16
- CHANGELOG updated under Unreleased (now included in this release)
