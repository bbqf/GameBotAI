# Feature Specification: Execution Logs Tab

**Feature Branch**: `029-execution-logs-tab`  
**Created**: 2026-03-02  
**Status**: Draft  
**Input**: User description: "I want to have the execution logs as a separate tab, on the same level as Execution, and after it, before Configuration. The tab should display a table with the following columns by default: timestamp (local time, not UTC), execution object name, status. Deault sorting is descending on the timestamp, however it should be possible to change the sort order by clicking on the column headers of any column. It should be also possible to filter by any column by entering a free text to filter for. By default no filters should be set. Filtering and sorting should be possible to combine, so the backend api should support this. When an entry is selected in the table, I should be able to see the details of the execution: summary, details from json, links to the executed objects and if available snapshot. and step outcomes. Make a suggestion for design that would work for small screen - phone and pc monitor, maybe make two different variants of the page. This page should be understandable for a non-tech user, so no json or similar should be displayed. Performance goals: first open should happen < 100ms, filter/sort changes for 1000 execution logs on the backend should happen within 300ms. Make sure these performance goals are met on the local and can be somewhat relaxed for the CI."

## Clarifications

### Session 2026-03-02

- Q: Who can access the Execution Logs tab? → A: Same visibility as existing authoring/execution pages (no new access restrictions).
- Q: How many logs should be shown by default on first open? → A: Show 50 most recent logs by default.
- Q: What matching rule should free-text filters use? → A: Case-insensitive contains match.
- Q: How should timestamps be displayed by default? → A: Default exact local timestamp, switchable to relative time.
- Q: What should happen during rapid sort/filter changes? → A: Cancel/ignore older requests and apply only the latest response.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review Recent Execution Outcomes (Priority: P1)

As an operator, I can open a dedicated Execution Logs tab and immediately see recent execution outcomes in a sortable, filterable list so I can quickly assess what happened.

**Why this priority**: This is the primary monitoring workflow and enables day-to-day confidence in automation results.

**Independent Test**: Can be fully tested by opening the tab, verifying default columns/sort, changing sort by header click, and applying free-text filters across columns.

**Acceptance Scenarios**:

1. **Given** the main navigation is shown, **When** the user views available tabs, **Then** an "Execution Logs" tab is present at the same level as "Execution" and appears after "Execution" and before "Configuration".
2. **Given** the user opens "Execution Logs", **When** the table loads, **Then** the default visible columns are timestamp (displayed in local time), execution object name, and status.
3. **Given** the table has entries, **When** the user first opens the tab, **Then** results are sorted by timestamp in descending order.
4. **Given** the user clicks any column header, **When** the header is clicked, **Then** sorting changes for that column and can be toggled between descending and ascending.
5. **Given** the user enters free text in a column filter, **When** filtering is applied, **Then** only entries matching that column filter are shown.
6. **Given** filters and sorting are both set, **When** the list refreshes, **Then** the result reflects the combined filter and sort state.
7. **Given** the user can access existing authoring/execution pages, **When** the user opens navigation, **Then** "Execution Logs" visibility matches the same access rules as those pages.

---

### User Story 2 - Understand One Execution in Plain Language (Priority: P1)

As a non-technical user, I can select one log entry and view a readable execution detail panel that explains what happened without raw JSON.

**Why this priority**: Users must understand failures and outcomes without technical decoding.

**Independent Test**: Can be tested by selecting an entry and verifying summary, step outcomes, links to executed objects, and optional snapshot are shown in readable labels.

**Acceptance Scenarios**:

1. **Given** a log entry is selected, **When** details are loaded, **Then** the user sees a summary section describing the execution outcome.
2. **Given** detail data contains structured execution information, **When** it is displayed, **Then** the content is shown in human-readable labels and text without raw JSON text blocks.
3. **Given** the execution references related objects, **When** details are shown, **Then** the user sees links that navigate to those executed objects.
4. **Given** a snapshot exists for the selected execution, **When** details are shown, **Then** the snapshot is visible or accessible from the detail panel.
5. **Given** step outcomes are recorded, **When** details are shown, **Then** each step outcome is displayed with clear status and message text understandable by non-technical users.

---

### User Story 3 - Use the Page Across Device Sizes (Priority: P2)

As a user on desktop or phone, I can use an optimized layout variant for my screen size so the same information remains readable and actionable.

**Why this priority**: Logs are often reviewed in different environments and must remain usable on small screens.

**Independent Test**: Can be tested by viewing the page at desktop and phone widths and verifying each variant preserves core tasks (scan, filter, sort, open details).

**Acceptance Scenarios**:

1. **Given** a desktop-width display, **When** the page loads, **Then** the user sees a two-pane variant with list and details visible together.
2. **Given** a phone-width display, **When** the page loads, **Then** the user sees a compact variant where the list is primary and selected details open in a dedicated detail view.
3. **Given** a user switches between list and detail on phone, **When** returning to the list, **Then** previous sort and filter state is preserved.

### Edge Cases

