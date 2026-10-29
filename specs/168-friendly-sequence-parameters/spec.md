# Feature Specification: Friendly Sequence Parameters

**Feature Branch**: `168-friendly-sequence-parameters`
**Created**: 2026-10-29
**Status**: Implemented
**Input**: User description: "I want to be able to specify in the UI parameters of the sequences in an end-user friendly way. For example, instead of specifying an image-id to be passed to the sequence, I want to select this image in a list of possible values. Instead of specifying a technical string that the sequence or command uses, I want to select from a list of user-readable texts that are mapped to the technical string later. If a user enters a number, it is later substituted in a technical string that the user does not need to understand. Make suggestions and ask questions on how to present this in the UI, as the current UI for parameters is very bare metal. This may, but does not have to be, enabled in the mobile UI."

## Overview

Today a parameter (feature 078) has a name, a type (text or number), a default, and a description. A person who supplies a value sees a plain text box. To supply an image, the person must know the technical image ID. To supply a mode, the person must know the exact technical string. To supply a port number, the person must know how the number fits into a longer technical string.

This feature adds a friendly layer on top of parameters. The author of a sequence declares *how* each parameter is asked for: a readable label, help text, and an input kind. The person who runs or schedules the sequence then sees a clear form. The form shows pickers, choice lists, and number fields. The system turns the person's choices into the technical values that the commands need.

## Clarifications

### Session 2026-10-29

- Q: Which input kinds are in scope? → **A: Four kinds: free text, number, choice list (readable label mapped to a technical value), and image picker.** *Rationale: these four kinds cover the examples in the request. More kinds can be added later.*
- Q: Where does the author define the friendly presentation? → **A: On the parameter declaration of the sequence (and of the command).** *Rationale: the declaration already owns the name, type, and default. One place for all of it keeps the model simple.*
- Q: Are existing parameters and saved data affected? → **A: No. Existing parameters keep working as plain text or number inputs with no migration.** *Rationale: the friendly fields are optional.*
- Q: Where does the number pattern live? → **A: On the number parameter declaration (one pattern per parameter).** *Rationale: simple for the author and the person.*
- Q: Which images does the picker show? → **A: Only the images that the author lists on the parameter declaration (allowed images).** *Rationale: the data model has no link from a game to an image. The author knows which images fit the sequence, so the list is short and safe.* (Changed on 2026-10-29. Before: images of the linked game.)
- Q: What is the phone support level? → **A: Reduced.** Filling in values works on a phone. Authoring is desktop-only.
- Q: Does this feature add a phone screen for values? → A: No. The value form works at phone width. The declaration editor shows "Use a larger screen". No phone screen lets a person edit values today, and this feature adds none. (Accepted on 2026-10-29.)
- Q: What does a saved choice binding store? → A: The technical value. The form finds the label by matching the value. *Rationale: no migration, and API clients keep working.*
- Q: May a person enter a value that is not in a choice list? → A: No. The list is strict. *Rationale: the person does not need to know technical strings.*
- Q: What happens at run time when a saved value no longer matches its declaration? → A: The run starts. The step that uses the value fails when it runs. *Rationale: the stored technical value is still given to the sequence, and the step reports the real problem.*
- Q: Where do friendly fields apply? → A: Sequences and commands both. *Rationale: both use one declaration model.*
- Q: What type do choice option values have? → A: The parameter type (text or number) sets the type of all option values. A number list accepts whole numbers only.
- Q: What accessibility rule applies to the new input kinds? → A: Every input kind works with the keyboard only, and every control has a text name that a screen reader reads.
- Q: What are the size limits for lists? → A: Up to 50 options per choice list. The image picker and any list of more than 12 items have a search box that filters by name.
- Q: Is the technical value ever shown to the person who supplies the value? → **A: Not in the normal form. The author and the execution log can show it.** *Rationale: the goal is that the person does not need to understand it. The log needs it for diagnosis.*

### Session 2026-10-29 (analysis fixes)

