# Feature Specification: OCR Read Endpoint

**Feature Branch**: `128-ocr-read-endpoint`
**Created**: 2026-10-07
**Status**: Draft
**Input**: User description: "Expose the OCR engine as a read endpoint (issue #274). An author must test a region, a parser, or an account before a production run, and see what the engine returns for a given picture."

## Clarifications

### Session 2026-10-07

- Q: Which status code does a failed capture or an unavailable OCR engine return? → A: 502 for a failed capture, 503 for an engine that is not available, each with a clear message. Never 500 for an input error.
- Q: Does the endpoint cache or reuse an earlier read? → A: No. Each request runs a fresh capture (serial) or reads the stored capture again, and runs the engine again. This lets the author test for stale results.
- Q: Which rule makes a region "outside the frame"? → A: Any part of the rectangle outside the frame (x or y below 0, or x+width or y+height above the frame size) gives 400. The message gives the frame size.
- Q: Which capture ids are valid? → A: The capture ids that the existing capture store of the project holds. An expired or unknown id gives 404.
- Q: Does the answer return the parsed value in a fixed form? → A: Yes. The parsed value is the duration as `hh:mm:ss` text, plus the total seconds. The answer also names the parser that ran.
- Q: Is the parser name case-sensitive? → A: No. The `ocrOffset` step has no parser name (it always uses the duration parser). The endpoint defines a registry of parser names with one name, `hh:mm:ss`, compared without case.

### Session 2026-10-07 (loop 2)

- Q: Which check order applies when a request has more than one fault? → A: First the checks that need no lookup (body, source count, parser name, region size above zero): 400. Then the lookup of the serial or capture id: 404. Then the capture or decode: 502. Then the engine availability: 503. Last, the region-inside-frame check (it needs the frame size): 400. A contract test covers one mixed-fault request.
- Q: Which frame size does the region check use for a stored capture? → A: The size of the decoded stored picture. The check is one shared step for both sources. Unit tests cover a region that ends exactly at the frame edge (valid) and one pixel beyond it (invalid), for both sources.
- Q: What if more than one running session has the same serial? → A: The endpoint uses the first running session that matches the serial. A test covers this case.
- Q: What if the `region` object is missing? → A: 400 `invalid_request`. A test covers this case.

### Session 2026-10-07 (loop 1)

- Q: What does a `serial` need? → A: A running session for that serial. With no running session the answer is 404 (`serial_not_found`).
- Q: Which status codes cover the host faults? → A: 502 when the capture fails or the crop or image decode fails. 503 when the host has no capture service, no OCR engine, or the engine throws.
- Q: What is the pre-processing that the endpoint shares? → A: Crop of the region, then the engine read. The step has no separate scale step. The same shared code does both.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read a region of the live screen (Priority: P1)

An author names an emulator and a rectangular region of the screen. The system takes the current frame of that emulator, reads the text in the region, and returns the raw text and a confidence value. The author sees what the OCR engine returns before a production run uses the same region.

**Why this priority**: This is the core need. Today the only trace of a read is one log line of a run. The author cannot call the engine alone.

**Independent Test**: Send a read request with an emulator serial and a region. The answer holds the raw text and a confidence value. The emulator receives no input.

**Acceptance Scenarios**:

1. **Given** a known emulator that shows a countdown, **When** the author sends a read request with the serial and the region of the countdown, **Then** the answer holds the raw text of the region and a confidence value.
2. **Given** the same request, **When** the system handles it, **Then** it sends no tap, swipe, key, or other input to the emulator.

---

### User Story 2 - Read a region of a stored capture (Priority: P1)

An author names a stored capture and a region. The system reads the text in the region of that capture. The system takes no new capture. The author can repeat the read on the same picture, for example to check if a bad read comes from the picture or from the engine.

**Why this priority**: Repeatable reads on one picture separate stale results from engine faults. This is the evidence gap in the issue.

**Independent Test**: Send a read request with a capture id and a region two times. Both answers hold the same raw text. No new capture appears.

**Acceptance Scenarios**:

1. **Given** a stored capture, **When** the author sends a read request with the capture id and a region, **Then** the answer holds the raw text and a confidence value, and no new capture is taken.
2. **Given** the same frame and region, **When** a read runs through this endpoint and through the `ocrOffset` step of `reschedule-self`, **Then** both give the same raw text.

---

