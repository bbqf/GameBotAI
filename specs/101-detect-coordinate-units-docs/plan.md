# Implementation Plan: Document detect vs detect-all coordinate units

**Branch**: `101-detect-coordinate-units-docs` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/101-detect-coordinate-units-docs/spec.md` (GitHub issue #188)

## Summary

`POST /api/images/detect` returns match coordinates as fractions 0..1 of the capture frame
(`ImageDetectionsEndpoints.DetectAsync` → `Normalization.NormalizeRect`, clamped), repeated under `bbox`;
`POST /api/images/detect-all` returns whole-number pixels (`DetectAllAsync` copies `m.BBox` as-is). Both use
`x`/`y`/`width`/`height`, and nothing in the OpenAPI document says which is which. Add a Swashbuckle
`ISchemaFilter` (`ImageDetectCoordinatesSchemaFilter`) that describes the coordinate fields of `MatchResult`,
`NormalizedRect` and `DetectAllMatch`, the `bbox` property, and `templateId` / `imageId`. Extend the detect
operation description with a units sentence and give detect-all its first description, holding both texts in
shared constants so the two duplicated detect branches in `SwaggerConfig.cs` (`ApplyTriggerExamples`, which wins
via `??=`, and `ApplyImageExamples`) cannot drift. Add top-level `x`/`y`/`width`/`height` to the detect response
example so it shows what the route really returns. A contract test pins all statements. No route, response body,
field or status code changes.

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Service)
**Primary Dependencies**: Swashbuckle.AspNetCore (SwaggerGen `ISchemaFilter`, `IOperationFilter`), Microsoft.OpenApi
**Storage**: N/A
**Testing**: xUnit + FluentAssertions contract tests (`tests/contract`, `WebApplicationFactory<Program>` reading `/swagger/v1/swagger.json`); existing detection contract tests (`tests/contract/Images`) as the no-behaviour-change guard
**Target Platform**: Windows service (ASP.NET Core minimal APIs)
**Project Type**: web-service
**Performance Goals**: None beyond existing; the filter runs only during OpenAPI document generation (a handful of string assignments on three schemas)
**Constraints**: The service does not feed XML comments to Swagger — schema descriptions must come from a schema filter (precedent: `ImageAlternatesSchemaFilter`, `QueueRosterSchemaFilter`). `ImageAlternatesSchemaFilter` already sets `MatchResult.matchedReferenceId`; the new filter must not touch that property. Operation descriptions use `??=`, so the first branch to run (`ApplyTriggerExamples`) decides the published text.
**Scale/Scope**: 1 new filter file, 1 registration line, shared description constants + two operation branches ×2 + one example edit in `SwaggerConfig.cs`, 1 new contract test file, living-doc updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality | Small cohesive filter mirroring `ImageAlternatesSchemaFilter`; XML summary on the class; description text in constants shared by both duplicate detect branches (removes a drift risk rather than adding one); no new dependencies. PASS |
| II. Testing | Contract test asserts the published contract. Documentation gap ⇒ "failing test first" honoured by writing the OpenAPI test before the filter/description edits and seeing it fail. Existing image detection contract tests guard unchanged responses. PASS |
| III. UX Consistency | Directly delivers "Provide help/usage for … API schemas"; resolves an inconsistency by stating it rather than breaking the stable response shape ("Inputs/outputs MUST be stable"). PASS |
| IV. Performance | No hot path; perf note: document-generation-only string assignments. PASS |
| V. Living Documentation | `docs/architecture.md` (detect units bullet + "Last reviewed"), `CHANGELOG.md` Unreleased entry, `specs/STATUS.md` row 101; spec Status set to Implemented at the end. PASS |

Post-design re-check (after research/contracts): design adds no project, no route, no runtime behaviour, no persistence; only descriptions and one example payload change. PASS.

## Project Structure

### Documentation (this feature)

```text
specs/101-detect-coordinate-units-docs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi-detect-coordinate-units.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Swagger/ImageDetectCoordinatesSchemaFilter.cs  # NEW: ISchemaFilter for MatchResult, NormalizedRect, DetectAllMatch
├── Swagger/SwaggerConfig.cs                       # detect description (+units) in both branches, new detect-all description, detect example
└── GameBotServiceSetup.cs                         # register options.SchemaFilter<ImageDetectCoordinatesSchemaFilter>()

tests/contract/Images/
└── ImageDetectCoordinateUnitsOpenApiTests.cs      # NEW: reads swagger.json, asserts FR-001..FR-007

docs/architecture.md                               # coordinate-units bullet; Last reviewed date
CHANGELOG.md                                       # Unreleased entry
specs/STATUS.md                                    # 101 row
```

**Structure Decision**: Single existing service project; the filter sits beside `ImageAlternatesSchemaFilter` in `src/GameBot.Service/Swagger/`, and the test beside `ImageAlternatesOpenApiTests` in `tests/contract/Images/`.

## Complexity Tracking

No constitution violations.
