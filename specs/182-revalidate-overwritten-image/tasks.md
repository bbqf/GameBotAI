# Tasks: Revalidate an image after an overwrite

**Input**: Design documents from `/specs/182-revalidate-overwritten-image/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/get-image-headers.md, quickstart.md

Tests come first (constitution, Principle II: a bug fix has a failing test first).

## Phase 1: Setup

- [ ] T001 Read `GetImageAsync` in `C:\src\GameBot\src\GameBot.Service\Endpoints\ImageReferencesEndpoints.cs` and the tests in `C:\src\GameBot\tests\contract\Images\GetImageTests.cs` and `C:\src\GameBot\tests\integration\Images\OverwriteImageTests.cs`. Note the factory setup and the helper methods to reuse.

## Phase 2: Foundational

No blocking work. The change has no shared base.

## Phase 3: User Story 1 - See the new picture after an overwrite (Priority: P1)

**Goal**: `GET /api/images/{id}` sends `Cache-Control: no-cache` and a strong `ETag`. A matching `If-None-Match` gives 304. After an overwrite, the old ETag gives 200 with the new bytes.

**Independent Test**: Run the new contract and integration tests. They fail before the fix and pass after it.

### Tests for User Story 1 (write first, they MUST fail)

- [ ] T002 [P] [US1] In `C:\src\GameBot\tests\contract\Images\GetImageTests.cs`, add a test that a `GET /api/images/{id}` response has `Cache-Control: no-cache` (header `CacheControl.NoCache` is true) and a non-empty strong `ETag` (no `W/` prefix).
- [ ] T003 [P] [US1] In `C:\src\GameBot\tests\contract\Images\GetImageTests.cs`, add a test that a `GET` with `If-None-Match` set to the ETag of the first `GET` returns 304 with no body, and that the 304 answer has `Cache-Control: no-cache` and the same `ETag`. Add a case where `If-None-Match` has a different value and the answer is 200 with the full body. Add a case with `If-None-Match: *` (304) and a case with a list that holds the right ETag and a wrong one (304).
- [ ] T004 [P] [US1] In `C:\src\GameBot\tests\integration\Images\OverwriteImageTests.cs`, add a test: upload an image, `GET` it and keep the ETag, `PUT` new bytes with the same size (use two valid PNG byte arrays of equal length that differ in one pixel), then `GET` with the old ETag in `If-None-Match`. Expect 200, the new bytes, and an ETag that differs from the old one.
- [ ] T005 [US1] Run the new tests with `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Images"` and record that T002 to T004 fail before the fix.

### Implementation for User Story 1

- [ ] T006 [US1] In `C:\src\GameBot\src\GameBot.Service\Endpoints\ImageReferencesEndpoints.cs`, change `GetImageAsync`: add `HttpContext` as a parameter, read the stream into a byte array, compute the ETag (strong, quoted, first 16 bytes of SHA-256 as 32 lowercase hex chars) in a small private helper with an XML doc, set `Cache-Control: no-cache` on `Response.Headers`, and return `Results.File(bytes, meta.ContentType, lastModified: meta.UpdatedAtUtc, entityTag: <EntityTagHeaderValue>, enableRangeProcessing: true)`. Keep the 404 paths, the log calls, and the metrics call as they are. Dispose the stream.
- [ ] T007 [US1] Run the tests of T002 to T004 and check that they pass.

**Checkpoint**: User Story 1 works alone.

## Phase 4: User Story 2 - Keep cheap revalidation (Priority: P2)

**Goal**: `If-Modified-Since` still gives 304 for an unchanged image.

**Independent Test**: A contract test sends `If-Modified-Since` with the `Last-Modified` value and expects 304.

- [ ] T008 [US2] In `C:\src\GameBot\tests\contract\Images\GetImageTests.cs`, add a test that a `GET` with `If-Modified-Since` set to the `Last-Modified` value of the first `GET` returns 304 (no regression). Run it and check that it passes.
- [ ] T008a [US2] In `C:\src\GameBot\tests\contract\Images\GetImageTests.cs`, add a test that a `GET` with `Range: bytes=0-3` returns 206 with 4 bytes, keeps `Accept-Ranges: bytes`, and carries `ETag` and `Cache-Control: no-cache`. Run it and check that it passes.
- [ ] T008b [US2] In `C:\src\GameBot\tests\contract\Images\GetImageNotFoundTests.cs`, add an assertion that the 404 answer has no `ETag` header and no `Cache-Control` header. Run it and check that it passes.

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T009 [P] In `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs`, in the `GET /api/images/{id}` branch (the `Get` and `ApiRoutes.Images + "/"` branch), document the `Cache-Control` and `ETag` response headers on the 200 response and add a 304 response with a short STE description. Do not change the other operations.
- [ ] T010 [P] Add a contract test in `C:\src\GameBot\tests\contract\Images\` (the new file `C:\src\GameBot\tests\contract\Images\GetImageOpenApiTests.cs`) that reads the OpenAPI document and checks that `GET /api/images/{id}` lists the `Cache-Control` and `ETag` headers on 200 and has a 304 response. Run it and check that it passes.
- [ ] T011 [P] In `C:\src\GameBot\docs\architecture.md`, add a short STE line in the REST API surface that states the `Cache-Control: no-cache` and `ETag` headers of `GET /api/images/{id}` and the 304 behavior. Refresh the "Last reviewed" line.
- [ ] T012 [P] Add a row for feature 182 in `C:\src\GameBot\specs\STATUS.md` and set `Status` in `C:\src\GameBot\specs\182-revalidate-overwritten-image\spec.md` to match the other finished specs.
- [ ] T013 Add a line for issue #377 in `C:\src\GameBot\CHANGELOG.md` (STE). Bump the version in `C:\src\GameBot\installer\versioning\version.override.json` by the rule in CLAUDE.md (compare with `origin/master`, set `patch`, `updatedBy`, `updatedAtUtc`) and add a version line to the CHANGELOG entry. Do this before the implementation commit.
- [ ] T014 Run the full Images tests and the `OpenApiBackwardCompatTests` with `dotnet test C:\src\GameBot\GameBot.sln`. Check that no test fails. The HTTP tests T002 to T004 and T008 prove SC-001 (the browser revalidates on each use and gets the new bytes). The web UI check in step 7 of `C:\src\GameBot\specs\182-revalidate-overwritten-image\quickstart.md` is a manual step, only if a local service is available. Put a short perf note in the PR body: one read of the image bytes for each GET, and no body on a 304 answer.

## Dependencies & Execution Order

- T001 first. T002 to T004 are parallel (T002 and T003 share a file, so write them in one edit pass). T005 after T002 to T004. T006 after T005. T007 after T006.
- T008, T008a, and T008b come after T006 (US2 has no extra code, it checks no regression).
- T009 to T012 are parallel and need T006. T010 needs T009. T013 and T014 come last.

## Implementation Strategy

MVP is User Story 1: T001 to T007. Then T008 and the polish tasks.
