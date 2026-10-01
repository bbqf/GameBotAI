# Feature Specification: OCR Offset Day Part

**Feature Branch**: `124-ocr-offset-day-part`  
**Created**: 2026-10-01  
**Status**: Implemented  
**Input**: User description: "Let the OCR duration parser of reschedule-self ocrOffset read a day part (GitHub issue #256). Closes #256."

## Clarifications

### Session 2026-10-01

- Q: Is white space between the day part and the time part required? → A: No. White space is optional, so "1d23:29:10" and "1d 23:29:10" give the same result. Reason: OCR often drops spaces.
- Q: Is the letter "d" case-sensitive? → A: No. "1D 23:29:10" gives the same result as "1d 23:29:10". Reason: OCR can change the case of a letter.
- Q: What does the parser do with a day part and a time part that has fewer than 2 colon groups, such as "1d 05:30"? → A: The day part adds to the existing "MM:SS" rule. "1d 05:30" is 1 day 5 minutes 30 seconds. Reason: this keeps the old rule for the time part unchanged.
- Q: What does the parser do with a "<N>d" token that is part of a word, such as "3days" or "Reward"? → A: The token "<N>d" counts only when the "d" is not followed by a letter, and the digits are not preceded by a letter or digit. Reason: this prevents false day parts from normal words.
- Q: In what format does the log line show the parsed value? → A: The log line keeps its current layout and shows the standard TimeSpan text, which includes the day part (for example "1.23:29:10"). Reason: this is the format the code already uses for a value of one day or more.
- Q: Does a day part count when the text has "<N>d" and a separate time token? → A: See the answer in the analyze loop 1 session below (it replaces the first answer for this question).

### Session 2026-10-01 (analyze loop 1)

- Q: (F1) What rule decides between a day part with a time part and a day part alone? → A: The parser first looks for the first time token ("HH:MM:SS" or "MM:SS"). If a time token exists, a day part counts only when it is directly before that time token (white space only between them). A day token after the time token, or apart from it, is ignored. The "<N>d" alone result applies only when the text has no time token at all. Reason: this gives "23:29:10 1d" the old result of 23:29:10 and keeps one rule. This replaces the last answer of the first session.
- Q: (F2) Which `max` does a test for 1 day 23:29:10 use? → A: The scenario sets `max` to "2.00:00:00". The default `max` of `ocrOffset` stays 24:00:00. A read of one day or more with the default `max` is out of bounds and uses the fallback. This is the existing rule and this feature does not change it.
- Q: (F3) What happens when the total offset is outside `min` or `max`? → A: The resolver uses the fallback offset, with reason "out-of-bounds". It does not clamp. This is the existing rule.
- Q: (F4) What does the parser do with "1d left 23:29:10", where other text comes between the day part and the time part? → A: The day part is not directly before the time token, so it is ignored. The result is 23:29:10, the same as before this change. Reason: this follows the F1 rule.
- Q: (C2, loop 2) Does the day token rule apply before or after the existing OCR digit fix? → A: After. The digit fix runs first, so a lone "Id" or "ld" becomes "1d" and reads as 1 day when the text has no time token. This is an accepted OCR risk. The rule that no letter comes before the digits applies to the text after the digit fix.
- Q: (F5) What does the parser do when the digits of the day count are too large for an integer? → A: Parsing fails (returns false), the same as for any overflow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Book a long timer with a day part (Priority: P1)

A sequence author uses the `reschedule-self` action with the `ocrOffset` option. The option reads a countdown timer from the screen with OCR and books the next run of the sequence at that offset from now. The game shows a long timer as "1d 23:29:10". Today the day part is ignored, and the run is booked 23:29:10 ahead instead of 1 day 23:29:10 ahead. The run wakes one day early and does one extra full run for each cycle. The author needs the day part to count in the booked offset.

**Why this priority**: This is the defect that was observed. It costs one extra full run for each cycle of the affected pool.

**Independent Test**: Give the parser the text "1d 23:29:10" and check that the offset is 1 day 23:29:10. Then run the `ocrOffset` flow on a screen that shows "Free in 1d 23:29:10" and check that the booked run is 1 day 23:29:10 ahead.

**Acceptance Scenarios**:

