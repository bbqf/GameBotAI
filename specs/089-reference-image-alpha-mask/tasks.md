# Tasks: Reference Image Transparency Masks

**Feature**: 089-reference-image-alpha-mask | **Branch**: `089-reference-image-alpha-mask`
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: [#190](https://github.com/bbqf/GameBotAI/issues/190)

**Tests**: Required. Constitution II mandates unit + integration coverage for executable
logic and a failing test reproducing the defect before the fix.

**Repo shell rules**: PowerShell with absolute paths; edit UTF-8 files with Edit/Write.

## Phase 1: Setup

- [X] T001 Add a deterministic masked-template fixture builder at `tests/unit/Vision/MaskFixtures.cs` that renders, in code with no committed binary assets, (a) a circular badge with an opaque interior and a fully transparent surround, (b) the same badge composited onto two visibly different synthetic backgrounds as opaque rectangles, and (c) a control frame with no badge — reproducing the issue-#190 geometry at 42x52 over a 1080x1920 frame

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: plumbing every user story depends on. No story phase may start until this completes.

- [X] T002 Add `TemplateImageDecoder` at `src/GameBot.Domain/Vision/TemplateImageDecoder.cs` with a `Decode(byte[] bytes)` that decodes with `ImreadModes.Unchanged`, returns the Mat when it is `CV_8U` with 1, 3, or 4 channels, and otherwise disposes it and falls back to `ImreadModes.Color`; XML-document that screenshots must keep using `ImreadModes.Color` and only templates use this (FR-001, FR-006)
- [X] T003 [P] Add init-only `Masked` (bool) and `RetainedPixelCount` (int) members to `TemplateMatchResult` in `src/GameBot.Domain/Vision/ITemplateMatcher.cs`, keeping the positional parameters `(Matches, LimitsHit)` unchanged so no existing construction site or test double breaks
- [X] T004 [P] Add unit tests for `TemplateImageDecoder` in `tests/unit/Vision/TemplateImageDecoderTests.cs` covering: an alpha PNG decodes to 4 channels, an opaque PNG decodes to 3 channels, a grayscale PNG decodes to 1 channel, and a 16-bit PNG falls back to the 3-channel `Color` decode

---

## Phase 3: User Story 1 — A non-rectangular target matches on its own pixels (P1)

**Goal**: a masked reference image is compared only on its retained pixels, so the same
badge matches over unlike backgrounds.

**Independent test**: the fixture badge scores >= 0.85 masked against a background it was
not cropped from, where the unmasked equivalent scores far below the gate, and the control
frame stays below the gate.

### Tests (write first — these must fail before T008–T010 land)

- [X] T005 [P] [US1] Add `tests/unit/Vision/TemplateMaskTests.cs` asserting the retained rule: alpha 128 and above is retained, alpha 127 and below is not; `TryCreate` returns false for a 3-channel Mat, false for a 4-channel Mat whose alpha is uniformly opaque, true for a genuinely mixed alpha; and that `RetainedCount` equals the counted opaque pixels
- [X] T006 [P] [US1] Add the regression case to `tests/unit/TemplateMatcherTests.cs`: the fixture badge over the foreign background scores below 0.85 with the opaque rectangular template and at or above 0.85 with the masked template, and the no-badge control frame stays below 0.85 with the masked template (FR-002, SC-001, SC-002)
- [X] T007 [P] [US1] Add a background-independence test to `tests/unit/TemplateMatcherTests.cs`: repainting the frame pixels that fall under the template's transparent region, leaving the retained region untouched, leaves the reported score unchanged to within 1e-9 (FR-002)
- [X] T007a [P] [US1] Add a uniform-retained-region test to `tests/unit/TemplateMatcherTests.cs`: a masked template whose retained pixels are all the same shade yields no match rather than an error or an arbitrary score (FR-009)
- [X] T007b [P] [US1] Assert in the T006 masked-match test that the returned bounding box is the **full template rectangle** — same width and height as the template — and not the bounding box of the retained pixels (FR-010)
- [X] T007c [P] [US1] Add a score-scale equivalence test to `tests/unit/TemplateMatcherTests.cs`: a masked template and an unmasked template that cover the identical retained region produce the same score, confirming a masked score sits on the same 0..1 scale so an existing threshold keeps its meaning (FR-003a)

### Implementation

- [X] T008 [US1] Add `TemplateMask` at `src/GameBot.Domain/Vision/TemplateMask.cs` with `TryCreate(Mat template, out Mat mask, out int retainedCount)` that succeeds only for an 8-bit 4-channel Mat whose alpha is not uniformly at or above 128, splitting the alpha channel and thresholding at 128 into a `CV_8UC1` 0/255 mask
- [X] T009 [US1] Add `MaskedTemplateMatch` at `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs` computing the masked ZNCC score map per research.md R-003: precompute the scalars `sum(m*T)` and `sum(m*T^2)`; run three `TemplateMatchModes.CCorr` correlations for `sum(m*T*I)`, `sum(m*I)`, and `sum(m*I^2)` on `CV_32F` inputs; combine per offset in `double`; return an empty map when the retained template variance is zero (FR-009) and score zero at any offset whose masked image variance is zero
- [X] T010 [US1] Branch `MatchAllAsync` in `src/GameBot.Domain/Vision/TemplateMatcher.cs`: when `TemplateMask.TryCreate` succeeds use the masked score map, otherwise leave the existing `Cv2.MatchTemplate(..., CCoeffNormed)` call byte-for-byte as it is; keep candidate collection, sorting, NMS, and `limitsHit` shared, and populate `Masked` and `RetainedPixelCount` on the result
- [X] T011 [US1] Switch the template decode in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs:201` (`POST /api/images/detect`) to `TemplateImageDecoder`, leaving the screenshot decode on `ImreadModes.Color`

**Checkpoint**: a masked image matches across backgrounds through `/api/images/detect`.

---

## Phase 4: User Story 2 — Existing reference images behave exactly as before (P1)

**Goal**: zero score drift for every image already in use, so no live threshold needs
recalibration.

**Independent test**: scores for opaque templates are bit-identical before and after.

### Tests

- [X] T012 [P] [US2] Add score-identity tests to `tests/unit/TemplateMatcherTests.cs`: for a 3-channel template and for a 4-channel all-opaque template, the score equals a directly-computed `Cv2.MatchTemplate(..., CCoeffNormed)` reference value exactly (not approximately), and `Masked` is false with `RetainedPixelCount` zero (FR-004, FR-005, SC-003)
- [X] T013 [P] [US2] Add a test asserting an all-opaque 4-channel template and the same image with its alpha channel dropped produce identical scores (FR-005)

### Implementation

- [X] T014 [US2] Document the zero-drift mechanism in `src/GameBot.Domain/Vision/TemplateMask.cs` and `src/GameBot.Domain/Vision/TemplateMatcher.cs`: an XML comment on `TryCreate` stating that the all-opaque rejection (implemented in T008) is what keeps every existing image on the untouched `CCoeffNormed` path, and a comment at the `TemplateMatcher` branch warning that routing an unmasked template through the masked formulation would be algebraically equal but not bit-identical, and would silently decalibrate live thresholds
- [X] T015 [US2] Run the existing detection suites (`tests/unit/TemplateMatcherTests.cs`, `tests/integration/ImageDetections*.cs`, `tests/contract/Images/DetectImageTests.cs`) and confirm no expected-score assertion changed; record the outcome in the task notes

**Checkpoint**: the feature is safe to deploy against live queues.

---

## Phase 5: User Story 3 — The mask survives upload and storage (P1)

**Goal**: an uploaded mask is never flattened, and a degenerate mask is refused where the
operator can act on it.

**Independent test**: upload an alpha PNG, retrieve it, confirm the transparency is intact;
upload a 3-opaque-pixel mask and get a 400.

### Tests

- [X] T016 [P] [US3] Add `tests/contract/Images/UploadImageMaskValidationTests.cs`: a fully transparent PNG is rejected 400 `invalid_image`, a mask retaining fewer than 16 pixels is rejected 400 with the retained count in the hint, a mask retaining 16 or more is accepted 201, and a PNG with no alpha is accepted unchanged (FR-008, contracts/upload-validation.md)
- [X] T017 [P] [US3] Add an alpha round-trip test to `tests/integration/ImageStorePersistenceTests.cs`: an uploaded alpha PNG retrieved via `GET /api/images/{id}` still carries transparency in exactly the same pixels, and `ReferenceImageStore.TryGet` returns a Bitmap whose alpha is preserved (FR-006, SC-004)

### Implementation

- [X] T018 [US3] Add the degenerate-mask check to `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs`: for uploaded bytes that decode to a 4-channel 8-bit image, count pixels with alpha at or above 128 and reject below 16; images without an alpha channel are not inspected
- [X] T019 [US3] Wire that check into the upload and replace paths in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs` (`POST /api/images` and `PUT /api/images/{id}`), returning the existing `invalid_image` 400 envelope with a message and hint naming the retained count and the 16-pixel minimum

**Checkpoint**: masks survive authoring end to end and degenerate ones fail loudly.

---

## Phase 6: User Story 4 — Sequences gated on masked images benefit with no re-authoring (P2)

**Goal**: masking is a property of the image, so every detection path honours it with no
sequence edit.

**Independent test**: an unmodified wait-for-image step resolves a target its unmasked
template missed.

### Tests

- [X] T020 [P] [US4] Add `tests/integration/MaskedDetectionIntegrationTests.cs` asserting an unmodified wait-for-image step and an unmodified tap-on-image step both resolve the fixture badge when the reference image is masked and fail when it is not, with no change to the sequence definition (FR-007, SC-005)
- [X] T021 [P] [US4] Add a `/api/images/detect-all` case to `tests/integration/ImageDetectionsEndpointTests.cs` confirming the sweep honours the mask and that its response shape is unchanged (no `masked` field)

### Implementation

- [X] T022 [P] [US4] Switch the template decode at `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs:349` (`POST /api/images/detect-all`) to `TemplateImageDecoder`
- [X] T023 [P] [US4] Switch the template decode at `src/GameBot.Service/Services/ImageDetectionHelper.cs:34` to `TemplateImageDecoder`, covering both the wait-for-image step and the game-readiness probe
- [X] T024 [P] [US4] Switch the template decode at `src/GameBot.Service/Services/CommandExecutor.cs:651` to `TemplateImageDecoder` for the tap-on-image primitive
- [X] T025 [US4] Make `ImageMatchEvaluator` in `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs` honour the mask on the trigger path: restrict the `Cv2.MeanStdDev` constant-template check and the `Cv2.Absdiff` same-size fallback to the retained pixels, so a masked image is not silently compared whole; the 4-channel BGRA Mat already arrives from `BitmapConverter.ToMat`

**Checkpoint**: every consumer named in FR-007 honours the mask.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T026 [P] Add additive `masked` and `retainedPixelCount` fields to `DetectResponse` in `src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs` and populate them in `ImageDetectionsEndpoints.cs`, leaving every existing field untouched; the field counts the pixels **kept**, matching the domain member `RetainedPixelCount` name for name (FR-011, contracts/detect-response.md)
- [X] T027 [P] Add `masked` and `retainedPixelCount` as structured fields on the detection-result log event in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs` without repurposing any existing message
- [X] T028 [P] Add a contract assertion to `tests/contract/Images/DetectImageTests.cs` that the new response fields are present, that `masked` is false with `retainedPixelCount` zero for an opaque image, and that existing fields are unchanged (G-1, G-4)
- [X] T029 [P] Document the new response fields in `src/GameBot.Service/Swagger/SwaggerConfig.cs` for the `/api/images/detect` response example
- [X] T030 Add a masked-path case to `tests/unit/Performance/TemplateMatcherBench.cs` asserting masked detection of a 42x52 template over a 1080x1920 frame stays within the 500 ms `Service:Detections:TimeoutMs` budget — that hard bound is the assertion; report the masked-to-unmasked ratio as an informational number rather than asserting on it, so the test does not fail on shared-runner timing noise (FR-012, SC-006)
- [X] T031 Update `docs/architecture.md` with reference-image masking — the detection behaviour, the additive response fields, and the upload rejection — and refresh its "Last reviewed" date (constitution V, NON-NEGOTIABLE)
- [X] T032 Set the `**Status**:` line in `specs/089-reference-image-alpha-mask/spec.md` to Implemented and add the matching row to `specs/STATUS.md` (constitution V)
- [X] T033 Run the full gate — `dotnet build C:\src\GameBot\GameBot.sln` then `dotnet test C:\src\GameBot\GameBot.sln` — and confirm green before the implementation commit; also confirm the new `Vision` files meet the constitution II baseline of >=80% line and >=70% branch coverage, adding cases if they fall short (constitution: red build or test is a hard stop)

---

## Dependencies

```text
Phase 1 (T001 fixtures)
   └─> Phase 2 (T002 decoder, T003 result fields, T004 decoder tests)
          ├─> Phase 3 US1 (T005-T011, incl. T007a-T007c)   <- MVP
          │      └─> Phase 4 US2 (T012-T015)   depends on the T010 branch existing
          │      └─> Phase 6 US4 (T020-T025)   depends on T002 + T010
          └─> Phase 5 US3 (T016-T019)   independent of the matcher; needs only T001
Phase 7 polish (T026-T033) last; T033 gates the commit
```

**Story independence**: US3 (upload/storage) can be built and tested without the matcher
work. US1 is the MVP. US2 is a guarantee over US1's branch rather than new behaviour. US4
is reach — it needs US1's matcher and the Phase 2 decoder.

## Parallel Execution

- **Phase 2**: T003 and T004 run alongside T002 once the decoder's signature is fixed.
- **Phase 3**: T005, T006, T007, T007a, T007c are independent test cases (T007b is an
  assertion inside T006). T008 and T009 are separate new files and can proceed together;
  T010 needs both.
- **Phase 5**: T016 and T017 in parallel; T018 before T019.
- **Phase 6**: T022, T023, T024 touch three different files — fully parallel. T025 is
  separate work.
- **Phase 7**: T026–T029 are parallel; T030–T033 are sequential at the end.

## Outcomes

- **T015 (zero score drift)**: the full suite — 1803 tests across unit, integration, and contract —
  passes with no expected-score assertion changed. The unmasked path runs the identical OpenCV call
  it always did, so this is structural, not a tolerance.
- **T030 (performance)**: measured on a 42x52 masked template over a 1080x1920 frame —
  **unmasked 99 ms, masked 189 ms, ratio 1.91x**. Comfortably inside the 500 ms detection timeout,
  confirming the research estimate that three DFT-based correlations cost a small multiple of one.
- **Deviation from plan**: `ValidateMaskRetention` keys off `TemplateMask.TryCreate` rather than a
  separate pixel count. An all-opaque alpha channel is not a mask, so a small opaque image — a 1x1
  PNG, say — must not be refused for retaining "too few" pixels. Counting without that condition
  would have broken existing uploads. `TryCountRetained` was dropped as the dead code it became.

## Implementation Strategy

**MVP**: Phase 1 + Phase 2 + Phase 3 (US1) delivers the capability the issue asks for — a
masked image matching across instances through `/api/images/detect`.

**Ship order**: US1 (the capability) → US2 (the safety guarantee that makes it deployable)
→ US3 (authoring survives the round trip) → US4 (every path honours it) → polish.

US2 is not optional despite adding no behaviour: without its score-identity evidence the
change cannot be deployed against live queues whose thresholds are hand-calibrated.
