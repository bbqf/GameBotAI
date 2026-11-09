# Feature Specification: Any-of image condition and WaitForImage for a list parameter

**Feature Branch**: `180-any-of-image-list`  
**Created**: 2026-11-09  
**Status**: Draft  
**Input**: User description: "Any-of image condition and WaitForImage for a list parameter (GitHub issue #379, FR-043). Source: https://github.com/bbqf/GameBotAI/issues/379. Closes #379. Builds on the list-valued sequence parameters feature (specs/179-list-valued-parameters)."

## Clarifications

### Session 2026-11-09

- Q: May an item of a written `imageIds` list be a placeholder? → A: Yes, an item may be a `{{name}}` placeholder of a single-value parameter, as `imageId` allows today. An item that names a multi-value parameter is refused at save, because a list inside a list is not defined. (Rationale: the list placeholder has one place, the `imageId` field.)
- Q: What name does the `WaitForImage` result use for the matched image? → A: `matchedImageId`. The existing `referenceImageId` stays and holds the first image of the list when a list is used. (Rationale: old readers keep working.)
- Q: Does a missing image id at run time fail the condition? → A: No. That item never matches. The existing missing-image failure applies only if the existing rules say so for every item. (Rationale: the operator list can hold a stale id.)
- Q: Is `match` allowed on a condition with a single `imageId`? → A: Yes, but the only value is `any`, and it has no effect for one image.
- Q: Does `WaitForImage` get an `imageIds` list field too? → A: Yes, the same two inputs as the condition: `imageIds` (written list) and `imageId` (one id or a list placeholder). (Rationale: the issue says "the same for WaitForImage".)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Image-visible condition for a list (Priority: P1)

A sequence author writes an `imageVisible` condition that is true when any one image of a list is visible on the screen. The list is either written in the condition, or comes from a list parameter through a `{{name}}` placeholder. The operator then selects the images (for example, quests) in the value form, and the condition follows that list.

**Why this priority**: This is the gap. Today `any` over `imageVisible` children works only for a list that is known when the sequence is written. It cannot follow a per-account, operator-chosen list.

**Independent Test**: Save a sequence with an `imageVisible` condition whose image field is `{{quests}}`, where `quests` is a multi-value image parameter. Run it with the list `[A, B]`. Show image B on the screen. The condition is true. Show neither. The condition is false.

**Acceptance Scenarios**:

1. **Given** an `imageVisible` condition with a list of image ids written in it, **When** any image of the list is visible, **Then** the condition is true.
2. **Given** the same condition, **When** no image of the list is visible, **Then** the condition is false.
3. **Given** an `imageVisible` condition with `imageId` set to `{{quests}}` for a multi-value image parameter that holds `[A, B]`, **When** the condition runs, **Then** it behaves as a condition on the list `[A, B]` with `match: "any"`.
4. **Given** a condition on a list and no `match` value, **When** it runs, **Then** the match mode is `any`.
5. **Given** a condition with one image id (no list), **When** it runs, **Then** it behaves as before this feature.
6. **Given** a list parameter that holds exactly one item, **When** the condition runs, **Then** it behaves as a condition on that one image.

---

### User Story 2 - WaitForImage for a list (Priority: P1)

A sequence author uses a `WaitForImage` step with a list. The step ends when any image of the list is visible. The list is written in the step or comes from a list parameter through a `{{name}}` placeholder.

**Why this priority**: The consumer task waits for the screen to show any one selected quest. A condition alone does not let a sequence wait.

**Independent Test**: Save a sequence with a `WaitForImage` step whose `imageId` is `{{quests}}`. Run it with `[A, B]`. Show image A after some time. The step ends when A is visible, and the result tells which image matched.

**Acceptance Scenarios**:

1. **Given** a `WaitForImage` step whose `imageId` is a list placeholder, **When** any image of the list becomes visible before the timeout, **Then** the step ends with success.
2. **Given** the same step, **When** no image of the list becomes visible before the timeout, **Then** the step ends with the same timeout outcome as for one image.
3. **Given** the step ends with success, **When** the result is read, **Then** the result names the image that matched.
4. **Given** a `WaitForImage` step with one image id, **When** it runs, **Then** it behaves as before this feature.

---

### User Story 3 - Placeholder expansion only where a list is allowed (Priority: P2)

A `{{name}}` placeholder of a list parameter expands to the list only in the image field of an `imageVisible` condition and in the image field of a `WaitForImage` step. In a position that needs one value, the save refuses the placeholder with a clear error.

**Why this priority**: A list in a one-value position has no defined meaning. The author must learn about the error at save time, not at run time.

**Independent Test**: Save a sequence that uses a list parameter in a one-value position (for example, a click target image). The save is refused with a message that names the parameter and the position.

**Acceptance Scenarios**:

1. **Given** a placeholder of a multi-value parameter in the image field of an `imageVisible` condition, **When** the author saves, **Then** the save is accepted.
2. **Given** a placeholder of a multi-value parameter in the image field of a `WaitForImage` step, **When** the author saves, **Then** the save is accepted.
3. **Given** a placeholder of a multi-value parameter in a position that needs one value, **When** the author saves, **Then** the save is refused with a message that names the parameter and the position.
4. **Given** a placeholder of a single-value parameter in these image fields, **When** the author saves, **Then** the save is accepted as before.
5. **Given** a placeholder of a multi-value parameter that is only part of a text, for example `x_{{quests}}`, in these image fields, **When** the author saves, **Then** the placeholder is not expanded to a list. The existing text rule of feature 179 applies (the items are joined by `|` in the text), and the save does not refuse it under FR-007. FR-007 applies only to a placeholder that is the whole value of the field.

---

### User Story 4 - Static check stays skipped (Priority: P2)

