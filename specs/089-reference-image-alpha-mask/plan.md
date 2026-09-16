# Implementation Plan: Reference Image Transparency Masks

**Branch**: `089-reference-image-alpha-mask` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/089-reference-image-alpha-mask/spec.md`
**Issue**: [#190](https://github.com/bbqf/GameBotAI/issues/190)

## Summary

Reference images gain an optional per-pixel transparency mask, so a non-rectangular target
is matched on its own pixels instead of on the scenery that happened to sit behind it in
the source crop.

The change is narrow and load-bearing in one place. Templates are decoded with
`ImreadModes.Unchanged` instead of `ImreadModes.Color`, so a 4-channel BGRA template
reaches `TemplateMatcher` with its alpha intact. `TemplateMatcher` then derives a binary
mask (alpha >= 128 retained) and computes a **masked ZNCC** — an exact restriction of the
existing `TM_CCOEFF_NORMED` measure to the retained pixels — using three `TM_CCORR`
correlations and two precomputed template scalars. Templates with no usable mask keep
running through today's untouched `Cv2.MatchTemplate(..., CCoeffNormed)` call, which is
what makes the zero-score-drift guarantee (SC-003) a structural property rather than a
numerical hope.

Because the mask is derived inside the matcher from the image itself, every consumer gets
it for free: `/api/images/detect`, `/api/images/detect-all`, the wait-for-image and
tap-on-image sequence steps, the game-readiness probe, and image triggers. No interface
signature changes, no sequence-schema change, no step field.

## Technical Context

**Language/Version**: C# / .NET 9
**Primary Dependencies**: OpenCvSharp4 (`Cv2.MatchTemplate`, `Cv2.Split`, `Cv2.Threshold`),
System.Drawing.Common (reference-image storage), ASP.NET Core minimal APIs
**Storage**: PNG files under the images storage root, shared by `FileImageRepository`
(verbatim bytes) and `ReferenceImageStore` (Bitmap round-trip). Unchanged by this feature.
**Testing**: xUnit across `tests/unit`, `tests/integration`, `tests/contract`; existing
`tests/unit/Performance/TemplateMatcherBench.cs` for the perf backstop
**Target Platform**: Windows service (`[SupportedOSPlatform("windows")]` on the image store)
**Project Type**: Web service + domain library (`GameBot.Domain`, `GameBot.Service`)
**Performance Goals**: masked detection completes inside the existing
`Service:Detections:TimeoutMs` (default **500 ms**); masked path no worse than ~3x unmasked
on a 42x52 template over a 1080x1920 frame
**Constraints**: zero score drift for every existing (opaque) reference image; no breaking
change to any API response or to `ITemplateMatcher`
**Scale/Scope**: five template-decode sites, one matcher, one upload validation point, one
trigger evaluator

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented maintainer
waiver exists.

| Principle | Gate | Assessment |
|---|---|---|
| I. Code Quality Discipline | Lint/format/static analysis clean; functions under ~50 LOC; public APIs documented | PASS by design. The masked computation is extracted into its own small type (`MaskedTemplateMatch`) rather than inflating `MatchAllAsync`, which also keeps it away from the build-time taint analyzers that struggle with large methods. New public members carry XML docs. |
| II. Testing Standards | Unit + integration coverage, ≥80% line / ≥70% branch on touched areas; a failing test before the fix | PASS. The reported defect is reproduced first as a fixture test: a circular badge over two different backgrounds, asserted to fail unmasked and pass masked. Score-identity tests pin FR-004/FR-005. |
| III. UX Consistency | Actionable errors; stable, versioned I/O | PASS. The degenerate-mask rejection returns the existing `invalid_image` error shape with a specific message. Response changes are additive only. |
| IV. Performance Requirements | Declared budget; perf note for hot-path changes; benchmark | PASS — and this **is** a hot path. Budget declared above; a benchmark case is added to the existing `TemplateMatcherBench`. Unmasked detection is untouched, so it cannot regress. |
| V. Living Documentation | `docs/architecture.md` updated with refreshed "Last reviewed"; `Status` lines and `specs/STATUS.md` consistent | PASS. Architecture doc gains the masking behaviour in its detection/API sections; this spec's `Status` and `specs/STATUS.md` are updated in the same change. |

**Naming**: no underscores in method names (CamelCase) — observed throughout.

**Result**: no violations; Complexity Tracking table omitted.

### Post-Design Re-evaluation

Re-checked after Phase 1. Unchanged — the design adds one domain type, one shared decode
helper, two additive DTO fields, and one validation rule. No new project, no new dependency,
no new persistence format, no interface break.

## Project Structure

### Documentation (this feature)

```text
specs/089-reference-image-alpha-mask/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── detect-response.md
│   └── upload-validation.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Vision/
│   ├── TemplateMatcher.cs          # branch to the masked path; unmasked path untouched
│   ├── MaskedTemplateMatch.cs      # NEW - masked ZNCC score map
│   ├── TemplateMask.cs             # NEW - alpha -> binary mask, retained-pixel count
│   ├── TemplateImageDecoder.cs     # NEW - alpha-preserving decode shared by all callers
│   └── ITemplateMatcher.cs         # additive members on TemplateMatchResult
└── Triggers/Evaluators/
    └── ImageMatchEvaluator.cs      # honour the mask on the trigger path

src/GameBot.Service/
├── Endpoints/
│   ├── ImageDetectionsEndpoints.cs     # decode Unchanged; additive response fields
│   ├── ImageDetectionsValidation.cs    # degenerate-mask rejection
│   ├── ImageReferencesEndpoints.cs     # reject degenerate masks at upload
│   └── Dto/ImageDetectionsDtos.cs      # masked / retainedPixelCount
├── Services/
│   ├── ImageDetectionHelper.cs         # decode Unchanged
│   └── CommandExecutor.cs              # decode Unchanged
└── Swagger/SwaggerConfig.cs            # document the additive fields

