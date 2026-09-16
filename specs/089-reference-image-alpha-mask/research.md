# Phase 0 Research: Reference Image Transparency Masks

**Feature**: 089-reference-image-alpha-mask
**Date**: 2026-09-16

## R-001: Where the alpha channel is lost today

**Finding**: Nothing in the storage layer destroys alpha. The loss is entirely at decode.

- `FileImageRepository` stores uploaded bytes verbatim under `{imagesRoot}/{id}.png`
  (`ImageReferencesEndpoints.UploadImageAsync` → `repo.SaveAsync`). A PNG with an alpha
  channel is already on disk with its alpha intact.
- `ReferenceImageStore.TryGet` reads that file with `new Bitmap(fs)`, which yields a
  32bppArgb `Bitmap` for an alpha PNG, and `AddOrUpdate` saves with `ImageFormat.Png`.
  Alpha survives both.
- Every template decode then calls `Mat.FromImageData(bytes, ImreadModes.Color)`, which
  **forces 3-channel BGR and discards alpha**. Five sites:
  - `ImageDetectionsEndpoints.cs:201` (`POST /api/images/detect`)
  - `ImageDetectionsEndpoints.cs:349` (`POST /api/images/detect-all`)
  - `CommandExecutor.cs:651` (tap-on-image primitive)
  - `ImageDetectionHelper.cs:34` (wait-for-image step and the game-readiness probe)
  - `ImageMatchEvaluator.cs:72` uses `BitmapConverter.ToMat`, which *does* produce a
    4-channel BGRA Mat — so alpha already reaches the matcher on the trigger path today.
- `TemplateMatcher.EnsureGrayscale` converts a 4-channel template with `BGRA2GRAY`, which
  ignores the alpha channel outright.

**Decision**: Decode templates with `ImreadModes.Unchanged` and let a 4-channel template
mean "masked". No storage-format change, no new upload field, no side-channel mask file.

**Consequence for backward compatibility**: a 4-channel template can already reach
`TemplateMatcher` today (via the trigger path) and is currently treated as opaque. After
this change it is treated as masked — which is the intended behaviour, and FR-005 keeps it
score-identical whenever that alpha is all-opaque.

## R-002: Does OpenCV's `matchTemplate` mask parameter cover our method?

**Finding**: No, not usably. OpenCV documents mask support for `TM_SQDIFF` and
`TM_CCORR_NORMED` only. The existing detection scores, the 0.85 gates calibrated against
them, and every threshold in live sequences are all `TM_CCOEFF_NORMED` (zero-mean
normalised cross-correlation). Switching method to obtain masking would change every score
in the system — a direct violation of FR-004/FR-005 and SC-003.

**Decision**: Compute masked ZNCC ourselves, as an exact restriction of `CCoeffNormed` to
the retained pixels.

**Alternatives considered**:
- *Switch to `TM_CCORR_NORMED` with a mask*: rejected — different measure, every existing
  score and threshold shifts.
- *Naive per-offset loop over the mask*: rejected on performance. A 42x52 template over a
  1080x1920 frame is ~2M offsets x 2184 pixels ≈ 4.5e9 operations, far past the 500 ms
  detection timeout.

## R-003: The masked ZNCC formulation

With mask `m ∈ {0,1}`, template `T`, image window `I`, and `n = Σ mᵢ`:

```
num   = Σ mᵢ·Tᵢ·Iᵢ − (Σ mᵢTᵢ)(Σ mᵢIᵢ)/n
varT  = Σ mᵢ·Tᵢ² − (Σ mᵢTᵢ)²/n
varI  = Σ mᵢ·Iᵢ² − (Σ mᵢIᵢ)²/n
score = num / sqrt(varT · varI)
```

`Σ mᵢTᵢ` and `Σ mᵢTᵢ²` are scalars computed once. The three image-dependent sums are each a
plain cross-correlation over the template window, so each is one `Cv2.MatchTemplate(...,
TemplateMatchModes.CCorr)` call:

| sum | call |
|---|---|
| `Σ mᵢ·Tᵢ·Iᵢ` | `MatchTemplate(I₃₂F, (m·T)₃₂F, CCorr)` |
| `Σ mᵢ·Iᵢ` | `MatchTemplate(I₃₂F, m₃₂F, CCorr)` |
| `Σ mᵢ·Iᵢ²` | `MatchTemplate((I²)₃₂F, m₃₂F, CCorr)` |

**Why this is the right formulation**: with an all-ones mask it reduces algebraically to
exactly `TM_CCOEFF_NORMED`. So the masked path is a strict generalisation, and a masked
score is directly comparable to an unmasked one — which is what FR-003a and the operator's
reused 0.85 gate require.