### User Story 3 - Test a parser on the read text (Priority: P2)

An author names a parser (for example `hh:mm:ss`) in the request. The answer holds the parsed value when the raw text parses. When the text does not parse, the answer holds the raw text, no parsed value, and a clear reason. A parse failure is not an HTTP error.

**Why this priority**: The author tests the full read-and-parse path that the `ocrOffset` step uses, but this adds value only after the read itself works.

**Independent Test**: Send a read request with a parser name on a picture with a clean countdown, then on a picture with a bad read. The first answer holds a parsed value. The second holds the raw text, no parsed value, and a reason, with a success status code.

**Acceptance Scenarios**:

1. **Given** a region with the text `02:10:35` and the parser `hh:mm:ss`, **When** the author sends the request, **Then** the answer holds the parsed duration.
2. **Given** a region with the text `8 ODeIOs35` and the parser `hh:mm:ss`, **When** the author sends the request, **Then** the answer holds the raw text, no parsed value, and a reason that says the text did not parse, with status 200.

---

### User Story 4 - Clear errors for bad requests (Priority: P2)

An author who sends a bad request gets a clear message and a correct status code, never a server fault for an input error.

**Why this priority**: Clear errors let the author fix a request fast.

**Independent Test**: Send each bad request listed below and check the status code and the message.

**Acceptance Scenarios**:

1. **Given** a request with no serial and no capture id, **When** it is sent, **Then** the answer is 400 with a clear message.
2. **Given** a request with both a serial and a capture id, **When** it is sent, **Then** the answer is 400 with a clear message.
3. **Given** a request with an unknown parser name, **When** it is sent, **Then** the answer is 400 and the message names the supported parsers.
4. **Given** a region with a width or height that is zero or negative, or a region that is not fully inside the frame, **When** the request is sent, **Then** the answer is 400 with a clear message.
5. **Given** an unknown serial or an unknown capture id, **When** the request is sent, **Then** the answer is 404.

---

### Edge Cases

- The region is empty of text: the answer holds empty raw text and a confidence value, and no error.
- The region has the same size as the frame: this is valid.
- The emulator exists, but the capture of a frame fails: the answer is 502 `capture_failed` with a clear message. It is not a 500.
- The OCR engine is not available: the answer is 503 `ocr_unavailable` with a clear message.
- The request has no body or a body that is not valid JSON: the answer is 400.
- The crop gives a null image (a GDI error): the answer is 502 `capture_failed`. A unit test MUST cover this case.
- A stored-capture PNG fails to decode: the answer is 502 `capture_failed`. A unit test MUST cover this case.
- The host is not Windows, or has no OCR engine: the answer is 503 `ocr_unavailable`. A unit test MUST cover this case (the engine is missing or throws).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST offer a read endpoint, `POST /api/ocr/read`, that takes a body with exactly one source (a `serial` or a `captureId`), a `region` (x, y, width, height), and an optional parser name.
- **FR-002**: With a `serial`, the system MUST take the current frame of the emulator, crop the region, run the OCR engine, and return the raw text and a confidence value.
- **FR-003**: With a `captureId`, the system MUST use the stored capture, take no new capture, and otherwise do the same as FR-002.
- **FR-004**: The system MUST use the same OCR engine and the same pre-processing (crop, then read) as the `ocrOffset` step of `reschedule-self`. The endpoint and the step MUST share one code path. The code MUST NOT be copied.
- **FR-005**: For the same frame and region, the raw text from the endpoint MUST equal the raw text that the `ocrOffset` step reads.
- **FR-006**: When a parser name is given, the system MUST return the parsed value when the text parses. When it does not parse, the system MUST return the raw text, no parsed value, and a clear reason, with a success status code.
- **FR-007**: The system MUST NOT send a tap, swipe, key, or other input to the emulator.
- **FR-008**: The system MUST return 400 with a clear message for: no source, two sources, an unknown parser name, a region with a non-positive size, a region that is not fully inside the frame, and a body that is missing or not valid.
- **FR-009**: The system MUST return 404 for an unknown capture id, and for a serial that has no running session.
- **FR-010**: The system MUST NOT return 500 for an input error, a capture failure, or an engine fault. A failed capture returns 502 `capture_failed`. A null crop (a GDI error) returns 502 `capture_failed`. A stored-capture PNG that fails to decode returns 502 `capture_failed`. A host with no capture service returns 503. A host with no OCR engine returns 503 `ocr_unavailable`. An engine that throws returns 503 `ocr_unavailable`. Each answer has a clear message.
- **FR-011**: Each request MUST run the capture (serial) or the stored-capture read, and the engine, again. The endpoint MUST NOT cache or reuse an earlier read.
- **FR-012**: A region is valid only when it lies fully inside the frame. The 400 message for a bad region MUST give the frame size.
- **FR-013**: The parsed value MUST be given as `hh:mm:ss` text and as total seconds, and the answer MUST name the parser that ran. The parser name MUST be compared without case.
- **FR-014**: The endpoint MUST show in the OpenAPI document with request and response schemas and descriptions, by the method that the project uses for other endpoints.
- **FR-015**: The repository documentation of the HTTP API MUST name the new endpoint.
- **FR-016**: The change MUST bump the minor release version from 4 to 5 and set `updatedAtUtc` in `installer\versioning\version.override.json`. The patch value MUST stay 0.
- **FR-017**: The change MUST NOT alter the behaviour, the fallback logic, or the log line of the `ocrOffset` step. It MUST NOT add an OCR engine, change the pre-processing, add a sequence action type, or change the web UI.

