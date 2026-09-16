# Tasks: Masked matching must not score featureless screen regions as perfect matches

**Feature**: 090-fix-masked-low-variance | **Issue**: [#196](https://github.com/bbqf/GameBotAI/issues/196) (B-013)
**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests are mandatory here**: the constitution requires a bug fix to include a failing test
reproducing the issue *before* the fix (Principle II), and FR-014 / SC-008 require the same.
Phase 2 is that gate.

**Total**: 33 tasks across 7 phases.

---

## Phase 1: Setup & baseline

**Goal**: know exactly what "unchanged" means before changing anything.

- [ ] T001 Record the pre-change baseline: run `dotnet test "C:\src\GameBot\GameBot.sln" --nologo` and the masked/unmasked timings from `tests/unit/Performance/MaskedTemplateMatcherBench.cs`; note the suite count and the two timings for later comparison against SC-004/SC-005
- [ ] T002 [P] Add a flat-scene and controlled-variance scene builder to `tests/unit/Vision/MaskFixtures.cs`: a solid single-shade frame, and a frame where every Nth pixel differs by one shade so a retained region's standard deviation can be dialled from 0 through ~0.5 to ~2.4
- [ ] T003 [P] Add an independent reference implementation of the masked similarity to `tests/unit/Vision/MaskFixtures.cs` — a direct double-precision mean-subtracted correlation over the retained pixels at one position, written from the definition rather than from the production code, so FR-002's "agrees with an independently computed value" is a real cross-check and not a tautology

**Checkpoint**: fixtures exist; the baseline numbers are written down.

---

## Phase 2: Foundational — the failing reproduction (BLOCKING)

**Goal**: prove the defect from the outside, before touching production code. Every task below MUST
fail against the current build. No implementation may start until T008 confirms they do.

- [ ] T004 Add `FlatSceneRegionReportsNoMatch` to `tests/unit/Vision/MaskedTemplateMatchTests.cs`: a masked template over a perfectly flat frame reports no match — asserting specifically that no returned confidence is infinite, `NaN`, or `>= 0.95`, **and** that the raw score at a suppressed position is exactly `0.0` rather than merely below threshold (FR-015)
- [ ] T005 Add `NearFlatSceneRegionReportsNoMatch` to `tests/unit/Vision/MaskedTemplateMatchTests.cs`: a frame flat except one pixel in 1024 (retained-region standard deviation ≈ 0.03) reports no match, with the raw score exactly `0.0` (FR-015)
- [ ] T006 Add `ScoreNeverLeavesTheMeasureRange` to `tests/unit/Vision/MaskedTemplateMatchTests.cs`: sweep scene standard deviation from 0 to ~5 and assert every value in the raw score map is finite and within [-1, 1], with zero non-finite positions
- [ ] T007 Add a flat-screen case to `tests/integration/MaskedDetectionIntegrationTests.cs`: detection over a screen that is flat under the mask returns no match through the endpoint path, and in particular never a score of exactly 1.0
- [ ] T008 Run T004–T007 and confirm all four are **red** against the unmodified build; capture the failure output (expected: `confidence = ∞`, clamped to `1`) as the evidence SC-008 requires

**Checkpoint**: the bug is reproduced by automated tests. Implementation is now unblocked.

---

## Phase 3: User Story 1 (P1) — a masked reference image does not match a featureless region

**Goal**: FR-001..FR-006, FR-015, FR-016. This phase alone removes the hazard and is the MVP.

**Independent test**: T004–T007 turn green while every existing test stays green.

- [ ] T009 [US1] Add the `MinimumShadeStdDev = 1.0` constant to `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs` with documentation stating it is shade levels on the 0–255 scale, is fixed and not configurable (FR-016), and is the same number `ImageMatchEvaluator` uses to call a template constant — cite research.md R-004 for the derivation
- [ ] T010 [US1] Apply the screen-side cutoff in `ComputeScoreMap` in `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs`: threshold `varI` to zero below `retainedCount * MinimumShadeStdDev²` via `ThresholdTypes.Tozero`, replacing the current threshold at 0
- [ ] T011 [US1] Make the division total in `ComputeScoreMap` in `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs`: build the unusable-denominator mask with `Cv2.Compare(sqrtVarI, 0, ..., LE)`, set the numerator to 0 and the denominator to 1 on those positions, then divide — and **delete the comment asserting that `Cv2.Divide` yields 0 on a zero divisor**, replacing it with what was actually measured (research.md R-002)
- [ ] T012 [US1] Clamp the score map into [-1, 1] at the end of `ComputeScoreMap` in `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs`, documenting that the range must hold where the score is produced because a downstream clamp is what concealed this defect (FR-003, contracts/score-range.md G-1)
- [ ] T013 [US1] Replace the `varT <= 0` template guard in `ComputeScoreMap` in `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs` with the same σ rule, closing the mirror case (FR-018, research.md R-005); update the method's XML docs, which currently describe only the "no variation in shade" case
- [ ] T013a [US1] Add a test to `tests/unit/Vision/MaskedTemplateMatchTests.cs` for FR-018: a masked template whose retained region has a standard deviation just below 1.0 reports no match, while one just above it is scored normally — the reference-image mirror of T004/T005
- [ ] T014 [US1] Run T004–T007 and confirm all four are now green; run the full `tests/unit` project and confirm nothing regressed

**Checkpoint**: the reported defect is fixed and proven fixed.

---

## Phase 4: User Story 2 (P2) — masked matching keeps the accuracy it was introduced for

**Goal**: FR-007, FR-002, FR-009. Prove the fix cost nothing on real content.

**Independent test**: feature 089's recorded scores still hold, with no assertion loosened.

- [ ] T015 [US2] Add a test to `tests/unit/Vision/MaskedTemplateMatchTests.cs` that a retained region with a standard deviation of ≈2.4 — the issue's own degenerate case — is scored normally rather than suppressed, asserting SC-001's two numbers explicitly: the score is at most 0.60, and within 0.01 of the independent reference from T003
- [ ] T015a [US2] Add a test to `tests/unit/Vision/MaskedTemplateMatchTests.cs` for FR-006: sweep the retained region's standard deviation across the cutoff (≈0.8, 0.95, 1.0, 1.05, 1.2, 2.0) and assert the score is continuous — no jump from a suppressed zero to a high score, and the first scored value above the cutoff is small rather than near the top of the scale
- [ ] T016 [US2] Add a test to `tests/unit/Vision/MaskedTemplateMatchTests.cs` asserting the masked score on genuinely matching content is ≥ 0.93 and on a detailed no-target frame is within 0.50–0.60, using the values feature 089 recorded (SC-003)
- [ ] T017 [US2] Add a test to `tests/unit/Vision/MaskedTemplateMatchTests.cs` that the masked score agrees with the independent reference from T003 within a stated tolerance across several detailed frames, and state the tolerance in the test name or a comment (FR-002)
- [ ] T018 [US2] Run the whole masked suite — `MaskedTemplateMatchTests`, `TemplateMaskTests`, `TemplateImageDecoderTests`, `MaskedDetectionIntegrationTests`, `DetectImageTests`, `UploadImageMaskValidationTests` — and confirm every pre-existing expected-score assertion still holds **unmodified** (SC-007); if any needed changing, stop and treat it as a regression rather than editing the expectation

**Checkpoint**: masking is as accurate as it was, and measurably so.

---

## Phase 5: User Story 3 (P2) — every calibrated threshold keeps its meaning

**Goal**: FR-008. Prove zero drift on the unmasked path.

**Independent test**: unmasked scores are identical to the baseline from T001.

- [ ] T019 [US3] Confirm `UnmaskedTemplateScoresExactlyWhatCCoeffNormedProduces` in `tests/unit/Vision/MaskedTemplateMatchTests.cs` still passes unchanged, and extend it to also cover a flat frame — the case where the unmasked and masked paths previously disagreed most
- [ ] T020 [US3] Confirm `AllOpaqueAlphaChannelScoresIdenticallyToImageWithoutOne` in `tests/unit/Vision/MaskedTemplateMatchTests.cs` still passes unchanged
- [ ] T021 [US3] Verify by inspection that no edit in Phase 3 lies on the unmasked path in `src/GameBot.Domain/Vision/TemplateMatcher.cs` — the `CCoeffNormed` call and its surrounding branch must be byte-identical — and record that in the task notes as the structural basis for SC-004

**Checkpoint**: nothing in the field needs recalibrating.

---

## Phase 6: User Story 4 (P3) — screen-watching triggers are equally protected

**Goal**: FR-011.

**Independent test**: an image trigger over a flat region does not fire.

- [ ] T022 [US4] Add a test to `tests/unit/Triggers/ImageMatchEvaluatorSimilarityTests.cs` that an image trigger on a masked reference image does not fire when the region under the mask is flat, exercising the general matcher path in `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`
- [ ] T023 [US4] Add a test to `tests/unit/Triggers/ImageMatchEvaluatorSimilarityTests.cs` covering the same-size shortcut path in `ImageMatchEvaluator` on a flat region, pinning the fact that the mean-absolute-difference branch does not divide and so cannot degenerate (research.md R-007)

**Checkpoint**: both trigger evaluation paths are covered and safe.

---

## Phase 7: Polish & cross-cutting concerns

- [ ] T024 Add `NoInformationPositionCount` to `TemplateMatchResult` in `src/GameBot.Domain/Vision/ITemplateMatcher.cs` and return the suppressed-position count from `ComputeScoreMap` in `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs`, propagating it through `src/GameBot.Domain/Vision/TemplateMatcher.cs` (FR-017, data-model.md)
- [ ] T025 Add log event 11006 to `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs` and emit it from `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs` only when the suppressed count is above zero, per [contracts/diagnostics.md](./contracts/diagnostics.md)
- [ ] T025a Add a test asserting log event 11006 is emitted when positions are suppressed and **not** emitted for a detection over ordinary content, so SC-009's "an operator can determine…" is verified rather than assumed
- [ ] T026 Add a contract test to `tests/contract/Images/DetectImageTests.cs` asserting the detect response body is unchanged — specifically that no no-information field appears on the wire, so a later change cannot add one by accident (FR-013)
- [ ] T027 Extend `tests/unit/Performance/MaskedTemplateMatcherBench.cs` to assert the SC-005 budget: a 42x52 template over a 1080x1920 frame under 300ms and no more than 2.5x the unmasked path; compare against the T001 baseline of 189ms / 99ms
- [ ] T028 Update `docs/architecture.md` for the corrected matching behaviour and the no-information rule, and refresh its "Last reviewed" date (constitution Principle V)
- [ ] T029 Update the `**Status**` line of `specs/089-reference-image-alpha-mask/spec.md` to record that it is iterated by 090, set this feature's own `Status`, and make `specs/STATUS.md` consistent with both (constitution Principle V)
- [ ] T030 Run `dotnet test "C:\src\GameBot\GameBot.sln" --nologo` and confirm the whole suite is green with no assertion loosened; a red result is a hard stop under the constitution's Definition of Done

---

## Dependencies

```
Phase 1 (T001–T003)
    │
    ▼
Phase 2 (T004–T008)  ◄── BLOCKING GATE: must be red before any implementation
    │
    ▼
Phase 3 / US1 (T009–T014)  ◄── the fix; MVP ends here
    │
    ├──────────────┬──────────────┬──────────────┐
    ▼              ▼              ▼              ▼
Phase 4 / US2  Phase 5 / US3  Phase 6 / US4   Phase 7
(T015–T018)    (T019–T021)    (T022–T023)    (T024–T030)
```

- T002 and T003 are independent of each other and of T001.
- T009 precedes T010–T013 (they use the constant). T010–T013 all edit
  `MaskedTemplateMatch.ComputeScoreMap`, so they are **sequential, not parallel**, despite being one
  logical change — they are listed separately so each is reviewable on its own. T013a follows T013.
- Phases 4, 5 and 6 are mutually independent once Phase 3 is complete.
- Within Phase 7, T024 precedes T025, T025a and T026; T027, T028 and T029 are independent.
  T030 is last.

## Parallel execution opportunities

- **Phase 1**: T002 and T003 together.
- **Phase 2**: T004, T005, T006 touch one file and should be written together but committed as one
  red batch; T007 is a different file and is genuinely parallel.
- **Phases 4–6**: all three stories can proceed at once — different test files, no shared production
  edits.
- **Phase 7**: T027, T028 and T029 in parallel after T024–T026.

## Implementation strategy

**MVP = Phases 1–3.** That is the whole safety fix: the hazard in issue #196 is gone once T014 is
green, and the result is shippable on its own. Phases 4–6 are the proof that the fix cost nothing —
required by the spec but not by the hazard. Phase 7 is diagnostics, budget and the constitution's
living-documentation obligations.

**Do not reorder Phase 2 after Phase 3.** A bug fix whose test was written after the fix does not
demonstrate that it fixes anything; the constitution requires the red test first and T008 is the
explicit checkpoint for it.
