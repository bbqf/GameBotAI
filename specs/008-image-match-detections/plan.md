# Implementation Plan: Image Match Detections

**Branch**: `001-image-match-detections` | **Date**: 2025-12-02 | **Spec**: `/specs/001-image-match-detections/spec.md`
**Input**: Feature specification from `/specs/001-image-match-detections/spec.md`

**Note**: This plan will guide execution across design, implementation, and testing.

## Summary

Add an additive image detection capability that finds zero or more occurrences of a stored reference image within the current screenshot, returning normalized bounding boxes and confidences in [0,1]. This does not replace existing endpoints. We will integrate a self-contained computer vision library that bundles native assets (no external installs), apply normalized cross-correlation/template matching, and perform non-maximum suppression to remove overlapping duplicates. New endpoints will expose this functionality alongside the existing `/images` API.

## Technical Context

**Language/Version**: C# / .NET 9 (Domain + Service)  
**Primary Dependencies**: OpenCvSharp4 + `OpenCvSharp4.runtime.win` (bundled native libs; no external install)  
**Storage**: Reuse existing file-based reference images under `data/images`  
**Testing**: xUnit + FluentAssertions; integration via `WebApplicationFactory<Program>`; contract tests for API schema  
**Target Platform**: Windows  
**Project Type**: ASP.NET Core Minimal API  
**Performance Goals**: p95 detection < 400 ms at 1080p with 128×128 template; ≤ 10 results returned by default  
**Constraints**: No breaking changes; no external system dependencies; deterministic results for same inputs  
**Scale/Scope**: Single-node service; tens of detections per call; templates up to ~256×256 (initial)

## Constitution Check

Validate planned work against the GameBot Constitution:
- Code Quality: Keep detection logic encapsulated (e.g., `TemplateMatcher` service) with narrow DI surface; enable analyzers and CA rules; no secrets; logging with structured events.
- Testing: Unit tests for matcher and NMS; golden-image tests for correctness; integration tests for endpoint (thresholds, overlaps, limits); contract tests validate schema.
- UX Consistency: New endpoint naming, status codes, and error payloads align with existing conventions; normalized coordinates and confidence semantics documented.
- Performance: Budgets defined above; include a simple benchmark harness and timeouts; guard max results; pre-convert to grayscale where helpful.

Re-check at design freeze to ensure no regressions to existing contracts.

## Project Structure

### Documentation (this feature)

```text
specs/001-image-match-detections/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
   └── openapi-image-detections.yaml
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Vision/
│       ├── ITemplateMatcher.cs
│       ├── TemplateMatcher.cs          # OpenCV-based template matching + NMS
│       └── Nms.cs                      # Non-maximum suppression utility
├── GameBot.Service/
│   └── Endpoints/
│       ├── ImageDetectionsEndpoints.cs # New additive endpoints
│       └── ImageReferencesEndpoints.cs # Unchanged (existing)

tests/
├── unit/
│   ├── TemplateMatcherTests.cs         # NCC mapping, thresholding, multi-match
│   └── NmsTests.cs                     # Overlap suppression correctness
├── integration/
│   └── ImageDetectionsEndpointTests.cs # API behavior, limits, errors
└── contract/
   └── ImageDetectionsContractTests.cs # OpenAPI schema conformance
```

**Structure Decision**: Add a small `Vision` area under Domain for the matcher and utilities; expose via new Service endpoints; preserve existing endpoints and evaluators.

## Phased Plan

1. Phase 0 – Research
  - Decide library: OpenCvSharp4 with `runtime.win` for bundled native assets.
  - Validate packaging/bitness and licensing; ensure Windows Server compatibility.
  - Spike: run template matching (TM_CCOEFF_NORMED) and measure on sample images.

2. Phase 1 – Design
  - Define request/response contract and OpenAPI (`openapi-image-detections.yaml`).
  - Specify normalized bbox convention and confidence semantics.
  - Design NMS (IoU-based) with tunable overlap (default 0.3).
  - Define config: `MaxResults` default 10; timeout 500 ms; optional overrides.

3. Phase 2 – Implementation
  - Add `OpenCvSharp4` packages to Domain project; wire DI for `ITemplateMatcher`.
  - Implement grayscale conversion, NCC, thresholding, peak finding, and NMS.
  - Implement new endpoints and input validation using existing logging patterns.

4. Phase 3 – Testing
  - Unit: synthetic images for known matches; NMS correctness; bounds/edge cases.
  - Integration: endpoint happy path, empty result, invalid ID, limits, timeouts.
  - Contract: validate JSON schema against OpenAPI; ensure no breaks to existing endpoints.

5. Phase 4 – Performance & Hardening
  - Micro-benchmark typical sizes; verify budgets; optimize buffer reuse.
  - Add safeguards against oversized templates; clamp and fast-fail.
  - Observability: structured timings + counts at debug level.

