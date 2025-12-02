# Implementation Tasks: Image Match Detections

Generated: 2025-12-02
Spec: `specs/001-image-match-detections/spec.md`
Plan: `specs/001-image-match-detections/plan.md`

## Phase 1: Setup

- [x] T001 Add OpenCvSharp4 + OpenCvSharp4.runtime.win packages to `src/GameBot.Domain/GameBot.Domain.csproj`
- [x] T002 Verify x64 build configuration and runtime load of OpenCV DLLs (log version on service start) in `src/GameBot.Service/Program.cs`
- [x] T003 Add configurable detection settings (DefaultThreshold, MaxResults, TimeoutMs, Overlap) to `data/config/config.json` and bind strongly in `src/GameBot.Service/Program.cs`
- [x] T004 [P] Add logging category constants for detection operations to `src/GameBot.Service/Logging/DetectionLogging.cs`
- [x] T005 Add new OpenAPI fragment inclusion for `openapi-image-detections.yaml` in `specs/master/contracts` aggregation (or reference in existing `specs/openapi.json` build script if present)

## Phase 2: Foundational

- [x] T006 Create `src/GameBot.Domain/Vision/ITemplateMatcher.cs` (MatchAll + config record types)
- [x] T007 Implement IoU helper and box struct in `src/GameBot.Domain/Vision/BoundingBox.cs`
- [x] T008 Implement Non-Maximum Suppression utility `src/GameBot.Domain/Vision/Nms.cs`
- [x] T009 [P] Implement grayscale + NCC wrapper using OpenCV in `src/GameBot.Domain/Vision/TemplateMatcher.cs`
- [x] T010 Wire `ITemplateMatcher` DI registration in `src/GameBot.Service/Program.cs`
- [x] T011 Add domain exceptions (InvalidReferenceImageException, DetectionTimeoutException) in `src/GameBot.Domain/Vision/DetectionErrors.cs`
- [x] T012 [P] Add performance timer helper for detections in `src/GameBot.Domain/Vision/DetectionTiming.cs`
- [x] T017 [US1] Unit tests: matcher multi-match & empty result in `tests/unit/TemplateMatcherTests.cs`
- [x] T018 [US1] Unit tests: NMS overlap suppression in `tests/unit/NmsTests.cs`

## Phase 3: User Story 1 (Find all matches P1)

Goal: Return zero or more matches with normalized bounding boxes and confidences ≥ threshold.
Independent Test: Given known template occurrences, API returns all matches above threshold; empty list when none.

