# Feature Specification: List-valued sequence parameters

**Feature Branch**: `179-list-valued-parameters`  
**Created**: 2026-11-09  
**Status**: Implemented  
**Input**: User description: "List-valued sequence parameters (select several images or options). Source: GitHub issue #378 (FR-042), https://github.com/bbqf/GameBotAI/issues/378. Closes #378."

## Clarifications

### Session 2026-11-09

- Q: How does the API carry a list value, when values are text today? → A: As a JSON array of text items. A plain text value is still accepted and is read as a list of one. (Rationale: keeps old data valid and the contract simple.)
- Q: What if an allowed image id or option value holds the pipe character `|`? → A: The system refuses such a declaration for a multi-value parameter, because `|` is the join character (FR-011).
- Q: Does an empty list for a required parameter block a queue start? → A: Yes. It is treated as "no value": the default applies if one exists, otherwise the queue start is refused as for any unsatisfied required parameter. At save time, an empty list in a known entry value is refused.
- Q: Does the stored order follow the order of selection or the order of the allowed list? → A: The order of selection (FR-004, FR-005).
- Q: Is a limit check on `minItems` and `maxItems` an error or a warning for stored values? → A: An error (refused), as the issue says "refused". Items that are not allowed stay a warning (FR-009).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Declare a multi-value parameter (Priority: P1)

A gamepack author declares an image parameter or a choice parameter that accepts more than one value. The author sets a "multiple" flag on the declaration and, if needed, a minimum and a maximum number of items.

**Why this priority**: Without the declaration, nothing else in this feature can exist. The consumer gamepack needs it to wait for any one of several quests.

**Independent Test**: Save a sequence with an image parameter that has the multiple flag. Read it back through the API and the published schema. The flag, `minItems`, and `maxItems` are present.

**Acceptance Scenarios**:

1. **Given** a declaration with kind `image` and `multiple: true`, **When** the author saves the sequence, **Then** the API accepts it and returns the flag in the response and in the published schema.
2. **Given** a declaration with kind `choice` and `multiple: true`, **When** the author saves it, **Then** the API accepts it in the same way.
3. **Given** a declaration with kind `text` or `number` and `multiple: true`, **When** the author saves it, **Then** the API refuses it with a message that names the allowed kinds.
4. **Given** a declaration with `minItems` greater than `maxItems`, or with a limit that is below 1, **When** the author saves it, **Then** the API refuses it.
5. **Given** a declaration without the flag, **When** it is saved and read, **Then** it is unchanged from before this feature.

---

### User Story 2 - Select several items in the value form (Priority: P1)

An operator fills the value form of a queue entry, a template entry, or an entry layer. For a multi-value parameter, the form shows all allowed icons (or options) and lets the operator select one or more of them.

**Why this priority**: This is the operator-facing result. It removes the need for one task or one sequence copy for each quest.

**Independent Test**: Open the value form for a multi-value image parameter. Select two icons. Save. Open again. The two icons are selected, in the saved order.

**Acceptance Scenarios**:

1. **Given** a multi-value image parameter with five allowed images, **When** the operator opens the value form, **Then** the form shows the five icons and lets the operator select any number of them.
2. **Given** the operator selects icon B then icon A, **When** the operator saves, **Then** the stored value is the ordered list `[B, A]`.
3. **Given** the operator selects the same icon two times, **When** the operator saves, **Then** the stored list holds that icon one time.
4. **Given** a single-value parameter, **When** the operator opens the value form, **Then** the form is unchanged from before this feature.
5. **Given** a multi-value choice parameter, **When** the operator opens the value form, **Then** the form shows the options and lets the operator select one or more.

---

### User Story 3 - Store and read list values in every layer (Priority: P1)

A list value is stored in entry-layer values, template values, and queue entry parameter values. A scalar value saved before this feature is read as a list of one.

**Why this priority**: Stored data must keep working. Old data must not break.

**Independent Test**: Load a queue entry that has the scalar value `quest_a` for a parameter that is now multi-value. The value reads as `[quest_a]`. Save a new list. The stored form is an ordered list of unique values.

**Acceptance Scenarios**:

1. **Given** a stored scalar value for a parameter that now has `multiple: true`, **When** the system reads the value, **Then** the system reads it as a list of one item.
2. **Given** a list value in a template entry, **When** a queue is built from the template, **Then** the queue entry holds the same ordered list.
3. **Given** a parameter that has no `multiple` flag and a stored scalar value, **When** the system reads it, **Then** it behaves as before this feature.
4. **Given** a list that holds duplicate items, **When** the system stores it, **Then** the system keeps the first occurrence of each item and the order of first occurrences.

---

### User Story 4 - Validate list values (Priority: P2)

The system checks list values against the declaration and gives clear feedback.

**Why this priority**: Wrong lists must be found before a run. This builds on stories 1 to 3.

**Independent Test**: Supply an empty list for a required multi-value parameter and refuse it. Supply a list with an item that is not allowed and get the same warning as for a single value.

**Acceptance Scenarios**:

1. **Given** a required multi-value parameter, **When** the supplied list is empty, **Then** the system refuses it.
2. **Given** a multi-value parameter that is not required, **When** no value is supplied, **Then** the system accepts it as before (no value, or the default).
3. **Given** a list with an item that is not in `allowedImages` (image) or in `options` (choice), **When** the system validates it, **Then** it gives the same warning as for a single value, one warning for each wrong item, and the warning never blocks a queue start.
4. **Given** `minItems` or `maxItems` is set, **When** the list has fewer items than `minItems` or more than `maxItems`, **Then** the system refuses it.
5. **Given** a list value for a parameter that is not multi-value and has more than one item, **When** the system validates it, **Then** it gives the invalid-value warning.

---

### User Story 5 - Resolve list values in bindings and placeholders (Priority: P2)

A list value flows through a binding to a step field and through a `{{name}}` placeholder in a defined way. This story does not depend on any new condition type.

**Why this priority**: A list that cannot be used at run time has no value. The resolution rules must be exact and tested.

**Independent Test**: Run a sequence in which a multi-value parameter is used in a text field with `{{name}}`. The field receives the items joined in the list order. Use the parameter in a single-image field with two items. The step fails with a clear message.

**Acceptance Scenarios**:

1. **Given** a list value and a `{{name}}` placeholder in a text field, **When** the placeholder resolves, **Then** the result is the items in list order, joined by the pipe character `|`, with no spaces. A list of one gives the item alone.
2. **Given** a list value and a placeholder that is the whole value of a field that takes one image id or one number, **When** the list has exactly one item, **Then** the field receives that item.
3. **Given** the same field and a list with two or more items, **When** the step runs, **Then** the step fails with a message that names the parameter, the field, and the item count. The system does not use the first item silently.
4. **Given** a binding from a caller to a callee parameter that is also multi-value, **When** the callee runs, **Then** the callee receives the full ordered list.
5. **Given** a binding of a list with two or more items to a callee parameter that is not multi-value, **When** the system validates the binding, **Then** it gives the invalid-value warning, and a run that reads the parameter in a one-value field fails as in scenario 3.
6. **Given** a multi-value parameter with a default, **When** no layer supplies a value, **Then** the default is read as a list in the same way as a stored scalar.

---

### Edge Cases

