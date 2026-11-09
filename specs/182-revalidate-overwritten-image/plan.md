# Implementation Plan: Revalidate an image after an overwrite

**Branch**: `182-revalidate-overwritten-image` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/182-revalidate-overwritten-image/spec.md`

## Summary

`GET /api/images/{id}` sends `Last-Modified` but no `Cache-Control` and no `ETag`. A browser reuses a stored copy with no request (heuristic freshness). After `PUT /api/images/{id}`, the web UI shows the old picture. The handler is `GetImageAsync` in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`. It returns `Results.Stream(..., lastModified: meta.UpdatedAtUtc, enableRangeProcessing: true)`.

The fix is in the handler only:

1. Send `Cache-Control: no-cache` on a found image.
2. Send a strong `ETag` that depends on the stored bytes.
3. Let ASP.NET Core answer `If-None-Match` and `If-Modified-Since` with 304 (it does this when `entityTag` and `lastModified` are set on the file result).
4. State the headers in the OpenAPI document and in `docs/architecture.md`.

No change to `PUT`, to the repository, to the detector, to other endpoints, or to the web UI.

## Technical Context

**Language/Version**: C# on .NET (existing solution)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (Swagger)
**Storage**: Existing file-based image store (`IImageRepository`). No change.
**Testing**: xUnit contract tests (`tests/contract/Images`) and integration tests (`tests/integration/Images`) with `WebApplicationFactory<Program>`
**Target Platform**: Windows service plus browser UI
**Project Type**: web-service with web UI
**Performance Goals**: An image is small (tens of KB). The handler reads the bytes once to build the ETag. A 304 answer has no body. No extra disk pass beyond that single read. The common case (unchanged image) moves no body bytes over the network.
**Constraints**: Success headers only. The 404 path and the `PUT` response stay as they are. No change to `Accept-Ranges` behavior.
**Scale/Scope**: 1 source file (handler), 1 Swagger file (header docs), 1 docs file, 2 test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Code quality (I): a small private helper for the ETag in the same file. XML docs on new members. PASS.
- Testing (II): bug fix, so a failing test comes first. Contract tests for the headers and the 304 paths. An integration test for the overwrite flow (old ETag gives 200 with new bytes). PASS.
- UX consistency (III): no UI change. The web UI gets the right picture with no new control. Phone support level: no change (no UI change). PASS.
- Performance (IV): goals declared above. PASS.
- Living documentation (V): update `docs/architecture.md` (REST API surface and "Last reviewed"), add a `specs/STATUS.md` row, set the spec `Status`. PASS.
- STE (VI): all new text is in STE. PASS.
- Version bump: bump `patch` in `installer/versioning/version.override.json` before the PR. Add a CHANGELOG line.
- No violations. The Complexity Tracking table is not needed.

Post-design re-check: no new violation. No new project, dependency, or store file.

## Project Structure

### Documentation (this feature)

```text
specs/182-revalidate-overwritten-image/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── get-image-headers.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/ImageReferencesEndpoints.cs   # GetImageAsync: strong ETag from the bytes, Cache-Control: no-cache
└── Swagger/SwaggerConfig.cs                # GET /api/images/{id}: document Cache-Control, ETag, and the 304 response

tests/
├── contract/Images/GetImageTests.cs        # headers, If-None-Match 304, If-Modified-Since 304
└── integration/Images/OverwriteImageTests.cs  # overwrite then conditional GET with the old ETag

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Keep the existing layers. The change stays in the one endpoint handler.

## Key design decisions

1. **Cache-Control.** Set `Cache-Control: no-cache` on the response of a found image, before the result runs (through `HttpContext.Response.Headers`). Write it for the 200 and for the 304 answer.
2. **ETag.** A strong ETag: the quoted, lowercase hex of the SHA-256 of the bytes, cut to the first 16 bytes (32 hex chars). It changes when the bytes change, also with the same size and time.
3. **How the bytes are read.** The handler reads the stream into a byte array once, hashes it, and returns `Results.File(bytes, contentType, lastModified, entityTag, enableRangeProcessing: true)`. ASP.NET Core then evaluates `If-None-Match`, `If-Modified-Since`, and range headers with the standard precondition rules (RFC 9110). The handler does not parse these headers.
4. **Order of checks.** `If-None-Match` wins over `If-Modified-Since` (standard rule, done by the framework).
5. **404 path.** No change. No new headers on a not-found answer.
6. **Docs.** Swagger: add the `Cache-Control` and `ETag` response headers to the 200 response and add a 304 response for `GET /api/images/{id}`. `docs/architecture.md`: add a short line in the REST API surface and refresh "Last reviewed".
7. **Out of scope.** No versioned path, no change to `PUT`, to the repository, to the detector, to other endpoints, or to the web UI.

## Complexity Tracking

No violations to justify.
