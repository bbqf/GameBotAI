# Feature Specification: Visual Command Recorder

**Feature Branch**: `055-record-command`  
**Created**: 2026-06-05  
**Status**: Implemented
**Input**: User description: "record a command instead of defining it manually; show all found images on the captured screen so that clicking images = identifiable areas instead of exact coordinates; also support key input and swipe; for primitive tap note not only the image but also position within the image"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Image-Region Tap (Priority: P1)

The user is authoring a command and wants to add a tap step that targets a recognizable on-screen element (a button, icon, or label). Instead of looking up the image ID and guessing coordinates, they open the Visual Step Picker, see the current emulator screen with all matched image regions highlighted, and click the region they care about. The step is captured with the correct image reference and the exact click offset from the image center.

**Why this priority**: This is the core value proposition — removing the need to know image IDs or raw coordinates. Everything else builds on this.

**Independent Test**: Can be fully tested by opening the picker, having at least one reference image visible on screen, clicking inside its bounding box, and verifying the resulting step references that image with a non-zero offset when the click is off-center.

**Acceptance Scenarios**:

1. **Given** the command editor is open and the emulator is connected, **When** the user opens the Visual Step Picker, **Then** the current emulator screenshot is displayed with all currently-matching reference image regions overlaid as labeled bounding boxes.
2. **Given** the picker is open with one or more image regions visible, **When** the user clicks inside a bounding box, **Then** a PrimitiveTap step is created referencing that image's ID with an offset equal to the click position relative to the center of that bounding box.
3. **Given** multiple overlapping bounding boxes exist at the click position, **When** the user clicks, **Then** the step is created for the highest-confidence match.
4. **Given** the click lands outside all matched regions, **When** the user clicks, **Then** no step is added and the click is ignored (or a visual hint is shown that no image was hit).

---

### User Story 2 — Key Input Recording (Priority: P2)

The user wants to record a keystroke (e.g., the Back button, Enter, or a D-pad direction) as a step without manually entering the key code. During a recording session they press the key on their keyboard and it is captured as a KeyInput step.

**Why this priority**: Key input is a common primitive action. Supporting it alongside taps makes the picker a complete recording tool for most commands.

**Independent Test**: Can be fully tested by opening the picker, pressing a key, and verifying a KeyInput step is added with the correct key identifier.

**Acceptance Scenarios**:

1. **Given** the Visual Step Picker is open, **When** the user presses a key on their keyboard, **Then** a KeyInput step is appended to the recorded steps list with the corresponding key identifier.
2. **Given** a key press is recorded, **When** the user reviews the step list, **Then** the step shows a human-readable key label (e.g., "Back", "Enter") rather than a raw code.

---

### User Story 3 — Swipe Recording (Priority: P3)

The user wants to record a swipe gesture (e.g., scroll down a list, drag an item). They press and drag on the emulator screenshot in the picker; the gesture is captured as a Swipe step with start/end coordinates and duration.

**Why this priority**: Swipe is needed for navigation-heavy games. It is less common than tap but required for complete coverage.

**Independent Test**: Can be fully tested by dragging on the screen in the picker and verifying a Swipe step is created with start/end coordinates and a duration proportional to drag duration.

**Acceptance Scenarios**:

1. **Given** the picker is open, **When** the user presses, holds, and releases the pointer in two different positions, **Then** a Swipe step is created with start coordinates, end coordinates, and a duration derived from how long the gesture took.
2. **Given** a very short drag (less than a minimum threshold distance), **When** the pointer is released, **Then** the gesture is treated as a tap, not a swipe.

---

### User Story 4 — Step Review and Save (Priority: P1)

The user can see all captured steps in a live list as they record, remove individual steps they don't want, and confirm the list to add all steps to the command at once.

**Why this priority**: Without review/save, the recording session produces no usable output. This is as critical as Story 1.

**Independent Test**: Can be tested by recording two or three steps, deleting one, and confirming that the remaining steps are appended to the command form.

**Acceptance Scenarios**:

1. **Given** one or more steps have been recorded, **When** the user views the picker, **Then** each step is shown in the order it was captured with a label describing its type and target.
2. **Given** at least one step is listed, **When** the user removes a step, **Then** it is removed from the list immediately without closing the picker.
3. **Given** two or more steps are listed, **When** the user reorders a step, **Then** the list reflects the new order immediately.
4. **Given** the user is satisfied with the step list, **When** they confirm, **Then** all recorded steps are appended to the command's step list in the current order and the picker closes.
5. **Given** the user decides to abandon the session, **When** they cancel, **Then** no steps are added to the command and the picker closes.

---

### Edge Cases

