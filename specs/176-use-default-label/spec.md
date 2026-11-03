# Feature Specification: Use default label in the parameter value form

**Feature Branch**: `176-use-default-label`
**Created**: 2026-11-03
**Status**: Implemented
**Input**: User description: "FR-039: Rename the Inherit box in the parameter value form to Use default" (GitHub issue #356, Closes #356)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Understand the box label (Priority: P1)

A person fills the parameter values of a sequence. Each row has a box. Today the label of the box is "Inherit". This is a technical word. The person cannot tell what the box does. The label must say "Use default".

**Why this priority**: This is the full change. It removes the confusion found in the manual check of feature 168 (Check 1).

**Independent Test**: Open the value form in each place that uses it. Look at the label of the box on each row.

**Acceptance Scenarios**:

1. **Given** the value form in a queue template entry, **When** the person looks at a row, **Then** the box label is "Use default" and the word "Inherit" is not shown.
2. **Given** the value form in a run request, **When** the person looks at a row, **Then** the box label is "Use default".
3. **Given** the value form in a step binding, **When** the person looks at a row, **Then** the box label is "Use default".
4. **Given** a screen reader user, **When** the focus is on the box of the parameter named `x`, **Then** the accessible name matches the new label (for example "Use default x").

---

### User Story 2 - See which default applies (Priority: P2)

A person must know the value that "Use default" gives. The form shows the default value next to the label. When the parameter has no default, the form shows "No default".

**Why this priority**: The label alone does not tell the person the value. This text helps the person decide to clear the box.

**Independent Test**: Open the form with one parameter that has a default and one that has none. Check the text next to each label.

**Acceptance Scenarios**:

1. **Given** a parameter with a default value, **When** the row shows, **Then** the default value shows next to the "Use default" label.
2. **Given** a parameter with no default value, **When** the row shows, **Then** "No default" shows next to the "Use default" label.
3. **Given** a parameter with no default, **When** the row shows, **Then** the box is still shown (a higher layer can give a value).

---

### Edge Cases

- A parameter has an empty-string default: the form treats it as a default and shows the marker "(empty)", not "No default".
- A parameter has a long default value: the text stays readable on a phone screen and does not break the row layout.
- The box is set and no higher layer gives a value and there is no default: the run fails with the existing error "could not be resolved from any scope". This does not change.

## Clarifications

### Session 2026-11-03

- Q: How does the default show when the default is a non-text value (image, option, number)? -> A: The form shows the same readable text that the value field shows for that type (for example the option label). Rationale: the person must recognise the value.
- Q: Does the author preview (feature 168) get the new text? -> A: No. The preview has no box, so it does not change. Rationale: the issue names only the box.
- Q: Is the default text part of the accessible name? -> A: No. The accessible name is "Use default <name>". The default text is a separate visible text. Rationale: the issue asks only for the name to match the label.

### Session 2026-11-03 (loop 1, from analyze)

- Q (U2): Which default text shows for option and number types? -> A: The same readable text as the value field for that type, reusing the existing display helper. If no helper exists, add one. A test covers an option default and a number default. Rationale: a raw value is not readable.
- Q (A1): How do SC-004 and FR-009 pass? -> A: SC-004 passes when one reviewer says yes to the question "does the label tell what the box does". FR-009 passes at the narrowest phone width in docs/mobile-ui-principles.md, with no clipped text. Rationale: one yes/no check each.
- Note (C1): The change is user-visible, so a CHANGELOG entry and a version bump are part of the work.

### Session 2026-11-03 (loop 2, from analyze)

- Q (I1): What text shows for an empty-string default? -> A: The marker "(empty)" shows next to the label. It is not "No default". Rationale: the person sees that a default exists and is empty.
- Q (G1): Does an image default show? -> A: Yes. An image default shows its image id as text. A test covers it. Rationale: the spec treats image like the other types.
- Q (U2): What is the FR-012 Status value? -> A: Follow the row of feature 175 in specs/STATUS.md as the model.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The value form MUST show the label "Use default" in place of "Inherit" in every place that uses it: queue template entry, run request, and step binding.
- **FR-002**: The accessible name of the box MUST match the new label.
- **FR-003**: When the parameter has a default value, the row MUST show the default value next to the label. An empty-string default MUST show the marker "(empty)". An image default MUST show its image id. Tests MUST cover these default types: choice, number, image, empty, null, and undefined.
- **FR-004**: When the parameter has no default value, the row MUST show "No default" next to the label.
- **FR-005**: The box MUST stay visible when the parameter has no default.
- **FR-006**: The behaviour of the box MUST NOT change: set means no value in this place; clear means a value for this place only.
- **FR-007**: The rules that resolve values across layers (feature 078) MUST NOT change. Existing resolve and run tests MUST pass with unchanged expected values.
- **FR-008**: Tests and text in docs/ that use the word "Inherit" for this box MUST use the new label.
- **FR-009**: The feature MUST state its phone support level (full) and follow docs/mobile-ui-principles.md. The new text MUST fit on a phone screen.
- **FR-010**: The change MUST add a CHANGELOG entry written in ASD-STE100 STE.
- **FR-011**: The change MUST bump the version in `installer/versioning/version.override.json` as CLAUDE.md and the constitution ("Version bump") require.
- **FR-012**: The change MUST update the Status of spec 176 and the entry in `specs/STATUS.md` as the project convention needs. Use the row of feature 175 in `specs/STATUS.md` as the format model.
- **FR-013**: Code comments in `ParameterBindingForm.tsx` and `ParameterFormPreview.tsx` that use the word "Inherit" for this box MUST change to "Use default".

### Key Entities

- **Parameter row**: One parameter of a sequence. It has a name, an optional default value, a "Use default" box, and a value field.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The word "Inherit" shows in 0 places of the value form in the 3 places that use it.
- **SC-002**: 100% of rows show either the default value or "No default" next to the label.
- **SC-003**: 100% of existing resolve and run tests pass with no change to their expected values.
- **SC-004**: In a manual check, a person can tell what the box does without help. The reviewer result goes in the PR description.

## Assumptions

- The author preview of feature 168 has no box and does not change.
- Only the visible label, the accessible name, the default text, and code comments change.
- The C# names that use `Inherit`, the `INHERIT` sentinel, and the backend tests stay unchanged (FR-006, FR-007).
- Feature 168 (author preview) is iterated by this feature. Add a note to its spec only if the `specs/STATUS.md` convention needs it.
- The class `parameter-binding-inherit` has no CSS rule today. The implementation MUST find the real stylesheet (search `src/web-ui/src` for the stylesheet of the parameters component) or use an existing styling approach for the default text, and the plan MUST name it. The plan names the stylesheet.
- The phone-width check result (FR-009) and the phone-check result go in the PR description.
- Tasks that edit `ParameterBindingForm.tsx` run in sequence. They have no parallel mark.
- Test and build commands use absolute paths and no directory change, for example `npm --prefix "C:\src\GameBot\src\web-ui" test` and `dotnet test` with an absolute solution path.
- The 44 px touch target rule still applies to the new layout. The phone check verifies it.
- The version bump rule is "patch on origin/master plus 1". The value is computed at push time. The spec has no fixed number.

## Out of Scope

- Any change to how values resolve across layers.
- Hide of the box when the parameter has no default. This needs a separate design.