6. Phase 5 – Docs & Release
  - Update README and specs quickstart; add examples.
  - Changelog entry; feature flag note if applicable.

---

## Phase 2 PR Scope (Foundational)

This PR will implement the foundational pieces for detections (no new endpoint yet):

- T006 Create `ITemplateMatcher` interface and config record types
- T007 Add `BoundingBox`/IoU helper
- T008 Implement `Nms` utility
- T009 OpenCV-based `TemplateMatcher` (grayscale + NCC + thresholding)
- T010 DI registration in `Program.cs` (no behavior changes to existing endpoints)
- T011 Domain exceptions
- T012 Detection timing helper

Notes:
- Stacked on Phase 1 PR; no breaking changes; no public API exposure yet.

---

## Phase 3 PR Scope (US1 – Endpoint + Tests)

This PR will implement the first user story: find all matches above threshold and return normalized bboxes and confidences.

- T013 DTOs for request/response
- T014 Input validation helpers
- T015 Endpoint `POST /images/detect`
- T016 Structured logging (start, result count, truncated, duration)
- T017 Unit tests: matcher multi-match & empty
- T018 Unit tests: NMS overlap suppression
- T019 Integration tests: happy path & empty responses
- T020 Contract test: schema conformance vs `openapi-image-detections.yaml`

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New dependency (OpenCvSharp4) | Enables robust, bundled CV without external install | Re-implementing CV is error-prone; ImageSharp lacks fast NCC and peak finding |

---

## Phase 4 PR Scope (US2 – Preserve Existing Endpoints)

Objective: Ensure the detections feature is purely additive and introduces no behavior or contract changes to existing endpoints and evaluators.

- T023 Regression tests: verify legacy `/images` endpoints behave exactly as before (invalid image → 400, missing → 404, overwrite flag on re-upload).
- T024 Evaluator invariance: No changes were made to `ImageMatchEvaluator` logic; detections use separate services (`ITemplateMatcher`, `/images/detect`). Documented here for traceability.
- T025 OpenAPI presence check: assert legacy paths (e.g., `/images`, `/images/{id}`, `/health`, `/api/ocr/coverage`) remain present alongside `/images/detect`.
- T026 Safeguard: `POST /images/detect` must not mutate stored image bytes. Integration test asserts on-disk SHA256 unchanged pre/post call.
- T027 Docs: CHANGELOG entry stating the new endpoint is additive with no breaking changes.

## Final Implementation Notes (Post Phases 1–6)

Architecture recap:
- Matching Strategy: OpenCV TM_CCOEFF_NORMED over grayscale screenshot and reference template; candidate points filtered by configured `Threshold` then transformed into bounding boxes.
- De-duplication: IoU-based NMS using configured `Overlap` (default 0.45 currently); results sorted by confidence descending and truncated to `MaxResults`.
- Normalization: Bounding boxes normalized to [0,1] relative to screenshot dimensions; confidences clamped to [0,1] prior to serialization.
- Cancellation & Timeouts: Linked CTS enforces `TimeoutMs` (default 500). `TemplateMatcher` polls `CancellationToken` between major OpenCV calls and early-exits when triggered, returning an empty set with `limitsHit=true` at the endpoint layer.
- Safeguards: Oversized templates (larger than screenshot or zero-area) short-circuit to avoid expensive work. Stress tests assert deterministic ordering and proper truncation when synthetic images produce high match counts.
- Metrics: Histogram for detection duration and counter for result counts recorded via `ImageDetectionsMetrics.Record`. Additional process-level memory metrics exposed at `/metrics/process` (working set, managed heap, configured budget) to observe resource usage.
- Determinism: Given identical screenshot + reference + settings, match ordering remains stable because confidence sorting precedes truncation.

Operational guidance:
- Tune `Service__Detections__Threshold` upward (>0.9) to reduce false positives in noisy UIs; lower it only when templates are small or partially occluded.
- Increase `Service__Detections__MaxResults` cautiously; each additional candidate requires IoU comparisons (O(n^2) worst-case pre-truncation) though typical counts remain low.
- If timeouts occur regularly, raise `Service__Detections__TimeoutMs` or reduce template size; persistent timeouts with small templates may indicate host CPU pressure.

Future considerations (not in scope):
- Multi-scale pyramid matching for scale-variant detections.
- GPU acceleration via OpenCL toggles (requires validating OpenCvSharp build capabilities on target hosts).
- Adaptive thresholding per-template (auto-calibration pass).

License & Third-Party Notice:
- OpenCvSharp4 and OpenCV native binaries are distributed under the BSD 3-Clause license. A consolidated notice has been added in `LICENSE_NOTICE.md`.

Release Documentation:
- README updated with request/response examples and parameter guidance.
- ENVIRONMENT.md now documents detection configuration keys (`Service__Detections__*`).
