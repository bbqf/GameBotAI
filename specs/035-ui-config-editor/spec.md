# Feature Specification: UI Configuration Editor

**Feature Branch**: `035-ui-config-editor`  
**Created**: 2026-04-14  
**Status**: Draft  
**Input**: User description: "Introduce a UI Configuration editor with dynamic backend-driven parameters, drag-and-drop reorder, filtering, and collapsible Backend section for API Base URL and Bearer Token"

## Overview

The Configuration tab currently only exposes API Base URL and Bearer Token fields. These are rarely changed and crowd out the many backend configuration parameters that operators need to tune regularly (log levels, detection thresholds, worker intervals, etc.). This feature replaces the existing minimal Configuration page with a full-featured editor that:

1. Hides the API Base URL / Bearer Token behind a collapsible "Backend Connection" section.
2. Dynamically renders every configuration parameter reported by the backend—automatically adapting when new parameters are added server-side.
3. Lets operators edit parameter values and apply changes to the running backend.
4. Supports drag-and-drop reordering of parameters so frequently changed items can be pinned to the top, with order persisted in the backend configuration file.
5. Provides a live text filter across parameter names and values.

## Assumptions

- The backend will need a new API endpoint (or extension of an existing one) to accept parameter value updates and persist them.
- Parameter ordering is persisted as the key order in the backend configuration JSON file; the UI will send a reorder request that the backend honours.
- Environment-sourced parameters are read-only in the UI (they can only be changed by modifying the host environment).
- Secret parameters (tokens, passwords) display masked values and may be edited but never reveal their stored value.
- The collapsible "Backend Connection" section defaults to collapsed when the page loads.
- Drag-and-drop reorder operates on the visible (filtered) list; dropping causes a full-list reorder that is sent to the backend.
- No authentication/authorisation changes are in scope—existing bearer token gating applies.

## Clarifications

### Session 2026-04-14

- Q: Are Default-sourced parameters editable or read-only? → A: Editable; editing a Default-sourced parameter promotes it to File source on save.
- Q: Should the UI warn when navigating away with unsaved edits? → A: Yes; dirty-state indicator on modified rows plus a confirmation prompt on navigate-away.
- Q: Should apply be per-row, global, or both? → A: Global "Apply All" button only; no per-row save action.
- Q: Should the UI render type-aware controls or plain text for parameter values? → A: Plain text inputs for all values; no type-specific controls.
- Q: Should reorder be persisted immediately on drop or batched with Apply All? → A: Immediately on drop; reorder is independent of value changes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 – View All Backend Configuration Parameters (Priority: P1)

An operator opens the Configuration tab and sees every configuration parameter the backend currently knows about, displayed as a scrollable list of name/value rows. Parameters appear in the same order they are stored in the backend configuration file. The list updates automatically whenever the backend adds or removes parameters (e.g., after a service upgrade and page refresh).

**Why this priority**: This is the foundational capability—without seeing parameters, no other editing, reordering, or filtering feature is useful.

**Independent Test**: Can be fully tested by loading the Configuration page and verifying every parameter returned by the backend appears in the UI in the correct order.

**Acceptance Scenarios**:

1. **Given** the backend reports 25 parameters, **When** the operator opens the Configuration tab, **Then** all 25 parameters are rendered with their names, values, and source indicators.
2. **Given** a new parameter is added to the backend configuration, **When** the operator refreshes the page, **Then** the new parameter appears in the list without any UI code change.
3. **Given** a parameter has source "Environment", **When** it is displayed, **Then** it is visually marked as read-only.
4. **Given** a parameter is flagged as secret, **When** it is displayed, **Then** its value shows a masked placeholder (e.g., "•••").

---

### User Story 2 – Edit a Parameter and Apply to Backend (Priority: P1)

An operator changes the value of a non-read-only parameter (e.g., log level, detection threshold) and clicks an "Apply" action. The new value is sent to the backend, persisted in the configuration file, and applied to the running service. The UI confirms success or shows an error.

**Why this priority**: Editing is the core use case—operators need to tune the service without restarting it or editing files by hand.

**Independent Test**: Can be tested by changing a parameter value in the UI, clicking Apply, then verifying the backend reports the updated value on the next GET.

