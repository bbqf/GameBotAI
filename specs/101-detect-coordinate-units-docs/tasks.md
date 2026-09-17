---
description: "Task list for documenting detect vs detect-all coordinate units in the OpenAPI document"
---

# Tasks: Document detect vs detect-all coordinate units

**Input**: Design documents from `specs/101-detect-coordinate-units-docs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi-detect-coordinate-units.md, quickstart.md

**Tests**: Requested by the spec (FR-009, SC-004). The contract test is written first and must fail before the filter and operation descriptions exist.

**Organization**: Tasks are grouped by user story. US1 (detect) and US2 (detect-all) share one filter file and one test file, so they are sequential, not parallel.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Re-verify research.md R1 against src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs (`DetectAsync` → `Normalization.NormalizeRect` + same values into `Bbox`; `DetectAllAsync` copies `m.BBox` pixels) and src/GameBot.Domain/Vision/Normalization.cs (`Clamp01`); fetch the current `/swagger/v1/swagger.json` from a test host and record the actual component schema names for the detect match, its `bbox`, and the detect-all match, and whether `bbox` is emitted as a bare `$ref`; correct data-model.md and contracts/openapi-detect-coordinate-units.md if anything differs

## Phase 2: Foundational

- [ ] T002 Create tests/contract/Images/ImageDetectCoordinateUnitsOpenApiTests.cs (namespace `GameBot.ContractTests.Images`; factory, env vars and `ReadDocumentAsync` shaped like tests/contract/Images/ImageAlternatesOpenApiTests.cs; class XML summary citing feature 101 / issue #188) with the shared helpers only: `MatchSchema(document, path)` that follows `paths.<path>.post.responses.200.content.application/json.schema` → `matches.items` `$ref` to its component schema (resolving `$ref` on the response schema itself too), `Property(schema, name)`, `Description(element)` (reads `description`, or the description beside an `allOf` wrapper), and `ResolveRef(document, element)`

---

## Phase 3: User Story 1 - Know the unit of a detect coordinate (Priority: P1) 🎯 MVP

**Goal**: The detect match schema, its `bbox`, and the detect operation state that coordinates are fractions 0..1 of the capture frame (FR-001, FR-002, FR-004, FR-007 detect half).

**Independent Test**: `GET /swagger/v1/swagger.json` satisfies the `POST /api/images/detect` table in contracts/openapi-detect-coordinate-units.md.

### Tests for User Story 1

- [ ] T003 [US1] In ImageDetectCoordinateUnitsOpenApiTests.cs add: (a) `DetectMatchCoordinatesAreFractions` — a `[Theory]` over `x`,`width` (expect `width`) and `y`,`height` (expect `height`): the detect match property description contains `fraction`, the expected dimension word, `not pixels` and `clamped`, and the `x` description contains `left` while the `y` description contains `top`; (b) `DetectBboxCoordinatesAreFractions` — the same four assertions on the schema `bbox` resolves to; (c) `DetectBboxSaysItRepeatsTopLevel` — the `bbox` property description contains `x/y/width/height`; (d) `DetectOperationStatesUnits` — `paths./api/images/detect.post.description` contains `fraction`, `pixels` and `detect-all`, and still contains `matchedReferenceId` and `captureId`; (e) `DetectExampleUsesFractions` — the 200 example `matches[0]` has top-level `x`,`y`,`width`,`height` and `bbox.*`, all numbers in 0..1. Run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter FullyQualifiedName~ImageDetectCoordinateUnitsOpenApiTests` and confirm (a)–(e) FAIL before T004

### Implementation for User Story 1