- No reference images match the current screen: overlays are empty; keyboard and swipe recording still work.
- Emulator not connected or screenshot capture fails on open: picker shows an error state with a retry option.
- Screenshot re-capture fails mid-session: picker shows an error notice, the previous screenshot and overlays remain displayed, and input is unblocked so the session can continue.
- Reference images match but confidence is below threshold: those regions are not shown as targets.
- Ambiguous click on overlapping regions: highest-confidence match wins.
- Click offset positions the tap outside the matched image bounds: offset is still recorded as-is (valid for edge taps).
- User opens picker with no reference images defined at all: picker shows the screenshot with no overlays; only key and swipe recording is available.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The command editor MUST provide an entry point to open the Visual Step Picker when adding steps to a command.
- **FR-003**: The picker MUST run image matching against all known reference images on the captured screenshot and overlay each match as a labeled bounding box.
- **FR-004**: When the user clicks inside a matched bounding box, the system MUST create a PrimitiveTap step with the matched image's identifier and an offset (X, Y) calculated as the click position minus the bounding box center.
- **FR-005**: When the user clicks and the click hits multiple overlapping bounding boxes, the system MUST select the match with the highest confidence score.
- **FR-006**: When the user clicks and no bounding box is hit, the system MUST NOT add a step.
- **FR-007**: When the user presses any key while the picker is focused, the system MUST add a KeyInput step with the identifier of the pressed key. No keys are reserved as UI shortcuts — including Escape and Enter.
- **FR-007b**: Confirm and cancel actions MUST only be triggered via explicit UI buttons, not keyboard shortcuts.
- **FR-008**: When the user performs a press-drag-release gesture with a displacement above a minimum threshold, the system MUST add a Swipe step with start coordinates, end coordinates, and a duration derived from the gesture elapsed time.
- **FR-009**: When a press-drag-release gesture displacement is below the minimum threshold, the system MUST treat the interaction as a tap, not a swipe.
- **FR-010**: Recorded steps MUST be shown in a live ordered list within the picker as they are captured.
- **FR-011**: The user MUST be able to remove individual steps from the recorded list before confirming.
- **FR-011b**: The user MUST be able to reorder steps in the recorded list before confirming.
- **FR-012**: On confirmation, all steps in the recorded list MUST be appended to the end of the command's existing step list, regardless of any cursor or selection position in the editor.
- **FR-013**: On cancellation, no steps MUST be added to the command.
- **FR-014**: The picker MUST capture the current emulator screenshot when opened and display it as the working state for the session.
- **FR-015**: The user MUST be able to trigger a re-capture (refresh the screenshot and rerun image matching) via an explicit button within the picker.
- **FR-015b**: While a re-capture is in progress, the picker MUST block all step-recording input (tap, key, swipe) and display a loading indicator. Input resumes once the new screenshot and overlays are ready.

### Key Entities

- **Visual Step Picker**: A full-screen modal overlay opened from the command editor, used to record primitive action steps against a live screenshot. The command editor is blocked until the picker is confirmed or cancelled.
- **Recorded Step**: A transient step entry (PrimitiveTap, KeyInput, or Swipe) captured during a picker session, pending confirmation.
- **Image Match Overlay**: A labeled bounding box drawn over a screenshot region where a reference image was detected with sufficient confidence.
- **Click Offset**: The signed pixel distance (X, Y) between the user's click position and the center of the matched bounding box, stored as the tap offset within the image.

## Clarifications

### Session 2026-06-05

- Q: Should image-match overlays recompute live or work on a captured snapshot? → A: Captured snapshot on open; user triggers re-capture explicitly via a button. Live refresh is out of scope.
- Q: Should the Visual Step Picker open as a modal, inline panel, or separate route? → A: Full-screen modal overlay; command editor is blocked until picker is confirmed or cancelled.
- Q: Maximum acceptable delay between new screenshot frame and updated overlays appearing? → A: Under 1 second.
- Q: Can recorded steps be reordered in the picker before confirming? → A: Yes, reordering is supported within the picker.

### Session 2026-06-05 (continued)

- Q: Which keys should be reserved as UI controls rather than captured as KeyInput steps? → A: None — every key including Escape and Enter is captured. Confirm and cancel are button-only.
- Q: Should recorded steps insert at the cursor position or always append to the end of the command? → A: Always appended to the end.
- Q: Should the picker block input during re-capture or allow recording against the old screenshot? → A: Input is blocked during re-capture; a loading indicator is shown.

## Assumptions

- The picker works on a captured snapshot taken at open time; image matching is not continuous. The user re-captures manually when they need an updated view.
- Swipe duration is derived from the actual time elapsed between press and release, not a fixed value.
- The minimum displacement threshold that distinguishes a tap from a swipe will be determined during implementation based on typical gesture sizes on emulator screens.
- Only the currently selected emulator session's screenshot is used; multi-device scenarios are out of scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a PrimitiveTap step referencing a recognized screen region without entering any image ID or coordinate manually.
- **SC-002**: A user can add a KeyInput step by pressing a key, without selecting from a dropdown or entering a key code.
- **SC-003**: A user can add a Swipe step by dragging on the screen, without entering numeric coordinates.
- **SC-004**: Building a 5-step command using the recorder takes less time than building the same command through the manual step editor.
- **SC-005**: All three primitive action types (tap, key input, swipe) can be captured within a single picker session without reopening or switching modes.
- **SC-006**: Image match overlays are fully rendered within 1 second of the picker opening or a manual re-capture being triggered.
