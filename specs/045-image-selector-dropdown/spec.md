# Feature Specification: Image Selector Dropdown

**Feature Branch**: `045-image-selector-dropdown`  
**Created**: 2026-05-30  
**Status**: Draft  
**Input**: User description: "Replace all image entry fields with a drop-down selection of available images. I also want to see miniature images in that drop-down. Find all text fields where image id is entered and replace them with such a selector"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select Image via Dropdown in Command Form (Priority: P1)

A bot author is editing a command and needs to assign an image to a step (primitive tap, wait for image, or detection reference). Instead of having to remember or type an image ID, they click a dropdown control that shows all available images with small thumbnails, and select the one they want.

**Why this priority**: This is the most frequently used image-selection context. Command steps currently require the author to know the exact image ID string, which is error-prone and slows down authoring.

**Independent Test**: Can be fully tested by opening a command edit form, finding a step with an image reference field, opening the dropdown, and confirming available images with thumbnails appear and selection populates the field correctly.

**Acceptance Scenarios**:

1. **Given** a command form is open with a primitive tap step, **When** the author clicks the image selector, **Then** a dropdown opens listing all available images, each showing a thumbnail and its ID.
2. **Given** the image dropdown is open, **When** the author selects an image, **Then** the step's image reference is set to that image's ID and the dropdown closes.
3. **Given** the image dropdown is open, **When** the author types a partial image name in the search field, **Then** the list filters to matching images in real time.
4. **Given** no images have been captured yet, **When** the author opens the image selector, **Then** the dropdown shows an empty state message indicating no images are available.
5. **Given** a step already has an image ID assigned, **When** the form loads, **Then** the selector displays the current image's thumbnail and ID as the selected value.

---

### User Story 2 - Select Image in Sequence Step Conditions (Priority: P1)

A bot author is editing sequence steps that use image-visible or wait-for-image conditions. Instead of typing an image ID into a text box, they use an image selector dropdown to pick the image visually.

**Why this priority**: Sequence step conditions are the second most common place where image IDs are entered. Visual selection reduces mistakes in complex sequences with many image references.

**Independent Test**: Can be fully tested by opening a sequence, editing a step with an image condition (wait-for-image or image-visible), and confirming the dropdown allows visual image selection.

**Acceptance Scenarios**:

1. **Given** a sequence step has a wait-for-image condition, **When** the author opens the image selector, **Then** available images are shown with thumbnails and the author can pick one.
2. **Given** a sequence step has an image-visible condition, **When** the author opens the image selector, **Then** available images are shown with thumbnails and the author can pick one.
3. **Given** the author selects an image from the dropdown, **Then** the condition's image ID field is updated to the selected image's ID.

---

### User Story 3 - Select Image in Loop and Break Conditions (Priority: P2)

A bot author is configuring a loop block or a break step that triggers based on whether a specific image is visible. They use the image selector dropdown to choose the image without typing its ID.

**Why this priority**: Loop and break conditions are less common than command steps and sequence conditions, but the same usability problem applies. Resolving this ensures consistency across all authoring surfaces.

**Independent Test**: Can be fully tested by opening a sequence with a loop block or break step that has an image-visible condition and confirming the image selector works there.

**Acceptance Scenarios**:

1. **Given** a loop block has an image-visible exit condition, **When** the author opens the image selector, **Then** available images with thumbnails appear and selection updates the condition.
2. **Given** a break step has an image-visible condition, **When** the author opens the image selector, **Then** available images with thumbnails appear and selection updates the condition.

---

### Edge Cases