### Key Entities

- **Read request**: one source (serial or capture id), a region, and an optional parser name.
- **Region**: a rectangle with x, y, width, and height, in pixels of the frame.
- **Read result**: the source (the serial or the capture id), the frame size, the raw text, a confidence value, and, when a parser is named, either the parsed value or a reason for the parse failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can read a region of a live emulator or of a stored capture with one request, and sees raw text and a confidence value.
- **SC-002**: For 100% of test pictures and regions, the endpoint raw text equals the raw text of the `ocrOffset` step.
- **SC-003**: In every test, the emulator receives zero input from a read request.
- **SC-004**: Every bad-request case in the acceptance scenarios gives the correct status code (400 or 404) and a non-empty message. Each host-fault edge case gives 502 or 503. None gives a 500.
- **SC-005**: A parse failure gives status 200 with the raw text and a reason in 100% of test cases.
- **SC-006**: The OpenAPI document and the API documentation both name the endpoint.

## Assumptions

- The goal to read a region in under 1 s at p95 is a design target only. No test checks it, and no success criterion depends on it.
- The crop code needs the Windows platform attribute. A host that is not Windows, or that has no OCR engine, answers 503 `ocr_unavailable`.
- The endpoint defines the supported parser names. The only name is `hh:mm:ss`. It maps to the duration parser that the `ocrOffset` step uses.
- The endpoint uses the same access rule as all other API endpoints of the service. It adds no new access rule.
- The confidence value has the same meaning as the value that the OCR engine gives to the `ocrOffset` step.
- Endpoint mapping (I1): `Program.cs` stays thin. The endpoint code is in `OcrReadEndpoints.cs` with a `MapOcrReadEndpoints` method. `Program.cs` gets one call line next to `app.MapCoverageEndpoints()`, the existing pattern for `/api/ocr/coverage`. The plan and the tasks must use this site. They must not use `GameBotServiceSetup.cs`.
- Test file names (I2): the plan and the tasks must use `tests\contract\Ocr\OcrReadContractTests.cs`, `tests\contract\Ocr\OcrReadNoInputTests.cs`, and `tests\contract\Ocr\OcrReadOpenApiTests.cs`.
- Null crop (U4): `OcrRegionReader.Read` does not throw when the crop is null. It returns a result that tells the caller that the crop failed. The endpoint maps this result to 502. A unit test covers this case.
- OpenAPI schema (C1): `OcrReadSchemaFilter` follows the pattern of `ImageDetectCoordinatesSchemaFilter`. It is an `ISchemaFilter`. The project has no XML comments in Swagger, so the filter adds all descriptions.
- Language (D1): all new or changed text follows ASD-STE100 STE. This covers spec, plan, tasks, docs, code comments, and messages. Use short sentences and no idioms. When the plan and the tasks are generated again, they must be written in STE.
- Status sync (D2): the `STATUS.md` row and the `Status` field of this spec change in the same task. The task is the last implementation task. FR on documentation includes this rule.
- Task order (T1): the first service task (the MVP) includes all request validation. This covers the exactly-one-source check, the region-missing check, the region-size check, the parser-name check, and the shared region-inside-frame check. Later tasks do not add validation that the MVP needs.
