# Feature Specification: OCR Text Condition for Sequences

**Feature Branch**: `158-ocr-text-condition`  
**Created**: 2026-10-21  
**Status**: Draft  
**Input**: User description: "GitHub issue #338: FR-035: add a condition type that matches text read by OCR. Add a sequence condition type `ocrText` with `region`, `expected` or `pattern`, and an optional `parser`. Closes #338."

## Clarifications

### Session 2026-10-21

- Q: Is the match case-sensitive and is `expected` an exact or a partial match? -> A: `expected` is an exact, case-sensitive match after white space at the start and end is removed. `pattern` is a regular expression that matches when it finds a match anywhere in the text. Rationale: this is the simplest rule, and `pattern` gives a looser match.
- Q: What does the condition do when the frame cannot be captured or the engine fails? -> A: It is false, and the run log gives the reason. Rationale: a sequence run must not crash, and `imageVisible` has the same fail-safe behaviour.
- Q: Does the web UI change? -> A: No. The issue asks for the sequence model and run only. Rationale: the issue has no UI request, and the non-goals forbid extra scope.
- Q: Which region rule applies at save time? -> A: Save time checks only that width and height are above zero. The "inside the frame" check happens at run time, because the frame size is not known at save time. A region outside the frame makes the condition false with a reason in the run log.
- Q: Does a parser change the compared value? -> A: Yes. With a parser, the comparison uses the parsed value in the text form that `POST /api/ocr/read` returns (`hh:mm:ss`). A text that does not parse makes the condition false.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Branch on text read from the screen (Priority: P1)

An author writes a sequence for a screen that shows the line "Interaction attempts left: N" in three states (ready, wait, empty). The number N tells the state. The author adds an `ocrText` condition to an `If` step. The condition names a region of the screen and the expected text. The sequence reads the text in the region and takes the branch that matches. A shift of one pixel in the game render does not change the result, because the condition compares text and not a small image.

**Why this priority**: This is the whole request. Without it, a sequence can decide a state by image only.

**Independent Test**: Save a sequence with an `If` step that has an `ocrText` condition. Run it on a frame where the region shows the expected text, then on a frame where it shows other text. The first run takes the `then` branch. The second run takes the `else` branch.

**Acceptance Scenarios**:

1. **Given** a region that shows the text "0", **When** an `If` step has an `ocrText` condition with `expected` "0" for that region, **Then** the condition is true and the `then` branch runs.
2. **Given** a region that shows the text "3", **When** the same step runs, **Then** the condition is false and the `else` branch runs.
3. **Given** a region that shows "Interaction attempts left: 0", **When** the condition has a `pattern` that matches the number 0 at the end of the line, **Then** the condition is true.

---

### User Story 2 - Use the condition in every condition place (Priority: P1)

An author uses the `ocrText` condition in an `If` step, in a `Loop` step, in a `breakCondition`, and as a child of an `all`, `any`, or `none` condition. The author can mix it with `imageVisible`, `commandOutcome`, and `lastRun` children.

**Why this priority**: The issue names each of these places in its acceptance criteria.

**Independent Test**: Save and run one sequence for each place. Each sequence takes the expected path.

**Acceptance Scenarios**:

1. **Given** a `Loop` step with an `ocrText` condition, **When** the text in the region matches, **Then** the loop follows its normal rule for a true condition.
2. **Given** a `breakCondition` with an `ocrText` condition, **When** the text matches, **Then** the loop stops.
3. **Given** an `all` condition with one `imageVisible` child and one `ocrText` child, **When** both are true, **Then** the `all` condition is true. When the `ocrText` child is false, the `all` condition is false.
4. **Given** an `any` condition or a `none` condition with an `ocrText` child, **When** the sequence runs, **Then** the result follows the normal rule of that condition.

---

### User Story 3 - Read the text with a parser (Priority: P2)

An author names a `parser` in the condition, as in `POST /api/ocr/read`. The system parses the text that it read. The condition compares the parsed result with the expected value.

**Why this priority**: The issue asks for the option, but the main use case needs only plain text.

**Independent Test**: Save a condition with `parser` `hh:mm:ss` and run it on a frame that shows a countdown. Run it on a frame with text that does not parse.

**Acceptance Scenarios**:

1. **Given** a region with the text "02:10:35" and the parser `hh:mm:ss`, **When** `expected` is "02:10:35", **Then** the condition is true.
2. **Given** a region with text that the parser cannot parse, **When** the condition runs, **Then** the condition is false and the run log shows the text that was read and that it did not parse.

---

### User Story 4 - See the text that was read in the run log (Priority: P1)

An author opens the run log of a sequence. For each `ocrText` evaluation, the log names the text that was read, the expected text or pattern, and the result. The author can find out why a branch was taken.

**Why this priority**: The issue requires it. Without it, a wrong read is hard to find.

**Independent Test**: Run a sequence with an `ocrText` condition. Open the run log. The log entry holds the text that was read.

**Acceptance Scenarios**:

1. **Given** a run with an `ocrText` condition, **When** the condition is evaluated, **Then** the log entry names the text that was read.
2. **Given** a condition that is a child of `all`, `any`, or `none`, **When** it is evaluated, **Then** its own log entry names the text that was read.

---

### Edge Cases

- The region is empty of text: the text read is empty. The condition is false unless `expected` or `pattern` matches empty text.
- The region is not fully inside the frame: the system rejects the saved condition when it can check the frame size, or the condition fails at run time with a clear reason in the run log. The condition is not true.
- The OCR engine is not available or fails: the condition is false and the run log shows a clear reason. The sequence does not crash.
- The condition has both `expected` and `pattern`, or has neither: the system rejects the sequence when it is saved, with a clear message.
- The `pattern` is not a valid regular expression: the system rejects the sequence when it is saved, with a clear message.
- The `parser` name is unknown: the system rejects the sequence when it is saved, and the message names the supported parsers.
- The region has a width or height that is zero or negative: the system rejects the sequence when it is saved.
- The text read has leading or trailing white space or line breaks: the comparison ignores white space at the start and end of the text.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept a new sequence condition type `ocrText`.
- **FR-002**: An `ocrText` condition MUST have a `region` (x, y, width, height). It MUST have exactly one of `expected` (text) or `pattern` (regular expression). It MAY have a `parser`.
- **FR-003**: When the condition is evaluated, the system MUST take the current frame of the emulator of the run, read the text in the region, and compare it with `expected` or `pattern`.
- **FR-004**: The condition MUST be true if and only if the text read matches `expected` (exact match, after removal of white space at the start and end) or the `pattern`.
- **FR-005**: When a `parser` is given, the system MUST apply the parser to the text that was read, with the same names and rules as `POST /api/ocr/read`. The condition then compares the parsed result (in the form that endpoint returns) with `expected` or `pattern`. When the text does not parse, the condition MUST be false.
- **FR-006**: The `ocrText` condition MUST be usable in an `If` step, in a `Loop` step, in a `breakCondition`, and as a child of the `all`, `any`, and `none` conditions.
- **FR-007**: The condition MUST use the same OCR engine and the same read code path (crop, then read) as `POST /api/ocr/read`. The code MUST NOT be copied.
- **FR-008**: The run log MUST name the text that was read for each evaluation of an `ocrText` condition, with the expected text or pattern and the result. This MUST also hold when the condition is a child of `all`, `any`, or `none`.
- **FR-009**: When the OCR engine is not available, the capture fails, or the read fails, the condition MUST be false and the run log MUST give a clear reason. The sequence run MUST NOT crash.
- **FR-010**: The system MUST reject a saved sequence that has an invalid `ocrText` condition (missing or invalid region, both or neither of `expected` and `pattern`, an invalid regular expression, an unknown parser name) with a clear message that names the field.
- **FR-011**: The system MUST send no tap, swipe, key, or other input to the emulator when it evaluates an `ocrText` condition.
- **FR-012**: The new condition type MUST show in the OpenAPI document of the sequence endpoints, by the method that the project uses for the other condition types, and the repository documentation of sequence conditions MUST describe it.
- **FR-013**: The change MUST NOT change `POST /api/ocr/read`, `reschedule-self`, `ocrOffset`, or the existing condition types. It MUST NOT add other condition types.
- **FR-014**: The change MUST NOT change the PNS repository or its tasks.
- **FR-015** *(web UI)*: If the web UI lists condition types, the feature MUST state its phone support level and follow docs/mobile-ui-principles.md. The plan decides whether the web UI changes. The issue does not request a UI change.

### Key Entities *(include if feature involves data)*

- **OCR text condition**: A condition of type `ocrText`. It has a region, an expected text or a pattern, and an optional parser name. It is true when the text read in the region matches.
- **OCR text evaluation record**: The run log entry for one evaluation. It holds the text that was read, the expected text or pattern, the parser name (if any), and the result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can make a sequence branch on the number in a text line of the screen with one `ocrText` condition and no reference image.
- **SC-002**: In 100% of test cases, the condition is true if and only if the text read matches the expected text or pattern.
- **SC-003**: The condition works in all six places named in the request (`If`, `Loop`, `breakCondition`, `all`, `any`, `none`), each with at least one test.
- **SC-004**: For 100% of evaluations, the run log names the text that was read.
- **SC-005**: For the same frame and region, the text that the condition reads equals the raw text from `POST /api/ocr/read`.
- **SC-006**: All existing tests for the existing condition types and for `POST /api/ocr/read` pass without change.

## Assumptions

- The only parser name today is `hh:mm:ss` (feature 128). The condition accepts the names that the endpoint accepts. A new parser name is out of scope.
- A match with `expected` is an exact match after removal of white space at the start and end. Case matters. An author who needs a looser match uses `pattern`.
- A `pattern` matches when it finds a match anywhere in the text read (a partial match), unless the author anchors it.
- With a `parser`, the comparison uses the `hh:mm:ss` text form of the parsed value.
- The condition reads a new frame at each evaluation. The system does not cache an earlier read.
- The condition uses the emulator of the sequence run, as the `imageVisible` condition does.