1. **Given** OCR text "1d 23:29:10", **When** the duration is parsed, **Then** the offset is 1 day 23:29:10.
2. **Given** OCR text "Free in 1d 23:29:10", **When** the duration is parsed, **Then** the offset is 1 day 23:29:10.
3. **Given** OCR text "Free in 1d 23:29:10" and `max` "2.00:00:00", **When** `reschedule-self` books the next run, **Then** the booking is 1 day 23:29:10 ahead of now.
4. **Given** OCR text "1d 23:29:10" is read, **When** the resolver writes the "offset source ocr" log line, **Then** the log line shows the parsed value with the day part (1 day 23:29:10).

---

### User Story 2 - Keep timers without a day part unchanged (Priority: P1)

A sequence author has sequences that read timers in the forms "HH:MM:SS" and "MM:SS". These sequences must give the same offsets as before this change.

**Why this priority**: A regression here changes the booking of sequences that work today.

**Independent Test**: Run the existing parser tests for "HH:MM:SS" and "MM:SS" text. All results stay the same.

**Acceptance Scenarios**:

1. **Given** OCR text "23:29:10", **When** the duration is parsed, **Then** the offset is 23:29:10, the same as before this change.
2. **Given** OCR text "05:30", **When** the duration is parsed, **Then** the offset is 5 minutes 30 seconds, the same as before this change.
3. **Given** OCR text with no duration, **When** the duration is parsed, **Then** parsing fails, the same as before this change.

---

### User Story 3 - Apply min and max limits to the total offset (Priority: P2)

A sequence author sets a `max` limit with days (for example "2.00:00:00") on `ocrOffset`. The limits apply to the total offset, which includes the days.

**Why this priority**: Without this, a long timer could be clamped or rejected on the wrong value.

**Independent Test**: Parse "1d 23:29:10" with `max` "2.00:00:00" and check that the offset is inside the limit and is not changed.

**Acceptance Scenarios**:

1. **Given** `max` "2.00:00:00" and OCR text "1d 23:29:10", **When** the offset is resolved, **Then** the offset is inside the limit and stays 1 day 23:29:10.
2. **Given** `max` "1.00:00:00" and OCR text "1d 23:29:10", **When** the offset is resolved, **Then** the offset is out of bounds, and the resolver uses the fallback offset with reason "out-of-bounds" (no clamp).
3. **Given** `min` "1.00:00:00" and OCR text "23:29:10", **When** the offset is resolved, **Then** the offset is out of bounds, and the resolver uses the fallback offset with reason "out-of-bounds", as before.
4. **Given** the default `max` (24:00:00) and OCR text "1d 23:29:10", **When** the offset is resolved, **Then** the offset is out of bounds and the fallback applies. The default `max` does not change in this feature.

---

### User Story 4 - Read a day count alone (Priority: P3)

The game can show a timer with a day count only, for example "2d". The parser reads this text as 2 days.

**Why this priority**: This is an optional addition. The issue asks for it only if it is easy to do.

**Independent Test**: Parse the text "2d" and check that the offset is 2 days.

**Acceptance Scenarios**:

1. **Given** OCR text "2d", **When** the duration is parsed, **Then** the offset is 2 days.
2. **Given** OCR text "Free in 2d", **When** the duration is parsed, **Then** the offset is 2 days.

---

### Edge Cases

