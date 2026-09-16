# Feature Specification: Reference Image Transparency Masks

**Feature Branch**: `089-reference-image-alpha-mask`
**Created**: 2026-09-16
**Status**: Draft
**Input**: GitHub issue #190 — "B-010: reference images cannot mask out background, so a circular badge template fails on a second instance" — https://github.com/bbqf/GameBotAI/issues/190

## Context

A reference image is always a rectangle, but many of the things an operator needs to
recognise on screen are not. The motivating case is a circular "resource ready" pin on
the game map: a 42x52 rectangular crop of it carries roughly a third dead background —
whatever map scenery happened to sit behind the pin in the source capture.

Matching scores that dead background as part of the target. The same pin over different
terrain therefore falls below its gate:

| template | score against a capture with the badge plainly on screen | gate |
|---|---|---|
| pns-collect-food | 0.5589 | 0.85 |
| pns-collect-wood | 0.4008 | 0.85 |
| pns-collect-steel | 0.5095 | 0.85 |
| pns-collect-gas | 0.5550 | 0.85 |
| pns-resource-cluster-anchor (rectangular target) | 0.9554 | 0.85 — passes |
| pns-city-anchor (rectangular target) | 1.0000 | 0.85 — passes |

The art itself is fine: cropped to the pin interior the same badge scores 0.986. But that
crop is unusable, because the bare glyph then scores 0.872 against an unrelated icon in
the top resource bar on a frame with no badge at all — a false positive above the gate.

So the authoring side has no good option: keep the pin border and carry dead background,
or drop it and collide with the HUD. The capability has to exist in the platform.

The cost of not having it is silent: a production queue collected nothing for four hours
while reporting `status: Running` with fresh heartbeats and every sequence succeeding,
because a no-match is indistinguishable from "nothing was ready".

## Clarifications

### Session 2026-09-16

Resolved autonomously during an unattended pipeline run. Each answer is the most
defensible option given the spec, the source issue, and the existing codebase.

- **Q: What transparency value separates a retained pixel from a masked-out one?**
  **A**: A pixel is retained when it is at least half opaque, and masked out below that.
  *Rationale*: a single unambiguous midpoint rule; a feathered edge pixel that is mostly
  transparent is mostly background, so excluding it keeps the contamination the feature
  exists to remove from creeping back in through soft edges.

- **Q: Must a masked score stay comparable to the thresholds tuned against unmasked scores?**
  **A**: Yes. The masked score is the same normalised-correlation measure on the same 0..1
  scale, restricted to the retained pixels.
  *Rationale*: operators reuse the 0.85 gate on a masked image without re-deriving it, and
  the acceptance criteria in SC-001/SC-002 are stated against that gate.

- **Q: How small may the retained region be before the image is refused?**
  **A**: Fewer than 16 retained pixels is refused at upload as degenerate; at or above that
  the image is accepted and its retained-pixel count is observable.
  *Rationale*: a handful of pixels correlates with almost anything, so accepting it would
  hand the operator a template that silently matches noise; 16 is small enough never to
  refuse a genuine target.

- **Q: Which detection paths must honour the mask?**
  **A**: All of them — single-image detection, whole-library sweep detection, the
  wait-for-image and tap-on-image sequence steps, the game-readiness probe, and image
  triggers.
  *Rationale*: FR-007 makes masking a property of the image; a path that ignored it would
  make an image's behaviour depend on the caller, which is exactly the inconsistency an
  operator cannot debug.

- **Q: How is "this detection used a mask" surfaced?**
  **A**: As an additive field on the detection response plus a structured log field. No
  existing field changes meaning or shape.
  *Rationale*: an operator comparing two scores needs to know which comparison they are
  looking at; additive-only keeps every existing consumer working.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A non-rectangular target matches on its own pixels (Priority: P1)

An operator authoring a reference image for a round badge erases the background around
the badge, leaving those pixels transparent, and uploads the image. From then on, every
detection of that reference image compares only the pixels the operator left opaque. The
scenery behind the badge no longer moves the score, so the same badge matches wherever it
appears.

**Why this priority**: This is the entire point of the feature. Without it the operator
has no way to express "this part of the crop is not the target", and non-rectangular
targets stay unmatchable across instances.

**Independent Test**: Upload one reference image whose background is transparent and one
identical image whose background is opaque scenery, run detection of each against the same
capture, and compare the scores. The masked one scores far higher on the target.

**Acceptance Scenarios**:

1. **Given** a reference image whose background pixels are transparent and whose subject
   is a circular badge, **When** detection runs against a capture where that badge is on
   screen over terrain unlike the terrain in the source crop, **Then** the reported score
   for the badge's location is at or above 0.85.
