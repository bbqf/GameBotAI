# Phase 0 Research: Masked matching scoring featureless screen regions as perfect matches

**Feature**: 090-fix-masked-low-variance | **Date**: 2026-09-16 | **Issue**: #196 (B-013)

## Summary

The reported symptom is real and reproduces, but **the mechanism proposed in the issue is not the
cause**. The issue hypothesised float32 catastrophic cancellation in `varI = sum(m*I*I) -
sum(m*I)^2/n` on a near-uniform patch. That hypothesis was tested and rejected. The actual cause is
a plain division by zero whose result is `±Infinity`, which is then **clamped to exactly 1.0 at the
API boundary** — which is why the reported score is `1.0000` and not some large arbitrary number.

This matters for the fix: no change to accumulation precision is needed, the arithmetic is sound,
and the remedy is a small, local guard plus a structural range clamp.

## R-001: Is float32 cancellation the cause? — **No**

**Method**: modelled `ComputeScoreMap`'s exact formula in numpy at both float32 (with an
FFT-based correlation, as OpenCV's `matchTemplate`/`CCorr` uses for `CV_32F`) and float64, over a
42x52 template with ~900 retained pixels against screen patches of controlled standard deviation
and mean.

**Result**: float32 tracked float64 to four decimal places at every level tested, including the
reported std of 2.43 and at means from 40 to 250.

| scene mean | scene std | exact f64 | shipped f32 | f64 accumulation |
|---|---|---|---|---|
| 200 | 2.43 | 0.0054 | 0.0054 | 0.0054 |
| 200 | 1.00 | 0.0234 | 0.0233 | 0.0234 |
| 200 | 5.00 | 0.0329 | 0.0329 | 0.0329 |
| 40 | 2.43 | 0.0226 | 0.0226 | 0.0226 |

Over a full near-uniform field the float32 maximum was 0.1568 against a float64 maximum of 0.1569,
with **zero** positions at or above 0.99 and **zero** positions above 1.0.

**Decision**: do **not** widen accumulation to float64. It would cost performance for no
correctness gain and would perturb the masked scores that feature 089 calibrated.

**Alternatives considered**: float64 accumulation throughout; a mean-subtracted (two-pass)
accumulation to avoid cancellation. Both rejected — they solve a problem that measurement shows
does not exist.

## R-002: What actually happens — **`Cv2.Divide` yields ±Infinity, not 0, on a zero divisor**

`MaskedTemplateMatch.ComputeScoreMap` ends with:

```csharp
Cv2.Threshold(varI, varI, 0, 0, ThresholdTypes.Tozero);
Cv2.Sqrt(varI, sqrtVarI);
// "Cv2.Divide yields 0 wherever the divisor is 0, so a zero-variance window scores 0 without a
//  separate branch."
Cv2.Divide(numerator, sqrtVarI, scores, 1.0 / Math.Sqrt(varT));
```

**That comment is false for `CV_32FC1`.** Measured directly against the real OpenCvSharp build, a
perfectly flat 400x300 frame under a 1304-retained-pixel circular mask produces:

| differing pixels | scene std under mask | raw map max | raw map min | positions > 1.0 | positions non-finite |
|---|---|---|---|---|---|
| none (flat) | 0.0000 | ∞ | −∞ | 0 | **69,524** |
| 1 in 4096 | 0.0000 | ∞ | −∞ | 0 | 42,089 |
| 1 in 1024 | 0.0277 | ∞ | −∞ | 0 | 65,466 |
| 1 in 256 | 0.0553 | ∞ | −∞ | 0 | 17,760 |
| 1 in 64 | 0.1198 | ∞ | −∞ | 0 | 351 |
| 1 in 16 | 0.2343 | 0.0515 | −0.0437 | 0 | 0 |
| 1 in 8 | 0.3333 | 0.0461 | −0.0279 | 0 | 0 |
| 1 in 2 | 0.5000 | 0.0297 | −0.0303 | 0 | 0 |

Two things stand out. First, the degenerate output is **non-finite**, never merely "large" — there
is not a single score above 1.0 anywhere in the sweep. Second, once the retained patch carries any
genuine variation at all (std ≥ 0.23), every score is sane and small. The failure is a cliff at
exactly-zero window variance, not a gradual degradation.

