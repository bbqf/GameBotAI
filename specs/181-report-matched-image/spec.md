# Feature Specification: Report which image of a list matched

**Feature Branch**: `181-report-matched-image`
**Created**: 2026-11-09
**Status**: Draft
**Input**: User description: "FR-044: Report which image of a list matched. Source: GitHub issue #380. After an any-of image check, the step result and a later condition or value can read the id of the image that matched."

## Clarifications

### Session 2026-11-09

- Q: Which checks give a matched image id? → A: The `WaitForImage` step and any step whose own top-level condition is an any-of image condition. Rationale: the issue says a condition also has no readable result.
- Q: What does a `matchedImage` condition do when the referenced step did not match? → A: It is false, with a reason in the run log. Rationale: a "no match" is a normal result, not an error.
- Q: What happens when the referenced step ran more than once? → A: The latest result is used. Rationale: this is the simplest rule and fits loops.
- Q: Is the matched image id a string? → A: Yes, the same id string as in the list. Rationale: it must compare to `imageId` and fit in text placeholders.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the matched image id in the step result (Priority: P1)

An author makes a sequence with a `WaitForImage` step that has a list of wanted images (feature 180). When the step ends with a match, the step result tells which image of the list matched, and with what confidence.

**Why this priority**: All other stories use this value. Without it, the sequence cannot know which image matched.

**Independent Test**: Run a `WaitForImage` step with a list of two images. Make the second image match. Read the step result. The result holds the id of the second image and the confidence.

**Acceptance Scenarios**:

1. **Given** a `WaitForImage` step with a list of images, **When** one image of the list matches, **Then** the step result holds `matchedImageId` with the id of that image and `confidence` with the match confidence.
2. **Given** a `WaitForImage` step with one image only, **When** the image matches, **Then** the step result holds `matchedImageId` with the id of that image.
3. **Given** a `WaitForImage` step, **When** no image matches before the time limit, **Then** the step result holds no `matchedImageId`.

---

### User Story 2 - Test the matched image id in a later condition (Priority: P1)

An author adds a condition after the image check. The condition tests if the matched image of a named earlier step is a given image. The shape is `matchedImage: { stepRef, imageId }`.

**Why this priority**: The author needs a branch in the sequence for each matched image (for example, one failure name for each quest).

**Independent Test**: Make a sequence with a `WaitForImage` step and a later condition `matchedImage: { stepRef, imageId }`. Run it twice, with a different image that matches each time. The condition is true only when the matched image equals `imageId`.

**Acceptance Scenarios**:

1. **Given** an earlier step that matched image A, **When** a condition tests `matchedImage` with `imageId` A for that step, **Then** the condition is true.
2. **Given** an earlier step that matched image A, **When** a condition tests `matchedImage` with `imageId` B for that step, **Then** the condition is false.
3. **Given** a condition that points to a step that has no matched image (no match, or other step type), **When** the condition runs, **Then** the condition is false and the run log tells why.
4. **Given** a condition that points to a step name that does not exist, **When** the sequence is saved or validated, **Then** the author gets a clear error.

---

### User Story 3 - Use the matched image id in a later step (Priority: P2)

An author uses a `{{...}}` placeholder in a later step to put the matched image id in a value, such as a log text, a notification, or a failure name (for example `announce-<id>`).

**Why this priority**: It lets one step handle all images, but the author can already branch with User Story 2.

**Independent Test**: Make a sequence with a `WaitForImage` step and a later step that has a placeholder for the matched image id of that step. Run it. The later step receives the id of the image that matched.

**Acceptance Scenarios**:

1. **Given** an earlier step that matched image A, **When** a later step has a placeholder for the matched image id of that step, **Then** the placeholder becomes the id of image A.
2. **Given** a placeholder for a step that has no matched image, **When** the later step runs, **Then** the run reports a clear error for the placeholder, in the same way as other unresolved placeholders.

---

### User Story 4 - See the matched image id in the run log (Priority: P2)

An operator reads the per-step run log after a run. The entry of the image check step shows the id of the matched image.

**Why this priority**: Operators need to see the cause of a branch without a debugger.

**Independent Test**: Run a sequence with an image check that matches. Read the per-step run log entry. It shows the matched image id.