**Acceptance Scenarios**:

1. **Given** the operator changes the value of a File-sourced parameter, **When** they click Apply, **Then** the backend persists the change and the refreshed snapshot reflects the new value.
2. **Given** the operator edits a secret parameter, **When** they type a new value and apply, **Then** the value is sent to the backend and stored; the UI continues to display a masked placeholder.
3. **Given** the operator tries to change an Environment-sourced parameter, **When** they view the row, **Then** the value field is disabled/read-only and no Apply action is available.
4. **Given** the backend rejects the update (e.g., invalid value), **When** the apply request fails, **Then** the UI shows an inline error message on that parameter row.
5. **Given** the operator edits multiple parameters, **When** they apply changes, **Then** all modified values are sent and persisted in a single operation.
6. **Given** the operator has unsaved edits, **When** they attempt to navigate away from the Configuration tab, **Then** a confirmation prompt warns them about unsaved changes.
7. **Given** one or more parameters have been modified but not yet applied, **When** the operator views the list, **Then** modified rows are visually highlighted to indicate dirty state.

---

### User Story 3 – Collapse Backend Connection Settings (Priority: P2)

The API Base URL and Bearer Token fields (currently the only content of the tab) are moved into a collapsible "Backend Connection" section at the top of the page. This section defaults to collapsed, keeping the focus on the backend parameters below.

**Why this priority**: Declutters the primary view; these two fields are seldom changed during normal operation.

**Independent Test**: Can be tested by verifying the section is collapsed on load, expanding it, changing the base URL, and confirming it still works.

**Acceptance Scenarios**:

1. **Given** the operator opens the Configuration tab, **When** the page loads, **Then** the "Backend Connection" section is collapsed and the dynamic parameter list is immediately visible.
2. **Given** the "Backend Connection" section is collapsed, **When** the operator clicks its header, **Then** it expands to reveal API Base URL and Bearer Token fields.
3. **Given** the section is expanded, **When** the operator changes the API Base URL, **Then** the change is applied across the app as it does today.

---

### User Story 4 – Filter Parameters by Name or Value (Priority: P2)

A search/filter input at the top of the parameter list lets the operator type a substring. Only parameters whose name or current value contains the substring (case-insensitive) are shown.

**Why this priority**: With 25+ (and growing) parameters, finding a specific one quickly is essential for usability.

**Independent Test**: Can be tested by typing a substring into the filter and verifying only matching rows remain visible.

**Acceptance Scenarios**:

1. **Given** the operator types "tesseract" into the filter, **When** the list updates, **Then** only parameters containing "tesseract" in their name or value are shown.
2. **Given** the operator clears the filter, **When** the input is empty, **Then** all parameters reappear in their current order.
3. **Given** no parameter matches the filter text, **When** the list updates, **Then** an empty-state message such as "No matching parameters" is displayed.
4. **Given** the filter is active, **When** the operator types additional characters, **Then** the list narrows incrementally without a perceptible delay.

---

### User Story 5 – Drag-and-Drop Parameter Reorder (Priority: P3)

The operator drags a parameter row to a new position in the list. The new order is sent to the backend and persisted in the configuration file so that it survives page reloads and service restarts.

**Why this priority**: Convenient but not blocking—operators can still scroll or filter to find parameters without reordering.

**Independent Test**: Can be tested by dragging a parameter to a new position, refreshing the page, and confirming the order is preserved.

**Acceptance Scenarios**:

1. **Given** the parameter list is displayed, **When** the operator drags "Logging__LogLevel__Default" from position 20 to position 1, **Then** the list re-renders with the parameter at position 1.
2. **Given** a reorder has been performed, **When** the operator refreshes the page, **Then** the parameters appear in the new persisted order.
3. **Given** the filter is active, **When** the operator reorders parameters within the filtered view, **Then** the reorder correctly maps to the full list positions and is persisted.
4. **Given** the backend is unreachable, **When** the reorder persist request fails, **Then** the UI reverts to the previous order and shows an error notification.

---

### Edge Cases

