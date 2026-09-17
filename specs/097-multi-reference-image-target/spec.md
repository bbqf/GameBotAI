# Feature Specification: Alternate Reference Images for One Detection Target

**Feature Branch**: `097-multi-reference-image-target`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #192 (https://github.com/bbqf/GameBotAI/issues/192) — "B-004: no multi-image detection target and no lighting robustness, so one anchor needs four hand-OR'd crops". Closes #192.

## Background

A reference image cropped from stable game art (`pns-resource-cluster-anchor`) scores 0.962 cross-instance
in daylight but only 0.808–0.871 under the game's night lighting. The usual 0.85 gate falls inside that
spread, so the same image passes or fails depending on the in-game time of day. It caused a live
`PNS.CollectResources` run to fail at night with its cluster-not-found outcome.

The only way to cover both lightings today is to capture extra night-lit crops and wire each one into the
sequence as another hand-OR'd `Break` condition. That multiplies steps in every sequence that needs the
anchor and makes "which crops represent this art" a structural property of each sequence rather than a
property of the image. The same pattern recurred for ready-badge templates (#190).

The issue accepts either of two platform fixes: lighting-robust matching, or "match any of these N
reference images" as one detection target. This feature delivers the second (see Assumptions for why the
first does not fix the observed failure).

## Clarifications

### Session 2026-09-17

- Q: When several references match (possibly at different places), which matches are reported? → A: The
  union of every reference's candidates, ordered by score and de-duplicated by the existing overlap rule,
  capped by the existing result limit; single-point consumers take the top one. (Rationale: identical to
  today for one reference, and a multi-instance detect still finds every instance.)
- Q: How does a detect response identify the matched reference without breaking existing clients? → A: The
  existing per-match template field keeps the named image id; a new additive per-match field carries the
  id of the reference that produced it. (Rationale: purely additive, no client breaks.)
