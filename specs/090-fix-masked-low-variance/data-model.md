# Phase 1 Data Model: 090-fix-masked-low-variance

**Feature**: 090-fix-masked-low-variance | **Date**: 2026-09-16

This change adds no persisted entity, no stored schema and no wire field. What follows is the set of
in-memory quantities the corrected comparison works with, the one rule applied to them, and the one
new domain field.

## Quantities

All computed inside `MaskedTemplateMatch.ComputeScoreMap`. `m` is the binary mask (1 where
retained), `T` the grayscale template, `I` the grayscale screen window at a candidate position, and
`n` the retained-pixel count.

| Quantity | Meaning | Form |
|---|---|---|
| `n` | Retained pixels in the mask. Positive; a zero count returns no score map. | `int` |
| `varT` | `Σ m·(T − T̄)²` over the template's retained pixels — that is, `n·σ_T²`. | `double`, scalar |
| `varI` | `Σ m·(I − Ī)²` at each candidate position — that is, `n·σ_I²`. | `CV_32FC1` map |
| `numerator` | `Σ m·(T − T̄)(I − Ī)` at each candidate position. | `CV_32FC1` map |
| `σ_T` | `sqrt(varT / n)` — the template's retained-region standard deviation, in shade levels. | derived |
| `σ_I` | `sqrt(varI / n)` — the screen region's standard deviation, in shade levels. | derived |
| `score` | `numerator / sqrt(varT · varI)`, the masked similarity. | `CV_32FC1` map |

**Why σ and not var.** `varI` scales with `n`, so a cutoff expressed on it would judge the same
screen content differently depending on how many pixels the reference image retains. `σ` divides
that out and is in shade levels — the units the screen is actually measured in. FR-005 requires
exactly this independence.

## The no-information rule

```
MinimumShadeStdDev = 1.0    (shade levels, 0–255 scale)

A retained region carries no information when σ < MinimumShadeStdDev.
A comparison involving such a region reports no match.
```

One constant, fixed and not configurable (FR-016). Derived in [research.md](./research.md) R-004:
the 8-bit noise floor for a 1304-pixel region is σ = 0.0277, and the lowest real content that must
keep being scored is σ = 2.43. The cutoff sits between them with margin on both sides, and is the
same number `ImageMatchEvaluator` already uses to call a template constant.

Applied on both sides:

| Side | Test | Outcome when it fails |
|---|---|---|
| Template retained region (FR-018) | `σ_T < 1.0` (replaces `varT <= 0`) | `ComputeScoreMap` returns `null`; the matcher reports no match for the whole frame. |
| Screen region, per position | `σ_I < 1.0`, i.e. `varI ≤ n · 1.0²` | That position scores exactly `0` and is counted as suppressed. |

## Score range invariant

```
-1.0 ≤ score ≤ 1.0    at every position, for every input
```

Enforced by clamping the score map where it is produced. Previously violated by `±Infinity`;
`Normalization.ClampConfidence` masked the violation downstream by turning `∞` into `1.0`. See
[contracts/score-range.md](./contracts/score-range.md).

## State transitions

Per candidate position, the comparison reaches exactly one of three states:

```
                 σ_T < 1.0  ──────────────────────────► no score map at all (null)
                                                         → matcher reports no match

  per position:  σ_I < 1.0  ──────────────────────────► score = 0, counted as suppressed
                 σ_I ≥ 1.0  ──────────────────────────► score = restricted TM_CCOEFF_NORMED,
                                                         clamped into [−1, 1]
```

The third branch is arithmetically identical to today's for every input that reaches it — which is
what makes SC-003 (masked accuracy unchanged) hold.

## New field

`TemplateMatchResult` (`src/GameBot.Domain/Vision/ITemplateMatcher.cs`) gains one init-only
property, alongside the existing `Masked` and `RetainedPixelCount`:

| Field | Type | Meaning |
|---|---|---|
| `NoInformationPositionCount` | `int` | How many candidate positions the no-information rule scored zero. `0` for an unmasked comparison, and for a masked comparison over content that is everywhere detailed. |

**Domain only.** It is not added to any response DTO (FR-013); it reaches an operator through log
event 11006 — see [contracts/diagnostics.md](./contracts/diagnostics.md). It counts positions
*suppressed*, never positions *scored*, and carries that name in the domain and in the log alike, in
the same spirit as feature 089's `retainedPixelCount`.

## Validation rules carried forward

Unchanged from feature 089, and re-verified by this change rather than modified:

- A mask retaining fewer than 16 pixels is refused at upload with `400 invalid_image`.
- A fully transparent template (zero retained pixels) reports no match.
- An all-opaque alpha is not treated as a mask at all, keeping the image on the untouched
  `CCoeffNormed` path — the structural basis of zero score drift.