- Q: How many options must a choice list have? → A: 1 to 50. A list with one option is valid, and the form selects it by default.
- Q: What happens when a required friendly parameter has no value? → A: The feature 078 rule stays. The entry save shows a warning, and the queue start is refused. A queue built-in or the default can supply the value.
- Q: How does the system treat `{{` in a technical value (an option value or a pattern result)? → A: `{{` inside longer text is allowed. The system marks the value as plain text and does not resolve it again. An option value that is exactly one placeholder is refused, because it collides with a pass-through binding.
- Q: What does the entry save do with a stored value that no longer matches its declaration? → A: The save works and returns a warning. The form shows the problem and the hint. The form does not accept a new invalid value from the person. The run starts (FR-016).
- Q: How does a sequence step pass its own parameter to a friendly parameter of a command? → A: The step form offers "Use a parameter of this sequence" with a list of parameters of the same type. The binding stores `{{name}}`.
- Q: What does the system do when a text-typed parameter (a text choice, or a number with a pattern) is used in a numeric field? → A: The command save is refused. The message names the field and the parameter.
- Q: Does the step-through values form use the shared value form? → A: Yes.
- Q: Are command-to-command step bindings checked like sequence step bindings? → A: Yes.
- Q: How do we verify the success criteria? → A: A regression test covers SC-005. Timed manual checks cover SC-001, SC-002, and SC-004. SC-003 and SC-006 are post-release measures.
- Q: Does the pattern step need a performance proof? → A: Yes. A hand-run benchmark and a perf note in the PR. The limit is absolute: less than 1 µs for one name resolution with a pattern (changed from "2% more", which no string operation can meet). CI skips the benchmark. A deterministic test counts calls.
- Q: What is the log label for image and number parameters? → A: None. Only a choice has a label. Image and number show the technical value.
- Q: What happens with a kind of text and a type of number? → A: The declaration save refuses the pair.
- Q: Do error messages carry a hint? → A: Yes. Each message names the problem and one way to fix it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick an image from a list instead of typing an ID (Priority: P1)

An author declares a sequence parameter of the kind "image". A person who adds this sequence to a queue template sees a picker that shows only the allowed images, which the author listed on the declaration, as small previews with names. The person selects one. The sequence receives the technical image ID.

**Why this priority**: Image IDs are the most error-prone technical value. A typo causes a silent failure at run time. This story removes the main pain.

**Independent Test**: Declare an image parameter on a sequence. Open the binding form on a queue-template entry. Select an image by its preview. Run the sequence. Check that the step that uses the image received the correct ID.

**Acceptance Scenarios**:

1. **Given** a sequence with an image parameter, **When** a person opens the value form, **Then** the form shows a picker with a preview and a name for each allowed image, and no text box for an ID. The picker shows no other image.
2. **Given** the picker is open, **When** the person selects an image, **Then** the form shows the selected image preview and name.
3. **Given** a saved selection and an image that was later deleted, **When** the form opens, **Then** the form shows a clear "image not found" state with a hint, and the person can select another image. The entry save works and returns a warning.
4. **Given** an image parameter with a default, **When** the person leaves the value on inherit, **Then** the form shows the default image preview.

---

### User Story 2 - Pick a readable option that maps to a technical string (Priority: P1)

An author declares a parameter of the kind "choice list". The author lists options. Each option has a readable label and a technical value. For example, label "Hard mode" maps to technical value `difficulty_3`. A person sees a drop-down or button group with the labels only. The sequence receives the technical value.

**Why this priority**: This is the second example in the request. It removes the need to know technical strings.

**Independent Test**: Declare a choice parameter with three options. Select the second option on a queue-template entry. Run the sequence. Check in the execution log that the command received the technical value of the second option.

**Acceptance Scenarios**:

1. **Given** a choice parameter with options, **When** a person opens the value form, **Then** the form shows only the readable labels.
2. **Given** a person selects a label, **When** the sequence runs, **Then** the commands receive the technical value mapped to that label.
3. **Given** two options with the same label, **When** the author saves the declaration, **Then** the save is refused with a clear message and a hint.
4. **Given** a saved selection and an author who later removes that option, **When** the form opens, **Then** the form shows that the option no longer exists and asks the person to select another.
5. **Given** a sequence step that calls a command with a choice parameter, **When** the author opens the step form, **Then** the form offers the options and also "Use a parameter of this sequence" with the parameters of the same type.

---

### User Story 3 - Enter a number that the system puts into a technical string (Priority: P2)

An author declares a number parameter with a friendly label, a minimum, a maximum, and an optional unit label (for example "seconds"). The author also gives a *technical form*: a text pattern where the number goes, for example `emulator-{value}`. A person enters `5558`. The sequence receives `emulator-5558`.

**Why this priority**: This is the third example in the request. It is useful but less frequent than image and choice values.

**Independent Test**: Declare a number parameter with a pattern. Enter a number in the value form. Run the sequence. Check that the command received the number placed inside the pattern.

**Acceptance Scenarios**:

1. **Given** a number parameter with minimum 1 and maximum 10, **When** a person enters 11, **Then** the form shows a clear error with a hint and does not accept the value.
2. **Given** a number parameter with the pattern `emulator-{value}`, **When** a person enters 5558, **Then** the sequence receives `emulator-5558`.
3. **Given** a number parameter with a unit label "seconds", **When** the form shows the field, **Then** the unit label is next to the field.
4. **Given** a pattern that has no `{value}` marker, **When** the author saves the declaration, **Then** the save is refused with a clear message and a hint.

---

### User Story 4 - A clearer authoring editor for parameters (Priority: P2)

An author opens the parameter editor of a sequence. The editor shows each parameter as a card with: label, help text, input kind, kind-specific settings (options list, allowed images, number range, pattern), default, and "required". The editor shows a live preview of the form that a person will see. The author does not need to know the internal name rules to get started. The editor suggests a name from the label.

**Why this priority**: The request names the current editor as "bare metal". Without a better editor, the new kinds are hard to author.

**Independent Test**: Open a sequence. Add one parameter of each kind. Check that the live preview shows the form the person will see, and that the author did not type any internal name by hand.

**Acceptance Scenarios**:

1. **Given** the author types a label "Difficulty", **When** the name field is empty, **Then** the editor suggests the name `difficulty`, and the author can change it.
2. **Given** the author changes the input kind, **When** the editor redraws, **Then** it shows only the settings that apply to that kind.
3. **Given** a parameter with settings, **When** the author looks at the preview, **Then** the preview matches the form that a person will see on a queue-template entry.
4. **Given** several parameters, **When** the author moves a card with the move buttons, **Then** the order in the preview and in the person's form changes to match.

---

### User Story 5 - The value form at phone width (Priority: P3)

The value form (pickers, choice lists, and number fields) works at phone width. Authoring (declaring parameters) stays on the desktop.

**Why this priority**: The request says the phone is optional. Filling in values is a small task that fits a phone. Authoring is a large task that does not.

**Independent Test**: Render the value form in the phone layout (375 px wide). Select an image, a choice, and enter a number. Check that the form works with touch-size controls and no horizontal scroll. (No phone screen hosts the form yet.)

**Acceptance Scenarios**:

1. **Given** a phone-size screen, **When** a person opens the value form, **Then** all input kinds work and every control is large enough to touch.
2. **Given** a phone-size screen, **When** a person opens the parameter editor of a sequence, **Then** the UI shows a notice that authoring needs a larger screen.

---

### Edge Cases