- No execution logs are available when the tab is opened.
- Filters return zero matches after one or more filter values are entered.
- A selected log references objects that were removed or are unavailable.
- A selected log has no snapshot.
- A selected log has partial step outcome information.
- A timestamp is missing or invalid in source data.
- Multiple rapid sort/filter changes occur while data is being refreshed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide an "Execution Logs" top-level tab in the same navigation level as "Execution" and "Configuration".
- **FR-002**: The "Execution Logs" tab MUST appear after "Execution" and before "Configuration" in navigation order.
- **FR-003**: The default log list view MUST show these columns: timestamp (in user local time), execution object name, and status.
- **FR-004**: On initial load, the log list MUST be sorted by timestamp in descending order.
- **FR-005**: Users MUST be able to sort by any visible list column through column header interaction.
- **FR-006**: Users MUST be able to apply free-text filtering for each visible list column.
- **FR-007**: By default, all filters MUST be empty.
- **FR-008**: Users MUST see one list result that reflects the active sorting and all active column filters together.
- **FR-009**: The backend log query interface MUST accept sort and filter inputs in the same request and return results consistent with those combined inputs.
- **FR-010**: Selecting a log entry MUST open an execution detail view.
- **FR-011**: The execution detail view MUST include a readable summary of outcome and context.
- **FR-012**: The execution detail view MUST show execution details in non-technical, human-readable format and MUST NOT expose raw JSON payload text to end users.
- **FR-013**: The execution detail view MUST include links to related executed objects when references exist.
- **FR-014**: The execution detail view MUST show snapshot content or snapshot access when a snapshot is available.
- **FR-015**: The execution detail view MUST present step outcomes with clear per-step statuses and readable descriptions.
- **FR-016**: The page MUST provide two responsive design variants: a desktop-oriented variant and a phone-oriented variant.
- **FR-017**: The desktop-oriented variant MUST support efficient list scanning and detail inspection without additional navigation steps.
- **FR-018**: The phone-oriented variant MUST prioritize list readability and provide a simple transition to a focused detail view.
- **FR-019**: The system MUST preserve active sorting and filtering state while navigating between list and detail in the same session.
- **FR-020**: The system MUST provide clear empty-state messaging for no logs and no-filter-results cases.
- **FR-021**: The system MUST provide clear feedback when related object links or snapshot content are unavailable.
- **FR-022**: The system MUST meet local performance targets for first-open and filter/sort operations defined in Success Criteria.
- **FR-023**: The system MUST execute CI performance checks with thresholds that are relaxed compared with local thresholds while still enforcing a performance guardrail.
- **FR-024**: Access to the "Execution Logs" tab and its details MUST follow the same visibility and authorization rules currently applied to existing authoring/execution pages (including both navigation visibility and data access), with no additional restrictions introduced by this feature.
- **FR-025**: On first open, the log list MUST load the 50 most recent execution logs by default.
- **FR-026**: Free-text filtering MUST use case-insensitive contains matching for each filterable column.
- **FR-027**: Timestamp values in the log list MUST default to exact local timestamp display and MUST allow users to switch to relative-time display.
- **FR-028**: During rapid sort/filter changes, the system MUST apply a latest-request-wins behavior so stale responses are ignored and only the newest result is shown.

### Key Entities *(include if feature involves data)*

- **Execution Log Entry**: A single recorded execution item with timestamp, execution object identity, status, and references to richer details.
- **Execution Log Query**: User-selected sort column/direction and free-text filters by column used together to retrieve a result set.
- **Execution Detail**: Human-readable detail model for one selected log, including summary, related object links, optional snapshot reference, and step outcomes.
- **Step Outcome**: A named step result inside one execution with status and user-facing message.

### Assumptions

- The system already captures execution logs with enough detail to generate a user-friendly summary and step outcomes.
- Related objects are addressable so detail links can open relevant pages.
- Users need recency-first monitoring behavior by default.
- "Somewhat relaxed" CI thresholds are interpreted as a measurable but looser threshold than local validation.

### Dependencies

- Availability of execution log data and detail fields from existing execution tracking.
- Availability of related object references for link generation.
- Availability of optional snapshot metadata for entries that include snapshots.
- Existing navigation model supports insertion of one additional peer tab.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In local validation runs with a dataset of 1,000 execution logs, first opening the "Execution Logs" tab shows the default list in under 100 ms for at least 95% of runs.
- **SC-002**: In local validation runs with a dataset of 1,000 execution logs, combined filter/sort updates complete in under 300 ms for at least 95% of runs.
- **SC-003**: In CI validation runs with a dataset of 1,000 execution logs, first open completes in under 200 ms and combined filter/sort updates complete in under 450 ms for at least 95% of runs.
- **SC-004**: In usability validation with representative non-technical users, at least 90% can correctly identify execution status and step outcomes without assistance.
- **SC-005**: In usability validation on both desktop and phone layouts, at least 90% of users can find one specific execution and open its details within 30 seconds.
