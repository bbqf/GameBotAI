# Feature Specification: Region-Restricted Image Detection

**Feature Branch**: `130-region-restricted-image-detection`
**Created**: 2026-10-09
**Status**: Implemented
**Input**: User description: "Restrict an image condition or tap target to a screen region. Source: GitHub issue #272 (FR-017). Closes #272."

## Clarifications

### Session 2026-10-09

- Q: Must the whole image lie inside the region, or only its centre? -> A: The whole image must lie inside the region. The search runs on the cropped region, so a match that crosses the region edge does not count. Rationale: this is the simplest rule and it matches "search only inside the region".
- Q: Is the region edge inclusive? -> A: The region covers pixels `x` to `x + width - 1` and `y` to `y + height - 1`. Rationale: standard pixel-rectangle meaning.
- Q: Does the save step reject a region that is larger than the capture? -> A: No. The capture size is not known at save time, so only the sign and presence checks apply. The run step clips the region to the capture (see FR-007).
- Q: Which other places use a region? -> A: Only `imageVisible` conditions and `detectionTarget` objects, wherever they appear. Other image features (for example a separate wait-for-image config) are not changed unless they embed one of these two types. Rationale: issue non-goal "no scope beyond the issue".
- Q: How does a read-back show "no region"? -> A: The `region` field is absent (or null). The service does not invent an empty or full-screen region. Rationale: existing data stays byte-for-byte equal on read-back.
- Q: Which score is used when two matches are inside the region? -> A: The existing selection rule is unchanged (for a target: its selection strategy; for a condition: the best score against the threshold). Rationale: threshold logic is a non-goal.

### Session 2026-10-09 (loop 1, from analyze findings)

- Q (I1): Can read-back show an empty region value? -> A: No. The response has no `region` field when none is set. US3 scenario 3 is corrected.
- Q (A1): If more than one region field is invalid, what does the 400 name? -> A: The 400 names every invalid field, each with its own message. Rationale: the author fixes all errors in one try.
- Q (C1): Does a `detectionTarget` inside a sequence step payload (waitForImage, primitiveTap) carry a region? -> A: Yes. Every place that holds a `detectionTarget` maps, validates, and returns the region (FR-011). If a payload path does not exist in the code, the plan notes it.
- Q (C2): Must the execution tree and step-through output show the region? -> A: Yes, as part of the condition description they already show. A test covers it.

### Session 2026-10-09 (loop 2, from loop 1 analyze)

- Q (C1): Is the region in a `primitiveTap` sequence payload validated at save time? -> A: Yes. One shared validator (`PixelRegion.Validate`) raises the 400 in every place; no place has its own copy of the rule (D1).
- Q (C2): Are the command-level `detection` and the `ensureGameRunning` readiness image in scope? -> A: Yes. Both hold a `detectionTarget`, so FR-011 applies and each needs an implementation task.
- Q (U3): How is SC-005 verified? -> A: By the quickstart check that one row of a list is selected with `region` only, and by the two-match tests of US1 and US2.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Limit an image condition to a screen region (Priority: P1)

A sequence author wants an image condition (`imageVisible`) to look for its image only in one part of the screen. For example: "the price icon in row 2 of the Mystery Shop list". The author adds a `region` with `x`, `y`, `width`, and `height` in pixels of the capture. The condition is true only if the image is found inside that region.

**Why this priority**: This is the core need. Without a region, a condition cannot link an icon to its own row. This causes unsafe taps (a 450-diamond button scored 0.860 against a 0.85 threshold and was tapped).

**Independent Test**: Save a sequence with an `imageVisible` condition that has a region. Read it back and check the region is the same. Run the condition on a capture where the image is inside the region (true) and on a capture where the image is only outside the region (false).

**Acceptance Scenarios**:

1. **Given** an `imageVisible` condition with a valid region, **When** the author saves it and reads it back, **Then** the response contains the same region values.
2. **Given** a capture that shows the image inside the region, **When** the condition runs, **Then** the condition is true.
3. **Given** a capture that shows the image only outside the region, **When** the condition runs, **Then** the condition is false.
4. **Given** a capture with the same image at two places, one inside and one outside the region, **When** the condition runs, **Then** only the match inside the region counts.

---

### User Story 2 - Limit a tap target to a screen region (Priority: P1)

A sequence author wants a tap target (`detectionTarget`) to find its image only inside a region. The tap then lands on the match inside the region, not on a better-scoring match in another place (for example, the Exchange button of a different row).

**Why this priority**: The tap target is where a wrong match causes a real action. The issue names it together with the condition.

