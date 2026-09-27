# Feature Specification: Detect does not report a time-limited measurement as an absence

**Feature Branch**: `master-y1x20e` (spec number 108)  
**Created**: 2026-09-27  
**Status**: Implemented  
**Input**: GitHub issue #223: "POST /api/images/detect returns an empty result with limitsHit:true for an image with alternates". Full description: see the issue and the feature description that started this spec.

## Background

`POST /api/images/detect` sometimes returns `{"matches":[],"limitsHit":true,"masked":false,"retainedPixelCount":0}` for a reference image that has alternates. Other calls on the same capture find the image at a high score. The operation description says: "A 200 always means a measurement was taken, so an empty matches array is a real absence". Thus the response breaks the contract.

### Cause

The service has one time limit for each detect call (`Service:Detections:TimeoutMs`, default 500 ms). When the limit expires, the service stops the match and returns a 200 with an empty `matches` array and `limitsHit: true`. This is the only path that gives `masked: false` and `retainedPixelCount: 0` together with `limitsHit: true` and no matches.

For an image with alternates, the service scores the named image and then each alternate, one after the other, in the same time limit. With a low threshold (the issue uses `0.05`), almost every position on the screen is a candidate for each reference, so each reference takes a large part of the limit. Two alternates make the work three times larger, but the limit stays the same. Thus the call goes near the limit, and the limit expires in some calls only. The frequency increases with the number of alternates.

Sequence conditions (`imageVisible`) and image-anchored taps use the same matcher, but they do not use this time limit. Thus the defect does not occur in these consumers.

## Clarifications

### Session 2026-09-27

- Q: Which of the two permitted solutions does the fix use when the time limit expires: an explicit failure, or the matches that the service found before the limit? → A: An explicit failure. Rationale: a partial result can omit a better match from a reference that the service did not score, so the caller cannot trust it; a failure is clear.
- Q: Which status and error code does the explicit failure use? → A: `504` with code `detection_timeout` and a message. Rationale: the API already uses `504 device_timeout` for an operation that did not complete in its time limit; the `{ code, message }` shape is the same as the other detect failures.
- Q: How does the fix remove the cause for images with alternates? → A: Each reference in the set gets the full configured time limit. The time limit of a call is `TimeoutMs` multiplied by the number of references (the named image plus its alternates that loaded). Rationale: an image without alternates keeps the same limit, and each alternate gets the same limit that it gets alone.
- Q: Does `limitsHit` keep a meaning? → A: Yes. `limitsHit: true` means only that `maxResults` cut the list of matches. A 200 response never has `limitsHit: true` because of the time limit. Rationale: this agrees with the README text and with the web UI message "Detection reached max results; more matches may exist."
- Q: Do the `imageVisible` condition and image-anchored taps change? → A: No. They do not use the time limit, so they do not have the defect. The research notes record the code path. Rationale: the non-goals forbid changes to the retry policy and the arrival checks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A 200 with no matches is a real absence (Priority: P1)

A script calls `POST /api/images/detect`. When the response is a 200 with an empty `matches` array, the image is not on the screen. When the service cannot complete the measurement in its time limit, the call fails explicitly.

**Why this priority**: This is the contract that the issue shows as broken. Guards and taps depend on it.

**Independent Test**: Use a match that does not complete before the time limit of the call. Call `detect`. The call returns `504` with code `detection_timeout`, not a 200.

**Acceptance Scenarios**:

1. **Given** a detect call where the time limit expires before the match completes, **When** the service replies, **Then** the status is `504`, the body is `{ "code": "detection_timeout", "message": "<text>" }`, and the body has no `matches` array.
2. **Given** a detect call that completes in its time limit and finds no match, **When** the service replies, **Then** the status is `200`, `matches` is empty, and `limitsHit` is `false`.
3. **Given** a detect call that completes and finds more matches than `maxResults`, **When** the service replies, **Then** the status is `200`, `matches` has `maxResults` items, and `limitsHit` is `true`, as before.

---

### User Story 2 - An image with alternates gets the same time for each reference (Priority: P1)

A script calls `detect` many times on the same capture for an image with two alternates, with a low threshold. Each call returns the best match.

**Why this priority**: This removes the cause of the intermittent failure in the issue (acceptance criteria 3 and 4).