- Text with a day part and an "MM:SS" part (for example "1d 05:30"): the existing rule reads "05:30" as minutes and seconds. The day part adds 1 day. The offset is 1 day 5 minutes 30 seconds.
- Overflow, case 1 (integer overflow): the digits of the day count are too large for an integer (for example "999999999999d 00:00:01"). Parsing fails and does not throw.
- Overflow, case 2 (range overflow): the day count fits in an integer but the total is outside the TimeSpan range (for example "2147483647d 00:00:01"). Parsing fails and does not throw. An out-of-range total does not become an "ocr-error". The resolver gives the reason "parse-failed".
- A day part counts only when it is directly before the first time token, with only white space between them. "23:29:10 1d" and "1d left 23:29:10" give 23:29:10, as before this change. "<N>d" alone counts only when the text has no time token.
- A lone "Id" or "ld": the OCR digit fix runs first and changes it to "1d". With no time token, the parser reads it as 1 day. This is an accepted OCR risk.
- The letter "d" with no number before it, or inside a word (for example "Free in" or "Reward"): this is not a day part and does not change the result.
- White space between the digits and the letter "d" (for example "1 d 23:29:10"): the parser is NOT required to read this as a day part. Only a "<N>d" token, with the "d" directly after the digits, counts.
- A run of digits before the "d" (for example "11d 23:29:10"): the parser reads the whole run, so "11d" is 11 days. A digit run that has a letter or digit before it is not a day part.
- Text with another unit (for example "1w 23:29:10"): the parser reads no other unit. The result is 23:29:10, the same as before this change.
- Text with no day part: the result stays the same as before this change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The OCR duration parser MUST read an optional leading "<N>d" before "HH:MM:SS" and add N days to the offset.
- **FR-002**: The OCR duration parser MUST read an optional leading "<N>d" before "MM:SS" and add N days to the offset.
- **FR-003**: The OCR duration parser MUST read "<N>d" alone as an offset of N days, and only when the text has no time token ("HH:MM:SS" or "MM:SS").
- **FR-004**: White space between the day part and the time part MUST be optional, and the letter "d" MUST be case-insensitive.
- **FR-005**: A "<N>d" token MUST count as a day part only when no letter or digit comes before its digits and no letter comes after the "d". This rule applies to the text after the existing OCR digit fix, so "Id" and "ld" become "1d" before the rule runs.
- **FR-006**: When the text has a time token, a day part MUST count only when it is directly before the first time token, with only white space between them. A day token after the time token, or apart from it, is ignored.
- **FR-007**: The OCR duration parser MUST find the day part when the timer is inside other text (for example "Free in 1d 23:29:10").
- **FR-008**: The OCR duration parser MUST give the same result as before this change for text without a day part.
- **FR-009**: The OCR duration parser MUST fail, and not wrap or truncate, when the total offset is too large to represent.
- **FR-010**: The `min` and `max` limits of `ocrOffset` MUST apply to the total offset, which includes the days.
- **FR-011**: The "offset source ocr" log line MUST keep its current layout and show the parsed value, with the day part, as standard TimeSpan text (for example "1.23:29:10").
- **FR-012**: The published API documentation of the `ocrOffset` payload MUST describe the day part if it states the OCR text format.
- **FR-013**: The change MUST NOT alter the other offset sources of `reschedule-self`.
- **FR-014**: The change MUST NOT add other duration units, such as weeks. Text such as "1w 23:29:10" gives 23:29:10.
- **FR-015**: Automated tests MUST cover the new formats and the unchanged old formats.

### Key Entities

- **OCR duration text**: The text that OCR reads from the screen. It can contain a countdown such as "1d 23:29:10", "23:29:10", "05:30", or "2d", with other text around it.
- **Parsed offset**: The duration that the parser extracts. It includes the day part. `reschedule-self` books the next run at this offset from now, after the `min` and `max` limits apply.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the text "Free in 1d 23:29:10", the booked run is 1 day 23:29:10 ahead. It is no longer 23:29:10 ahead.
- **SC-002**: After deploy, the pool in the observed case does zero extra full runs for each cycle that were caused by a missing day part. This is an outcome metric that the team checks after deploy. No automated test checks it.
- **SC-003**: 100% of the existing tests for timers without a day part pass with no change to their expected values.
- **SC-004**: The tests cover each of the four formats: "1d HH:MM:SS", the same inside other text, "HH:MM:SS" without a day part, and "<N>d" alone.
- **SC-005**: For `max` "2.00:00:00", a read of 1 day 23:29:10 stays inside the limit.
- **SC-006**: The log line for a read with a day part shows the day part in the parsed value.

## Assumptions

- The day part is a whole number of days followed by the letter "d", with optional white space before the time part.
- The OCR digit fixes that exist today (for example "O" read as "0", "I" or "l" read as "1") stay as they are. The letter "d" is not changed by them. The day token rule applies after these fixes, so "Id" or "ld" reads as "1d". This is an accepted risk.
- Test intent stays separate: the resolver result is covered by User Story 3 scenario 1, and the log line text is covered by User Story 1 scenario 4. Tests for these two checks stay separate in intent (resolver result versus log text).
- Parser tests share one test file, so they are not run in parallel. The no-time-token branch is delivered with the day-alone story.
- The day count and the other parts are summed into one offset.
- The change is limited to the domain parser, its tests, and the API documentation text. The resolver and its log line, and the payload validator, need no change. The PNS authoring repository and its sequences are out of scope.
- The API documentation changes only where a text that states the OCR duration format exists. If no such text exists, no documentation change is needed.
- A day part counts only when it is directly before the first time token, and "<N>d" alone counts only when the text has no time token (see FR-003 and FR-006).
- A day part with no time part is accepted because it is easy to do with the same rule.
