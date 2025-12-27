# Research: Image Match Detections

Date: 2025-12-02  
Feature: specs/001-image-match-detections/spec.md

## Options Considered

- OpenCvSharp4 (+ OpenCvSharp4.runtime.win)
  - Pros: Mature template matching (TM_CCOEFF_NORMED), fast; packages bundle native DLLs for Windows; good community.
  - Cons: Native interop; ensure x64 process; larger deployment size.

- Emgu CV
  - Pros: Comprehensive wrapper over OpenCV.
  - Cons: Licensing/packaging complexity; heavier; less straightforward bundling.

- SixLabors.ImageSharp only
  - Pros: Pure managed; simple packaging.
  - Cons: Lacks optimized NCC/peak detection; would require custom implementation; likely slower.

## Decision

Choose OpenCvSharp4 with `OpenCvSharp4.runtime.win` to satisfy "no external installs" while leveraging optimized OpenCV functions.

## Feasibility Spike

- Implement TM_CCOEFF_NORMED on sample 1080p screenshots with 128×128 templates.
- Extract response peaks above threshold; apply IoU-based NMS (0.3 default).
- Expect sub-400ms p95 on target hardware per success criteria.

## Risks & Mitigations

- Bitness mismatch: enforce x64 build; verify RID-specific assets.
- Deployment size: measure package impact; acceptable for service.
- Performance variance: add timeout and cap results; consider early stopping if needed.

## References

- OpenCV Template Matching: https://docs.opencv.org/4.x/d4/dc6/tutorial_py_template_matching.html
- OpenCvSharp GitHub: https://github.com/shimat/opencvsharp