The save does not check a placeholder image id against the stored images. It gives the `static_check_skipped` warning, as it does today.

**Why this priority**: The list is known only at run time. The existing behavior must stay stable.

**Independent Test**: Save a sequence with a list placeholder in an `imageVisible` image field. The save is accepted and the response holds the `static_check_skipped` warning.

**Acceptance Scenarios**:

1. **Given** a list placeholder in an allowed image field, **When** the author saves, **Then** the response holds the `static_check_skipped` warning and no image-exists error.
2. **Given** a list written in the condition with an image id that does not exist, **When** the author saves, **Then** the save gives the same missing-image feedback as for one image id.

---

### Edge Cases

- An empty list at run time: the condition is false and `WaitForImage` waits until the timeout. An empty list for a required parameter is already refused by feature 179.
- A list with one item: it behaves as one image.
- A list item that is not a stored image id at run time: that item never matches and the run does not fail for it. See FR-006a for the result when no item is a stored image.
- A list of more than 50 items: not possible for a valid parameter (feature 179 limit). A written list follows the same limit.
- Duplicate items in a written list: they count once.
- A `match` value other than `any`: the save refuses it. This feature adds no other match modes.
- A condition that holds both `imageId` and `imageIds`: the save refuses it.
- A nested `not` around the condition: `not` of the any-of condition is true when no image of the list is visible.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `imageVisible` condition MUST accept an optional list `imageIds` of image ids. A condition MUST NOT hold both `imageId` and `imageIds`.
- **FR-002**: The `imageVisible` condition MUST accept an optional `match` field. The only allowed value is `any`. When the condition uses a list, the default is `any`.
- **FR-003**: A condition on a list MUST be true when at least one image of the list is visible. It MUST be false when none is visible, and when the list is empty.
- **FR-004**: The `imageId` field of an `imageVisible` condition MUST accept a `{{name}}` placeholder that is the whole value and names a multi-value parameter. At run time the placeholder MUST expand to the list of the parameter, in that position only, and the condition MUST use `match: "any"`.
- **FR-005**: The `WaitForImage` step MUST accept the same inputs: an `imageId` that is a list placeholder, and a written list. The step MUST end with success when any image of the list is visible before the timeout, and with the existing timeout outcome otherwise.
- **FR-005a**: The `WaitForImage` step MUST also accept an optional written list `imageIds`, with the same rules as the condition (FR-001). An item of a written list MAY be a placeholder of a single-value parameter. The save MUST refuse an item that names a multi-value parameter.
- **FR-005b**: In the `WaitForImage` step, "the image field" and `imageId` of this spec mean `detectionTarget.referenceImageId` (and the same key in the field-template overlay). The written list is `detectionTarget.referenceImageIds`.
- **FR-006**: The result of a `WaitForImage` step with a list MUST name the image that matched in a new field `matchedImageId`. The existing single-image result fields (including `referenceImageId`) MUST keep their meaning; with a list, `referenceImageId` holds the first image of the list.
- **FR-006a**: An item of a list that is not a stored image at run time MUST never match, and MUST NOT fail the run by itself. For a condition, the result is false when no item matches. For a `WaitForImage` step, the outcome is the existing `image_unavailable` outcome when no item is a stored image, and the timeout outcome when some items are stored images but none shows.
- **FR-007**: A placeholder of a multi-value parameter in a position that needs one value MUST be refused at save, with an error that names the parameter and the position. The only positions that allow a list are the image field of an `imageVisible` condition and the image field of a `WaitForImage` step.
- **FR-008**: The save MUST NOT check a placeholder image id against the stored images. It MUST give the `static_check_skipped` warning, as today.
- **FR-009**: Conditions and steps that use one image id MUST keep their behavior, their stored form, and their API response.
- **FR-010**: The API and the published schema (Swagger) MUST describe `imageIds`, `match`, the list placeholder in `imageId`, and the new `WaitForImage` result field.
- **FR-011**: This feature MUST NOT add other match modes, MUST NOT add other condition types, and MUST NOT change other behavior of list parameters.
- **FR-012** *(web UI change, if the editor changes)*: Any change to the web editor for these fields MUST state its phone support level and follow docs/mobile-ui-principles.md. The support level is full.
- **FR-013**: Tests MUST cover each requirement above. The docs and the API contract MUST describe the new fields and the rules for placeholder positions.

### Key Entities

- **Image-visible condition**: gets `imageIds` (list) and `match` (`any`). Keeps `imageId` for one image or a list placeholder.
- **WaitForImage step**: its image field accepts a list placeholder or a written list. Its result names the matched image.
- **List parameter**: a multi-value image parameter from feature 179. It supplies the list at run time.

## Assumptions

- The list is an ordered list of image ids. Order does not change the result of `any`.
- The matched image (`matchedImageId`) in the `WaitForImage` result is the first image of the list order that is visible when the step ends.
- The field names `imageIds` and `match` are as in the issue.
- The editor changes are limited to what is needed to author the new fields. If the editor already stores free text for the image field, no editor change is needed.
- The existing timeout, poll, and threshold settings of `WaitForImage` apply to the list as a whole.
- The existing rules for text placeholders of feature 179 stay in force for placeholders that are not the whole value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: One sequence acts on any one of the operator-selected quests with no copy of the sequence and no fixed list written at authoring time.
- **SC-002**: 100% of conditions and steps that use one image id give the same result as before this feature.
- **SC-003**: 100% of saves that put a list placeholder in a one-value position are refused with a message that names the parameter and the position.
- **SC-004**: Each rule above (list in condition, placeholder in condition, placeholder in `WaitForImage`, default match, refusal in one-value position, skipped static check) has at least one automated test with the expected result.
- **SC-005**: The published schema and the docs show the new fields, and a reader finds the placeholder-position rules without reading the code.