- [ ] T004 [US1] Create src/GameBot.Service/Swagger/ImageDetectCoordinatesSchemaFilter.cs: `internal sealed class ImageDetectCoordinatesSchemaFilter : ISchemaFilter` (XML summary citing feature 101 / issue #188, that detect reports fractions and detect-all pixels under the same names, and that the service does not feed XML comments to Swagger). `internal const string` descriptions for the detect fields: `DetectXDescription`, `DetectYDescription`, `DetectWidthDescription`, `DetectHeightDescription` (each: fraction 0..1 of the capture frame width/height, from the frame's left/top edge, clamped to 0..1, not pixels; multiply by the capture width/height for pixels; detect-all reports the same box in pixels) and `BboxDescription` (repeats this match's top-level x/y/width/height, same fraction-of-frame unit). `Apply`: for `typeof(MatchResult)` describe `x`,`y`,`width`,`height`,`bbox` (for `bbox`, if the property schema is a bare `$ref`, replace it with `new OpenApiSchema { AllOf = { refSchema }, Description = … }`); for `typeof(NormalizedRect)` describe the same four fields with the same constants. Never touch `matchedReferenceId` (owned by `ImageAlternatesSchemaFilter`)
- [ ] T005 [US1] Register `options.SchemaFilter<ImageDetectCoordinatesSchemaFilter>();` after the other schema filters in src/GameBot.Service/GameBotServiceSetup.cs
- [ ] T006 [US1] In src/GameBot.Service/Swagger/SwaggerConfig.cs: add `private const string ImageDetectDescription` holding the existing detect description text unchanged plus an appended sentence stating that match `x`/`y`/`width`/`height` (and `bbox`) are fractions 0..1 of the capture frame width/height, not pixels — multiply by the capture size for pixels — while POST /api/images/detect-all reports the same box in pixels (feature 101); replace the inline string in BOTH detect branches (`ApplyTriggerExamples` and `ApplyImageExamples`) with `operation.Description ??= ImageDetectDescription;` (research.md R5); in `ImageDetectResponse()` add top-level `x`,`y`,`width`,`height` to `matches[0]` equal to its `bbox` values (research.md R6)
- [ ] T007 [US1] Build and run the T003 filter; (a)–(e) must pass. Fix wording (not assertions) until green

**Checkpoint**: A reader of the detect route can tell its unit from the document.

---

## Phase 4: User Story 2 - Know the unit of a detect-all coordinate (Priority: P1)

**Goal**: The detect-all match schema and operation state that coordinates are pixels (FR-003, FR-005, FR-007 detect-all half).

**Independent Test**: `GET /swagger/v1/swagger.json` satisfies the `POST /api/images/detect-all` table in the contract.

- [ ] T008 [US2] In ImageDetectCoordinateUnitsOpenApiTests.cs add: (f) `DetectAllMatchCoordinatesArePixels` — a `[Theory]` over `x`,`y`,`width`,`height`: the detect-all match property description contains `pixels` and `not fractions`, and the `x` description contains `left` while the `y` description contains `top`; (g) `DetectAllOperationStatesUnits` — `paths./api/images/detect-all.post.description` contains `pixels`, `fraction` and `/api/images/detect`; (h) `DetectAllExampleUsesPixels` — the 200 example `matches[0]` `x`,`y`,`width`,`height` are all JSON integers. Run the filter and confirm (f)–(g) FAIL (h may already pass)
- [ ] T009 [US2] In ImageDetectCoordinatesSchemaFilter.cs add `DetectAllXDescription`, `DetectAllYDescription`, `DetectAllWidthDescription`, `DetectAllHeightDescription` (pixels of the capture frame, from the frame's left/top edge, not fractions; POST /api/images/detect reports the same box as fractions of the frame) and a `typeof(DetectAllMatch)` branch describing the four fields
- [ ] T010 [US2] In src/GameBot.Service/Swagger/SwaggerConfig.cs add `private const string ImageDetectAllDescription` (scores every stored reference image against one capture named by captureId; match x/y/width/height are pixels of that capture, not fractions, while POST /api/images/detect reports the same box as fractions 0..1 of the frame, so never compare values from the two routes directly) and set `operation.Description ??= ImageDetectAllDescription;` in BOTH detect-all branches
- [ ] T011 [US2] Build and run the test filter; (a)–(h) must pass

**Checkpoint**: Both routes' units are published.

---

## Phase 5: User Story 3 - Recognise the identifier naming difference (Priority: P3)

**Goal**: `templateId` and `imageId` each name their counterpart (FR-006).

- [ ] T012 [US3] In ImageDetectCoordinateUnitsOpenApiTests.cs add (i) `IdentifierFieldsNameTheirCounterpart` — detect match `templateId` description contains `imageId` and `detect-all`; detect-all match `imageId` description contains `templateId` and `/api/images/detect`. Confirm it FAILS
- [ ] T013 [US3] In ImageDetectCoordinatesSchemaFilter.cs add `TemplateIdDescription` (the requested reference image id; POST /api/images/detect-all reports the same identifier as imageId) and `ImageIdDescription` (the reference image id; POST /api/images/detect reports the same identifier as templateId), applied in the `MatchResult` and `DetectAllMatch` branches; re-run the test filter — (a)–(i) green

---

## Phase 6: Regression guards (FR-008, FR-009, SC-003, SC-004)

- [ ] T014 Temporarily comment out the T005 registration in src/GameBot.Service/GameBotServiceSetup.cs, run ImageDetectCoordinateUnitsOpenApiTests and confirm the schema-description facts (a), (b), (c), (f), (i) fail (SC-004); restore it, re-run and confirm green; `git diff src/GameBot.Service/GameBotServiceSetup.cs` shows only the T005 line added
- [ ] T015 After T014's restore is green (not in parallel: shared build output and test data directory), run the unchanged-behaviour guards with no test edits: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Images|FullyQualifiedName~ImageDetections|FullyQualifiedName~OpenApi|FullyQualifiedName~SwaggerDocsTests"` plus `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~ImageDetect"` (FR-008, SC-003)

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T016 [P] Update docs/architecture.md: after the feature 089 `masked`/`retainedPixelCount` bullet in the detection section, add a bullet that `POST /api/images/detect` reports match `x`/`y`/`width`/`height` (and `bbox`) as fractions 0..1 of the capture frame while `POST /api/images/detect-all` reports the same box in pixels, under the same field names, and that this is stated in the OpenAPI document by `ImageDetectCoordinatesSchemaFilter` rather than changed, to keep existing callers working (feature 101, issue #188); set the "Last reviewed" line to `2026-09-17 (feature 101 detect coordinate units documented in OpenAPI)`
- [ ] T017 [P] Add an Unreleased → Added entry at the top of CHANGELOG.md in the style of the 100 entry: detect coordinate units documented in the OpenAPI document (101-detect-coordinate-units-docs, #188) — detect fractions vs detect-all pixels on every coordinate field and both operations, `bbox` repeats the top-level box, `templateId`/`imageId` name each other, detect example now shows top-level coordinates; documentation only, responses unchanged. Edit with the Edit tool only
- [ ] T018 [P] Add a `| 101 | Document detect vs detect-all coordinate units | Implemented |` row after the 100 row in specs/STATUS.md and set `**Status**: Implemented` in specs/101-detect-coordinate-units-docs/spec.md
- [ ] T019 Full gate: `dotnet build C:\src\GameBot\GameBot.sln` — confirm no warnings or errors in ImageDetectCoordinatesSchemaFilter.cs, SwaggerConfig.cs, GameBotServiceSetup.cs or ImageDetectCoordinateUnitsOpenApiTests.cs — then the full contract test project `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj` (rerun once if a known flaky test such as QueueTemplateLink or MaskedTemplateMatchTests fails); all green before commit

---

## Dependencies & Execution Order

- T001 → T002 → US1 (T003 → T004 → T005 → T006 → T007) → US2 (T008 → T009 → T010 → T011) → US3 (T012 → T013) → T014 → T015
- US1–US3 are sequential: they edit the same filter, config and test files
- Polish T016–T018 can run in parallel after T013 (documentation files only; no builds); T019 last

### Parallel Opportunities

```text
After T013:  T016, T017, T018
```

## Implementation Strategy

- **MVP**: Phase 3 (US1) — detect is where the surprising unit lives.
- **Then**: US2 (the other half of the hazard), US3, regression guards, living docs and full gate.
