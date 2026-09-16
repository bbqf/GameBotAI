# Feature Specification: Masked matching must not score featureless screen regions as perfect matches

**Feature Branch**: `090-fix-masked-low-variance`
**Created**: 2026-09-16
**Status**: Implemented
**Input**: User description: "Fix B-013: an alpha-masked reference image scores 1.0 against any near-uniform screen region. GitHub issue #196 — https://github.com/bbqf/GameBotAI/issues/196 — Closes #196. Regression in the masked-matching feature shipped by #190 / PR #195 (commit 0a2d5a5)."

## Context

Feature 089 (issue #190, PR #195) gave a reference image the ability to carry a transparency
mask, so that a template shaped like a circular badge is compared on its own pixels rather than
on the rectangle of scenery around it. That capability works: on ordinary screen content a
masked badge template scores 0.98–1.00 where the badge really is and 0.53–0.55 where it is not,
which is exactly the cross-instance problem #190 was filed to fix.

It fails on one class of screen content. When the part of the screen lying under the mask is
*nearly featureless* — every pixel almost the same shade, as in the dimmed backdrop the target
game draws behind every modal dialog — a masked reference image scores **1.0000**: a perfect
match, at a place where the thing being looked for is simply not present.

This is the classic degenerate case of a normalised similarity measure. The measure asks "how
well does the pattern of light and dark in the template line up with the pattern of light and
dark on the screen?", and normalises by how much pattern each side actually has. When the screen
side has almost none, that normalisation divides away almost everything, and what is left is
arithmetic noise rather than evidence. The honest answer there is "this region carries no
information about whether the target is present", not "perfect match".

Two facts establish that this is a defect in how the score is computed rather than a real match:

- Computing the same masked similarity measure independently, on the same image content, gives
  **0.5171**, not 1.0. The screen region under the mask has a shade standard deviation of
  **2.43** on a 0–255 scale.
- Two different masked reference images (`pns-collect-steel` and `pns-collect-steel-b`) both
  score exactly 1.0 on the same screen at the same location, across three separate captures.
  A genuine match is specific; this is not.

The equivalent behaviour on the unmasked path is already correct: the unmasked version of the
very same reference image scores 0.3209 on comparable content.

**Why this is urgent.** A detection score is what arms a tap. With this defect every masked
reference image also "matches" any dimmed modal at the top of the scale, so an automation step
can be told to tap a location on a screen it was never meant to act on. The specific reported
hazard: on the small rendering of the game's quit dialog, the screen-identity anchor a sequence
uses to confirm "I am on the city screen" still matches at 0.9999, so the sequence's own guard
passes and the following step dispatches a tap onto a dialog whose buttons are *Cancel* and
*Confirm-to-quit*. There is no workaround: the operator cannot make the screen less featureless,
because the screen is whatever the game draws. Masked reference images have been withdrawn from
production as a result, which leaves the original #190 defect unfixed in the field.

The repository has met the mirror image of this before — a featureless *template* scoring 1.0
against smooth screen regions — and fixed it from the authoring side by making the template
carry more detail. That remedy does not apply here, because it is the *screen*, not the
template, that is featureless.

## Clarifications

### Session 2026-09-16

- Q: When a retained screen region falls under the no-information rule, does the system report a
  similarity of zero, or omit that position from the results entirely? → A: Report zero; the
  caller's threshold filters it out.
  *Rationale*: this is already what a perfectly uniform region does today, so it keeps one rule for
  the whole degenerate family, leaves the response shape untouched (FR-013), and avoids inventing a
  second "absent" concept that every consumer would have to learn.
- Q: Is the cutoff of the no-information rule fixed and documented, or configurable per reference
  image or per call? → A: Fixed and documented; not configurable.
  *Rationale*: a configurable cutoff is a second threshold, in units no operator can observe, that
  would have to be calibrated per screen — it multiplies the calibration burden the masking feature
  was meant to reduce, and a wrong setting silently re-arms the very hazard this fixes.
- Q: When a candidate position is suppressed as carrying no information, is that visible to an
  operator diagnosing a detection that "should have matched"? → A: Yes — recorded in the existing
  detection diagnostics, not in the response body.
  *Rationale*: silent suppression turns this fix into the next hard-to-diagnose bug ("it used to
  match and now it does not"), but surfacing it in the response body would change the contract that
  FR-013 freezes. The existing diagnostic channel carries it at no contractual cost.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A masked reference image does not match a featureless screen region (Priority: P1)

An operator has a masked reference image for a badge that appears on the game's city screen. The
game is currently showing a modal dialog, so the city screen behind it is dimmed to a nearly
uniform wash and the badge is not present at all. The operator (or an automation step) asks
whether the reference image is on screen. The answer is no — the reported similarity is low,
comparable to what the same comparison produces when computed independently, and nowhere near
the top of the scale.

**Why this priority**: This is the defect. Until it holds, every masked reference image is a
false-positive generator on any dimmed screen, and a false positive here arms a tap on a dialog
that can quit the game. Nothing else in this feature matters if this does not hold.

**Independent Test**: Ask for a masked reference image against a screen whose content under the
mask is near-uniform, and confirm the reported similarity is low rather than 1.0. Fully testable
on its own with a synthetic screen of controlled uniformity, and it alone removes the hazard.

**Acceptance Scenarios**:

1. **Given** a masked reference image and a screen region whose shade standard deviation under
   the mask is about 2.4 out of 255, **When** detection runs, **Then** the reported similarity
   is at most 0.60 and agrees with an independently computed reference value, rather than 1.0.
2. **Given** the same masked reference image and a screen region that is perfectly uniform under
   the mask, **When** detection runs, **Then** no match is reported.
3. **Given** two different masked reference images and one dimmed-modal screen, **When**
   detection runs for both, **Then** neither reports a top-of-scale similarity at the same
   location.
4. **Given** a masked reference image and any screen content whatsoever, **When** detection
   runs, **Then** the reported similarity never exceeds the top of the declared 0–1 scale.
5. **Given** an automation step that waits for, or taps on, a masked reference image, **When**
   the screen shows a dimmed modal, **Then** the step reports the image as absent and dispatches
   no tap.

---

### User Story 2 - Masked matching keeps the accuracy it was introduced for (Priority: P2)

An operator's masked badge reference images continue to find real badges across game instances,
which is the capability feature 089 delivered and the reason masking exists. Fixing the
featureless-region defect does not cost any of that accuracy.

**Why this priority**: The fix is worthless if it buys correctness on modal screens by breaking
detection on real ones. #190 is still open in the field precisely because masking had to be
withdrawn; the operator needs to be able to put it back.

**Independent Test**: Re-run the established masked-detection cases — real badges present, and
ordinary screens with no badge — and confirm the similarity values are the ones already
recorded, not merely "above threshold".

**Acceptance Scenarios**:

1. **Given** a masked badge reference image and a screen where that badge is genuinely present,
   **When** detection runs, **Then** the reported similarity is at least 0.93, as before.
2. **Given** the same reference image and an ordinary detailed screen with no badge, **When**
   detection runs, **Then** the reported similarity stays in the 0.50–0.60 band, as before.
3. **Given** any reference image and screen content with ordinary detail, **When** detection
   runs masked, **Then** the reported similarity agrees with an independently computed reference
   value for the same masked comparison, within a stated tolerance.
4. **Given** the established set of masked-detection cases, **When** the suite runs, **Then**
   every recorded expected similarity still holds with no expectation loosened to accommodate
   the fix.

---

### User Story 3 - Every calibrated threshold in the field keeps its meaning (Priority: P2)

An operator has dozens of live automation steps whose thresholds were hand-calibrated against
unmasked reference images over months. Those steps are untouched by this change: an unmasked
reference image scores exactly what it scored before, to the last digit.

**Why this priority**: A change to shared similarity arithmetic is the kind of change that
silently shifts every score in the system. The zero-drift guarantee for unmasked images is what
makes this fix safe to deploy without recalibrating the field, and it needs to be stated and
checked, not assumed.

**Independent Test**: Run the unmasked detection cases and confirm the similarity values are
unchanged from the current build.

**Acceptance Scenarios**:

1. **Given** a reference image that carries no transparency, **When** detection runs, **Then**
   the reported similarity is identical to the value the current build reports.
2. **Given** a reference image whose transparency marks every pixel as retained, **When**
   detection runs, **Then** it is treated as unmasked and the reported similarity is identical
   to the value the current build reports.

---

### User Story 4 - Screen-watching triggers are equally protected (Priority: P3)

An operator has a trigger that fires when a given image appears on screen. With a masked
reference image, that trigger does not fire on a dimmed modal any more than a detection call
reports a match there.

**Why this priority**: The trigger path performs its own comparison, including a same-size
shortcut and a featureless-template check, and so can carry the same degeneracy independently of
the detection path. Lower priority only because triggers are a narrower blast radius than the
detection calls that arm taps, not because the exposure is different in kind.

**Independent Test**: Evaluate an image trigger whose reference image is masked against a
near-featureless screen and confirm it does not fire.

**Acceptance Scenarios**:

1. **Given** an image trigger on a masked reference image, **When** the screen under the mask is
   near-featureless, **Then** the trigger does not fire.
2. **Given** an image trigger on a masked reference image and a screen the same size as the
   reference image, **When** the screen under the mask is near-featureless, **Then** the trigger
   does not fire.

---

### Edge Cases

- **Perfectly uniform screen region under the mask.** No information at all; reported as no
  match. This is the existing documented behaviour (089 FR-009) and must survive the fix.
- **Screen region just above the no-information cutoff.** The reported similarity must move
  smoothly as the screen gains detail — it must not jump from "no match" to the top of the scale
  across a hair's-breadth change in the screen.
- **A genuine target that is itself very low in contrast.** A region too featureless to
  distinguish is reported as no match even if the operator believes the target is there. This is
  the intended trade-off: the measure genuinely has no evidence to offer, and a false absence
  costs a retry while a false presence arms a tap.
- **Featureless reference image.** A reference image with no variation under its own mask has an
  undefined similarity and reports no match; it must not become matchable as a side effect of
  this fix. The same holds for one with almost no variation (FR-018) — that is this defect
  mirrored onto the reference image, and the repository has met it before as the
  `pns-never-matches` sentinel.
- **Reference image whose retained region is very small.** Fewer retained pixels means a noisier
  estimate of how much detail the screen carries; the no-information judgement must stay sound at
  the smallest retained-pixel count the system accepts.
- **Very large reference image.** The no-information judgement must not become unreliable as the
  retained-pixel count grows and the accumulated quantities get large.
- **Similarity arithmetic overshooting the scale.** Rounding can push a computed similarity
  fractionally above the top of the 0–1 scale; the reported value must stay inside the scale so
  that "1.0" keeps meaning "perfect match".
- **Detection deadline.** Detection that overruns its deadline is reported as an absence, so any
  extra work the fix costs must leave a comfortable margin — a fix that turns matches into
  timeouts has not fixed anything.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Masked detection MUST NOT report a top-of-scale similarity for a screen region
  whose retained content is near-featureless. For the reported case — a retained screen region
  with a shade standard deviation of about 2.4 out of 255 — the reported similarity MUST be at
  most 0.60.
- **FR-002**: A masked similarity MUST agree, within a stated tolerance, with an independently
  computed value of the same masked comparison, for screen content at every level of detail
  including near-featureless content. The tolerance MUST be stated in the design and checked.
- **FR-003**: A reported similarity MUST lie within the declared 0–1 scale; it MUST NOT exceed
  the top of the scale under any screen content.
- **FR-004**: When the retained region of the screen has no variation in shade, the system MUST
  report no match, rather than an error or an arbitrary similarity. (Carried forward unchanged
  from 089 FR-009.)
- **FR-005**: The system MUST define a single, documented rule for when a retained screen region
  carries too little detail for the similarity to mean anything, and MUST report no match in that
  case. The rule MUST be expressed in terms of the screen content itself, so that it holds
  independently of the reference image's size or its number of retained pixels.
- **FR-006**: The similarity reported for screen content just above that rule's cutoff MUST be
  continuous with the values reported for more detailed content — no discontinuity from "no
  match" to a high similarity.
- **FR-007**: Masked detection MUST preserve the accuracy feature 089 delivered: at least 0.93
  where the target is genuinely present, and within the established 0.50–0.60 band on ordinary
  detailed screens where it is not.
- **FR-008**: An unmasked reference image — one carrying no transparency, or one whose
  transparency retains every pixel — MUST report a similarity identical to the one the current
  build reports. This change MUST introduce no drift on the unmasked path.
- **FR-009**: The similarity measure MUST remain the same measure. The masked similarity stays a
  restriction of the existing measure to the retained pixels, so that a threshold calibrated on
  one is meaningful on the other.
- **FR-010**: Every consumer of image matching MUST inherit the corrected behaviour without any
  change to how it asks — single detection, the whole-library sweep, waiting for an image,
  tapping on an image, the readiness probe, and image triggers alike.
- **FR-011**: Screen-watching image triggers MUST report absence on a near-featureless screen
  region, including on the same-size-screen shortcut path and the featureless-reference-image
  check that path performs.
- **FR-012**: Masked detection MUST complete within the existing detection deadline with a
  comfortable margin, so that a detection is never reported as an absence merely because the
  corrected arithmetic took longer.
- **FR-013**: No interface, request shape, response shape, or stored automation definition may
  change. The corrected behaviour MUST be derived from the reference image itself, exactly as the
  masking behaviour already is.
- **FR-014**: The change MUST carry a check that reproduces the reported failure — a masked
  reference image against a screen region of controlled, near-featureless detail — and that check
  MUST fail against the current build and pass after the fix.
- **FR-015**: A candidate position suppressed by the no-information rule MUST report a similarity
  of zero and remain subject to the caller's ordinary threshold. It MUST NOT be removed from
  consideration by a separate mechanism, and no new "absent" concept may be introduced for it.
- **FR-016**: The cutoff of the no-information rule MUST be a single fixed, documented value. It
  MUST NOT be configurable per reference image, per call, or by deployment configuration.
- **FR-017**: When the no-information rule suppresses a candidate position, the system MUST record
  that fact through the existing detection diagnostics, so an operator investigating a detection
  that "should have matched" can see that it was suppressed and why. This MUST NOT appear in the
  response body (see FR-013).
- **FR-018**: The no-information rule MUST apply to the reference image's retained region as well as
  to the screen's. A reference image whose retained region carries less detail than the rule's
  cutoff MUST report no match, for the whole screen rather than per position. This extends FR-004,
  which covers only a reference image with *no* variation at all: a reference image with almost none
  is the same degeneracy on the other side of the comparison, and the same single cutoff (FR-016)
  governs both.

### Key Entities

- **Reference image**: A stored image an operator compares against the screen. It may carry
  transparency marking which of its pixels count.
- **Retained region**: The pixels of a reference image that count toward the comparison, and by
  extension the correspondingly shaped patch of screen underneath a candidate position.
- **Similarity**: The 0–1 value a comparison reports at a candidate position, against which an
  operator's threshold is applied. Its scale is shared between masked and unmasked comparisons.
- **Screen detail**: How much the shades within a retained screen region vary. Near-zero detail
  is the condition under which a similarity carries no information.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A masked reference image checked against a near-featureless screen region (shade
  standard deviation about 2.4 of 255) reports a similarity of at most 0.60, down from 1.0000
  today, and within 0.01 of an independently computed value for the same comparison.
- **SC-002**: Across every verification case, no masked similarity exceeds 1.0.
- **SC-003**: Masked similarities on real content are unchanged: at least 0.93 where the target
  is present, 0.50–0.60 on detailed screens where it is not.
- **SC-004**: Unmasked similarities are identical to the current build's values on every
  existing case — zero drift, measured rather than assumed.
- **SC-005**: Detection of a 42x52 reference image over a 1080x1920 screen completes in under
  300ms, comfortably inside the 500ms deadline; the masked path costs no more than 2.5x the
  unmasked path (today: 189ms versus 99ms, 1.91x).
- **SC-006**: An image trigger on a masked reference image does not fire on a near-featureless
  screen, on both the general and same-size evaluation paths.
- **SC-007**: The whole existing test suite passes, with no recorded expected similarity
  loosened or removed to accommodate the change.
- **SC-008**: A check reproducing the reported failure exists, demonstrably fails against the
  pre-fix build, and passes after the fix.
- **SC-009**: An operator investigating a masked detection that reported no match can determine
  from the existing diagnostics whether the no-information rule suppressed it, without attaching a
  debugger or rebuilding.

## Assumptions

- **A-001**: `docs/api-bugs.md` (row B-013) and `docs/findings.md`, named in the issue, belong to
  the separate PNS automation repository and are **not** present in this repository. Updating them
  is outside this repository's scope. The documentation obligation here is this repository's own
  living documentation — `docs/architecture.md` and the spec `Status` lines — per the
  constitution's Living Documentation principle.
- **A-002**: The three live captures cited in the issue live in that same separate repository and
  are not available here. Verification therefore uses fixtures that reproduce the reported
  statistics — a 42x52 reference image with 895 retained pixels of 2184, over screen content of
  controlled shade standard deviation around 2.4 — rather than the original captures. The
  reported values (1.0000 observed, 0.5171 expected) are the calibration target.
- **A-003**: *(resolved in clarification — now FR-015 and FR-016, no longer an assumption.)* The
  remaining assumption is only that the cutoff's value can be chosen from measurement during
  design: far below the detail of any real game screen, and far above the arithmetic noise floor.
  Should measurement show no such gap exists, the design must say so rather than pick a value
  inside the overlap.
- **A-004**: The masked path's accuracy figures quoted as "unchanged" (0.93+, 0.50–0.60) are the
  ones recorded by feature 089's verification; they are the baseline this change must hold.
  **Verified with one documented deviation**: the 0.93+ figure holds on the synthetic fixtures, but
  the 0.50–0.60 no-target band does not — the fixture scores ~0.11, because its backdrop and badge
  are derived from deliberately unrelated hashes so nothing can correlate by accident, whereas a
  real city screen shares structure with the badge drawn on it. The fixture is therefore *stricter*
  than the field, not looser, and the test asserts the band it can honestly support (< 0.60, with
  present and absent separated by more than 0.3) rather than a field number that does not apply to
  it.
- **A-005**: "Identical" for unmasked similarity means the unmasked comparison is not reached by
  this change at all — the guarantee is structural, not a numerical tolerance.

## Terminology

This spec says **similarity** throughout for the 0–1 value a comparison reports. The design
documents, the code and the API say **score** (in the matcher) and **confidence** (on the wire) for
the same quantity. They are one concept under three names; the names are pre-existing and are not
renamed by this change.

## Out of Scope

- Changing the similarity measure itself. Switching to a different correlation or difference
  measure would shift every score in the system and invalidate every calibrated threshold.
- Any change to the public interface, request or response shapes, or the stored automation
  definition schema.
- Any change to the unmasked comparison path.
- Solving this at authoring time — new upload-time validation as the primary remedy, or guidance
  that operators make reference images carry more detail. The issue is explicit that the screen,
  not the reference image, is the degenerate side, and the operator cannot act on the screen.
- Revisiting feature 089's design. Masking is verified correct on detailed content and stays.
- Re-applying the withdrawn masked reference images in the PNS automation repository, or any
  other change to that repository's artifacts. That is the operator's call once this ships.
- Updating `docs/api-bugs.md` / `docs/findings.md`, which are not in this repository (see A-001).

## Dependencies

- Feature 089 (`specs/089-reference-image-alpha-mask/`), which introduced masked matching. This
  change corrects a defect in it; 089's requirements — particularly FR-009 on undefined
  similarity — remain in force and are extended here.
- The existing detection deadline behaviour, under which an overrunning detection is reported as
  an absence.
