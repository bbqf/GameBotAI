# Feature Specification: Resolve parameter placeholders in inline WaitForImage

**Feature Branch**: `186-fix-waitforimage-placeholder`
**Created**: 2026-11-10
**Status**: Draft
**Input**: User description: "Fix GitHub issue #386: B-041: Inline WaitForImage does not resolve a parameter placeholder in referenceImageId (image_unavailable). Closes #386."

## Background

An author can put a parameter placeholder, for example `{{name}}`, in the image field of a step. The condition `imageVisible` resolves such a placeholder. An inline WaitForImage step does not. The step keeps the literal text `{{name}}` in `detectionTarget.referenceImageId`, and it ends with `image_unavailable`. The published schema of `DetectionTargetDto.referenceImageIds` says that a placeholder of a multi-value parameter in `referenceImageId` has the same effect as a list of images.

Measured on 2026-11-09, with a sequence that has two image parameters: `q` (multi-value, two allowed images `pns-showdown-tile` and `pns-city-anchor`) and `s` (one value). The queue entry gave `q` = both images and `s` = `pns-city-anchor`. The image `pns-city-anchor` was on the screen.

| Step | Image field | Result |
|------|-------------|--------|
| wfi-single | `{{s}}` | `image_unavailable`, field still `{{s}}` |
| wfi-list | `{{q}}` | `image_unavailable`, field still `{{q}}` |
| wfi-literal-list | `referenceImageIds` with the two ids | `image_detected` |

In the same run, the condition `imageVisible` with `{{q}}` resolved and matched. So the parameter value reaches the entry correctly. Only the inline WaitForImage field is not resolved.

A second defect: for wfi-literal-list, the step log shows `referenceImageId` = `pns-showdown-tile` and no `matchedImageId`, but the screen showed `pns-city-anchor`. The published `WaitForImageStepResultDetails` has `matchedImageId`. The log of this step does not have it.

## Clarifications

### Session 2026-11-10

- Q: What happens when the placeholder has no value in the scope? → A: The step fails with the same unresolved-parameter error that the `imageVisible` condition gives. It does not give `image_unavailable`. Reason: one rule for all image placeholders.
- Q: What does `matchedImageId` show for a step with one image? → A: The resolved image id itself. Reason: a single image is its own match.
- Q: What does the log show as `referenceImageId` for a multi-value placeholder? → A: The first resolved id. `matchedImageId` shows the image that matched. The log has no new list field. Reason: `referenceImageId` is a single-value field.
- Q: Which rule applies when a step has a multi-value placeholder and also a written `referenceImageIds`? → A: The same rule that the resolver already uses for command steps with a detection target. This feature adds no new rule.
- Q: Does the fix apply to WaitForImage steps that sit in a loop body or an if body? → A: Yes. Each step resolves against the scope in effect where it runs.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Single-value image parameter in WaitForImage (Priority: P1)

An author puts the placeholder of a single-value image parameter in the image field of an inline WaitForImage step. When the sequence runs, the step uses the image that the parameter holds.

**Why this priority**: This is the basic case of the bug. Without it, the author cannot use a parameter to choose the image of a wait step.

**Independent Test**: Run a sequence with parameter `s` = `pns-city-anchor` and an inline WaitForImage step with `{{s}}`. The image is on the screen. The step ends with `image_detected`.

**Acceptance Scenarios**:

1. **Given** a parameter `s` that holds a valid image, and that image is on the screen, **When** an inline WaitForImage step with `{{s}}` runs, **Then** the step resolves the placeholder, detects the image, and does not give `image_unavailable`.
2. **Given** the same setup, **When** the step runs, **Then** the step log shows the resolved image id, not the text `{{s}}`.

---

### User Story 2 - Multi-value image parameter in WaitForImage (Priority: P1)

An author puts the placeholder of a multi-value image parameter in the image field of an inline WaitForImage step. The step waits for any of the images of the parameter. The effect is the same as a step with `referenceImageIds`.

**Why this priority**: The published schema promises this behavior. The author expects it.

**Independent Test**: Run a sequence with parameter `q` = `pns-showdown-tile` and `pns-city-anchor`. The image `pns-city-anchor` is on the screen. The step has `{{q}}`. The step ends with `image_detected`.