Through the public matcher, on a flat frame at a threshold of 0.95:

```
matches at threshold 0.95 on a perfectly flat frame: 3
  confidence = ∞  clamped = 1  at 0,224
  confidence = ∞  clamped = 1  at 11,244
  confidence = ∞  clamped = 1  at 23,224
```

**Decision**: make the division total. Never divide by a denominator that is not safely positive.

## R-003: Why the reported score is exactly `1.0000`

`ImageDetectionsEndpoints` reports `Normalization.ClampConfidence(m.Confidence)`, and
`ClampConfidence` is `Clamp01`, which is `v < 0 ? 0 : (v > 1 ? 1 : v)`. Since `∞ > 1`, every
infinite confidence is reported as exactly `1.0`.

This closes every loose end in the issue report:

- **Exactly 1.0000, not 0.98 or 1.04** — it is a clamp, not a correlation.
- **Two different reference images scoring 1.0 at the same position** — the value never depended on
  the template at all; any masked template over the same flat region yields ∞.
- **Only on dimmed modals** — that is where large exactly-flat regions exist. A detailed game screen
  has no window with zero variance under the mask.
- **The unmasked path unaffected on the same screens** — OpenCV's own `CCoeffNormed` handles its own
  degenerate case internally and never reaches this code.
- **The independently computed restriction giving 0.5171** — the reporter computed a correlation at
  the position the API reported, but that position was chosen by non-maximum suppression among
  infinities. The reported score and the reported position never came from a correlation.

The clamp is also why this survived feature 089's test suite: it converts a glaring `∞` into a
plausible-looking `1.0`. **A downstream clamp that hides an out-of-range score is part of the
defect**, which is why FR-003 requires the range to hold at the source.

**Decision**: keep `ClampConfidence` (it is a reasonable boundary guard) but make it a no-op for
this path by guaranteeing the range where the score is produced.

## R-004: The no-information cutoff

FR-005 requires one documented rule, expressed in terms of screen content so that it does not vary
with template size; FR-016 requires it fixed rather than configurable.

`varI` as computed is `Σ m·(I − Ī)²`, which is `n · σ²` for the retained patch. Dividing out `n`
gives the patch's standard deviation in shade levels — a quantity that is independent of how many
pixels the mask retains, and directly comparable across reference images of any size. The rule is
therefore stated on `σ`, not on `varI`.

Choosing the value:

- **Noise floor**: the smallest non-zero standard deviation an 8-bit patch can have is one pixel
  differing by one shade. For 1304 retained pixels that is σ = 0.0277 — measured above, and
  confirmed as carrying no information (it still produced ∞ at 65,466 positions).
- **Real content**: the degenerate case in the issue has σ = 2.43 and a legitimate score of 0.5171.
  That must stay above the cutoff and keep being scored normally. Ordinary detailed screens are far
  higher again.
- **Existing precedent**: `ImageMatchEvaluator` already encodes "constant" as
  `tplStdDev.Val0 < 1.0`. Reusing that number keeps one definition of "featureless" in the codebase
  rather than introducing a second, differently-calibrated one.

**Decision**: `σ < 1.0` shade level means no information; report no match. There is a clear gap
between the noise floor (0.0277) and the lowest real content that must survive (2.43), and 1.0 sits
inside it with margin on both sides — satisfying the residual assumption A-003 left open.

**Alternatives considered**:
- A cutoff on `varI` directly — rejected: it scales with the retained-pixel count, so the same
  screen content would be judged differently by a large and a small reference image, violating
  FR-005.
- A relative cutoff (σ as a fraction of the patch mean) — rejected: it would make a dark screen
  harder to reject than a bright one for no principled reason, and it has no precedent here.
- Machine epsilon on the denominator — rejected: it fixes only the exact-zero cliff and leaves
  σ = 0.0277 (a single differing pixel) scoring as though it were evidence.

## R-005: Apply the rule to the template side as well

