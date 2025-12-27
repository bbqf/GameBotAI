# Feature Specification: Disk-backed Reference Image Storage

**Feature Branch**: `001-image-storage`  
**Created**: 2025-11-26  
**Status**: Draft  
**Input**: User description: "Introduce the disk-backed storage for the images to be used as referenced images. The images should be stored on disk under the data directory."

### User Story 1 - Persist uploaded reference images (Priority: P1)

Users can upload a reference image once and rely on it across restarts; the system persists images under the `data/` directory and loads them on startup.

**Why this priority**: Enables stable, repeatable image-match triggers without re-uploading after service restarts.

**Independent Test**: Upload an image, verify it’s accessible via `GET /images/{id}`, restart the service, and verify the image remains resolvable without re-upload.

**Acceptance Scenarios**:

1. **Given** the service is running, **When** a user `POST /images` with `{ id: "Home", data: <base64> }`, **Then** the image is written to `data/images/Home.png` and `GET /images/Home` returns 200.
2. **Given** the image is persisted, **When** the service restarts, **Then** `GET /images/Home` still returns 200 without re-upload.

### User Story 2 - Resolve images by ID (Priority: P2)

Image-match triggers can resolve a reference image by `referenceImageId` from disk-backed store seamlessly.

**Why this priority**: Ensures triggers operate independently of memory lifetime.

**Independent Test**: Create an `image-match` trigger with `referenceImageId` referencing a persisted file; test endpoint returns `Satisfied` when screen matches.

**Acceptance Scenarios**:

1. **Given** `data/images/Home.png` exists, **When** an `image-match` trigger with `referenceImageId: "Home"` is tested, **Then** the evaluator loads `Home.png` from disk and uses it.

### User Story 3 - Controlled overwrite & cleanup (Priority: P3)

Users can overwrite an existing reference image by re-uploading with the same `id`, and can delete an image when no longer needed.

**Why this priority**: Prevents stale images and allows updates.

**Independent Test**: Upload image A (`id: Home`), verify; upload image B with same id, verify it replaced; delete the image and verify `GET /images/Home` returns 404.

**Acceptance Scenarios**:

1. **Given** `Home.png` exists, **When** a new `POST /images` with `id: Home` is sent, **Then** the file is overwritten, and subsequent resolutions use the new content.
2. **Given** an image exists, **When** `DELETE /images/{id}` is called, **Then** file is removed from `data/images` and `GET` returns 404.

### Edge Cases

- Upload non-image or corrupted data → return 400 with `invalid_image` and no file written.
- Upload with invalid id (empty/whitespace, path traversal characters) → 400 `invalid_request`.
- Concurrent uploads of same id → last write wins; intermediate writes are atomic to avoid partial files.
- Missing file on resolve (deleted externally) → `GET /images/{id}` returns 404; evaluator treats as Pending with `reason: reference_missing`.

### Functional Requirements

- **FR-001**: System MUST persist uploaded reference images under `data/images/{id}.png|.jpg`.
- **FR-002**: System MUST expose `POST /images` to upload `{ id, data(base64) }` and return 201 with `{ id }`.
- **FR-003**: System MUST expose `GET /images/{id}` returning 200 when resolvable, 404 when missing.
- **FR-004**: System MUST load all files under `data/images` on startup into the image store.
- **FR-005**: System MUST allow overwriting an existing image via `POST /images` with same `id`.
- **FR-006**: System SHOULD expose `DELETE /images/{id}` to remove persisted images.
- **FR-007**: Image-match evaluator MUST resolve images by id using disk-backed store and fail gracefully when missing.
- **FR-008**: System MUST validate id format (no path traversal, length <= 128, alphanumeric, dash/underscore allowed).

### Key Entities

- **ReferenceImage**: `{ id, format, bytes }` logical concept mapped to files under `data/images` and loaded into memory.

### Measurable Outcomes

- **SC-001**: Uploaded images remain resolvable across restarts (100% in happy path).
- **SC-002**: Image upload/resolve operations complete under 500 ms for 95% of requests.
- **SC-003**: 0 partial writes observed under concurrent uploads (atomicity ensured).
- **SC-004**: 90% of image-match trigger tests succeed without requiring re-upload after restart.
# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`  
**Created**: [DATE]  
**Status**: Draft  
**Input**: User description: "$ARGUMENTS"

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

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently - e.g., "Can be fully tested by [specific action] and delivers [specific value]"]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]
