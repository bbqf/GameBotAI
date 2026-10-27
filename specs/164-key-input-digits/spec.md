# Feature Specification: Digit Keys and Raw Key Codes in Key Input Steps

**Feature Branch**: `164-key-input-digits`
**Created**: 2026-10-26
**Status**: Implemented
**Input**: User description: "Add the digit keys 0 to 9 to the key input step of a command. Also let the step accept a raw Android key code. Document this in the Swagger docs."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send a digit key from a command (Priority: P1)

An author builds a command that must press the number 6 on the device. The author adds a key input step with the key name "6". When the command runs, the device receives the key press for the digit 6.

**Why this priority**: Today the key input step rejects digit names, and the step log does not show the error. Authors cannot type a number in a saved command. This is the main gap.

**Independent Test**: Create a command with one key input step with key "6". Run the command on a session. Check that the device receives the key event for the digit 6 and that the step outcome is "executed".

**Acceptance Scenarios**:

1. **Given** a command with a key input step with key "6", **When** the command runs, **Then** the device receives the key press for the digit 6 and the step outcome is "executed".
2. **Given** a key input step with a key from "0" to "9", **When** the step runs, **Then** the device receives the key press for that digit.
3. **Given** a key input step with key name "NUMPAD_6", **When** the step runs, **Then** the device receives the key press for the number pad digit 6.
4. **Given** a key input step with a current key name such as "ENTER" or "A", **When** the step runs, **Then** the behavior is the same as before this feature.
5. **Given** a key input step that the device does not accept, **When** the step runs, **Then** the step outcome is "not_accepted" with a reason code, and not "executed".
6. **Given** a sequence step with a `key` action with key text "6", **When** the sequence runs, **Then** the device receives the key press for the digit 6.

---

### User Story 2 - Send any Android key by its raw key code (Priority: P2)

An author must press a key that has no name in the key list (for example, a media key). The author writes the Android key code as a number, for example "85", in the key input step. When the command runs, the device receives that key code.

**Why this priority**: This lets authors reach every Android key without a new release for each key name. It is less urgent than the digits, which most games need.

**Independent Test**: Create a key input step with key "85". Run it. Check that the device receives key code 85.

**Acceptance Scenarios**:

1. **Given** a key input step with key "85", **When** the step runs, **Then** the device receives key code 85.
2. **Given** a key input step with a number that is outside the valid range, **When** the step runs, **Then** the step does not send a key and the outcome gives the reason that the key code is out of range.
3. **Given** a key input step with key text that is not a listed name and not a whole number, **When** the step runs, **Then** the step does not send a key and the outcome gives the reason that the key name is unknown.
4. **Given** a sequence step with a `key` action with key text "85", **When** the sequence runs, **Then** the device receives key code 85.

---

### User Story 3 - Find the key rules in the Swagger docs (Priority: P2)

An integrator reads the Swagger docs for the command step and for the key action. The docs list the accepted key names, the digit keys, and the raw key code rule, with an example for each.

**Why this priority**: The digit keys were hard to find because the docs did not list them. Clear docs prevent the same question later.

**Independent Test**: Open the Swagger UI. Find the key input step of the command API. Check that the text and the examples show a digit key and a raw key code.

**Acceptance Scenarios**:

1. **Given** the Swagger docs, **When** a reader opens the key input step, **Then** the docs state the accepted key names, the digit keys "0" to "9", the number pad names, and the raw key code rule.
2. **Given** the Swagger docs, **When** a reader looks at the examples, **Then** at least one example uses a digit key and at least one example uses a raw key code.
3. **Given** the key input field in the web UI, **When** an author looks at the field, **Then** the hint text shows a digit example and a raw key code example.

---

### Edge Cases

