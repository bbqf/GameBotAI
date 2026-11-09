# Feature Specification: Revalidate an image after an overwrite

**Feature Branch**: `182-revalidate-overwritten-image`
**Created**: 2026-11-09
**Status**: Implemented
**Input**: User description: "Issue #377 (B-040): An overwritten image stays stale in the browser. GET /api/images/{id} has no Cache-Control and no ETag. Closes #377."

## Clarifications

### Session 2026-11-09

- Q: Which `Cache-Control` value does the endpoint send? → A: Exactly `no-cache`. Rationale: the issue names it, and the browser then stores the image but revalidates on each use. `no-store` would stop all reuse and cost body transfer.
- Q: Is the ETag strong or weak? → A: Strong (quoted value, no `W/` prefix). Rationale: the ETag stands for the exact bytes, and range requests need a strong validator.
- Q: Does the ETag cover the bytes or only the time? → A: It MUST change when the bytes change, even when the time and size stay the same. Rationale: acceptance criterion 2 and the edge case of two overwrites with the same size.
- Q: Does the 404 answer get the new headers? → A: No. Only a successful image response gets them. Rationale: the non-goal keeps the error path unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the new picture after an overwrite (Priority: P1)

An operator corrects an image with `PUT /api/images/{id}`. The operator then opens the web UI (queue editor, parameter panel with image choices). The UI must show the new picture. The operator does not do a hard refresh and does not clear the browser cache.

**Why this priority**: Today the browser shows the old picture. The wrong picture next to the right name looks like a failed update. This is the only fault in the issue.

**Independent Test**: Overwrite an image with new bytes. Send `GET /api/images/{id}` with the ETag of the old bytes. The service returns 200 with the new bytes and a new ETag.

**Acceptance Scenarios**:

1. **Given** a stored image, **When** a client sends `GET /api/images/{id}`, **Then** the response has `Cache-Control: no-cache` and an `ETag` header.
2. **Given** a client that holds the ETag of the old bytes, **When** the image is overwritten with new bytes and the client sends a conditional `GET` with the old ETag, **Then** the service returns 200 with the new bytes and a different ETag.
3. **Given** an image that did not change, **When** a client sends `GET /api/images/{id}` with a matching `If-None-Match`, **Then** the service returns 304.

---

### User Story 2 - Keep cheap revalidation (Priority: P2)

A client that already sends `If-Modified-Since` keeps the 304 answer when the image did not change. The browser stores the image and asks the service each time. An unchanged image costs no body transfer.

**Why this priority**: The fix must not remove the revalidation that works today.

**Independent Test**: Send `GET /api/images/{id}` with `If-Modified-Since` set to the `Last-Modified` value of an unchanged image. The service returns 304.

**Acceptance Scenarios**:

1. **Given** an unchanged image, **When** a client sends `If-Modified-Since` with the `Last-Modified` value, **Then** the service returns 304.

---

### Edge Cases

- A client sends a non-matching `If-None-Match`: the service returns 200 with the full body.
- A client sends `If-None-Match: *` or a list of ETags: standard HTTP rules apply.
- The image does not exist: the service returns the same 404 as today, with no change.
- The bytes change twice with the same size: the ETag still changes, because it depends on the bytes (or a change marker of the bytes), not only on the size.
- A range request (`Accept-Ranges: bytes`) keeps the same behavior as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A `GET /api/images/{id}` response MUST have `Cache-Control: no-cache`.
- **FR-002**: A `GET /api/images/{id}` response MUST have an `ETag` header. The ETag MUST change when the stored bytes change.
- **FR-003**: A `GET /api/images/{id}` with a matching `If-None-Match` MUST return 304.
- **FR-004**: A `GET /api/images/{id}` with `If-Modified-Since` MUST still return 304 when the image did not change.
- **FR-005**: After `PUT /api/images/{id}` with new bytes, a conditional `GET` with the old ETag MUST return 200 with the new bytes.
- **FR-006**: The OpenAPI document MUST state the `Cache-Control` and `ETag` response headers of `GET /api/images/{id}`. `docs/architecture.md` MUST state them if the file describes the endpoint.
- **FR-007**: The feature MUST NOT change the `PUT /api/images/{id}` request or response, how the detector reads the stored bytes, the cache headers of other endpoints, or the path of the image.
- **FR-008**: The feature MUST NOT change the web UI code, unless the header change is not enough to show the new picture.

### Key Entities

- **Stored image**: The bytes of an image under an image id. It has a last-modified time. The ETag is a validator of these bytes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After an overwrite, 100% of the next loads of the image in the web UI show the new picture, with no hard refresh and no cache clear.
- **SC-002**: An unchanged image causes a 304 answer with no body on 100% of conditional requests (`If-None-Match` or `If-Modified-Since`).
- **SC-003**: A regression test fails on the code before the fix and passes after it.
- **SC-004**: The API documentation states the new headers.

## Assumptions

- The handler for `GET /api/images/{id}` is in the service project. The plan step finds it.
- The ETag can come from a hash of the bytes or from a change marker of the stored file. The plan step chooses.
- The browser revalidates a response with `Cache-Control: no-cache` on each use. This is standard HTTP behavior.
