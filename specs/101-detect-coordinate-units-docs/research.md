# Research: Document detect vs detect-all coordinate units

## R1 — What units does each route really return?

- **Decision**: detect = fractions 0..1 of the capture frame, clamped; detect-all = whole-number pixels. Origin is the
  frame's top-left and `x`/`y` is the match box's top-left corner on both routes.
- **Evidence**: `ImageDetectionsEndpoints.DetectAsync` calls `Normalization.NormalizeRect(m.BBox.X, …, screenshotMat.Cols,
  screenshotMat.Rows, …)`, which divides x/width by the frame width and y/height by the frame height and applies
  `Clamp01`; the same four values are assigned to the top-level fields and to `Bbox`. `DetectAllAsync` assigns
  `m.BBox.X/Y/Width/Height` (ints) directly. `DetectAllMatch.X` etc. are `int`; `MatchResult.X` etc. are `double`.
- **Alternatives considered**: trusting the issue's observation alone — rejected; the spec requires verifying from code.

## R2 — Unify the units or document them?

- **Decision**: Document. No wire change.
- **Rationale**: Issue #188 is labelled `documentation` and accepts documentation. Converting either route would silently
  break callers that already apply the workaround, violating constitution III ("Inputs/outputs MUST be stable").
- **Alternatives considered**: add new explicitly-named fields (e.g. `xPx`) — out of scope, not asked for.

## R3 — How to publish per-field descriptions?

- **Decision**: New `ImageDetectCoordinatesSchemaFilter : ISchemaFilter`, keyed on `context.Type` for `MatchResult`,
  `NormalizedRect`, `DetectAllMatch`, registered in `GameBotServiceSetup` next to the other schema filters.
- **Rationale**: The service does not feed XML comments to Swagger; every prior description feature uses a schema filter.
  A separate filter keeps the feature cohesive; it only sets properties `ImageAlternatesSchemaFilter` does not touch.
- **Alternatives considered**: extending `ImageAlternatesSchemaFilter` — rejected, it is scoped to feature 097;
  `[Description]` attributes — not honoured by the current SwaggerGen setup without annotations support.

## R4 — The `bbox` property description

- **Decision**: Describe the `bbox` property on `MatchResult` by setting `Description` on the property schema. In
  OpenAPI 3.0 a `$ref` property ignores siblings, so if Swashbuckle emits `bbox` as a bare `$ref`, wrap it as
  `allOf: [ { $ref } ]` with the description (Swashbuckle's own pattern for described references).
- **Rationale**: keeps the description visible to generators and schema viewers. The test asserts on the property's
  `description` wherever it is emitted.

## R5 — Operation descriptions and the duplicated detect branch

- **Decision**: Hold the detect description in one constant `ImageDetectDescription` (existing text + a units sentence)
  and a new `ImageDetectAllDescription`, both in `SwaggerConfig`, and use them in both `ApplyTriggerExamples` and
  `ApplyImageExamples` branches.
- **Rationale**: `??=` means the earlier `ApplyTriggerExamples` branch wins; editing only one copy would either be
  invisible or leave a stale twin. Removing the duplicate branch is a refactor beyond scope; sharing the constant is
  the minimal safe change.

## R6 — Example payloads (FR-007)

- **Decision**: The detect example already uses fractions under `bbox`; add the matching top-level
  `x`/`y`/`width`/`height` (same fractions) so the example matches the real response shape. The detect-all example
  already uses pixel integers — unchanged. The test asserts detect example values are ≤ 1 and detect-all example
  values are integers > 1.

## R7 — Locating schemas in the test

- **Decision**: Resolve schemas by following the `$ref` from each operation's 200 response
  (`matches.items.$ref`), rather than hard-coding component names.
- **Rationale**: robust to schema-id customisation (a known schema-id clash exists elsewhere in this document).