**Acceptance Scenarios**:

1. **Given** an image check step that matched, **When** the operator reads the per-step run log, **Then** the entry shows the matched image id and the confidence.
2. **Given** an image check step that did not match, **When** the operator reads the entry, **Then** the entry shows no matched image id.

---

### Edge Cases

- Two or more images of the list match at the same time: the result reports the image that the existing feature 180 matching already selects. This feature adds no new matching logic.
- A step with a list of one image: the result holds that image id on a match.
- The condition tests an image id that is not in the list of the referenced step: the condition is false. The system gives no warning.
- The referenced step runs in a loop or more than once: the condition and placeholder use the latest result of that step.
- Old stored sequences and old step results with `referenceImageId` and no `matchedImageId`: they keep working and read the same.
- A step with an any-of image list as its own condition (feature 180, not a `WaitForImage` step): the matched image id is available to later steps under the name of that step (FR-013).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The result of an any-of image check MUST hold `matchedImageId` (the id of the image that matched) and `confidence` when an image matches.
- **FR-002**: The result MUST hold no `matchedImageId` when no image matches.
- **FR-003**: The existing fields of the `WaitForImage` result (`referenceImageId`, `confidence`) MUST stay and keep their meaning, so that existing clients keep working.
- **FR-004**: A condition MUST be able to test the matched image of an earlier step with the shape `matchedImage: { stepRef, imageId }`. The condition is true only when the matched image id of the referenced step equals `imageId`.
- **FR-005**: A condition that refers to a step with no matched image MUST be false and MUST NOT stop the run with an error. The run log MUST tell why.
- **FR-006**: The system MUST reject (at save or validation time) a `matchedImage` condition with a `stepRef` that does not exist in the sequence, with a clear message.
- **FR-007**: A later step MUST be able to use the matched image id of an earlier step in a `{{...}}` placeholder.
- **FR-008**: A placeholder for a matched image id that does not exist MUST give the same kind of clear error as other unresolved placeholders.
- **FR-009**: The per-step run log entry of an image check step MUST show the matched image id and the confidence when an image matched.
- **FR-010**: The feature MUST NOT change the matching logic of the any-of image list from feature 180.
- **FR-011**: The feature MUST keep old sequences and old results valid with no migration.
- **FR-013**: A step whose own condition is a top-level, non-negated any-of image condition (`imageVisible`) MUST give the matched image id to later conditions and placeholders under the name of that step, in the same way as a `WaitForImage` step. A nested or negated condition gives no matched image id. The latest result of a step is used.
- **FR-012** *(web UI)*: If the web UI shows the step result, the condition editor, or the run log, the phone support level is **reduced**: the condition editor gets a new type with two fields, and the run log shows one more field. The feature MUST follow docs/mobile-ui-principles.md.

### Key Entities

- **Image check result**: The result of a `WaitForImage` step or an any-of image condition. Holds `matchedImageId`, `confidence`, and the existing `referenceImageId`.
- **Matched-image condition**: A condition `matchedImage: { stepRef, imageId }` that refers to an earlier step by name.
- **Matched-image placeholder**: A `{{...}}` reference in a later step that resolves to the matched image id of a named earlier step.
- **Per-step run log entry**: The log record of one step. Shows the matched image id for image checks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test with a list of N images (N from 1 to 5), the result names the correct matched image in 100% of runs where one image matches.
- **SC-002**: An author can make one sequence that takes a different path for each image of a list, with no repeated image check per image.
- **SC-003**: For 100% of image checks that matched, the per-step run log shows the matched image id with no extra action by the operator.
- **SC-004**: All existing sequences and tests that use `WaitForImage` and feature 180 lists pass with no change.

## Assumptions

- The `WaitForImage` result type (`WaitForImageStepResultDetails`) and the condition model are in this repository. The planning phase finds the exact code.
- The exact placeholder name (for example `{{steps.<name>.matchedImageId}}`) follows the existing placeholder syntax. The planning phase picks the name that fits the existing syntax.
- A condition with `matchedImage` is a new condition type that is added next to the existing types.
- No change is made to the reporting repository. Its feature 028 is only a consumer.
- The issue gives no scope beyond the four criteria. This spec adds none.