- What happens when the available images list is very long (50+ images)? The dropdown must remain usable via scrolling and search filtering.
- What happens when the currently assigned image ID no longer exists (image was deleted)? The selector should show the stale ID as text with a visual indicator that it is not found in the image library.
- What happens when the image thumbnail cannot be loaded? A placeholder icon is shown in its place.
- What happens when the user clears the selection? The image reference field becomes empty/unset where the field is optional.
- What happens if the image list fetch fails on dropdown open? An error message is shown with a retry option (FR-012).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST replace every free-text image ID input field with an image selector dropdown component across all authoring surfaces (command form steps, sequence step conditions, loop block conditions, break step conditions).
- **FR-002**: The image selector dropdown MUST fetch and display the current image library each time the dropdown is opened, so authors always see an up-to-date list without needing to reload the page.
- **FR-003**: Each entry in the dropdown list MUST show a miniature thumbnail of the image alongside its ID/name. (Size constraint specified in FR-009.)
- **FR-004**: The image selector MUST include a search/filter field that narrows the displayed list in real time as the user types.
- **FR-005**: Selecting an image from the dropdown MUST populate the associated data field with the selected image's ID and close the dropdown.
- **FR-006**: When an image selector field already has a value, it MUST display the selected image's thumbnail and ID as the current selection.
- **FR-007**: When the image library is empty, the dropdown MUST display a clear empty-state message.
- **FR-008**: When a previously assigned image ID is not found in the current image library, the selector MUST display the stale ID with a visual warning indicator. For the required field (LOC-01), the parent form MUST block saving and show a validation error until the stale reference is replaced with a valid image.
- **FR-009**: Thumbnail images in the dropdown MUST be small enough (miniature size — 32×32px recommended) that the list remains compact and scannable. See also FR-003 (thumbnails required per entry).
- **FR-010**: For optional image fields (LOC-02 through LOC-09), the selector MUST provide a way to clear the current selection. LOC-01 (primitive tap image) is required and MUST NOT offer a clear action.
- **FR-011**: While the image list is being fetched, the dropdown MUST show a loading indicator so the author knows data is being retrieved.
- **FR-012**: If the image list fetch fails, the dropdown MUST show an error message and allow the author to retry.

### Affected UI Locations

The following locations contain image ID text fields that MUST be replaced:

| Location | Context | Required / Optional |
|----------|---------|---------------------|
| **LOC-01** | Command form — Primitive tap step: "Primitive tap image ID" | **Required** |
| **LOC-02** | Command form — Wait for image step: "Wait image ID" | Optional |
| **LOC-03** | Command form — Detection step: "Reference image ID" | Optional |
| **LOC-04** | Sequences page — Step editor (inline): "Wait image ID" | Optional |
| **LOC-05** | Sequences page — Step editor (inline): "Image Id" (image-visible condition) | Optional |
| **LOC-06** | Sequences page — Add step modal: "Wait image ID" | Optional |
| **LOC-07** | Sequences page — Edit step form: "Wait image ID" | Optional |
| **LOC-08** | Loop block header — Image-visible exit condition: "Image ID" | Optional |
| **LOC-09** | Break step row — Image-visible condition: "Image ID" | Optional |

### Key Entities

- **Image**: A captured screenshot region stored in the image library, identified by a unique ID/name and represented visually by its stored image data.
- **Image Selector**: A reusable UI component that presents the image library as a searchable dropdown with thumbnails, replacing raw text inputs for image ID entry.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 9 identified image ID text input locations are replaced with the image selector dropdown component — verifiable by inspection.
- **SC-002**: Authors can select any image from the dropdown in under 10 seconds in a library of up to 50 images, without needing to know or type the image ID.
- **SC-003**: The image selector dropdown is implemented as a single reusable component reused across all affected locations (zero duplicate implementations).
- **SC-004**: Thumbnail images render correctly for all stored images when the dropdown is opened.
- **SC-005**: Search filtering reduces the displayed list within one keystroke of user input, with no noticeable lag.
- **SC-006**: Authors selecting an image via the dropdown make fewer image-ID errors compared to free-text entry (validated by the absence of "image not found" errors in execution logs during normal authoring).

## Clarifications

### Session 2026-05-30

- Q: Which image fields can be left blank (optional) vs. must always have a value (required)? → A: LOC-03 (detection reference) and all condition-based image fields (LOC-02, LOC-04 through LOC-09) are optional; only LOC-01 (primitive tap image) is required.
- Q: When the required field (LOC-01) contains a stale image ID that no longer exists, what should happen when the author tries to save? → A: Block save and show a validation error.
- Q: Should the image selector always reflect the latest image library, or is a page-load snapshot acceptable? → A: Always fresh — the dropdown fetches the current image list each time it is opened.

## Assumptions

- The image library API already provides a way to list all available images and retrieve their thumbnail/preview data; no new backend endpoints need to be created.
- Image thumbnails are already stored or can be derived from existing image data without additional processing.
- The existing `ImagePicker.tsx` component was evaluated as a starting point but found unsuitable: it uses a native `<select>` element that cannot embed thumbnail images, and it fetches on mount rather than on each open. A new `ImageSelectorDropdown` component is built from scratch; `ImagePicker.tsx` is audited for remaining usages and retired if superseded (see T015).
- The feature does not affect the image creation or upload flow (the "Image name" field in the emulator capture cropper creates a new image and is out of scope).
- The "Image ID" field on the Images list page (for creating a new image entry) is also out of scope, as it defines a new ID rather than selecting an existing one.
