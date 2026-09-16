# Implementation Plan: Masked matching must not score featureless screen regions as perfect matches

**Branch**: `090-fix-masked-low-variance` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/090-fix-masked-low-variance/spec.md`
**Issue**: [#196](https://github.com/bbqf/GameBotAI/issues/196) (B-013)

## Summary

A masked reference image scores a perfect `1.0000` against any screen region that is flat under the
mask. Measurement (see [research.md](./research.md)) shows the cause is not the numerical
cancellation the issue hypothesised — float32 tracks float64 to four decimals throughout — but a
division by zero in `MaskedTemplateMatch.ComputeScoreMap` that yields `±Infinity`, which
`Normalization.ClampConfidence` then clamps to exactly `1.0` at the API boundary. A flat 400x300
frame produces 69,524 infinite positions in the score map.

The fix makes the division total: the denominator is forced safe before the divide, positions whose
retained screen region carries less than one shade level of standard deviation are scored zero as
carrying no information, and the result is clamped into the measure's own [−1, 1] range so that no
out-of-range score can reach a caller. The same one-shade-level rule replaces the template side's
`varT <= 0` guard, closing the mirror case. Everything lands inside one method, so no interface,
schema, or caller changes — every consumer inherits the fix.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: OpenCvSharp4 (`Cv2.MatchTemplate`, `Cv2.Threshold`, `Cv2.Divide`), xUnit
**Storage**: N/A — the change is confined to in-memory image comparison
**Testing**: xUnit; unit (`tests/unit`), integration (`tests/integration`), contract (`tests/contract`)
**Target Platform**: Windows service host (`GameBot.Service`), Windows 11
**Project Type**: Single solution — domain library + web service + three test projects
**Performance Goals**: masked detection of a 42x52 template over a 1080x1920 frame under 300ms and
no more than 2.5x the unmasked path (baseline today: 189ms masked, 99ms unmasked, 1.91x); the hard
ceiling is the 500ms detection timeout, beyond which a detection is reported as an absence
**Constraints**: zero score drift on the unmasked path (structural — that path is not reached);
no change to any request/response shape or sequence schema; the masked measure stays an exact
restriction of `TM_CCOEFF_NORMED` to the retained pixels
**Scale/Scope**: one domain method substantially changed, one domain record field added, one log
event added; ~6 test files touched or created

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. The change is local to `ComputeScoreMap`, which stays well under 50 LOC of logic. The existing XML docs on that method **state the false premise that caused this bug** ("Cv2.Divide yields 0 wherever the divisor is 0") and MUST be corrected, not merely extended. No new dependencies. |
| **II. Testing Standards** | PASS. Constitution requires a failing test reproducing the bug *before* the fix — T004–T007 are written and confirmed red at the T008 gate before any implementation (FR-014 / SC-008). Touched area is a single method already at high coverage; new branches are directly covered. |
| **III. UX Consistency** | PASS. No interface change. The one-shade-level definition of "featureless" is deliberately the same number `ImageMatchEvaluator` already uses for a constant template, so the codebase keeps one meaning for the term. Diagnostics follow the established "own event id, never repurpose a format" pattern. |
| **IV. Performance Requirements** | PASS with a declared budget (SC-005) and a benchmark task (T027) against the existing `MaskedTemplateMatcherBench`. Added work is O(result map) elementwise against three existing full-frame correlations. |
| **V. Living Documentation (NON-NEGOTIABLE)** | PASS. `docs/architecture.md` describes the matching capability and MUST be updated with a refreshed "Last reviewed" date (T028). Spec 089's `Status` line MUST be updated to record that it is iterated by 090, with `specs/STATUS.md` kept consistent (T029). |

**Definition-of-Done note**: the constitution makes any red build or test a hard stop. The full
suite (1803 tests) must be green locally before the implementation commit, and no expected-score
assertion may be loosened to get there (SC-007).

### Post-Phase-1 re-check

Still PASS. The design adds one field to an existing domain record and one log event; it introduces
no new project, no new abstraction, and no new configuration surface. The Complexity Tracking table
below is empty because there is nothing to justify.

## Project Structure

### Documentation (this feature)

```text
specs/090-fix-masked-low-variance/
├── plan.md              # This file
├── research.md          # Phase 0 — root cause, rejected hypothesis, cutoff derivation
├── data-model.md        # Phase 1 — the quantities and the rule
├── quickstart.md        # Phase 1 — how to reproduce and verify
├── contracts/
│   ├── score-range.md       # The guarantee a reported similarity carries
│   └── diagnostics.md       # The new log event
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Vision/
│   ├── MaskedTemplateMatch.cs      # CHANGED — the fix lives here, and only here
│   ├── ITemplateMatcher.cs         # CHANGED — TemplateMatchResult gains a diagnostic count
│   ├── TemplateMatcher.cs          # CHANGED — propagates the count; no logic change
│   ├── TemplateMask.cs             # unchanged
│   ├── TemplateImageDecoder.cs     # unchanged
│   └── Normalization.cs            # unchanged — kept as a boundary guard, now a no-op here
└── Triggers/Evaluators/
    └── ImageMatchEvaluator.cs      # unchanged — inherits the fix; covered by a test so it stays fixed

src/GameBot.Service/Endpoints/
├── ImageDetectionsEndpoints.Logging.cs   # CHANGED — new event 11006
└── ImageDetectionsEndpoints.cs           # CHANGED — emits it; response DTO untouched

