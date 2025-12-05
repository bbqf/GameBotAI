# Feature Specification: Image Match Detections

**Feature Branch**: `001-image-match-detections`  
**Created**: 2025-12-02  
**Status**: Draft  
**Input**: User description: "Introducing improved image matching trigger. I need a new image matching functionality (don't replace, but extend the old endpoints), given a reference image and the certainty set by the trigger, the system should try to find the image, possibly multiple times, within current screenshot and return the list of positions along with certainties for these positions. Use an external library, for example OpenCV or something similar for this. Don't depend on externally installed binaries/libraries, bring everything into the application that it needs in runtime."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find all matches in current screenshot (Priority: P1)

As an automation author, I want to request all occurrences of a stored reference image in the current screenshot, filtered by a similarity threshold, so I can act on each detected position.

**Why this priority**: Core value of feature; enables multi-target actions in a single evaluation.

**Independent Test**: Upload a known reference image, request detections with a threshold, and verify the response contains all expected positions with confidences above the threshold.

**Acceptance Scenarios**:

1. Given a valid reference image ID and a similarity threshold, When requesting detections, Then the system returns a list of matches with positions and confidences ≥ threshold.
2. Given no matches above threshold, When requesting detections, Then the system returns an empty list and 200 OK.

---

### User Story 2 - Preserve existing endpoints and triggers (Priority: P2)

As a service integrator, I want existing image endpoints and trigger evaluation behavior to remain unchanged, while new detection capability is exposed via additional endpoints, so existing clients are not broken.

**Why this priority**: Backward compatibility for existing automation flows.

**Independent Test**: Execute existing POST/GET/DELETE image endpoints and trigger evaluation flows; verify no behavior change and that new detection endpoint is additive.

**Acceptance Scenarios**:

1. Given existing endpoints, When used as before, Then behavior and payloads remain unchanged.
2. Given the new detection endpoint, When called, Then it does not alter stored images or trigger configs unless explicitly requested.

---

### User Story 3 - Bounded, normalized output (Priority: P3)

As a client, I want match positions expressed in a normalized coordinate system and confidences in [0,1], so my logic is resolution-independent and thresholds are portable.

**Why this priority**: Simplifies cross-device automation and testing.

**Independent Test**: Validate that positions are normalized to screenshot dimensions and confidences are clamped to [0,1].

**Acceptance Scenarios**:

1. Given a 1920x1080 screenshot, When matches are reported, Then x,y,width,height are in [0,1] relative to screenshot size.
2. Given any detector output, When serialized, Then confidence is in [0,1] and monotonic with similarity.

---

### Edge Cases

- Reference image larger than screenshot or region → return empty matches.
- Threshold = 1.0 (only perfect matches) and 0.0 (all detections) → handle without error.
- Overlapping detections → apply non-maximum suppression rules to avoid duplicate near-identical boxes.
- Screenshot unavailable or null → return 200 with empty list and an explanatory reason.
- Invalid or missing reference image ID → 404 not found with standard error shape (no changes to existing behaviors).
- Extremely large images or many detections → enforce max results and timeout safeguards; return partial results with a clear indicator when limits reached.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a new detection operation that returns zero or more matches of a stored reference image within the current screenshot without altering existing endpoints.
- **FR-002**: System MUST accept: reference image identifier, similarity threshold, and optional maxResults; SHOULD accept optional suppression overlap parameter for de-duplication.
- **FR-003**: System MUST return a list of matches, each with: normalized bounding box `{x,y,width,height}` relative to current screenshot and a `confidence` in [0,1].
- **FR-004**: System MUST bundle any detection library assets required at runtime; MUST NOT require users to install external system dependencies.
- **FR-005**: System MUST behave deterministically for a given screenshot and reference image with the same parameters (idempotent read operation).
- **FR-006**: System MUST gracefully handle no-screen or invalid reference image by returning an empty list or standardized not-found error (consistent with existing service errors).
- **FR-007**: System MUST enforce performance and safety guards: a configurable timeout and a maximum number of returned matches.
- **FR-008**: System MUST document the API contract (request/response fields, ranges, limits) alongside existing images contract without breaking changes.
- **FR-009**: System SHOULD provide consistent similarity semantics with existing triggers (confidence monotonic with similarity, range [0,1]).
- **FR-010**: System SHOULD support normalized coordinates regardless of screen resolution; internal pixel math MUST be accurate to avoid off-by-one at edges.

### Assumptions

- The detection operates on the latest full screenshot (no streaming/video pipeline required).
- Scale and rotation invariance are out of scope initially; detection assumes equal scale and upright orientation; clients can provide appropriately scaled references if needed.
- Existing image storage and validation (IDs, formats) continue to apply without change.

### Key Entities *(include if feature involves data)*

- **ReferenceImage**: Existing stored image identified by `referenceImageId`.
- **MatchRequest**: Parameters for a detection query: `referenceImageId`, `threshold` [0..1], `maxResults` (default reasonable), optional `overlap` for suppression.
- **MatchResult**: A single detection result: `bbox` `{x,y,width,height}` normalized to screenshot, `confidence` [0..1].
- **MatchResponse**: Collection of `MatchResult` plus optional `limitsHit` boolean if results were truncated or timeout occurred.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 1080p screenshots and 128x128 references, 95% of detection requests complete under 400 ms on target hardware with `maxResults=10`.
- **SC-002**: On a curated validation set, precision ≥ 0.95 at the configured threshold; recall ≥ 0.90 up to `maxResults`.
- **SC-003**: Existing image endpoints and trigger evaluations exhibit no behavior change (zero regressions in existing contract and integration tests).
- **SC-004**: No external runtime dependencies required by consumers; a clean machine can run detections without additional installs.
