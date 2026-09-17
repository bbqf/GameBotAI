# Feature Specification: Document detect vs detect-all coordinate units

**Feature Branch**: `101-detect-coordinate-units-docs`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #188 — https://github.com/bbqf/GameBotAI/issues/188 — Closes #188. "B-012: detect returns normalised coordinates and detect-all returns pixels, under the same field names."

## Background

The two image-detection routes report the same match box in different units under the same field names.
For one 540x960 capture and one template, with byte-identical confidence:

- `POST /api/images/detect-all` returns `"x":285, "y":463, "width":150, "height":28` — **pixels** in the capture frame.
- `POST /api/images/detect` returns `"x":0.52777, "y":0.48229, "width":0.27777, "height":0.02916` at the top
  level and the identical values again under `bbox` — **fractions of the capture frame** (285/540 = 0.52778,
  463/960 = 0.48229).

The reference image is also named differently: `imageId` (with `imageName`) on detect-all, `templateId` on detect.

Nothing in the published API description says which unit a caller is holding. Anything that reads `.x` from both
routes interchangeably — comparing a match against a stored crop rectangle, deriving a tap offset — is wrong by a
factor of the frame dimension, and a value like `0.53` looks like a believable coordinate, so the mistake is silent.

The issue accepts either unifying the units or stating the difference in the OpenAPI document. Changing either
route's wire format would silently break callers that already apply the documented workaround (multiply detect's
values by the capture width/height), so this feature **documents** the difference.