**Acceptance Scenarios**:

1. **Given** a multi-value image parameter and one of its images on the screen, **When** an inline WaitForImage step with `{{q}}` runs, **Then** the step ends with `image_detected`.
2. **Given** the same setup, **When** the step runs, **Then** the result is the same as for a step with the same ids in `referenceImageIds`.

---

### User Story 3 - Step log shows the matched image (Priority: P2)

A person who reads the step log of a WaitForImage step sees which image matched. This applies to a step with one image, a list in `referenceImageIds`, and a resolved multi-value placeholder.

**Why this priority**: The log helps the author find faults. It is a separate defect, but the same step.

**Independent Test**: Run a step with the two ids in `referenceImageIds`. Only the second image is on the screen. The log shows `matchedImageId` = the second id.

**Acceptance Scenarios**:

1. **Given** a list of two images where only the second is on the screen, **When** the step runs and detects it, **Then** the log shows `matchedImageId` with the second id, and not only the first listed id.
2. **Given** a step with a placeholder, **When** the step runs, **Then** the log shows the resolved first image id in `referenceImageId` and the image that matched in `matchedImageId`.

---

### Edge Cases

- The parameter value is an image that is not on the screen: the step times out with the normal timeout result, not `image_unavailable`.
- The parameter value is an image id that does not exist: the step gives `image_unavailable` for that id, as for a literal id that does not exist.
- A multi-value placeholder holds one value only: the step behaves as a step with one image.
- The placeholder is in `fieldTemplates`: it resolves in the same way as the inline field.
- A step with a literal image id and no placeholder: the behavior does not change.
- The placeholder names a parameter that the scope does not have: the step gives the same error as the `imageVisible` condition gives for an unknown parameter.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST resolve a placeholder in the `detectionTarget.referenceImageId` of an inline WaitForImage step against the scope of the step, before the wait starts.
- **FR-002**: The resolution MUST work in the same way as the resolution of `imageVisible.imageId`, for a single-value image parameter.
- **FR-003**: The system MUST treat a multi-value image placeholder in `referenceImageId` as the same effect as `referenceImageIds`, as the published schema says.
- **FR-004**: The system MUST NOT give `image_unavailable` when the parameter value is a valid image that is on the screen.
- **FR-005**: The step log of a WaitForImage step MUST show the resolved image id in `referenceImageId` (the first id for a list), not the placeholder text.
- **FR-006**: The step log of a WaitForImage step MUST show `matchedImageId` as `WaitForImageStepResultDetails` defines. For a list of images, it MUST show the image that matched, not only the first listed id.
- **FR-007**: The system MUST NOT change the resolution of the `imageVisible` condition, the way parameter values reach the queue entry, or the condition-based workaround.
- **FR-008**: The system MUST NOT add a feature other than the placeholder resolution and the `matchedImageId` log field.
- **FR-009**: This feature does not change the web UI. Phone support level: not applicable (service behavior only).

### Key Entities

- **Inline WaitForImage step**: A sequence step that waits for one or more images on the screen. It has a detection target with `referenceImageId`, `referenceImageIds`, and `fieldTemplates`.
- **Image parameter**: A sequence parameter of kind `image`. It has one value or many values (`multiple`).
- **Step log details**: The `detailAttributes` of a step result. For WaitForImage they follow `WaitForImageStepResultDetails`, with `matchedImageId`.

## Assumptions

- The scope that `imageVisible` uses for placeholders is also available to the inline WaitForImage step.
- A failed resolution gives the same error type as the `imageVisible` condition gives.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the measured case, the step with `{{s}}` ends with `image_detected` in 100% of runs where the image is on the screen.
- **SC-002**: In the measured case, the step with `{{q}}` ends with `image_detected` in 100% of runs where one listed image is on the screen.
- **SC-003**: In 100% of detected WaitForImage runs, the step log shows `matchedImageId` with the image that matched (`pns-city-anchor` in the measured case).
- **SC-004**: The step log of a run with a placeholder never shows the text `{{name}}` as the image.
- **SC-005**: Existing steps with literal image ids give the same results as before.