**Decision**: Three `CCorr` correlations plus one squared-image pass, combined per offset
in `double`. OpenCV picks a DFT-based evaluation for these, so cost is a small multiple of
today's single `MatchTemplate` call rather than a multiple of the template area.

**Precision note**: `Cv2.MatchTemplate` accepts only `CV_8U` and `CV_32F`, so the sums
accumulate in float32. Worst-case magnitude is `n·ΣI² ≈ 3e11`; float32 gives ~1.8e4
absolute error against a typical `varI` of ~1e10, i.e. a relative error near 1e-6 — three
orders of magnitude below the precision any threshold is expressed to. The final
combination is done in `double` to avoid compounding it.

## R-004: Preserving exact scores for unmasked templates

**Decision**: Branch, do not unify. `TemplateMatcher` keeps its current
`Cv2.MatchTemplate(..., CCoeffNormed)` call untouched for templates without a usable mask,
and only enters the masked path when the template is 8-bit, 4-channel, and its alpha is not
uniformly opaque. Assumption A-004 in the spec commits to this: FR-004 asks for identical
scores, not scores that agree to within rounding, and the only way to guarantee that is to
run the identical code.

**Rejected**: routing everything through the masked formulation with an all-ones mask.
Algebraically equal, but numerically it would differ in the last few digits from OpenCV's
own optimised path, which is exactly the silent threshold drift SC-003 forbids.

## R-005: Masking at the matcher, not at the call sites

**Decision**: Keep `ITemplateMatcher.MatchAllAsync(screenshot, templateMat, config, ct)`
unchanged and have `TemplateMatcher` derive the mask from the template Mat's own alpha
channel.

**Rationale**: masking becomes a property of the image, as FR-007 demands. Every consumer —
`/detect`, `/detect-all`, wait-for-image, tap-on-image, the readiness probe, image triggers
— then needs only its decode mode corrected, with no signature change, no new step field,
and no sequence-schema change. It also avoids adding a parameter to an interface with test
doubles across the suite.

**Alternatives considered**: an overload taking an explicit mask Mat (rejected — every call
site would have to opt in, and a path that forgot to would silently ignore an operator's
mask, the exact inconsistency FR-007 exists to prevent).

## R-006: Threshold for "retained"

**Decision**: `alpha >= 128` is retained; `alpha < 128` is masked out. Matches the spec's
clarified answer ("at least half opaque").

**Rationale**: a single fixed midpoint is reproducible across editors. Weighting by
fractional alpha instead was rejected: it makes the score depend on how an operator's tool
feathered the selection, and it would reintroduce background contamination through soft
edges, which is the defect being fixed.

## R-007: Degenerate masks

- **Zero retained pixels** and **fewer than 16 retained pixels**: rejected at upload
  (FR-008) with an explanatory 400, rather than at detection time. Failing at authoring
  time is the only place the operator can act on it; failing at detection time reproduces
  the silent no-match this feature exists to eliminate.
- **Uniform retained region** (`varT == 0`): the score is mathematically undefined. Report
  no match (FR-009). `TemplateMatcher` already returns an empty result for degenerate
  inputs, so this follows the established convention rather than inventing an error path.
- **Non-8-bit or otherwise unusual decodes** (16-bit PNG, palette): `ImreadModes.Unchanged`
  can return depths the current pipeline never sees. Guard by falling back to the existing
  `ImreadModes.Color` decode whenever the unchanged decode is not 8-bit — today's behaviour
  is preserved for anything exotic.

## R-008: Performance budget

`Service:Detections:TimeoutMs` defaults to **500 ms**, and a detection that exceeds it is
reported as an empty match set with `limitsHit` — indistinguishable from an absence. So a
masked path that is merely "slower" would resurrect the silent-stall failure mode in a new
guise.

**Decision**: budget masked detection at no more than ~3x unmasked, and back it with a
benchmark in the existing `tests/unit/Performance/TemplateMatcherBench.cs`. The masked path
costs three `CCorr` correlations plus one squared-image conversion, against one correlation
today; the existing O(W·H) candidate scan in `MatchAllAsync` is unchanged and is already a
significant share of the total.

## R-009: Observability

**Decision**: add init-only `Masked` and `RetainedPixelCount` members to the existing
`TemplateMatchResult` record rather than new positional constructor parameters, so no
existing construction site or test double breaks. Surface them as additive
`masked` / `retainedPixelCount` fields on the `/api/images/detect` response and as structured
log fields. FR-011 requires additivity explicitly.