- What happens when the backend is offline when the page loads? — The parameter list shows an error state with a "Retry" button; the Backend Connection section remains usable so the operator can correct the base URL.
- What happens when two operators edit the same parameter concurrently? — Last-write-wins; no locking is required, but the UI refreshes the snapshot after applying so the operator sees the current state.
- What happens when a parameter's value is very long (e.g., a base-64 encoded image)? — Out of scope for this feature. Values render in a standard-width text input; no truncation or expandable textarea is provided. May be addressed in a future enhancement.
- What happens when the backend removes a parameter between loads? — The parameter disappears on the next refresh; any unsaved edit to that parameter is discarded.
- What happens when the user drags onto a read-only (Environment) parameter? — Reorder still works; read-only is about editing the value, not about list position.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Configuration page MUST display all parameters from the backend configuration snapshot in the order they appear in the configuration data.
- **FR-002**: The parameter list MUST update dynamically—when the backend adds or removes parameters, the UI MUST reflect the change after a page refresh without code deployment.
- **FR-003**: Each parameter row MUST display the parameter name, its current value (masked for secrets), and its source (Default, File, or Environment).
- **FR-004**: Parameters sourced from "Environment" MUST be rendered as read-only; the operator MUST NOT be able to edit their values. Parameters sourced from "Default" MUST be editable; saving a modified Default parameter MUST persist it as a File-sourced override.
- **FR-005**: The operator MUST be able to edit the value of any non-read-only parameter inline within the list.
- **FR-006**: The UI MUST provide a single global "Apply All" mechanism that sends all modified parameter values to the backend in a single request. There MUST NOT be per-row save actions.
- **FR-007**: The backend MUST persist updated parameter values to the configuration file and apply them to the running service without requiring a restart.
- **FR-008**: The API Base URL and Bearer Token fields MUST be placed inside a collapsible "Backend Connection" section at the top of the Configuration page.
- **FR-009**: The "Backend Connection" section MUST default to collapsed on page load.
- **FR-010**: A text filter input MUST be provided above the parameter list; filtering MUST be case-insensitive and match against both parameter name and value substrings.
- **FR-011**: The operator MUST be able to reorder parameters via drag and drop.
- **FR-012**: Parameter reorder MUST be persisted to the backend immediately on drop, independently of the "Apply All" value-change flow. The persisted order MUST survive page reloads and service restarts.
- **FR-013**: Secret parameter values MUST be masked in the UI and MUST NOT be logged or exposed in network responses in plain text.
- **FR-014**: When the backend is unreachable, the UI MUST show an appropriate error state and allow the operator to retry or adjust the backend connection settings.
- **FR-015**: When applying changes fails, the UI MUST display an error message and preserve the operator's unsaved edits so they can retry.
- **FR-016**: Modified but unapplied parameter rows MUST be visually highlighted to indicate dirty state. If the operator navigates away from the Configuration tab with unsaved edits, the UI MUST display a confirmation prompt before discarding changes.
- **FR-017**: All parameter values MUST be rendered as plain text inputs regardless of their underlying data type. Type-specific controls (toggles, spinners) are out of scope.

### Key Entities

- **ConfigurationParameter**: A single setting known to the backend. Key attributes: name (unique identifier), value (current effective value), source (Default / File / Environment), isSecret (whether the value should be masked).
- **ConfigurationSnapshot**: A point-in-time view of all configuration parameters as reported by the backend. Includes metadata such as generation timestamp, service version, and refresh count.
- **ParameterOrder**: Not a discrete entity. Display order is determined by the insertion order of keys in the `Parameters` dictionary of `ConfigurationSnapshot`, which `System.Text.Json` preserves during serialization. See research R2.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All backend configuration parameters are visible in the UI within one page load—operators no longer need to inspect configuration files on disk to see current settings.
- **SC-002**: Operators can change a parameter value and have it take effect on the running service in under 30 seconds (edit → apply → confirmation).
- **SC-003**: When a new backend parameter is added, it appears in the UI after a page refresh with zero frontend code changes.
- **SC-004**: The filter narrows the parameter list to matching results within 200 ms of keystroke input for up to 100 parameters.
- **SC-005**: Drag-and-drop reorder persists across page reloads: the parameter list order after refresh matches the order the operator set.
- **SC-006**: The "Backend Connection" section is collapsed by default, reducing visual noise for returning operators on 100% of visits.
