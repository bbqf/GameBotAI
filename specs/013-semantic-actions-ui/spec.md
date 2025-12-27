# Feature Specification: Semantic Actions UI

**Feature Branch**: `[001-semantic-actions-ui]`  
**Created**: 2025-12-27  
**Status**: Draft  
**Input**: User description: "Semantic UI for Actions. I need UI to be intelligent and to support the action types that the API supports. The user should be able to specify all supported actions and their attributes that differ, depending on action type, without having to enter JSON. The attribute values should be validated."

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

### User Story 1 - Create a valid action without JSON (Priority: P1)

Users select an action type from a list of supported types, see contextual fields that match that type, enter attributes with guidance, and save a valid action without touching raw JSON.

**Why this priority**: This is the core value—enables non-technical users to author actions successfully and quickly.

**Independent Test**: A tester can create a brand-new action end-to-end by selecting a type, completing fields, and saving successfully; no other features are required.

**Acceptance Scenarios**:

1. **Given** no existing actions, **When** the user selects an action type and completes all required attributes with valid values, **Then** the system confirms the action is valid and saves it.
2. **Given** the user enters an out-of-range or wrong-format value, **When** they attempt to save, **Then** the UI blocks saving and presents clear, field-specific validation guidance.

---

### User Story 2 - Edit an existing action safely (Priority: P2)

Users open an existing action, update attributes, optionally change the action type, and save changes with safeguards (e.g., confirmation when type change discards incompatible attributes).

**Why this priority**: Editing is essential for iteration; safeguards prevent accidental data loss and invalid states.

**Independent Test**: A tester can open an existing action, make valid and invalid edits, observe validation behavior, and save successfully when valid.

**Acceptance Scenarios**:

1. **Given** an existing action, **When** the user updates attributes with valid inputs, **Then** the system validates and saves changes.
2. **Given** an existing action with filled attributes, **When** the user changes the type to one with different attributes, **Then** the UI warns about incompatible attributes and requires explicit confirmation before proceeding.

---

### User Story 3 - Browse and duplicate actions (Priority: P3)

Users view a list of actions, filter by type, and duplicate an action to accelerate authoring.

**Why this priority**: Improves efficiency and discoverability; duplication reduces repetitive manual entry.

**Independent Test**: A tester can filter actions, select one, duplicate it, and save the duplicate after minimal edits.

**Acceptance Scenarios**:

1. **Given** multiple actions exist, **When** the user filters by a specific type, **Then** only actions of that type are shown.
2. **Given** an action is selected, **When** the user chooses Duplicate, **Then** a new unsaved action is created with identical attributes and can be saved after validation.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Switching action type after entering attributes: incompatible attributes are discarded only after explicit confirmation; compatible attributes are retained.
- Required attribute missing: saving is blocked with clear, inline error messages and guidance.
- Numeric attributes: reject non-numeric input and out-of-range values with precise messages (show allowed ranges).
- Enumerated attributes: only allow values from defined sets; show friendly labels.
- Unknown/unsupported action type appears (e.g., backend adds a new type not yet recognized): the UI gracefully prevents selection and instructs users to update definitions.
- Very large attribute payloads: UI remains responsive; validation completes under reasonable time constraints (see Success Criteria).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The UI MUST present a list of supported action types to the user and allow selecting exactly one type for an action.
- **FR-002**: The UI MUST render contextual attribute fields based on the selected action type and hide fields irrelevant to that type.
- **FR-003**: The UI MUST validate attribute values client-side before save: requiredness, allowed ranges, formats, and enumerations must be enforced.
- **FR-004**: The UI MUST display clear, field-level error messages and guidance when validation fails, preventing ambiguous or generic errors.
- **FR-005**: The UI MUST block saving when any required attribute is invalid or missing; draft saves are not supported for invalid actions.
- **FR-006**: On action type change, the UI MUST warn users if any existing attributes will be discarded and require explicit confirmation to proceed.
- **FR-007**: The system MUST source the catalog of supported action types and their attribute definitions dynamically from the backend as the single authoritative source.
- **FR-008**: The UI MUST enable editing of existing actions with the same validation guarantees as creation.
- **FR-009**: The UI SHOULD provide duplication of an existing action to accelerate authoring, with independent validation on the duplicate.
- **FR-010**: The UI MUST ensure accessibility basics (clear labels, keyboard navigation, readable contrast) so that users can complete authoring without mouse-only interactions.
- **FR-011**: Real-time preview/simulation is out of scope for this release; authoring focuses on validated form-based entry.

Assumptions: Users are non-technical authors who prefer forms over raw JSON. All validation rules (requiredness, ranges, formats, enums) are available from an authoritative definition. Saving writes through existing backend flows; this spec focuses on user value, not implementation details.

### Key Entities *(include if feature involves data)*

- **Action**: Represents a user-authored instruction; attributes: identifier, display name, `type`, `attributes` (key-value pairs governed by type definition), validation status (valid/invalid with messages).
- **Attribute Definition**: Describes a single attribute for a given action type; includes label, data type (text, number, boolean, enum), constraints (required, range, pattern, allowed values), help text.
- **Action Type**: The category of action; defines the set of attribute definitions and any inter-attribute constraints.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: First-time users can create and save a valid action in under 60 seconds.
- **SC-002**: 90% of users successfully complete action creation on their first attempt.
- **SC-003**: 100% of invalid attribute inputs are caught before save, with clear guidance enabling correction.
- **SC-004**: Reduce support requests related to “JSON authoring” by 80% within one release cycle.
- **SC-005**: Validation feedback appears instantly (perceived as immediate) and does not block normal typing or selection.