- A person leaves a required friendly parameter empty. The entry save shows a warning. The queue start is refused (feature 078 rule, FR-019).
- The author lists an image ID that does not exist. The declaration save shows a warning, and the picker shows that item as "image not found" and cannot select it.
- The author lists no allowed image on an image parameter. The declaration save is refused with a clear message.
- The author removes an allowed image that a saved binding uses. The value form follows FR-013.
- A choice list has only one option. The form still shows it, and it is selected by default.
- A parameter was a plain text parameter before this feature and the author now changes it to a choice list. Saved values that are not in the new list show as "unknown value", the entry save returns a warning, and the person must select a new one.
- A technical value contains `{{` markers. The system marks the value as plain text and does not resolve it again (FR-027).
- The execution log shows both the readable label and the technical value for each friendly parameter.
- Two sequences with the same parameter name but different kinds run in one scope. The nearest declaration wins, as in feature 078.
- A text-typed parameter is used in a numeric field. The command save is refused (FR-026).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each parameter declaration of a sequence or a command MUST support an optional readable label and an optional help text. When the label is empty, the UI MUST show the parameter name.
- **FR-002**: Each parameter declaration MUST support an input kind: free text, number, choice list, or image. The default kind MUST be the kind that matches the existing type (text or number).
- **FR-003**: For a choice list, the author MUST be able to define 1 to 50 options. Each option MUST have a readable label and a technical value. Labels MUST be unique in one list. Technical values MUST be unique in one list. The save check MUST refuse a longer list with a clear message and a hint.
- **FR-004**: The parameter type (text or number) MUST set the type of all option values in a choice list. A number list MUST accept whole numbers only, and the save check MUST refuse any other value. A number choice MUST be usable in a numeric field.
- **FR-005**: For a choice list, the value form MUST show the readable labels only, and the system MUST give the mapped technical value to the sequence.
- **FR-006**: For an image parameter, the value form MUST show a picker with a preview and a name for each allowed image, and the system MUST give the technical image ID to the sequence.
- **FR-007**: An image parameter declaration MUST list 1 to 50 allowed image IDs. The author MUST choose them from the stored images in the editor, with a preview. The picker in the value form MUST show only the allowed images. The declaration save MUST refuse an empty list and MUST warn about an ID with no stored image.
- **FR-008**: The image picker, and any choice list, of more than 12 items MUST have a search box that filters the items by name.
- **FR-009**: For a number parameter, the author MUST be able to set an optional minimum, maximum, and unit label.
- **FR-010**: For a number parameter, the author MUST be able to set an optional technical pattern that contains one `{value}` marker. The system MUST put the entered number in the marker and give the result to the sequence.
- **FR-011**: A number pattern MUST be set once, on the number parameter declaration. A command field MUST NOT have its own pattern.
- **FR-012**: The value form MUST check a value against its declaration (range, list membership, image existence) while the person edits it, and MUST NOT accept a new invalid value. The service MUST check the same rules when it saves a queue-template entry, a sequence step binding, or a command step binding, and MUST return a warning for a value that does not match. Every message MUST name the problem and one way to fix it, and MUST NOT use technical terms.
- **FR-013**: When a saved value no longer matches its declaration (deleted image, removed option, changed range), the value form MUST show a clear state for the problem and MUST NOT silently change the value.
- **FR-014**: A saved binding MUST store the technical value: the option value for a choice, the image ID for an image, and the entered number for a number. The value form MUST find the readable label by matching the stored value to the declaration. A stored value with no match MUST follow FR-013.
- **FR-015**: A choice list MUST be strict. The value form MUST NOT allow a value that is not in the list.
- **FR-016**: The system MUST NOT refuse a queue start or a run only because a saved value no longer matches its declaration. The run MUST give the stored technical value to the sequence, and the step that uses it reports any failure when it runs.
- **FR-017**: The execution log MUST show the technical value that the commands received for each friendly parameter. For a choice, it MUST also show the readable label of the option. Parameters whose name matches the secret-name rule from feature 078 MUST stay masked.
- **FR-018**: Existing parameter declarations, bindings, queue templates, and sequences MUST keep their behavior with no migration. A declaration without friendly fields MUST appear as a plain text or number input.
- **FR-019**: A required parameter with no value MUST follow the feature 078 rule: the entry save shows a warning, and the queue start is refused. A queue built-in or the default can supply the value. A stale value is not a missing value: it never stops a queue start (FR-016).
- **FR-020**: The parameter editor MUST show each parameter as a card with kind-specific settings, and MUST show a live preview of the form that a person will see.
- **FR-021**: The parameter editor MUST suggest an internal name from the label when the name is empty. The author MUST be able to change the name.
- **FR-022**: The value form MUST be the same on every call site that supplies parameter values (queue-template entry, sequence step, command step, step-through values form).
- **FR-023**: The friendly presentation fields MUST be included in the public API documentation of sequences and commands.
- **FR-024** *(web UI change)*: The feature MUST state its phone support level and follow docs/mobile-ui-principles.md. The phone support level is **reduced**: the value form works at phone width; authoring parameter declarations is desktop-only.
- **FR-025**: Every input kind (text, number, choice list, image) MUST work with the keyboard only. Every control MUST have a text name that a screen reader reads. An image in the picker MUST use its name as the text name.
- **FR-026**: The command save MUST be refused when a text-typed parameter (a text choice, or a number with a pattern) is used in a numeric field. The message MUST name the field and the parameter and give a hint.
- **FR-027**: The system MUST allow `{{` inside longer text in an option value and in a pattern. It MUST mark the resulting value as plain text and MUST NOT resolve it again. The declaration save MUST refuse an option value that is exactly one placeholder (for example `{{a}}`), because it collides with a pass-through binding (FR-028). The message gives a hint.
- **FR-028**: A step form that binds a friendly parameter MUST offer "Use a parameter of this sequence" with the parameters of the same type. The binding MUST store `{{name}}`, and the system MUST NOT check such a value against the list or range.