**Independent Test**: Calculate the time limit for a reference set with a primary and two alternates. It is three times the configured limit. Calculate it for an image without alternates. It is the configured limit.

**Acceptance Scenarios**:

1. **Given** an image with two alternates and `TimeoutMs` 500, **When** the service scores it, **Then** the time limit of the call is 1500 ms.
2. **Given** an image without alternates and `TimeoutMs` 500, **When** the service scores it, **Then** the time limit of the call is 500 ms, as before.
3. **Given** an image with alternates where one alternate file is missing, **When** the service scores it, **Then** the time limit counts only the references that loaded.

---

### User Story 3 - The API description tells the truth (Priority: P2)

An integrator reads the OpenAPI document for `POST /api/images/detect`. The description and the error examples show the `504 detection_timeout` failure and the meaning of `limitsHit`.

**Why this priority**: Callers must know the new failure to handle it. It is not necessary for the fix itself.

**Independent Test**: Read `/swagger/v1/swagger.json`. The detect operation has a `504` response example with code `detection_timeout`, and the description names the time limit.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document, **When** an integrator reads the detect operation, **Then** it has a `504` example with `code: detection_timeout`.
2. **Given** the OpenAPI document, **When** an integrator reads the detect description, **Then** it says that a time limit failure is a `504`, and that `limitsHit` means that `maxResults` cut the list.

### Edge Cases

- The client closes the request before the match completes: the service stops the match. The response does not matter because the client does not read it.
- `TimeoutMs` has a value below 1: the service uses 1 ms for each reference, as before.
- A very large set of alternates: the time limit increases with the number of references. The limit applies only to this endpoint.
- `POST /api/images/detect-all` does not use the time limit, so it does not change.
- An image without alternates on a slow screen: when its time limit expires, the call now returns `504` and not a 200. This is the same contract fix for all images.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `POST /api/images/detect` MUST NOT return a `200` when the time limit expires before the match completes.
- **FR-002**: When the time limit expires, the service MUST return `504` with body `{ "code": "detection_timeout", "message": "<text>" }`. The message MUST tell the caller that the call did not measure the screen and that the caller can send it again.
- **FR-003**: The time limit of a detect call MUST be `TimeoutMs` multiplied by the number of references that the service scores (the named image plus each alternate that loaded). The value for each reference MUST be at least 1 ms.
- **FR-004**: For an image without alternates, the time limit MUST stay `TimeoutMs`, and the matches, scores and `limitsHit` value MUST stay the same as before.
- **FR-005**: In a `200` response, `limitsHit: true` MUST mean only that `maxResults` cut the list of matches.
- **FR-006**: The service MUST write a warning log entry when the time limit expires. The entry MUST contain the image ID, the number of references, the time limit and the elapsed time.
- **FR-007**: The OpenAPI description of `POST /api/images/detect` MUST describe the `504 detection_timeout` failure and the meaning of `limitsHit`, and MUST have a `504` example.
- **FR-008**: The `imageVisible` sequence condition, image-anchored taps, the retry policy, `POST /api/images/detect-all` and the matcher scores MUST NOT change.
- **FR-009**: The README and the changelog MUST describe the new failure and the new time limit rule.

### Key Entities

- **Detection time limit**: the time that one detect call has for its match. It is `TimeoutMs` for each reference in the set.
- **Reference set**: the named image and the alternates that loaded for it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In an automated test, a detect call whose match does not complete in the time limit returns `504 detection_timeout` and never a `200` with an empty `matches` array.
- **SC-002**: For an image with N alternates, the time limit of a detect call is N+1 times the configured limit.
- **SC-003**: All existing detect tests for images without alternates pass with no change to their expected matches, scores or `limitsHit` values.
- **SC-004**: The OpenAPI document has the `504 detection_timeout` example for `POST /api/images/detect`.

## Assumptions

- The observed empty responses came from the time limit path. The response `masked: false, retainedPixelCount: 0` with `limitsHit: true` and no matches occurs only on that path.
- The matcher speed for one reference does not change. The fix does not make the matcher faster.
- Callers that already treat `limitsHit: true` with no matches as "not measured" continue to work: they now get a `504`, which they also send again or treat as a failure.