- Q: Where must the matched reference be observable outside the detect endpoint? → A: In the service logs
  for every detection that used alternates; sequence/command/trigger result shapes stay unchanged.
  (Rationale: diagnosable without widening execution-log contracts beyond the issue's scope.)
- Q: What happens when two writers set the same image's alternates concurrently? → A: Last write wins;
  each write replaces the whole list atomically. (Rationale: matches how image overwrite already behaves.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One image covers day and night renderings (Priority: P1)

A sequence author has a reference image captured in daylight and one or more crops of the same art
captured at night. They register the night crops as **alternates** of the daylight image. Every existing
detection that already names the daylight image — conditions (`If`/`Break`/loop exits), wait-for-image
steps, image-anchored taps, triggers, the readiness gate and the single-image detect endpoint — now counts
a match on the primary image **or any of its alternates** as a match, without the author editing a single
sequence step.

**Why this priority**: This is the defect in the issue — a live failure caused by lighting, and the
multiplied-steps workaround. It delivers the full value on its own.

**Independent Test**: Store a primary image and an alternate crop that only the night screenshot matches.
Detect against the night screenshot with the primary image id: before alternates are set, no match; after
the alternate is registered, a match is reported at the alternate's location, identifying the alternate as
the reference that matched.

**Acceptance Scenarios**:

1. **Given** image `A` (day crop) with alternate `A-night` registered, and a screen on which only `A-night`
   scores at or above the threshold, **When** any detection names `A`, **Then** the detection reports a
   match at `A-night`'s location, with `A-night`'s score.
2. **Given** image `A` with alternate `A-night`, and a screen on which both score above the threshold at
   the same place, **When** a detection names `A`, **Then** one match is reported there, from the
   higher-scoring reference; equal scores prefer the primary image, then alternates in registered order.
3. **Given** image `A` with alternates, and a screen on which none scores at or above the threshold,
   **When** a detection names `A`, **Then** the detection reports no match, exactly as a single image with
   no match does today.
4. **Given** a sequence `Break` condition naming `A` with an `absent` expectation, **When** only an
   alternate is visible, **Then** the condition treats the art as present (not absent).
5. **Given** a detect-endpoint response for a primary with alternates, **When** a match is reported,
   **Then** the response identifies which reference (primary or which alternate) produced it, so an author
   can tell which crop is doing the work; runtime detections log the same information.

---

### User Story 2 - Manage an image's alternates (Priority: P1)

An author can set, view and clear the list of alternates for a stored image through the API, and sees the
alternates when reading the image's metadata.

**Why this priority**: Story 1 cannot be used without a way to declare alternates.

**Independent Test**: Set alternates `[B, C]` on image `A`, read them back, replace with `[C]`, clear them;
each read reflects the last write, and invalid requests are refused with actionable errors.

**Acceptance Scenarios**:

1. **Given** stored images `A`, `B`, `C`, **When** the author sets `A`'s alternates to `[B, C]`, **Then**
   reading `A`'s alternates returns `[B, C]` in that order, and `A`'s metadata lists them.
2. **Given** `A` has alternates, **When** the author sets an empty list, **Then** `A` has no alternates and
   detects exactly as a plain single image.
3. **Given** a request naming an alternate that is not a stored image, `A` itself, a duplicate id, or more
   than the maximum number of alternates, **When** it is submitted, **Then** it is refused with a
   validation error naming the offending id or limit, and `A`'s existing alternates are unchanged.
4. **Given** an alternates request for an image id that is not stored, **When** it is submitted, **Then**
   it is refused as not found.

---

### User Story 3 - Alternates survive lifecycle operations (Priority: P2)

Alternate lists are durable: they survive a service restart, are included in backup/restore, and behave
predictably when an involved image is overwritten or deleted.

**Why this priority**: Without durability the fix silently disappears after a restart or a restore, and
the live failure returns at night.

**Independent Test**: Set alternates, restart the service (or reload the store), confirm they are still in
effect; back up and restore, confirm they return; delete an alternate image and confirm detection still
works with the remaining references.

**Acceptance Scenarios**:

1. **Given** `A` has alternates, **When** the service restarts, **Then** `A`'s alternates are unchanged and
   still used by detection.
2. **Given** `A` has alternates, **When** a backup is taken and restored, **Then** `A`'s alternates are
   restored with the images.
3. **Given** `A` has alternates `[B, C]` and `B` is deleted, **When** a detection names `A`, **Then** it
   uses `A` and `C`, does not fail, and the missing alternate is logged; reading `A`'s alternates still
   shows `B` flagged as missing so the author can clean it up.
4. **Given** `A` has alternates and `A` itself is deleted, **When** the deletion completes, **Then** `A`'s
   alternate list is removed with it (a later image re-uploaded as `A` starts with no alternates).
5. **Given** `A` has alternates, **When** `A`'s image content is overwritten, **Then** its alternates are
   kept.

---

### Edge Cases

- An alternate that is itself a primary with its own alternates: alternates are **not** expanded
  transitively; only the named image's direct alternates are used.
- An image used as an alternate is still a normal image: naming it directly detects only that image (plus
  its own direct alternates, if any).
- Alternates with different sizes or masks than the primary: each reference is matched on its own terms
  (its own size and transparency mask); a reference larger than the screen simply cannot match.
- A reference whose masked region carries no information yields no match for that reference only; the
  others are still evaluated.
- The "detect all images" endpoint keeps reporting every stored image individually by its own id; it does
  not merge alternates into their primary.
- A detection that is cancelled or times out partway through the references reports the same outcome it
  reports today for a single image.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let an author associate an ordered list of alternate reference images with a
  stored image (the primary), replace that list, read it back, and clear it.
- **FR-002**: The system MUST refuse an alternates list that names an image that is not stored, the primary
  itself, the same id twice, or more than 8 alternates, with a validation error that names the offending
  id or the limit, and MUST leave the existing list unchanged on refusal.
- **FR-003**: Wherever a detection names a single reference image today — sequence/command conditions,
  wait-for-image steps, image-anchored taps, image-match triggers, the game readiness gate, and the
  single-image detect endpoint — the system MUST evaluate the primary and each of its direct alternates and
  treat a match on any of them as a match on the named image.
- **FR-004**: Each reference MUST be scored against the same threshold the detection already uses; the
  system MUST NOT change any threshold, default, or the score a single reference produces.
- **FR-005**: When more than one reference matches, the reported matches MUST be the union of every
  reference's candidates, ordered by score (equal scores prefer the primary, then alternates in registered
  order, then the existing positional tie-break), de-duplicated by the existing overlap rule and capped by
  the existing result limit. Consumers that act on one point MUST use the top match.
- **FR-006**: The single-image detect endpoint MUST identify, per match, which reference produced it in a
  new additive field, while the existing template field keeps the named image id; every runtime detection
  that uses alternates MUST log the matched reference. Execution result shapes of sequences, commands and
  triggers MUST stay unchanged.
- **FR-015**: Setting an image's alternates MUST replace the whole list atomically; concurrent writes
  resolve as last write wins.
- **FR-007**: An image with no alternates MUST detect exactly as it does today (same matches, scores,
  locations, response shape apart from any purely additive field, and same performance characteristics).
- **FR-008**: A missing (deleted) alternate MUST be skipped at detection time without failing the detection
  and MUST be logged; reading the alternates list MUST flag it as missing.
- **FR-009**: Alternates MUST NOT be expanded transitively.
- **FR-010**: Alternate lists MUST persist across service restarts and MUST be included in backup and
  restore.
- **FR-011**: Deleting a primary image MUST delete its alternate list; overwriting a primary's content MUST
  keep it.
- **FR-012**: Image metadata MUST include the image's alternates.
- **FR-013**: The alternates API MUST be described in the published API schema with its inputs, outputs and
  error responses.
- **FR-014**: The "detect all images" endpoint MUST continue to report each stored image by its own id.

### Key Entities

- **Reference image (primary)**: an existing stored image, addressed by id, that detections name.
- **Alternate list**: an ordered set of other stored image ids belonging to one primary; each entry
  represents another rendering (e.g. night lighting, another building instance) of the same art.
- **Matched reference**: in a detection result, the id of the primary or alternate whose crop produced the
  reported match.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A single named image with a registered night alternate is detected on a night screenshot it
  previously failed on, with zero sequence steps added — reducing the resource-cluster anchor from four
  OR'd conditions to one.
- **SC-002**: For every image with no alternates, detection results on a fixed set of screenshots are
  identical to before the change (same matches, scores and locations).
- **SC-003**: Detection time for an image with N alternates grows no more than roughly linearly in the
  number of references (about N+1 single matches), and is unchanged for images with none.
- **SC-004**: 100% of invalid alternates requests are refused with an error naming the offending id or
  limit, leaving stored state unchanged.
- **SC-005**: Alternates set before a restart or a backup/restore are all present and effective afterwards.

## Assumptions

- **Why not lighting-robust matching**: detection already scores with a correlation that normalises each
  comparison window for global brightness and contrast, yet the observed night scores still drop to
  0.81–0.87 — the night rendering differs by more than a global luma/contrast shift. Adding another
  normalisation step would also alter the scores of every existing image, which the calibrated thresholds
  across live queues depend on and which the issue's non-goals rule out.
- Alternates are a property of the image, so declaring them once benefits every sequence, trigger and
  endpoint that names the image.
- The limit of 8 alternates is ample (the observed case needs 3) while bounding detection cost.
- Authoring alternates is API-only in this feature; a web UI editor is out of scope.

## Non-Goals

- Changing or migrating the PNS automation repository's sequences or templates.
- Changing confidence thresholds, defaults, or the scoring of existing detections.
- Lighting normalisation, multi-scale or rotation-invariant matching, or any other detection feature.
- Merging alternates into their primary in the "detect all images" endpoint.
