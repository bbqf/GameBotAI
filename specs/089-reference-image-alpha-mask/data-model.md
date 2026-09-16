# Phase 1 Data Model: Reference Image Transparency Masks

**Feature**: 089-reference-image-alpha-mask
**Date**: 2026-09-16

No persisted schema changes. The mask is carried by the reference image's own bytes and
derived at match time; nothing new is stored, indexed, or migrated.

## Entities

### Reference image (existing — unchanged on disk)

| Attribute | Change |
|---|---|
| id, contentType, sizeBytes, filename, createdAtUtc, updatedAtUtc | unchanged |
| bytes | unchanged in handling — already stored verbatim, so an alpha channel already survives upload and retrieval |

The only change is in how those bytes are **decoded** for matching: `ImreadModes.Unchanged`
instead of `ImreadModes.Color`.

### TemplateMask (new, in-memory only)

Derived from a template's alpha channel. Never persisted, never separately addressable.

| Field | Type | Meaning |
|---|---|---|
| `Mask` | `Mat` (CV_8UC1, values 0 or 255) | 255 where the template pixel is retained |
| `RetainedCount` | `int` | number of retained pixels; `n` in the scoring formula |

**Creation rule** (`TemplateMask.TryCreate`) — succeeds only when **all** hold:

1. The template Mat is 8-bit (`CV_8U`).
2. It has 4 channels (BGRA).
3. Its alpha channel is not uniformly >= 128.

Condition 3 is what guarantees FR-005: an all-opaque alpha yields no mask, so the template
takes the untouched unmasked path and scores identically.

**Classification rule** (FR-003): `alpha >= 128` retained, `alpha < 128` masked out.

**Validity**:

| Retained count | Behaviour |
|---|---|
| 0 | rejected at upload (FR-008); at match time, no mask is produced and no match is reported |
| 1–15 | rejected at upload (FR-008) as degenerate |
| >= 16 | valid mask |

### TemplateMatchResult (existing — additive only)

```csharp
public sealed record TemplateMatchResult(IReadOnlyList<TemplateMatch> Matches, bool LimitsHit) {
  public bool Masked { get; init; }
  public int RetainedPixelCount { get; init; }
}
```

Added as init-only members, not positional parameters, so every existing construction site
and test double compiles unchanged. Defaults (`false`, `0`) describe the unmasked path
truthfully.

`RetainedPixelCount` counts the pixels **kept**, never the pixels masked out, and is
surfaced on the API under the matching name `retainedPixelCount` — one word, one meaning,
at every layer.

### TemplateMatch, BoundingBox, TemplateMatcherConfig (existing — unchanged)

`TemplateMatch.BBox` remains the **full template rectangle** for masked matches too
(FR-010), so coordinates resolved from a match keep their existing meaning.

## Derived quantities (transient, per detection)

| Quantity | Source | Lifetime |
|---|---|---|
| `Σ mᵢTᵢ`, `Σ mᵢTᵢ²` | template + mask | per call, scalar |
| `Σ mᵢTᵢIᵢ`, `Σ mᵢIᵢ`, `Σ mᵢIᵢ²` | three `TM_CCORR` correlations | per call, one score-map-sized Mat each, disposed before candidate collection |

## Validation rules mapped to requirements

| Rule | Requirement | Enforced at |
|---|---|---|
| Alpha >= 128 is retained | FR-003 | `TemplateMask.TryCreate` |
| Masked-out pixels affect neither numerator nor normalisation | FR-002 | `MaskedTemplateMatch.ComputeScoreMap` |
| No alpha ⇒ scores identical to before | FR-004 | `TemplateMatcher` branch (existing call untouched) |
| All-opaque alpha ⇒ no mask ⇒ identical scores | FR-005 | `TemplateMask.TryCreate` condition 3 |
| Alpha preserved end to end | FR-006 | `TemplateImageDecoder` (storage already preserves it) |
| Fewer than 16 retained pixels rejected | FR-008 | `ImageDetectionsValidation`, at upload |
| Uniform retained region ⇒ no match | FR-009 | `MaskedTemplateMatch` (`varT <= 0`) |
| Match rect is the full template rect | FR-010 | shared candidate collection |
| Masked-ness observable | FR-011 | `TemplateMatchResult` → detect response + logs |
