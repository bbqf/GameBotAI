# Implementation Plan: Alternate Reference Images for One Detection Target

**Branch**: `097-multi-reference-image-target` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/097-multi-reference-image-target/spec.md` (issue #192)

## Summary

A stored reference image gains an optional, ordered **alternates** list, persisted as a small JSON
sidecar per primary under `<imagesRoot>\.alternates\{id}.json` by a new `FileImageAlternatesRepository`
(`GameBot.Domain.Images`). A new `ReferenceImageSetLoader` loads a primary plus its existing direct
alternates (reporting missing ones). Matching is fanned out by a new decorator,
`ReferenceSetTemplateMatcher : ITemplateMatcher` (`GameBot.Domain.Vision`): its `MatchAllAsync` runs the
inner matcher once for the primary and once per alternate, tags each `TemplateMatch` with a new
init-only `ReferenceId`, merges candidates (score desc → reference order → bbox), re-applies the
existing `Nms` with the caller's overlap/max, and logs which reference matched. **With zero alternates the
decorator is never constructed** — callers pass the original matcher, so single-image scoring stays
bit-identical (FR-007/SC-002).

The decorator slots into every consumer without widening their coordinate-resolution code:
`POST /api/images/detect` (+ additive `matchedReferenceId` per match), `ImageDetectionHelper.TryDetect`
(wait-for-image steps and the readiness gate), `CommandExecutor.TryDetectAndTap` (image-anchored taps),
and `ImageMatchEvaluator` (image-match triggers and sequence image conditions via
`ImageDetectionConditionAdapter`), which takes the max similarity across references. New endpoints
`GET`/`PUT /api/images/{id}/alternates`; image metadata lists alternates; deleting a primary deletes its
sidecar; backup includes alternate images plus `image-alternates/{id}.json` entries, restore re-applies
them. Swagger does not read XML comments, so the new endpoints carry `.WithDescription(...)` operation
descriptions and a new `ImageAlternatesSchemaFilter` describes the new schema fields (including
`matchedReferenceId`). `detect-all` is untouched (FR-014).

## Technical Context

**Language/Version**: C# / .NET 9
**Primary Dependencies**: ASP.NET Core minimal APIs, OpenCvSharp4 (`Cv2.MatchTemplate` via existing `TemplateMatcher`), System.Text.Json, Swashbuckle (operation descriptions set in code; XML comments are not included)
**Storage**: File system — image PNGs in the images root (unchanged); new sidecars `<imagesRoot>\.alternates\{id}.json` written via temp-file + replace
**Testing**: xUnit + FluentAssertions: unit (`tests/unit`), integration (`tests/integration`, `WebApplicationFactory` with `GAMEBOT_TEST_SCREEN_IMAGE_B64`), contract (`tests/contract`)
**Target Platform**: Windows service
**Project Type**: web-service
**Performance Goals**: Zero added cost for images without alternates (one sidecar existence check, no extra match). With N alternates, detection cost ≈ (N+1) single matches plus an O(k log k) merge over ≤ (N+1)·maxResults candidates (SC-003). Max 8 alternates bounds worst case at 9 matches.
**Constraints**: No threshold/default/score change for any existing image (FR-004/FR-007); response shapes change only additively; `detect-all` unchanged; no transitive expansion (FR-009)
**Scale/Scope**: ~12 source files (5 new), ~8 test files (5 new), docs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate | Status | Notes |
|------|--------|-------|
| I. Code quality | PASS | Fan-out lives in one decorator, loading in one loader, storage in one repository — consumers change by a few lines each; CamelCase; XML docs on public members; handlers stay named methods (taint-analyzer cost) |
| II. Testing | PASS | Failing tests first: decorator unit tests (merge order, tie → primary, NMS across references, zero-alternates passthrough), repository unit tests (round-trip, atomic replace, delete), loader unit tests (missing alternates), evaluator max-similarity test; integration tests for alternates CRUD/validation, detect with a night-only alternate, wait-for-image/condition via a sequence, delete + restart persistence, backup/restore; contract test for OpenAPI operation descriptions |
| III. UX consistency | PASS | Error envelope `{ error: { code, message, hint } }` as used by image endpoints; codes `invalid_request`, `not_found`, `invalid_alternates`; additive `matchedReferenceId` |
| IV. Performance | PASS | Hot path untouched when no alternates (decorator not constructed); perf note in PR with a micro-benchmark comparing 1 vs 1+3 references |
| V. Living docs | PASS | `docs/architecture.md` images section + persistence layout + Last reviewed; `specs/STATUS.md` row; CHANGELOG entry |

Post-design re-check: PASS. No new projects; one new persisted file kind documented in [data-model.md](data-model.md); contract in [contracts/image-alternates.md](contracts/image-alternates.md).

## Project Structure

### Documentation (this feature)

```text
specs/097-multi-reference-image-target/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/image-alternates.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/Images/ImageAlternatesRepository.cs          # new: IImageAlternatesRepository + FileImageAlternatesRepository
src/GameBot.Domain/Images/ImageAlternatesValidator.cs           # new: max 8, no self, no dup, ids valid+existing
src/GameBot.Domain/Images/ReferenceImageSetLoader.cs            # new: primary + existing alternates + missing ids
src/GameBot.Domain/Vision/ReferenceSetTemplateMatcher.cs        # new: ITemplateMatcher decorator (fan-out, merge, NMS, log)
src/GameBot.Domain/Vision/ITemplateMatcher.cs                   # TemplateMatch.ReferenceId (init-only, additive)
src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs   # max similarity across references
src/GameBot.Service/Services/ImageDetectionHelper.cs            # accept alternates, wrap matcher
src/GameBot.Service/Services/CommandExecutor.cs                 # tap + wait-for-image load sets, pass alternates
src/GameBot.Service/Services/EnsureGameRunning/GameReadinessProbe.cs # load set, pass alternates
src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs       # detect: wrap matcher, matchedReferenceId
src/GameBot.Service/Endpoints/Dto/*MatchResult*                 # MatchedReferenceId
src/GameBot.Service/Endpoints/ImageAlternatesEndpoints.cs       # new: GET/PUT /api/images/{id}/alternates
src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs       # metadata lists alternates; delete removes sidecar
src/GameBot.Service/Services/BackupService.cs                   # include alternate images + sidecars; restore them
src/GameBot.Service/GameBotServiceSetup.cs                      # register repository; map endpoints

tests/unit/Vision/ReferenceSetTemplateMatcherTests.cs           # new
tests/unit/Images/FileImageAlternatesRepositoryTests.cs         # new
tests/unit/Images/ReferenceImageSetLoaderTests.cs               # new
tests/unit/Images/ImageAlternatesValidatorTests.cs              # new
tests/unit/ImageMatchEvaluatorAlternatesTests.cs                # new
tests/integration/ImageAlternatesIntegrationTests.cs            # new: CRUD, detect, wait-for-image, condition, delete, restart
tests/integration/Backup*                                       # extend: alternates round-trip
tests/contract/Images/ImageAlternatesOpenApiTests.cs            # new

docs/architecture.md, specs/STATUS.md, CHANGELOG.md
```

**Structure Decision**: Existing single-service layout. Alternates are a property of the image, so they
live in `GameBot.Domain.Images` beside the image repository, and matching fan-out is an
`ITemplateMatcher` decorator so no coordinate resolver or DTO pipeline needs a list-of-templates overload.

## Complexity Tracking

No violations.
