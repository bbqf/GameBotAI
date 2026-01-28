# Feature Specification: Emulator Screenshot Cropping

**Feature Branch**: `022-emulator-image-crop`  
**Created**: 2026-01-20  
**Status**: Draft  
**Input**: User description: "Let's work on preparing images easily. I want to be able to capture the image from the emulator screenshot, mark a rectangular part of the screenshot visually and create an stored image using the marked area."

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Capture and crop screenshot (Priority: P1)

An image author captures the current emulator screen, draws a rectangle around the area of interest, and saves it as a reusable image in one flow.

**Why this priority**: This is the core value of the feature—rapidly turning emulator visuals into stored assets without external tools.

**Independent Test**: From the emulator view, capture, draw, and save a rectangle; verify the cropped image file matches the selected area and is stored with the chosen name.

**Acceptance Scenarios**:

1. **Given** the emulator view is open, **When** the user triggers capture and drags a rectangle over the screenshot, **Then** the system saves a cropped image that matches the drawn area and confirms the save location.
2. **Given** a user adjusts the rectangle before saving, **When** they resize or reposition it, **Then** the preview reflects the final rectangle and the saved image matches the adjusted bounds.

---

### User Story 2 - Name and store cropped image (Priority: P2)

An image author names the cropped area and confirms where it is stored so it can be reused without confusion.

**Why this priority**: Clear naming and storage avoids duplicated work and makes the captured asset discoverable by others.

**Independent Test**: Save a cropped image with a custom name; confirm the file exists in the expected location with the requested name and metadata.

**Acceptance Scenarios**:

1. **Given** the user has drawn a rectangle, **When** they provide a name and confirm saving, **Then** the system stores the cropped image under that name and shows where it was saved.
2. **Given** the user attempts to reuse an existing name, **When** they confirm saving, **Then** the system prevents silent overwrite and offers a clear choice to rename or replace.

---

### User Story 3 - Handle failed or invalid selections (Priority: P3)

An image author is guided when a capture fails or the rectangle is too small, and can retry without losing context.

**Why this priority**: Keeps the workflow resilient so users are not blocked by transient errors or accidental selections.

**Independent Test**: Attempt a capture with an invalid rectangle and with a simulated capture failure; verify the user receives guidance and can retry successfully.

**Acceptance Scenarios**:

1. **Given** the user draws a rectangle that is too small to be useful, **When** they attempt to save, **Then** the system blocks the save, explains the issue, and keeps the capture available for resizing.
2. **Given** a capture fails (e.g., emulator not ready), **When** the user retries, **Then** the system reports the failure cause, preserves the ability to retry, and succeeds once the emulator is ready.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Emulator window not available or minimized when capture is triggered.
- Rectangle selection is zero-sized or below a minimum threshold.
- User cancels after drawing a rectangle—capture should discard without saving or side effects.
- Duplicate image names—prompt to rename or confirm overwrite.
- Save location unavailable (e.g., folder missing or read-only)—inform user and keep capture for retry.
- Rapid successive captures—each crop should be stored separately without mixing coordinates.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST let users trigger a capture of the current emulator view from within the authoring workflow.
- **FR-002**: System MUST display the captured screenshot and allow users to draw, resize, and reposition a rectangular selection before saving, enforcing a minimum size of 16x16 pixels.
- **FR-003**: System MUST allow users to name the cropped image and confirm the intended storage location before saving.
- **FR-004**: System MUST save only the selected rectangle as a stored image file in PNG format and confirm when the file is written successfully.
- **FR-005**: System MUST prevent unintended overwrites by detecting duplicate names and providing clear options to rename or replace.
- **FR-006**: System MUST handle invalid selections or capture failures with actionable guidance and allow retry without losing the current screenshot.

### Key Entities *(include if feature involves data)*

- **Screenshot Capture Session**: Represents a single captured emulator view; includes capture time, source context, and availability for cropping until dismissed.
- **Stored Image Asset**: Represents the saved cropped image; includes user-provided name, source capture reference, crop bounds, and storage location for reuse.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: 90% of users complete capture, selection, and save within 30 seconds on first attempt during usability testing.
- **SC-002**: 100% of saved images match the final user-selected rectangle with no more than a 1-pixel deviation when inspected.
- **SC-003**: 100% of attempts to save with a duplicate name result in a clear prompt that avoids unintended overwrites.
- **SC-004**: At least 95% of users report knowing where their cropped image was saved immediately after completion in feedback surveys.

## Assumptions

- Emulator is running and reachable when capture is initiated; user has permission to view and capture its screen.
- Cropping is limited to axis-aligned rectangles; no rotation or freeform shapes are required.
- Stored images are saved to the existing project image storage location; sufficient disk space is available.
- The workflow is single-user per session; concurrent edits to the same filename are unlikely and handled via duplicate-name prompts.

## Clarifications

### Session 2026-01-20

- Q: What is the minimum rectangle size allowed before saving? → A: 16x16 px minimum before saving.
- Q: What format should cropped images be saved in? → A: PNG only for consistent lossless quality.
