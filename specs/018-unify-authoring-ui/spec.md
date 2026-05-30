# Feature Specification: Unified Authoring Object Pages

**Feature Branch**: `[017-unify-authoring-ui]`  
**Created**: 2025-12-27  
**Status**: Draft  
**Input**: User description: "Unify the page format for all the objects. Use Actions as Template and implement the same UI for all other objects. Authoring UI is supposed to be used by non-technical users, so instead of specifying JSON and technical IDs, always use the dropdowns for referenced objects and individual atttribute fields. Pay attention to the arrays in the API: it means the arrays have to be manupulated (CRUD+reorder) as new objects in the UI."

## Clarifications

### Session 2025-12-27

- Q: Should reference dropdowns allow creating new referenced objects inline or only pick existing ones? → A: Provide a “Create new” entry that opens the unified creation flow in a side panel/modal, then returns and auto-selects the new item.
- Q: Should saves apply immediately or use draft/publish? → A: Every save updates the live object immediately (no drafts).

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

### User Story 1 - Create any object with the unified page (Priority: P1)

Non-technical author creates or edits an object (e.g., Command, Trigger, Game profile) using the Action-style page layout with clear sections, field-level inputs, and dropdowns for all references instead of JSON or IDs.

**Why this priority**: Establishes a single mental model across the catalog so any object can be authored without technical knowledge.

**Independent Test**: A participant can create a new Command from scratch using only form fields and dropdowns (no raw JSON/IDs) and save it successfully.

**Acceptance Scenarios**:

1. **Given** the authoring UI is open on the new Command page, **When** the user fills required fields and selects linked Actions from dropdowns, **Then** the Command saves without exposing JSON or ID inputs.
2. **Given** an existing Trigger, **When** the user opens it in the unified layout, edits a field, and saves, **Then** the change persists and the layout matches the Action template sections.

---

### User Story 2 - Manage array-based fields visually (Priority: P2)

Author edits collections (e.g., ordered steps, detection targets, trigger actions) through add/edit/remove/reorder controls that mirror the Action template pattern.

**Why this priority**: Arrays are central to configuring behavior; non-technical authors must manage them without hand-editing JSON arrays.

**Independent Test**: User adds two items, reorders them, deletes one, and saves with the correct order preserved for at least one object type.

**Acceptance Scenarios**:

1. **Given** a list of sequence steps, **When** the user adds a step via “Add step”, edits its fields inline, and drags it above another step, **Then** the saved sequence reflects the new content and order.
2. **Given** a trigger with multiple actions, **When** the user removes one action using the UI control, **Then** the remaining actions stay in order and no orphaned references appear.

---

### User Story 3 - Confidently navigate across object types (Priority: P3)

Author switches between different object pages (Action, Command, Trigger, Game profile) and recognizes the same page structure, field behaviors, and save/cancel affordances.

**Why this priority**: Consistency reduces training time and data entry errors when working across multiple object types.

**Independent Test**: In a task where users edit three different object types, they can locate equivalent sections without guidance and complete edits within expected time.

**Acceptance Scenarios**:

1. **Given** the unified layout, **When** the user opens an Action then a Command, **Then** section ordering, buttons, and reference controls remain consistent.
2. **Given** a user unfamiliar with Triggers, **When** they open the Trigger page after editing Actions, **Then** they can identify where to edit conditions and referenced actions without help.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Large lists (10+ items) in array sections still support drag/drop reorder and remain scrollable without losing context.
- Missing or deleted referenced objects are surfaced with clear messaging and a safe fallback (e.g., disable save until resolved).
- Unsaved changes warning triggers when navigating away from a page with pending edits in any section.
- Validation errors highlight both the field and its section, preventing partial saves that would produce inconsistent arrays or references.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: All object pages (Actions, Commands, Triggers, Game profiles, Sequences) MUST follow the Action page structure: overview, required metadata, references, array sections, and finalize controls.
- **FR-002**: Creation and edit flows MUST expose field-level inputs for every attribute; raw JSON or direct ID entry MUST NOT be required or shown.
- **FR-003**: Reference fields MUST use searchable dropdowns that list valid objects with human-friendly labels and brief context (e.g., name, type) before selection.
- **FR-003a**: Reference dropdowns MUST include a “Create new” affordance that opens the standard unified creation experience in a side panel/modal and auto-selects the created item on return.
- **FR-004**: Array attributes MUST support add, edit, delete, and reorder interactions consistent across object types, with visual confirmation of order changes prior to save.
- **FR-005**: Required fields MUST be validated with inline guidance; save MUST be blocked until required inputs are complete and references resolve.
- **FR-006**: Unsaved changes MUST trigger a warning before navigation away or page close, preserving in-progress edits until the user explicitly discards them.
- **FR-007**: Save/Cancel MUST appear in the same location and behave consistently on every object page; no separate draft/publish flow.
- **FR-008**: Contextual help/tooltips MUST explain field intent for non-technical users, especially where terms originate from backend models.
- **FR-009**: After save, the system MUST return to a view that reflects the updated object and confirms that arrays and references persist in the chosen order.
- **FR-010**: List/search entry points for each object type MUST route to the unified detail page, ensuring no legacy layout remains accessible.
- **FR-011**: Saving an object writes changes live immediately; the UI MUST communicate this and confirm successful save without a draft stage.

### Key Entities *(include if feature involves data)*

- **Action**: Reusable operation with attributes like name, parameters, and execution details; can be referenced by Commands or Triggers.
- **Command**: User-facing command definition that references Actions and detection targets; includes metadata, parameters, and ordered steps/targets.
- **Trigger**: Condition set that links events to Actions or Commands; includes criteria arrays and referenced behaviors.
- **Game Profile**: Object grouping game metadata, bindings to actions/commands/triggers, and arrays such as input mappings.
- **Sequence/Step Collection**: Ordered arrays of steps or actions that must support insertion, editing, and reorder.

### Assumptions

- The object types above represent the full set of authorable items; new types will reuse the same layout rules.
- Reference dropdowns can be populated from existing catalog data with stable display names.
- Typical array sizes are manageable (dozens, not hundreds), allowing drag/drop without pagination.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: In usability tests, 90% of non-technical users create a new Command without encountering JSON/ID fields and complete the flow in under 3 minutes.
- **SC-002**: All object detail pages share identical section order and control placement, confirmed by a UI audit checklist with 100% compliance.
- **SC-003**: Array operations (add, edit, delete, reorder) succeed on first attempt at least 95% of the time across object types during validation sessions.
- **SC-004**: At least 90% of users rate clarity ≥ 4/5 when switching between two different object pages in a survey after guided tasks.
- **SC-005**: Data entry error tickets related to authoring drop by 30% in the first release cycle after rollout.
