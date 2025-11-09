# Research: Triggered Profile Execution

## Overview
Resolve technical unknowns for OCR engine, image matching technique, and trigger persistence model supporting timely (≤2s) evaluation with minimal false positives.

---
## Item 1: OCR Engine Selection
- **Decision**: Use Tesseract OCR via a .NET wrapper.
- **Rationale**: Mature, widely adopted, configurable accuracy versus performance trade-offs, supports whitelist/region-based extraction required for confined screen regions.
- **Alternatives Considered**:
  - Windows OCR APIs: Simplified integration but less control over tuning and deployment portability.
  - Cloud OCR (e.g., Vision APIs): Higher latency, network dependency, potential cost and privacy concerns.
  - Custom lightweight OCR: Increased development/maintenance burden and likely lower accuracy initially.

## Item 2: Image Matching Technique
- **Decision**: Use template matching with normalized cross-correlation combined with downscaled grayscale preprocessing.
- **Rationale**: Deterministic, fast for small regions, threshold tuning (e.g., ≥0.85) aligns with spec; avoids complexity of feature-based methods for 2D UI elements.
- **Alternatives Considered**:
  - Perceptual hashing (pHash): Good for overall similarity but weaker for localized region matching and threshold granularity.
  - SIFT/ORB feature matching: Robust to scale/rotation but overkill for stable 2D UI; higher computational cost.
  - ML-based classification: Adds model training overhead and potential drift.

## Item 3: Trigger Persistence Model
- **Decision**: Persist triggers embedded within the profile JSON file under a `triggers` array.
- **Rationale**: Simplifies atomic updates (profile + triggers together); reduces file count and coordination complexity; aligns with existing file-based repository pattern.
- **Alternatives Considered**:
  - Separate triggers file per profile: More granular but introduces synchronization risk and additional lookups.
  - Central triggers index: Useful for cross-profile queries but premature given current scope (per-profile evaluation).
  - Database storage: Not justified until higher scale; increases operational complexity.

## Cross-Cutting Considerations
- **Performance**: Downscale screenshot (e.g., 50%) before matching/OCR to reduce CPU while maintaining detection reliability; trade-off monitored in tests.
- **False Positives Mitigation**: Require consecutive confirmations for text-not-found to avoid flicker; single pass sufficient for image-match above threshold.
- **Extensibility**: Designing evaluation service with interface `ITriggerEvaluator` allows future trigger types (e.g., pixel-color, network-event) without refactoring core loop.
- **Testing Strategy**: Synthetic images/fixtures for deterministic image and OCR tests; mock evaluator interfaces for unit tests; integration tests with sample screenshots.

## Summary
Selections prioritize deterministic, locally computable techniques with controllable thresholds and minimal external dependencies, enabling responsive evaluation loops and straightforward persistence.