tests/
├── unit/
│   ├── TemplateMatcherTests.cs             # masked scoring, degenerate masks, score identity
│   ├── TemplateMaskTests.cs                # NEW - threshold rule, retained count
│   └── Performance/TemplateMatcherBench.cs # masked-path budget
├── integration/
│   ├── MaskedDetectionIntegrationTests.cs  # NEW - end-to-end across detection paths
│   └── ImageStorePersistenceTests.cs       # alpha survives the store round trip
└── contract/Images/
    ├── DetectImageTests.cs                 # additive response fields
    └── UploadImageMaskValidationTests.cs   # NEW - degenerate-mask 400

docs/architecture.md                        # living-doc update (constitution V)
specs/STATUS.md                             # status roll-up
```

**Structure Decision**: The existing two-project split is kept. All matching logic stays in
`GameBot.Domain/Vision` (no OpenCV knowledge leaks into the service layer); the service
layer changes are limited to decode mode, two additive DTO fields, and one validation rule.

## Implementation Approach

### 1. Alpha-preserving decode (FR-001, FR-006)

A single `TemplateImageDecoder.Decode(byte[] bytes)` in `GameBot.Domain/Vision` replaces the
five scattered `Mat.FromImageData(bytes, ImreadModes.Color)` template decodes:

- Decode with `ImreadModes.Unchanged`.
- If the result is 8-bit (`CV_8U`) with 1, 3, or 4 channels, return it as-is.
- Otherwise dispose it and fall back to `ImreadModes.Color` — exotic depths keep today's
  exact behaviour (R-007).

Screenshot decodes keep `ImreadModes.Color`; only templates change.

### 2. Mask derivation (FR-003, FR-008, FR-009)

`TemplateMask.TryCreate(Mat template, out mask, out retainedCount)`:

- Returns false unless the template is 8-bit with 4 channels.
- Splits the alpha channel, thresholds at 128 (`alpha >= 128` retained).
- Returns false when every pixel is retained — an all-opaque alpha is not a mask, and this
  is precisely what makes FR-005 hold: such a template takes the untouched unmasked path.
- Reports `retainedCount` for observability and for the degenerate checks.

### 3. Masked scoring (FR-002, FR-003a, FR-010)

`MaskedTemplateMatch.ComputeScoreMap(graySrc, grayTpl, mask, retainedCount)` implements the
R-003 formulation: precompute `Σ mT` and `Σ mT²`; run three `TM_CCORR` correlations for
`Σ mTI`, `Σ mI`, `Σ mI²`; combine per offset in `double`. When `varT <= 0` (uniform retained
region) return no match (FR-009); when `varI <= 0` at an offset, that offset scores 0.

Match rectangles stay the **full template rectangle**, not the bounding box of the retained
pixels (FR-010), so every coordinate already resolved from a match keeps its meaning.

### 4. Matcher branch (FR-004, FR-005)

`TemplateMatcher.MatchAllAsync` gains one branch at the top: if `TemplateMask.TryCreate`
succeeds, take the masked score map; otherwise run the existing `Cv2.MatchTemplate(...,
CCoeffNormed)` line exactly as it is today. Candidate collection, sorting, NMS, and
`limitsHit` are shared by both paths and unchanged.

### 5. Reach (FR-007)

Each of the five decode sites moves to `TemplateImageDecoder`. `ImageMatchEvaluator` is the
one path needing more than a decode change: its `MeanStdDev` constant-template check and its
`Absdiff` same-size fallback both need restricting to the retained pixels, or a masked image
would silently be compared whole on that path.

### 6. Upload validation (FR-008)

`ImageDetectionsValidation` gains a mask check used by `POST /api/images` and
`PUT /api/images/{id}`: if the uploaded image has an alpha channel and fewer than 16
retained pixels, reject with 400 `invalid_image` and a message naming the retained count.
Images without alpha are not inspected, so the existing upload path is unaffected.

### 7. Observability (FR-011)

`TemplateMatchResult` gains init-only `Masked` and `RetainedPixelCount`. `/api/images/detect`
surfaces them as additive `masked` and `retainedPixelCount` JSON fields and logs them
structurally. No existing field changes. The field counts pixels **kept**, and carries the
same name in the domain and on the wire.

## Risks

| Risk | Mitigation |
|---|---|
| A previously-opaque-looking image with a stray transparent pixel silently changes behaviour | The masked path engages only when alpha is genuinely non-uniform; the retained count is reported so any such change is visible rather than silent. Contract tests pin the opaque case. |
| Masked path slower than the 500 ms detection timeout, recreating silent no-matches | Declared budget plus a benchmark case; DFT-based correlations keep cost independent of template area. |
| float32 accumulation in `MatchTemplate` degrades scores on low-contrast regions | Quantified in R-003 (~1e-6 relative); final combination in `double`. Low-contrast regions are near-degenerate for the unmasked path today as well. |
| `ImreadModes.Unchanged` returning an unexpected depth for some stored image | Explicit 8-bit guard with fallback to the current `Color` decode. |

## Phase Outputs

- **Phase 0**: [research.md](./research.md) — decode-loss analysis, why OpenCV's own mask
  parameter does not apply, the masked ZNCC formulation, precision and perf budget.
- **Phase 1**: [data-model.md](./data-model.md), [contracts/](./contracts/),
  [quickstart.md](./quickstart.md).
- **Phase 2**: `tasks.md` via `/speckit-tasks` — not created by this command.