2. **Given** that same masked reference image, **When** detection runs against a control
   capture taken from the same camera position with no badge on screen, **Then** no match
   is reported at or above 0.85.
3. **Given** a reference image with transparent regions, **When** detection runs, **Then**
   the transparent pixels contribute nothing to the reported score — replacing the colour
   behind those transparent pixels with any other colour leaves the score unchanged.

---

### User Story 2 - Existing reference images behave exactly as before (Priority: P1)

Every reference image already in use is fully opaque. An operator who changes nothing sees
no change: the same scores, the same matches, the same thresholds still calibrated. The
live sequences that gate on those thresholds keep working untouched.

**Why this priority**: Equal in priority to Story 1. Detection thresholds across live
queues are hand-calibrated against today's scores; a score shift of even a few hundredths
would silently arm or disarm production automation. This story is what makes the feature
safe to deploy at all.

**Independent Test**: Run the existing detection test suite and a score-comparison check
over opaque reference images before and after the change; scores must be identical.

**Acceptance Scenarios**:

1. **Given** a reference image with no transparency information at all, **When** detection
   runs against any capture, **Then** the reported scores are identical to those produced
   before this feature existed.
2. **Given** a reference image that carries transparency information but every pixel is
   fully opaque, **When** detection runs, **Then** the reported scores are identical to
   those for the same image with no transparency information.

---

### User Story 3 - The mask survives upload and storage (Priority: P1)

An operator uploads an image with transparent regions and the platform keeps them. The
mask is not flattened against a background colour on the way in, on the way to disk, or on
the way back out when the image is downloaded for review.

**Why this priority**: Also P1 — a mask that is silently discarded at upload makes Story 1
unachievable, and does so invisibly: the operator sees a successful upload and a failing
match with no indication of why.

**Independent Test**: Upload an image with known transparent pixels, download it again, and
confirm the transparency is still present and unchanged.

**Acceptance Scenarios**:

1. **Given** an image with transparent regions, **When** it is uploaded as a reference
   image and then retrieved, **Then** the retrieved image still has transparency in exactly
   those regions.
2. **Given** an image with transparent regions, **When** it is uploaded and then used for
   detection, **Then** the detection honours the transparency rather than treating the
   transparent area as a solid colour.

---

### User Story 4 - Sequences gated on masked images benefit with no re-authoring (Priority: P2)

A sequence step that waits for, or taps on, a reference image starts benefiting from that
image's mask the moment the masked image replaces the unmasked one. No step needs a new
field, and no sequence needs to be edited or re-validated.

**Why this priority**: P2 because Stories 1-3 deliver the capability and Story 4 is about
its reach. But it is what turns the capability into a fix for the reported outage, since
the stalled collection ran from a sequence, not from a manual detection call.

**Independent Test**: Point an existing unmodified sequence step at a masked reference
image and confirm the step resolves the target that the unmasked image missed.

**Acceptance Scenarios**:

1. **Given** an unmodified sequence step that gates on a reference image, **When** that
   reference image is replaced by a masked version of itself, **Then** the step uses the
   mask without any change to the sequence definition.
2. **Given** any detection path that consumes reference images, **When** a masked image is
   used, **Then** that path honours the mask — masking is a property of the image, not of
   the caller.

---

### Edge Cases

- **Fully transparent reference image**: an image with no opaque pixels at all describes no
  target. It is rejected at upload with a clear reason rather than being stored as an image
  that matches everything or nothing unpredictably.
- **Partially transparent pixels**: a pixel at least half opaque is retained, anything less
  is masked out, so a soft anti-aliased edge produces a stable, reproducible score rather
  than one that depends on how the operator's editor happened to feather the selection.
- **Very small opaque area**: a mask leaving fewer than 16 retained pixels yields a score
  that correlates with almost anything, so the image is refused at upload rather than
  stored as a target that silently matches noise.
- **Uniform opaque region**: if every opaque pixel in the template is the same shade, the
  correlation is mathematically undefined. The system reports no match rather than a
  nonsensical score or an error.
- **Template larger than the capture**: unchanged from today — no match is reported.
- **Detection latency**: masked matching must not push detection past the existing
  detection timeout, or sequences that currently pass would start timing out.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support reference images that carry per-pixel transparency
  information, and MUST treat that transparency as a mask over the image's own pixels.
- **FR-002**: During detection, pixels classified as masked-out MUST contribute nothing to
  the reported score — neither to the similarity measure nor to the normalisation it is
  divided by. The score MUST depend only on the opaque pixels of the template and the
  capture pixels beneath them.
