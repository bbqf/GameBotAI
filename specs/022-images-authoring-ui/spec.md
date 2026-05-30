# Feature Specification: Images Authoring UI

**Feature Branch**: `[021-images-authoring-ui]`  
**Created**: 2025-12-30  
**Status**: Draft  
**Input**: User description: "Extend Authoring UI with the Images. In the same look and feel as other Authoring pages, implement all functions of Images API. Additionally, extend the GET /api/images/{id} so that it actually returns the saved image and display it in the web UI. Table on the list page should list the ID's only, click on that  should open a detail page, showing the image and providing a possibility to overwrite an image (make a choice if a new endpoint PUT is needed or a POST with the same ID can be reused). From the detail page it should also be possible to delete the image, provided it's not used in the triggers."

## Clarifications

### Session 2025-12-30

- Q: Which columns should the detection results table display? → A: Use matches schema fields: templateId, score, x, y, width, height, overlap, plus note when maxResults cap is hit.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse image IDs (Priority: P1)

An author opens the Images section, sees a table of image IDs, and selects one to view details without needing other metadata.

**Why this priority**: Listing and drilling into images is the foundation for all other actions (overwrite, delete) and must match the existing authoring look and feel.

**Independent Test**: Launch the Images list, confirm IDs render, click an ID, and verify navigation lands on a detail page for that ID.

**Acceptance Scenarios**:

1. **Given** stored images exist, **When** the author opens the Images list, **Then** only image IDs appear in the table with pagination or scrolling consistent with other authoring pages.
2. **Given** an image ID in the list, **When** the author clicks it, **Then** a detail view loads for that ID.

---

### User Story 2 - View and overwrite image (Priority: P2)

An author opens an image detail page, sees the stored image rendered, and replaces it by uploading a new file for the same ID.

**Why this priority**: Ensures the GET /api/images/{id} endpoint returns the saved image and supports refresh/overwrite without creating new IDs.

**Independent Test**: For an existing image ID, open the detail page, confirm the image preview loads, upload a replacement file, and verify the new image displays after save.

**Acceptance Scenarios**:

1. **Given** an existing image ID, **When** the author opens its detail page, **Then** the stored image renders using the updated GET /api/images/{id} response.
2. **Given** the author selects a new image file, **When** they submit overwrite for that ID, **Then** the stored content is replaced using PUT /api/images/{id} and the preview refreshes to the new image.

---

### User Story 3 - Run image detection (Priority: P3)

An author opens an image detail page, clicks Detect, supplies or accepts detection parameters, and reviews detection results in a table.

**Why this priority**: Enables authors to validate images against detection without leaving the authoring UI, using the existing /api/images/detect endpoint.

**Independent Test**: From a detail page, trigger detection with defaults (maxResults=1, threshold=0.86, overlap=0.1); verify the POST is issued, results render in a table mirroring the matches array, and a note indicates if maxResults capped the output.

**Acceptance Scenarios**:

1. **Given** an image is open on the detail page, **When** the author clicks Detect with default parameters, **Then** the UI posts to /api/images/detect with maxResults=1, threshold=0.86, overlap=0.1 and shows results in a table with one row per match.
2. **Given** detection returns fewer matches than requested, **When** the table renders, **Then** it lists all matches with columns templateId, score, x, y, width, height, overlap and states whether the maxResults limit was reached or more matches may exist.
3. **Given** the author edits parameters, **When** they rerun detection, **Then** the request uses the updated values and the table refreshes accordingly.

---

### User Story 4 - Delete unused image (Priority: P4)

An author removes an image that is not referenced by any triggers from the detail page after confirming deletion.

**Why this priority**: Keeps the image store clean while protecting trigger configurations.

**Independent Test**: Select an ID not used by triggers, delete it from the detail page, and verify it disappears from the list and cannot be fetched afterward.

**Acceptance Scenarios**:

1. **Given** an image ID not referenced by triggers, **When** the author confirms deletion, **Then** the image is removed and no longer appears in the list or detail view.
2. **Given** an image ID referenced by one or more triggers, **When** the author attempts deletion, **Then** the operation is blocked with a message naming the blocking trigger IDs and the image remains available.

---

### Edge Cases

- Uploading a non-image file or a file exceeding the allowed size is rejected with a clear error message and no partial save.
- GET /api/images/{id} for a missing ID returns a not-found response that the UI surfaces as a friendly message with a link back to the list.
- Concurrent overwrite attempts on the same ID result in the last confirmed submission winning, and the UI signals if the displayed preview is stale relative to the latest save.
- Attempting to delete while triggers reference the image always blocks deletion and indicates which triggers must be updated first.
- If the list is empty, the page still loads with guidance on how to add the first image.
- Detection with zero matches shows an empty table state and a note that the image produced no detections under the chosen parameters; detection errors surface non-fatal messages without breaking navigation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Images list page MUST present stored images as a table of IDs only, using the established authoring UI layout and controls.
- **FR-002**: Selecting an ID MUST navigate to a detail page that displays the stored image preview using GET /api/images/{id} returning the saved image content and its content type.
- **FR-003**: When GET /api/images/{id} is called for a missing ID, the UI MUST show a clear not-found message and provide navigation back to the list without error dialogs.
- **FR-004**: Authors MUST be able to upload a new image by supplying an ID and file; successful creation adds the ID to the list and enables immediate detail navigation.
- **FR-005**: Overwriting an existing image MUST use PUT /api/images/{id} with the replacement file; on success the stored content is replaced while retaining the same ID and any metadata.
- **FR-006**: The detail page MUST refresh the preview and any displayed metadata immediately after a successful overwrite to reflect the latest saved image.
- **FR-007**: Upload and overwrite operations MUST validate file type (standard image formats) and enforce a maximum file size of 10 MB, returning actionable errors when validation fails.
- **FR-008**: From the detail page, authors MUST be able to initiate detection via POST /api/images/detect, with default parameters maxResults=1, threshold=0.86, overlap=0.1, and adjust parameters before sending.
- **FR-009**: Detection results MUST render in a table using matches array fields: templateId, score, x, y, width, height, overlap; the UI MUST indicate when maxResults was reached so authors know more matches may exist.
- **FR-010**: The detail page MUST provide a delete action; deletion MUST proceed only when the image is not referenced by any triggers and must be blocked with reference details when dependencies exist.
- **FR-011**: After successful deletion, the image ID MUST be removed from the list and further GET requests for that ID MUST return not-found.
- **FR-012**: All user-facing error states (validation failures, blocked deletion, missing image, detection errors) MUST be surfaced with concise guidance consistent with other authoring pages.

### Key Entities *(include if feature involves data)*

- **Image Asset**: Identified by ID; includes filename (if provided), content type, size (validated against 10 MB limit), created/updated timestamps, and stored binary content retrievable via GET /api/images/{id}.
- **Trigger Reference**: Mapping between triggers and image IDs used to enforce deletion protection when an image is referenced.

## Dependencies and Assumptions

- Existing Images API endpoints are available to be wired into the authoring UI; no new storage mechanism is introduced beyond current image persistence.
- Authoring UI layout components and navigation patterns already exist and will be reused to match the established look and feel.
- Trigger definitions expose image ID references so the UI can check and surface dependencies before deletion.
- Standard image formats (e.g., png, jpg, jpeg) are acceptable, and the 10 MB size limit is sufficient for authoring needs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can open an image detail from the list and see the preview rendered within 2 seconds for stored images up to 10 MB on a standard office network.
- **SC-002**: Uploading or overwriting an image for an existing ID completes with the new preview visible within 5 seconds for files up to 10 MB.
- **SC-003**: 100% of deletion attempts on referenced images are blocked with a message that identifies at least one referencing trigger ID, and no referenced image is removed during testing.
- **SC-004**: Deleting an unreferenced image removes it from the list and prevents further retrieval via detail view within 2 seconds, with zero broken trigger references reported after deletion.