**Independent Test**: Save a command with a `detectionTarget` that has a region. Read it back. Run it on a capture that has two matches, one inside the region and one outside with a higher score. The tap lands on the match inside the region.

**Acceptance Scenarios**:

1. **Given** a `detectionTarget` with a valid region, **When** the author saves it and reads it back, **Then** the response contains the same region values.
2. **Given** two matches where the best match is outside the region, **When** the target resolves, **Then** the tap coordinates come from the match inside the region.
3. **Given** the image is not inside the region, **When** the target resolves, **Then** the target reports "not found" and no tap is made.

---

### User Story 3 - Existing data keeps its behaviour (Priority: P1)

An operator has sequences and commands that have no region. These sequences and commands must work exactly as before. They search the whole screen.

**Why this priority**: Existing production queues depend on this. A change here can break running automation.

**Independent Test**: Load existing sequence and command data with no region. Run the existing test suite. Results are the same as before the change.

**Acceptance Scenarios**:

1. **Given** a condition or target with no region, **When** it runs, **Then** it searches the whole capture, as it does today.
2. **Given** stored data from before this feature, **When** the service loads it, **Then** the data loads without error and has no region.
3. **Given** a condition or target with no region, **When** the author reads it back, **Then** the response has no `region` field and no other field changes.

---

### User Story 4 - Reject a bad region at save time (Priority: P2)

An author sends a region that cannot work: a negative origin, a zero or negative size, or a missing field. The service refuses it with a clear 400 error. It does not store it and it does not drop it silently.

**Why this priority**: A silent drop is the current fault. A clear error stops the author from believing a region is active when it is not.

**Independent Test**: Send each invalid region form to the save endpoints. Each one returns a 400 response that names the field.

**Acceptance Scenarios**:

1. **Given** a region with `width` of 0 or less, **When** the author saves, **Then** the service returns 400 with a message that names `width`.
2. **Given** a region with a negative `x` or `y`, **When** the author saves, **Then** the service returns 400.
3. **Given** a region with a missing `x`, `y`, `width`, or `height`, **When** the author saves, **Then** the service returns 400.

---

### Edge Cases

- The region extends past the edge of the capture: the search area is the part of the region that lies inside the capture.
- The region lies fully outside the capture, or the clipped area is smaller than the image to find: the match is "not found". There is no error at run time.
- The region is smaller than the image to find: the match is "not found".
- A tap target with a region: the tap coordinates are in full-capture pixels, not relative to the region origin.
- The capture size is different from the size the author used (for example, another emulator resolution): the region stays in capture pixels. The feature does not scale it.
- A region that is present but `null`: the service treats it as no region.
- The author sends one of the old names (`searchRegion`, `roi`, `bounds`, or flat `x, y, width, height` on the condition): the feature does not add these names. Only `region` is a supported field.
- Step-through and execution trees show the condition. They show the region where they show the other condition settings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An image condition (`imageVisible`) MUST accept an optional `region` with integer `x`, `y`, `width`, and `height`, in pixels of the capture.
- **FR-002**: A tap target (`detectionTarget`) MUST accept the same optional `region`.
- **FR-003**: The service MUST store the region and MUST return it unchanged when the condition or target is read back through every API that returns it.
- **FR-004**: The service MUST validate a region when it saves the object. `x` and `y` MUST be 0 or more. `width` and `height` MUST be more than 0. All four fields MUST be present. An invalid region MUST give a 400 response that names every invalid field.
- **FR-005**: When a region is set, the image detection MUST search only inside the region. A match outside the region MUST NOT count as a match.
- **FR-006**: The coordinates a detection returns (and the tap point for a tap target) MUST be in full-capture pixels, not relative to the region.
- **FR-007**: When the region extends past the capture, the search area MUST be the part inside the capture. When no usable search area remains, the result MUST be "not found" and not an error.
- **FR-008**: A condition or target with no region MUST behave exactly as it does today. Stored data from before this feature MUST load unchanged.
- **FR-009**: The feature MUST NOT change the match threshold logic, MUST NOT add row-aware detection, and MUST NOT add other field names for the region.
- **FR-010**: The API contract documentation (OpenAPI) MUST describe the `region` field on both objects.
- **FR-011**: The region MUST be accepted wherever an `imageVisible` condition or a `detectionTarget` can be defined in the existing API (for example, sequence step conditions, composite conditions, command steps, and sequence step payloads such as waitForImage and primitiveTap), so that one rule applies in all places.
- **FR-012**: The execution tree and step-through output MUST show the region as part of the condition description they already show.