The template side currently guards only `if (varT <= 0) return null`. A masked template whose
retained region has a tiny but non-zero variance produces a tiny denominator and the same
uncontrolled score — the mirror of this defect, and the one the repository already met once as the
`pns-never-matches` sentinel. `TemplateMask.TryCreate` cannot catch it, because it only refuses an
all-opaque alpha, not a low-contrast retained region.

**Decision**: apply the same σ ≥ 1.0 rule to the template's retained region, replacing the
`varT <= 0` guard. One rule, one constant, both sides. This widens the existing "no variation in
shade" guard that 089 FR-009 describes; because it is a genuine widening rather than a pure
correction, it is carried by its own requirement, **FR-018**, added to the spec after the
cross-artifact analysis flagged it as behaviour no requirement authorized.

**Risk checked**: the existing test `MaskedTemplateWithUniformRetainedRegionReportsNoMatch` builds a
template with a uniform interior (σ = 0) and expects no match — it passes under both the old and the
new guard. The other masked fixtures use hash-derived badge colours spanning 0–255, far above the
cutoff.

## R-006: How to make the division total

Dividing and then repairing the result does not work: `∞ × 0` is `NaN`, so masking the quotient
afterwards cannot recover a clean zero. The denominator must be made safe **before** the division:

1. Threshold `varI` to zero wherever it is below `n · σ_min²` (`ThresholdTypes.Tozero`, which keeps
   `src > thresh` and zeroes the rest).
2. Take the square root.
3. Build a mask of the positions where the denominator is not usable (`Cv2.Compare(..., LE)`).
4. On those positions set the numerator to 0 and the denominator to 1, so the quotient is exactly 0.
5. Divide.
6. Clamp the result into [−1, 1] so no rounding can put a score outside the measure's range
   (FR-003), making the downstream `ClampConfidence` a genuine no-op rather than a concealer.

**Decision**: the above, entirely inside `ComputeScoreMap`. No caller changes, no interface change,
no schema change — FR-010 and FR-013 hold structurally because the change does not leave this method.

**Performance**: adds one compare, two masked `SetTo`s and two clamps over the result map, against
the three existing full-frame correlations that dominate the cost. Expected to be lost in the noise
of the 189ms baseline; measured under T027 against the 300ms / 2.5x budget in SC-005.

## R-007: The trigger path carries the same defect

`ImageMatchEvaluator.ComputeSimilarity` ends with `Math.Max(0, Math.Min(1, ...Confidence))` — the
same clamp, so an infinite confidence becomes a similarity of 1.0 and the trigger fires. Its
own `tplIsConstant` check measures the **template's** standard deviation, not the region's, so a
detailed template over a flat region goes to the matcher path and inherits the defect.

**Decision**: the fix in `ComputeScoreMap` repairs this path too, because it is the same matcher.
No separate remedy is needed for the general path. The same-size shortcut path does not divide at
all (it is a mean-absolute-difference), so it is not exposed — verified by reading, and covered by a
test so it stays that way.

## R-008: Diagnostics (FR-017)

`TemplateMatchResult` already carries domain-only fields (`Masked`, `RetainedPixelCount`) that are
not part of the wire contract. Adding a count of positions suppressed by the no-information rule
follows that established shape, and the endpoint logs it through the existing
`ImageDetectionsEndpointComponent` channel as its own event — the pattern feature 089 used for
`LogDetectMask`, which explicitly avoids repurposing an existing log format.

**Decision**: add `NoInformationPositionCount` to `TemplateMatchResult`; log it from the detect
endpoint as a new event id (11006). The response DTO is untouched, so FR-013 holds.

**Alternatives considered**: plumbing an `ILogger` into `TemplateMatcher` — rejected, it would put
logging concerns into the domain for one diagnostic; adding the count to the response body —
rejected by FR-013.

## Reproduction assets

The probe used for R-001 lived at `tests/unit/Vision/B013Probe.cs` and was removed once its findings
were recorded here; its substance becomes the permanent regression tests required by FR-014. The
numpy model used to reject the cancellation hypothesis was a scratch script and is not committed —
its inputs, method and full results are recorded in R-001 above so the conclusion can be re-derived
without it.