- A key name has spaces or mixed case (for example " 6 " or "numpad_6"): the service removes the spaces at the start and end and ignores case, as it does for the old names.
- A numeric key has a plus or minus sign, a decimal point, or extra text (for example "-1", "+5", "6.5", "6a"): it is not a whole number, so the step fails with the reason that the key name is unknown.
- A numeric key is "0": it is the digit key 0, not the raw key code 0. A name from the key list always has priority over a raw key code.
- A raw key code is outside the range 0 to 300 (for example "301" or a number too large to read): the step does not send a key.
- Saved commands and gamepacks with the old key names stay valid and run the same.
- A session without a device does not read the key text. A key input step on such a session keeps the outcome "executed".

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The key input step MUST accept the key names "0" to "9" and send the key press for that digit to the device.
- **FR-002**: The key input step MUST accept the key names "NUMPAD_0" to "NUMPAD_9" and send the key press for that number pad digit to the device.
- **FR-003**: The key input step MUST accept a whole number from 0 to 300 as a raw Android key code when the text is not a name in the key list, and MUST send that key code to the device.
- **FR-004**: A name in the key list MUST have priority over a raw key code. The text "6" MUST send the digit key 6, not key code 6.
- **FR-005**: The key input step MUST NOT send a key when the key text is a whole number outside 0 to 300. The step outcome MUST give a clear reason.
- **FR-006**: The key input step MUST NOT send a key when the key text is neither a listed name nor a whole number. The step outcome MUST give the reason that the key name is unknown. The session input route MUST give the reason `key: unknown key name '<text>'`.
- **FR-007**: The service MUST ignore case and MUST ignore spaces at the start and end of a key name, as it does for the old names.
- **FR-008**: The key action of the session input route and the `key` action of a sequence step MUST accept the same digit names, number pad names, and raw key codes in the key name field. The numeric key code field MUST work as before.
- **FR-009**: All current key names MUST keep the same behavior when the device accepts the key. Saved commands, sequences, and gamepacks MUST NOT need any change.
- **FR-010**: The Swagger docs MUST describe the accepted key names, the digit keys, the number pad names, and the raw key code rule (range and priority) for the key input step and for the key action.
- **FR-011**: The Swagger docs MUST include one example with a digit key and one example with a raw key code.
- **FR-012**: The command validation at save time MUST stay as it is: the key is a required, non-empty text. The key check happens at run time.
- **FR-013**: When the device accepts no key, the status of a key input step MUST be "not_accepted", and not "executed". The outcome MUST have one reason code: `key_input_unknown_key`, `key_input_code_out_of_range`, or `key_input_not_accepted`.
- **FR-014**: The key input field of the web UI MUST show hint text with a digit example and a raw key code example. Phone support level: full. The change adds text only, with no layout change, and follows docs/mobile-ui-principles.md.

### Key Entities

- **Key input step**: A command step that presses one key on the device. It has one key text.
- **Key text**: A key name from the key list, or a whole number that is a raw Android key code.
- **Key list**: The set of names that map to Android key codes. This feature adds the digit names and the number pad names.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can make a command that presses each digit from 0 to 9, and 100% of the 10 digit keys reach the device as the correct key.
- **SC-002**: An author can send any Android key code from 0 to 300 with a key input step, with no new key name needed.
- **SC-003**: 100% of the key names that worked before this feature give the same result after it, when the device accepts the key.
- **SC-004**: A reader of the Swagger docs can find the digit key rule and the raw key code rule in the key input step in under 1 minute, with an example for each.
- **SC-005**: In 100% of the tested cases where the device accepts no key, the step outcome shows "not_accepted" with a reason code.

## Assumptions

- The valid range 0 to 300 for a raw key code is the same as the range that the Swagger docs give for the numeric key code field.
- The digit names use the main number row of Android (key codes 7 to 16). The number pad names use key codes 144 to 153.
- The key text field of the web UI already accepts free text. Only the hint text changes (FR-014).
- No version migration is needed for saved data, because the key text format stays the same.
- Out of scope: other key names that the step recorder writes (for example F1 to F12 and `DPAD_UP`). The backend does not know them. A raw key code is the workaround. A separate issue covers them.