tests/unit/Vision/
├── MaskedTemplateMatchTests.cs     # CHANGED — near-flat and flat scene cases, range invariant
├── MaskFixtures.cs                 # CHANGED — a flat / controlled-variance scene builder
└── TemplateMaskTests.cs            # unchanged
tests/unit/Triggers/                # CHANGED — trigger does not fire on a flat region
tests/unit/Performance/
└── MaskedTemplateMatcherBench.cs   # CHANGED — asserts the SC-005 budget still holds
tests/integration/
└── MaskedDetectionIntegrationTests.cs    # CHANGED — end-to-end flat-screen case
docs/architecture.md                       # CHANGED — living documentation
specs/089-reference-image-alpha-mask/spec.md, specs/STATUS.md   # CHANGED — Status lines
```

**Structure Decision**: no structural change. The defect is one arithmetic step in one domain
method; the deliberate design goal is that the blast radius stays there, which is what makes
FR-010 ("every consumer inherits it") and FR-013 ("nothing on the wire changes") true structurally
rather than by inspection.

## Design

### The rule

For a retained region of `n` pixels, `Σ m·(x − x̄)²` equals `n·σ²`, so `σ = sqrt(var / n)` is the
region's standard deviation in shade levels — independent of how many pixels the mask retains, which
is what FR-005 demands. The rule is:

> A retained region whose shade standard deviation is below **1.0** carries no information. A
> comparison involving one reports no match.

Applied to the screen region per candidate position, and to the template's retained region once.
`MinimumShadeStdDev = 1.0` is a single documented constant (FR-016), chosen in R-004 between the
8-bit noise floor (σ = 0.0277 for one differing pixel) and the lowest real content that must keep
being scored (σ = 2.43, the issue's own case, legitimately 0.5171). It is the same number
`ImageMatchEvaluator` already uses for a constant template.

### The computation

`ComputeScoreMap` changes in four places:

1. **Template guard** — replace `if (varT <= 0) return null` with the σ rule:
   `if (varT <= 0 || Math.Sqrt(varT / n) < MinimumShadeStdDev) return null`.
2. **Screen-region cutoff** — threshold `varI` to zero below `n · MinimumShadeStdDev²` instead of
   below 0, using `ThresholdTypes.Tozero` (which keeps `src > thresh`).
3. **Total division** (FR-018 covers the template guard in step 1) — a quotient cannot be repaired after the fact, because `∞ × 0` is `NaN`. So
   before dividing: build the unusable-denominator mask with `Cv2.Compare(sqrtVarI, 0, ..., LE)`,
   then `numerator.SetTo(0, unusable)` and `sqrtVarI.SetTo(1, unusable)`. The quotient is then
   exactly 0 there, with no division by zero anywhere in the map.
4. **Range clamp** — clamp the score map into [−1, 1] so FR-003 holds at the source. A clamp only
   downstream is what hid this defect for a release; the range must be true where it is produced.

The method also returns how many positions the rule suppressed, for FR-017.

### What deliberately does not change

- **The measure.** Still an exact restriction of `TM_CCOEFF_NORMED` to the retained pixels. For any
  region above the cutoff the arithmetic is byte-for-byte what it is today, so every masked score
  feature 089 recorded is unchanged (SC-003).
- **The unmasked path.** Not reached by any of this. Zero drift is structural (SC-004, FR-008).
- **`Normalization.ClampConfidence`.** Kept as a boundary guard. After the fix it is a no-op for
  this path, which is the point — it should never again be the thing that makes an impossible score
  look plausible.
- **Every caller, DTO and schema.** Untouched (FR-010, FR-013).

## Phase 0 — Research

Complete. See [research.md](./research.md). All unknowns resolved; no NEEDS CLARIFICATION remain.
Headline results: the issue's cancellation hypothesis is **rejected by measurement** (R-001); the
cause is `Cv2.Divide` returning `±Infinity` on a zero divisor, contrary to the comment asserting
otherwise (R-002); the `1.0000` is `Clamp01(∞)` (R-003); the cutoff is derived in R-004; the
template-side mirror case in R-005; the total-division technique in R-006; the trigger path's
exposure in R-007; diagnostics in R-008.

## Phase 1 — Design & Contracts

Complete. [data-model.md](./data-model.md) records the quantities, the rule and the one new field.
[contracts/score-range.md](./contracts/score-range.md) states the guarantee a reported similarity
now carries; [contracts/diagnostics.md](./contracts/diagnostics.md) specifies log event 11006.
[quickstart.md](./quickstart.md) is the reproduce-and-verify path.

Agent context (`CLAUDE.md`) is updated to point at this plan.

## Risks

| Risk | Mitigation |
|---|---|
| The σ cutoff rejects a legitimate low-contrast target. | Accepted and specified (spec Edge Cases): a region below one shade level genuinely has no evidence to offer, and a false absence costs a retry where a false presence arms a tap. The cutoff sits 2.4x below the lowest real case observed. |
| The template-side guard change rejects an existing reference image. | Only reachable for masked images, which are currently withdrawn from production. Existing fixtures are far above the cutoff; `MaskedTemplateWithUniformRetainedRegionReportsNoMatch` passes under both guards (R-005). |
| Added elementwise work breaches the detection deadline. | Budgeted in SC-005 and asserted by T-024 against the existing benchmark. Added work is O(result map) against three existing full-frame correlations. |
| A masked score shifts and silently invalidates a calibrated threshold. | Above the cutoff the arithmetic is unchanged, and T017 asserts agreement with independently computed reference values on detailed content. No existing expected-score assertion may be relaxed (SC-007, enforced by T018). |

## Complexity Tracking

No constitutional violations to justify — the table is intentionally empty.