- **FR-003**: The system MUST classify each template pixel as retained when it is at least
  half opaque and masked-out below that, so that the same image always produces the same
  classification regardless of how its edges were feathered.
- **FR-003a**: A masked score MUST be the same normalised-correlation measure, on the same
  0..1 scale, as an unmasked score — restricted to the retained pixels. A threshold tuned
  against unmasked scores MUST keep its meaning when applied to a masked image.
- **FR-004**: A reference image with no transparency information MUST produce scores
  bit-identical to those the system produced before this feature existed.
- **FR-005**: A reference image whose every pixel is fully opaque MUST produce scores
  identical to the same image with no transparency information.
- **FR-006**: The system MUST preserve transparency through the reference-image upload,
  storage, and retrieval path; an uploaded image's transparency MUST NOT be flattened
  against a background colour at any stage.
- **FR-007**: Every detection path that consumes reference images MUST honour the mask:
  single-image detection, whole-library sweep detection, the sequence steps that wait for
  or tap on a reference image, the game-readiness probe, and image triggers. Masking MUST
  NOT require any change to the sequence schema or to existing sequence definitions.
- **FR-008**: The system MUST reject, at upload time and with an explanatory error, a
  reference image whose retained region is degenerate — no retained pixels at all, or fewer
  than 16 of them.
- **FR-009**: When the retained region of a template has no variation in shade, making the
  score undefined, the system MUST report no match rather than an error or an arbitrary
  score.
- **FR-010**: Masked detection MUST report the matched region's position and size on the
  same basis as unmasked detection — the full template rectangle, not the bounding box of
  the retained pixels — so that coordinates resolved from a match are unchanged in meaning.
- **FR-011**: The system MUST make it observable whether a given detection used a mask, and
  how many pixels that mask retained, so an operator diagnosing a score can tell a masked
  comparison from an unmasked one. This MUST be additive — no existing response field
  changes shape or meaning.
- **FR-012**: Detection of a masked reference image MUST complete within the same time
  budget that governs detection today.

### Key Entities

- **Reference image**: a stored image an operator registers under an id and matches against
  captures. Gains an optional per-pixel transparency channel. Its existing attributes — id,
  content type, size, timestamps — are unchanged.
- **Mask**: the derived per-pixel retained/masked-out classification of a reference image,
  computed from its transparency. Not separately stored or separately addressable; it is a
  property of the image.
- **Detection result**: unchanged in shape — a score and a rectangle per match. The score's
  meaning narrows to "similarity over the retained pixels".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A circular badge reference image whose background is made transparent scores
  at or above 0.85 against a capture from a different device instance on which the
  equivalent rectangular image scored 0.5095.
- **SC-002**: The same masked reference image produces no match at or above 0.85 against a
  control capture, taken from the same camera position, in which the badge is absent.
- **SC-003**: Across every existing opaque reference image, scores after the change are
  identical to scores before the change — zero drift, so no live threshold needs
  recalibration.
- **SC-004**: Transparency uploaded with a reference image is still present, pixel for
  pixel, when that image is retrieved.
- **SC-005**: A sequence step gated on a reference image resolves a target that the
  unmasked version of that image missed, with no edit to the sequence.
- **SC-006**: Detection of a masked reference image completes within the detection timeout
  already in force, on a template and capture of the sizes described above.

## Assumptions

- **A-001**: Operators author masks in an ordinary image editor and express them as image
  transparency. The platform does not need to provide a mask-drawing tool; the issue asks
  for the matching capability, not an authoring UI.
- **A-002**: The "circular badge" acceptance evidence in SC-001 and SC-002 is reproduced in
  this repository as a fixture-based test using captures and templates of equivalent
  character, since the referenced live captures and the `pns-collect-*` templates live in a
  separate automation repository.
- **A-003**: Transparency is carried by the image format already used for reference images;
  no new upload format or side-channel mask file is introduced.
- **A-004**: "Identical scores" in FR-004, FR-005, and SC-003 means the existing computation
  is left in place for unmasked images rather than replaced by a general one that happens to
  agree to within rounding.

## Out of Scope

- A per-template ignore-region list, and first-class "match any of these N templates" as a
  single detection target. The issue names both as weaker alternatives to masking.
- Re-authoring or re-cropping the `pns-collect-*` reference images. Those live in the
  separate PNS automation repository; this work delivers the platform capability only.
- Any change to queue-level failure policy, or to how a no-match is distinguished from
  "nothing was ready". The four-hour silent stall motivates this feature but its detection
  is not asked for here.
- Lowering detection thresholds anywhere.
- A mask-authoring or mask-preview user interface.