### Key Entities

- **Region**: A rectangle in capture pixels. It has `x`, `y` (top-left corner), `width`, and `height`. It is optional.
- **Image condition (`imageVisible`)**: A condition that is true when a named image is visible. It gains an optional Region.
- **Detection target (`detectionTarget`)**: A tap target that is the position of a named image. It gains an optional Region.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a capture with the same image at two places, a condition or target with a region around one place gives the result for that place in 100% of test cases.
- **SC-002**: A region saved on a condition or target is returned with identical values in 100% of read-back tests (no dropped region).
- **SC-003**: Every existing test for conditions and targets without a region passes with no change to its expected result.
- **SC-004**: Each invalid region form tested (non-positive size, negative origin, missing field) gives a 400 response that names the field, and nothing is stored.
- **SC-005**: An author can limit a match to one row of a list using only the `region` field, with no extra helper steps (such as a BACK step to close a wrong dialog).

## Assumptions

- Region values are integers in pixels of the capture image that the service uses for detection. No scaling between resolutions is part of this feature.
- The field name is `region`. This follows the shape in issue #272.
- The wider issue named other field names (`searchRegion`, `roi`, `bounds`, flat `x, y, width, height`). The feature does not support them. The feature adds one supported name only.
- Region fields on the stored model and the API model use the same names, so the existing read-back path carries them without a new mapping layer.
- The Mystery Shop task change in the PNS repository is out of scope. It uses this feature after release.

## Planning notes

These notes guide plan and task generation.

- **Parallel marks**: A task is marked [P] only if no other task in the same phase edits the same file. Tasks that edit the same file (for example SequencesEndpoints.cs, the MapPerStepCondition code, or one test file) run in sequence. User Story 4 tasks run after User Story 1 and User Story 2.
- **Legacy condition check**: The legacy class `ImageVisibleCondition` may have an API read or write path. Tasks MUST include an early check for this. Tasks MUST also include a conditional task in the User Story 1 phase: "If the check finds a read or write path, map and validate Region there".
- **No [P] with dependency**: A task that depends on another task in the same file MUST NOT carry [P], and its text MUST NOT say "sequential after ...". Order is given by task order and phase only.
- **Concrete file paths**: Tasks name exact paths, with no "find the file" wording. Known paths: `C:\src\GameBot\src\GameBot.Domain\Commands\Blocks\Condition.cs` (the `Blocks.Condition` type), `C:\src\GameBot\src\GameBot.Domain\Commands\SequenceStepCondition.cs`, `C:\src\GameBot\src\GameBot.Domain\Commands\ImageVisibleCondition.cs` (legacy), `C:\src\GameBot\src\GameBot.Service\Models\SequenceStepContracts.cs`, `C:\src\GameBot\src\GameBot.Service\Services\ImageDetectionHelper.cs`, `C:\src\GameBot\src\GameBot.Service\Services\Conditions\ImageDetectionConditionAdapter.cs`, `C:\src\GameBot\src\GameBot.Service\Services\Conditions\ImageVisibleConditionAdapter.cs`. The docs file is `C:\src\GameBot\docs\architecture.md` (the only file in `docs` that mentions `imageVisible` or `detectionTarget`); the plan confirms it and names it in the docs task.
- **Single validation rule (D1)**: The region rule (FR-004) lives in one place: `PixelRegion.Validate`. Endpoint maps and domain validators only call it. No task adds a second copy of the rule.
- **primitiveTap payload (C1)**: Tasks MUST include validation of the `region` in a `primitiveTap` sequence payload through `PixelRegion.Validate`, with a contract test for a 400 response.
- **Command-level detection and readiness image (C2)**: Tasks MUST include one implementation task for the command-level `detection` and one for the `ensureGameRunning` readiness image. Each task maps, validates, stores, and returns the region (FR-011).
- **Detection helper and adapter check (C3)**: Tasks MUST include one check task for both `ImageDetectionHelper.cs` and `ImageDetectionConditionAdapter.cs`. If either file reads a condition or a target, it carries the region to the detection call.
- **Setup check results (U2)**: The results of the legacy-class check and the `primitiveTap` payload check are recorded in one place: the section "Setup check results" in `C:\src\GameBot\specs\130-region-restricted-image-detection\research.md`. Later tasks and the PR text refer to that section.
- **Region types**: The fraction `Region` (in `Blocks.Condition` and `ImageMatchParams`) is different from the new pixel region (`PixelRegion`). Tasks MUST include a unit test that checks this: when both a fraction Region and a PixelRegion are set, the detection uses the PixelRegion.