- An empty list for a parameter that is not required: the system treats it as "no value" and uses the default if one exists.
- A list item that is a whole placeholder, for example `{{other}}`: the system does not check the item at save time, as for a single value.
- A list with items in a different case from the allowed list: the match is exact, as for a single value.
- Removing an item from `allowedImages` or `options` after values are saved: the system gives the invalid-value warning for the stale item and does not block a queue start.
- A list with more than 50 items: the allowed lists hold at most 50 items, so a valid list cannot be longer than 50.
- `minItems` greater than the number of allowed items: the system refuses the declaration because no valid list could exist.
- Text and number parameters: no change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The parameter declaration MUST have an optional boolean flag `multiple`. The default is false. The flag MUST be valid only for kind `image` and kind `choice`. A declaration of any other kind with the flag set MUST be refused.
- **FR-002**: The declaration MUST accept optional whole numbers `minItems` and `maxItems`, valid only when `multiple` is true. Each limit MUST be 1 or more. `minItems` MUST NOT be greater than `maxItems`, and MUST NOT be greater than the number of allowed items.
- **FR-003**: The API and the published schema (Swagger) MUST accept and return `multiple`, `minItems`, and `maxItems` on the parameter declaration. A declaration without these fields MUST give the same response as before this feature.
- **FR-004**: The value form MUST show all allowed items of a multi-value parameter and MUST let the person select one or more. The form MUST keep the order of selection.
- **FR-005**: The stored value of a multi-value parameter MUST be an ordered list of unique values. The system MUST remove duplicates and keep the first occurrence of each item.
- **FR-006**: Entry-layer values, template values, and queue entry parameter values MUST accept a list value for a multi-value parameter, and MUST keep the order.
- **FR-007**: A scalar value saved before the flag existed MUST be read as a list of one item when the declaration is multi-value. Single-value parameters MUST keep their behavior.
- **FR-008**: The system MUST refuse an empty list for a required multi-value parameter, at save time for values the save can know and when a queue starts.
- **FR-009**: The system MUST give the existing invalid-value warning for each list item that is not in `allowedImages` (image) or `options` (choice). The warning MUST NOT block a queue start.
- **FR-010**: When `minItems` or `maxItems` is set, the system MUST refuse a list with fewer items than `minItems` or more items than `maxItems`.
- **FR-011**: A `{{name}}` placeholder in a text field MUST resolve a list value to the items in list order, joined by `|`. A list of one item MUST resolve to the item alone.
- **FR-012**: A placeholder that is the whole value of a field that takes one value (one image id or one number) MUST resolve a list of one item to that item. A list of two or more items MUST fail the step with a message that names the parameter, the field, and the item count.
- **FR-013**: A binding between two multi-value parameters MUST pass the full ordered list. A list of two or more items bound to a parameter that is not multi-value MUST give the invalid-value warning.
- **FR-013a**: The API MUST carry a list value as an array of text items. A plain text value MUST still be accepted for a multi-value parameter and MUST be read as a list of one. A declaration of a multi-value parameter MUST be refused when an allowed image id or an option value holds the character `|`.
- **FR-014**: The default of a multi-value parameter MUST be read as a list in the same way as a stored scalar (FR-007). The default MAY hold several items joined by `|`. Each item MUST be an allowed item, and the save MUST refuse a default that breaks this rule or the item limits.
- **FR-015**: This feature MUST NOT add an any-of image condition and MUST NOT change the `text` and `number` kinds.
- **FR-016** *(web UI change)*: The value form change MUST state its phone support level and follow docs/mobile-ui-principles.md. The support level is full: the icon selection works by touch and fits a phone width.
- **FR-017**: Tests MUST cover each requirement above. The docs and the API contract MUST describe the new fields, the stored list form, and the resolution rules.

### Key Entities

- **Parameter declaration**: gets the new fields `multiple`, `minItems`, and `maxItems`. Kind stays `image` or `choice` for multi-value use.
- **Parameter value**: a scalar or an ordered list of unique values. A scalar for a multi-value parameter is read as a list of one.
- **Parameter binding / entry value**: a named value in an entry layer, a template entry, or a queue entry. It may hold the list.

## Assumptions

- The flag is named `multiple`, as the issue proposes. The limits are named `minItems` and `maxItems`.
- Pipe-joined text is the text form of a list because it works in regular expression alternation, which is a common use for patterns.
- A list with several items in a one-value field fails the step instead of using one item, so that no choice is made silently.
- The unique-value rule keeps first occurrences, so the operator order is kept.
- Existing warnings and the rule that stale values never stop a queue start (feature 168) stay in force.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator selects two or more quests in one value form and saves them in one queue entry, with no extra task or sequence copy.
- **SC-002**: 100% of values saved before this feature load and run with the same result as before.
- **SC-003**: 100% of lists that break a rule (empty required list, count outside the limits) are refused with a message that names the parameter and the rule.
- **SC-004**: Each list-resolution rule (text, one-value field, binding, default) has at least one automated test with the expected result.
- **SC-005**: The published schema and the docs show the new fields, and a reader finds the resolution rules without reading the code.