### Key Entities

- **Parameter declaration (extended)**: The existing declaration (name, type, default, required, description) plus: label, help text, input kind, and kind-specific settings.
- **Choice option**: A pair of a readable label and a technical value, in an ordered list that belongs to one choice parameter.
- **Number settings**: Minimum, maximum, unit label, and technical pattern that belong to one number parameter.
- **Allowed image**: The technical ID of a stored image that the author lists on an image parameter. The person sees it as a preview and a name.
- **Friendly value**: What the person selects or enters in the form. The binding stores the technical value (FR-014). Only a number pattern maps the value at run time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person can supply an image parameter by selecting it from the picker in under 10 seconds, with no typed text. (Timed manual check.)
- **SC-002**: A person can supply a choice parameter in 2 interactions or fewer (open, select). (Manual check.)
- **SC-003**: In a test with new users, 90% supply all values of a 3-parameter sequence correctly with no help and no knowledge of technical IDs or strings. (Post-release measure.)
- **SC-004**: An author can add a choice parameter with three options and see the preview in under 2 minutes. (Timed manual check.)
- **SC-005**: 100% of existing saved sequences, commands, and queue templates load and run with the same result as before the feature. (Regression test.)
- **SC-006**: Zero runs fail because of a mistyped image ID or technical string for parameters that use the friendly kinds. (Post-release measure.)
- **SC-007**: Filling in values on a phone-size screen works for all four input kinds with no horizontal scroll.
- **SC-008**: One name resolution with a number pattern takes less than 1 µs. (Hand-run benchmark for the perf note. The benchmark test checks the same limit; CI skips it.)

## Assumptions

- The system can list all stored images with a preview, so the author can choose the allowed images. An image has no link to a game.
- The existing parameter resolution (declared, ambient, queue built-ins, fail fast) from feature 078 stays unchanged. This feature changes how values are asked for and mapped, not how scopes resolve.
- A choice list gives one value per selection. Multi-select is out of scope.
- A decimal number is out of scope. Numbers stay whole numbers as in feature 078.
- Translation of labels into other languages is out of scope.
- Command parameters and sequence parameters use the same friendly fields, because both use the same declaration model.
- An API client can save a value that does not match its declaration. The service returns a warning, and the run starts (FR-012, FR-016).