- [x] T013 [US1] Implement request/response DTOs in `src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs`
- [x] T014 [US1] Implement validation (threshold/maxResults/overlap ranges) in `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs`
- [x] T015 [P] [US1] Implement endpoint `POST /images/detect` in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
- [x] T016 [US1] Add structured logger messages (start, result count, truncated, duration) in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs`
- [x] T017 [US1] Unit tests: matcher multi-match & empty result in `tests/unit/TemplateMatcherTests.cs`
- [x] T018 [US1] Unit tests: NMS overlap suppression in `tests/unit/NmsTests.cs`
- [x] T019 [US1] Integration tests: happy path & empty responses in `tests/integration/ImageDetectionsEndpointTests.cs`
- [x] T020 [US1] Contract test: schema conformance against `specs/001-image-match-detections/contracts/openapi-image-detections.yaml` in `tests/contract/ImageDetectionsContractTests.cs`
- [x] T021 [US1] Add example to quickstart `specs/001-image-match-detections/quickstart.md`
- [x] T022 [US1] Update README snippet for detection usage in `README.md`

## Phase 4: User Story 2 (Preserve existing endpoints P2)

Goal: Ensure existing image endpoints and trigger evaluation unchanged; detection is additive.
Independent Test: Existing endpoints behave identical pre/post feature merge.

- [x] T023 [US2] Backward compatibility regression tests additions (ensure unchanged responses) in `tests/integration/ImageReferencesBackwardCompatTests.cs`
- [x] T024 [US2] Confirm no modifications to existing evaluator logic (`ImageMatchEvaluator.cs`) except additive DI; document invariance in `specs/001-image-match-detections/plan.md`
- [x] T025 [US2] Add contract diff check between previous `specs/openapi.json` and updated version in `tests/contract/OpenApiBackwardCompatTests.cs`
- [x] T026 [P] [US2] Add safeguard: endpoint does not mutate stored images (assert hash unchanged) in `tests/integration/ImageDetectionsEndpointTests.cs`
- [x] T027 [US2] Update CHANGELOG with “Additive detection endpoint (no breaking changes)” in `CHANGELOG.md`

## Phase 5: User Story 3 (Normalized output P3)

Goal: Deliver normalized coordinates & confidence in [0,1]; resolution independent.
Independent Test: Coordinates scale with varying screenshot sizes; confidence clamped.

- [ ] T028 [US3] Implement normalization helper ensuring float precision in `src/GameBot.Domain/Vision/Normalization.cs`
- [ ] T029 [US3] Unit tests: coordinate normalization across multiple resolutions in `tests/unit/TemplateMatcherTests.cs`
- [ ] T030 [US3] Unit tests: confidence transform/clamp logic in `tests/unit/TemplateMatcherTests.cs`
- [ ] T031 [US3] Integration tests: normalized boxes values in `tests/integration/ImageDetectionsEndpointTests.cs`
- [ ] T032 [US3] Document normalization guarantees in `specs/001-image-match-detections/data-model.md`
- [ ] T033 [US3] Add explanation of confidence semantics to `specs/001-image-match-detections/quickstart.md`

## Phase 6: Performance & Hardening

- [ ] T034 Add micro-benchmark harness (template sizes, thresholds) in `tests/unit/Performance/TemplateMatcherBench.cs`
- [ ] T035 Implement timeout logic with cancellation in `src/GameBot.Domain/Vision/TemplateMatcher.cs`
- [ ] T036 [P] Add metrics counters (duration, result count) in `src/GameBot.Service/Endpoints/ImageDetectionsMetrics.cs`
- [ ] T037 Safeguard oversized template early-return in `src/GameBot.Domain/Vision/TemplateMatcher.cs`
- [ ] T038 Stress test: large screenshot & maxResults overflow handling in `tests/integration/ImageDetectionsEndpointTests.cs`
- [ ] T039 Verify memory usage stays < configured budget in test harness log output `tests/integration/ResourceLimitsTests.cs`

## Phase 7: Docs & Release

- [ ] T040 Update feature plan with final implementation notes in `specs/001-image-match-detections/plan.md`
- [ ] T041 Add release notes section in `CHANGELOG.md`
- [ ] T042 Add example detection JSON to README in `README.md`
- [ ] T043 Prepare feature flag documentation (if gating) in `ENVIRONMENT.md`
- [ ] T044 Ensure OpenCv license file compliance in `LICENSE_NOTICE.md`

## Phase 8: Polish & Cross-Cutting

- [ ] T045 Refactor duplicated grayscale code (if any) into shared util `src/GameBot.Domain/Vision/ImageProcessing.cs`
- [ ] T046 [P] Verify structured logging keys align with existing naming conventions in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs`
- [ ] T047 Add analyzer suppression (if needed) with justification in `src/GameBot.Domain/GlobalSuppressions.cs`
- [ ] T048 Final pass: ensure deterministic ordering of matches (confidence desc) in `src/GameBot.Domain/Vision/TemplateMatcher.cs`
- [ ] T049 Add integration test for deterministic ordering in `tests/integration/ImageDetectionsEndpointTests.cs`
- [ ] T050 Tag coverage report improvements for new code paths in `tools/coverage/README.md`

## Dependencies & Story Ordering

Order: US1 (core capability) → US2 (backward compatibility validation) → US3 (normalization guarantees). Performance & Hardening depends on foundational + US1 completion. Docs & Release after all stories. Polish after all functional phases.

## Parallel Execution Examples

- T009 and T012 can run parallel once interfaces (T006) exist.
- US1 endpoint (T015) can proceed in parallel with unit test creation (T017, T018) after matcher (T009) lands.
- Performance metrics (T036) can be added while normalization tasks (US3) proceed.

## MVP Scope

MVP includes: T006–T018 (foundational + US1 core detection, tests, endpoint, logging). Delivers multi-match detection API.

## Independent Test Criteria Per Story

- US1: All known template occurrences returned; empty list when none; response shape matches contract.
- US2: No diffs in existing endpoint responses; new endpoint additive only.
- US3: Normalized boxes and confidence values correct across resolutions; confidence always in [0,1].

## Task Count Summary

- Total Tasks: 50
- US1 Tasks: 9 (T013–T021)
- US2 Tasks: 5 (T023–T027)
- US3 Tasks: 6 (T028–T033)
- Setup: 5
- Foundational: 7
- Performance & Hardening: 6
- Docs & Release: 5
- Polish: 8

## Format Validation

All tasks follow required format: `- [ ] T### [P]? [USn]? Description with file path`.