Verified against the current service behaviour (not only the issue's observation):

- detect: every coordinate is the pixel value divided by the capture frame's width (x, width) or height (y, height),
  clamped to the range 0..1; top-level `x`/`y`/`width`/`height` always equal `bbox.x`/`bbox.y`/`bbox.width`/`bbox.height`.
- detect-all: coordinates are whole-number pixels in the capture frame; `imageName` currently carries the same value
  as `imageId`.
- On both routes `x`/`y` is the top-left corner of the match box, measured from the top-left of the frame.

## Clarifications

### Session 2026-09-17

- Q: Should the fix unify the units or document them? → A: Document them. Rationale: the issue is labelled
  `documentation`, accepts documentation as a resolution, and a wire change would silently break existing callers.
- Q: Where must the unit statements live? → A: On the published response schemas themselves (per-field
  descriptions) and in each route's operation description. Rationale: schema field descriptions are what generated
  clients and schema viewers surface next to the value; the operation text is what a reader of the route sees first.
- Q: Is a documentation change verified by a test? → A: Yes, an automated contract test reads the published
  OpenAPI document and asserts the statements are present. Rationale: prevents silent loss of the descriptions in
  later refactors, matching the precedent of the sequence-nesting and queue-roster documentation features.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Know the unit of a detect coordinate from the API description (Priority: P1)

An automation author reads the published API description for `POST /api/images/detect` and learns, without
measuring a known landmark, that the returned `x`/`y`/`width`/`height` (top level and inside `bbox`) are fractions
0..1 of the capture frame's width/height, and how to turn them into pixels.

**Why this priority**: detect is the route whose unit is the surprise (normalised values under pixel-sounding names);
this is where the silent error originates.

**Independent Test**: Fetch the published OpenAPI document and inspect the detect response schema and operation text.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** a reader inspects the detect match schema's `x`, `y`, `width`,
   `height`, **Then** each field's description states that it is a fraction (0..1) of the capture frame's width (x,
   width) or height (y, height), not pixels.
2. **Given** the published OpenAPI document, **When** a reader inspects the `bbox` schema's fields, **Then** they carry
   the same normalised-unit statement, and the `bbox` property states it duplicates the top-level values.
3. **Given** the detect operation description, **When** read, **Then** it states the normalised unit, how to convert
   to pixels (multiply by the capture width/height), and that detect-all reports pixels instead.

---

### User Story 2 - Know the unit of a detect-all coordinate from the API description (Priority: P1)

The same author reads `POST /api/images/detect-all` and learns its `x`/`y`/`width`/`height` are pixels in the capture
frame, and that detect reports the same box as fractions.

**Why this priority**: the hazard is the pairing; documenting only one side leaves a reader of the other side unwarned.

**Independent Test**: Fetch the OpenAPI document and inspect the detect-all match schema and operation text.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** a reader inspects the detect-all match schema's `x`, `y`,
   `width`, `height`, **Then** each description states the value is in pixels of the capture frame.
2. **Given** the detect-all operation description, **When** read, **Then** it states the pixel unit and that detect
   returns the same box as fractions of the frame, so values from the two routes must not be compared directly.

---

### User Story 3 - Recognise the identifier naming difference (Priority: P3)

A reader of either route learns that detect's `templateId` and detect-all's `imageId` identify the same kind of
thing — a stored reference image id — so they can join results from the two routes.

**Why this priority**: a minor inconsistency named in the issue; it causes confusion, not wrong numbers.

**Independent Test**: Inspect the `templateId` and `imageId` field descriptions in the OpenAPI document.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** a reader inspects detect's `templateId`, **Then** its description
   says it is the reference image id, the same identifier detect-all reports as `imageId`.
2. **Given** the published OpenAPI document, **When** a reader inspects detect-all's `imageId`, **Then** its description
   says it is the reference image id, the same identifier detect reports as `templateId`.

### Edge Cases

- A match box that touches or runs past the frame edge: detect's values are clamped to 0..1; the description must
  not promise an unclamped value.
- The published example payloads for both routes must be consistent with the documented units (a detect example must
  not show pixel-sized numbers, a detect-all example must not show fractions).
- The documentation must not change any response value, field name, type, or which fields are present.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The published OpenAPI description of detect's match `x`, `y`, `width`, `height` MUST state that the value is
  a fraction 0..1 of the capture frame's width (x, width) or height (y, height), measured from the frame's top-left.
- **FR-002**: The published description of each field of detect's `bbox` MUST carry the same normalised-unit statement,
  and the `bbox` property MUST state that it repeats the match's top-level `x`/`y`/`width`/`height`.
- **FR-003**: The published description of detect-all's match `x`, `y`, `width`, `height` MUST state that the value is in
  pixels of the capture frame, measured from the frame's top-left.
- **FR-004**: The detect operation description MUST state that coordinates are normalised fractions of the frame, how to
  convert them to pixels, and that detect-all reports the same box in pixels.
- **FR-005**: The detect-all operation description MUST state that coordinates are pixels and that detect reports the same
  box as fractions of the frame.
- **FR-006**: The published description of detect's `templateId` and detect-all's `imageId` MUST each state that it is the
  reference image id and name the counterpart field on the other route.
- **FR-007**: The published example responses for both routes MUST use values consistent with each route's documented unit.
- **FR-008**: No response value, field name, field type, or field presence of either route may change.
- **FR-009**: An automated contract test MUST fail if any statement in FR-001 through FR-006 is missing from the published
  OpenAPI document.

### Key Entities

- **detect match**: one match from `POST /api/images/detect` — `templateId`, `matchedReferenceId`, `score`, `confidence`,
  `bbox`, `x`, `y`, `width`, `height`, `overlap`. Coordinates are normalised.
- **bbox (normalised rectangle)**: `x`, `y`, `width`, `height`, identical to the detect match's top-level values.
- **detect-all match**: one match from `POST /api/images/detect-all` — `imageId`, `imageName`, `x`, `y`, `width`,
  `height`, `confidence`. Coordinates are pixels.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the coordinate fields on both routes' match schemas (4 on detect's match, 4 on `bbox`, 4 on
  detect-all's match — 12 fields) carry a unit statement in the published API description.
- **SC-002**: A reader can determine which unit a coordinate from either route is in using only the published API
  description, with zero landmark measurements.
- **SC-003**: Zero changes to the JSON returned by either route for the same input (existing detection contract tests pass
  unchanged).
- **SC-004**: Removing any one of the required statements causes at least one automated test to fail.

## Assumptions

- The OpenAPI document served by the service is the authoritative API description callers read.
- The issue's observation matches current behaviour; this was confirmed from the service's own mapping of match boxes.
- Whether the difference predates the B-009 fix (#176, PR #185) is out of scope.

## Non-Goals

- Changing the wire format or units of either route.
- Renaming `templateId` / `imageId`, removing `imageName`, or removing `bbox`.
- Changing detection behaviour, thresholds, or matching logic.
- Documenting other image routes.
