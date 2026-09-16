# Contract: the guarantee a reported similarity carries

**Feature**: 090-fix-masked-low-variance | **Issue**: #196 (B-013)

## Scope

Every similarity produced by `ITemplateMatcher` and reported through
`POST /api/images/detect`, `POST /api/images/detect-all`, wait-for-image, tap-on-image, the
readiness probe, and image triggers.

## Guarantees

### G-1 — Range holds at the source

A similarity is always a real number in `[-1, 1]`. It is never `Infinity`, never `NaN`, and never
outside that range, for any reference image and any screen content.

This is enforced where the score is computed, not where it is reported. `Normalization.ClampConfidence`
remains in place as a boundary guard, but after this change it never has anything to clamp on this
path. **Before this change it was the only thing standing between an `Infinity` and a caller, and it
turned that `Infinity` into a convincing `1.0`** — which is how the defect survived a release. A
range guaranteed only at the boundary is not a guarantee; it is a disguise.

### G-2 — A featureless region reports no match

If the screen region under the mask has a shade standard deviation below `1.0` on the 0–255 scale,
the similarity at that position is exactly `0`.

This is an absence of evidence, not a measurement of dissimilarity. It is reported as `0` and
filtered by the caller's ordinary threshold; no separate "absent" concept is introduced (FR-015).

### G-3 — A featureless reference image reports no match (FR-018)

If the reference image's retained region has a shade standard deviation below `1.0`, the comparison
reports no match at all — for the whole frame, not per position.

### G-4 — The cutoff is fixed

`1.0` shade level is a single documented constant. It is not configurable per reference image, per
call, or by deployment configuration (FR-016). An operator cannot tune it, and does not need to.

### G-5 — Content above the cutoff is scored exactly as before

For any position whose screen region is at or above the cutoff, the similarity is arithmetically
what the current build produces: an exact restriction of `TM_CCOEFF_NORMED` to the retained pixels.
No masked score that feature 089 recorded moves.

### G-6 — The unmasked path is untouched

A reference image with no alpha, or with an all-opaque alpha, is not masked, does not enter this
code, and reports a similarity identical to today's. Zero score drift is structural, not a
tolerance (FR-008).

## Observable consequences

| Situation | Before | After |
|---|---|---|
| Masked image over a perfectly flat region | `1.0000` (clamped `∞`) | `0` |
| Masked image over a region with one differing pixel (σ = 0.0277) | `1.0000` (clamped `∞`) | `0` |
| Masked image over the reported dimmed modal (σ = 2.43) | `1.0000` | unchanged arithmetic, ≈`0.52` |
| Masked image over a genuine target | `0.98`–`1.00` | unchanged |
| Masked image over a detailed screen, no target | `0.53`–`0.55` | unchanged |
| Unmasked image, any screen | as recorded | identical |

## Non-goals

- No change to any request or response shape, field name, or status code.
- No change to the similarity measure.
- No new configuration.
