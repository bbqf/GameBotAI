# Feature Specification: Authoring CRUD UI

**Feature Branch**: `001-authoring-crud-ui`  
**Created**: 2025-12-26  
**Status**: Draft  
**Input**: Extend the web UI authoring experience: add a navigation menu to select object types (actions, commands, games, sequences, triggers). For each, display a list of existing objects and provide full CRUD operations aligned with available backend capabilities. When an API requires referencing another object by ID, present a dropdown showing human-readable names while using IDs internally.

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

### User Story 1 - Navigate and browse objects (Priority: P1)

Users open the authoring UI, use a navigation menu to select an object type (actions, commands, games, sequences, triggers), and see a list of existing objects with key details.

**Why this priority**: Enables discovery and orientation; foundational for all CRUD tasks.

**Independent Test**: Verify that a user can select any object type and view its list without performing create/edit/delete.

**Acceptance Scenarios**:

1. **Given** the user is on the authoring UI, **When** they select "Actions" in the navigation, **Then** the actions list loads showing names and primary attributes.
2. **Given** a large set of objects, **When** the list loads, **Then** items are ordered predictably (e.g., by name) and render within the expected time.

---

### User Story 2 - Create new object with references (Priority: P1)

Users create a new item in any object type and, where needed, select related objects using dropdowns that display names while persisting IDs.

**Why this priority**: Establishes authoring capability; references must be easy and accurate.

**Independent Test**: Create a new command that references existing actions via dropdowns and verify it is persisted and listed.

**Acceptance Scenarios**:

1. **Given** the user selects "Commands", **When** they click "Create" and fill required fields including selecting one or more actions from name-based dropdowns, **Then** the new command is created and appears in the list.
2. **Given** a required reference, **When** the user selects a name from the dropdown, **Then** the system stores/sends the correct underlying ID.

---

### User Story 3 - Edit and delete existing objects (Priority: P2)

Users open an existing item to update fields and references; users can also delete with confirmation.

**Why this priority**: Maintains data accuracy and enables cleanup.

**Independent Test**: Edit an existing trigger’s attributes and linked objects, then delete the trigger with confirmation.

**Acceptance Scenarios**:

1. **Given** an existing sequence, **When** the user edits its steps and saves, **Then** the updated sequence appears in the list and reflects changes.
2. **Given** an existing item, **When** the user chooses delete and confirms, **Then** the item is removed and the list updates.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- No objects exist for a type: show an empty-state message with a clear "Create" action.
- Duplicate names across objects: dropdowns must disambiguate (e.g., include type or additional context). [NEEDS CLARIFICATION: Are names unique per type?]
- Deleting objects referenced by others: prevent deletion or guide resolution. [NEEDS CLARIFICATION: Soft delete vs permanent and handling of references]
- Backend error or validation failure: show friendly, actionable error guidance and preserve user inputs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide a navigation menu with entries for actions, commands, games, sequences, and triggers.
- **FR-002**: For each entry, display a list view showing object names and key attributes, ordered predictably (e.g., by name).
- **FR-003**: Support create, read, update, and delete operations for each object type with clear success and error messaging.
- **FR-004**: When a field requires referencing another object, present a dropdown showing names; store/send the correct underlying IDs.
- **FR-005**: Validate required fields and constraints before submission; prevent invalid saves and preserve user input on error.
- **FR-006**: Confirm destructive actions (delete) with an explicit confirmation step.
- **FR-007**: Lists update immediately after successful create, edit, or delete to reflect the current state.
- **FR-008**: Provide basic filtering/search by name in lists when counts are large (assumed threshold). [Assumption]
- **FR-009**: Access to CRUD actions is limited to authorized users. [NEEDS CLARIFICATION: Who can perform destructive actions?]

### Key Entities *(include if feature involves data)*

- **Action**: Authoring unit with `id`, `name`, description; may be referenced by commands and triggers.
- **Command**: Executable instruction with `id`, `name`, parameters; may reference one or more actions.
- **Game**: Context container with `id`, `name`; groups actions/commands and metadata.
- **Sequence**: Ordered collection with `id`, `name`, steps referencing commands; supports reordering.
- **Trigger**: Condition set with `id`, `name`, criteria; may reference actions/commands or sequences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can navigate to any object type and see its list in under 5 seconds (95% of attempts).
- **SC-002**: Create/edit operations complete with confirmation within 3 seconds (95% of attempts) and reflect in lists immediately.
- **SC-003**: 90% of users successfully complete primary authoring tasks (create or edit) on first attempt during UAT.
- **SC-004**: Support inquiries about authoring tasks decrease by 40% after feature rollout.
