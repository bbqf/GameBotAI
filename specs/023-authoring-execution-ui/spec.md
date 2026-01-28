# Feature Specification: Authoring & Execution UI Visual Polish

**Feature Branch**: `023-authoring-execution-ui`  
**Created**: 2026-01-28  
**Status**: Draft  
**Input**: User description: "Visual bugfixes and improvements of authoring and Execution UI"

## Clarifications

### Session 2026-01-28

- Q: What happens if auto-stop fails when starting a new session for the same game/emulator? → A: Remove the prior session from the running list without retrying; assume it already aborted in the background.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Detection settings save reliably (Priority: P1)

Authors need the Detection section of the command create/edit UI to persist target configuration and parameters so that saved commands run with the intended detection behavior.

**Why this priority**: Current detection settings are silently discarded, causing commands to execute with missing or default detection targets, breaking automation reliability.

**Independent Test**: Create or edit a command, set detection target and parameters, save, reopen, and confirm the exact values persist without manual re-entry.

**Acceptance Scenarios**:

1. **Given** an author creates a command and selects a detection target with parameters, **When** they save the command, **Then** reopening the command shows the detection target and parameters exactly as entered.
2. **Given** an author edits an existing command that already has detection configured, **When** they modify detection parameters and save, **Then** subsequent retrieval (UI reload or API read) returns the updated detection configuration with no loss of data.
3. **Given** an author attempts to save a command without a required detection target, **When** they submit the form, **Then** the UI blocks save, highlights the missing fields, and does not lose previously entered detection values.

---

### User Story 2 - Command execution reuses cached session (Priority: P2)

Operators should be able to execute a command using the session started earlier without re-entering a session identifier when a cached session is available.

**Why this priority**: Forcing manual session IDs increases friction and errors; reusing the active session reduces failed executions and speeds troubleshooting.

**Independent Test**: Start a session to obtain a session ID, then execute a command without providing one; verify the system auto-uses the cached session. Repeat with stale/missing cache to ensure clear guidance.

**Acceptance Scenarios**:

1. **Given** an operator has started a session and a session ID is cached, **When** they execute a command without supplying a session ID, **Then** the execution uses the cached session ID and succeeds without prompting for one.
2. **Given** no session is cached, **When** an operator tries to execute a command without providing a session ID, **Then** the UI blocks execution and clearly prompts to start a session or enter an ID, without losing entered command parameters.
3. **Given** a cached session ID exists but is stale/closed, **When** an operator executes a command, **Then** the UI surfaces a clear error, does not run the command, and preserves command inputs while prompting to start a new session.

---

### User Story 3 - Session banner shows cached session with stop (Priority: P3)

Operators should see the current cached session ID once at the top of the Execution UI with a clear button to stop/end the session.

**Why this priority**: Visible session context reduces confusion about which session is active and provides a quick escape hatch to end stale sessions.

**Independent Test**: Start a session, navigate to the Execution UI, confirm the session ID appears once in a banner/header with a stop action; ensure stop ends the session and clears the banner.

**Acceptance Scenarios**:

1. **Given** a cached session ID exists, **When** the operator opens the Execution UI, **Then** the session ID is shown once in a header/banner with a single adjacent stop/end button.
2. **Given** the operator clicks the stop/end button, **When** the action completes, **Then** the session ends, the banner disappears, and subsequent executions require starting a new session or providing an ID.
3. **Given** no cached session exists, **When** the operator views the Execution UI, **Then** no session banner is shown and the UI prompts to start a session before execution.

---

### User Story 4 - Running sessions list with stop controls (Priority: P4)

Operators need to see all currently running sessions at the top of the Execution UI, each with a stop control; starting a new session for the same game and emulator must automatically stop the previous one.

**Why this priority**: Prevents conflicting sessions for the same game/emulator and gives operators quick control to stop active sessions before executing commands.

**Independent Test**: Start sessions for different game/emulator pairs, verify they appear in the running list with stop buttons; start a new session for an existing game/emulator and confirm the prior session stops and is replaced.

**Acceptance Scenarios**:

1. **Given** multiple sessions across different game/emulator pairs are running, **When** the operator opens the Execution UI, **Then** each active session appears in the running-sessions list with a visible stop control.
2. **Given** an operator starts a new session for a game/emulator that already has a running session, **When** the new session starts, **Then** the previous session for that game/emulator is stopped automatically and only the new session remains listed.
3. **Given** an operator clicks stop on a running session entry, **When** the action completes, **Then** that session is removed from the list and no longer usable for execution.

---

### User Story 5 - Author edits automations without visual defects (Priority: P5)

Authors need to review and edit automation definitions in the authoring UI without controls overlapping, clipping, or hiding key actions.

**Why this priority**: Visual defects in authoring flows block configuration work and create rework; resolving them restores day-to-day productivity.

**Independent Test**: Load representative authoring pages with long names and multiple form sections at desktop widths (1280-1920px); verify all labels, inputs, and primary actions are visible without horizontal scrolling or overlap.

**Acceptance Scenarios**:

1. **Given** an author opens any authoring form containing long command/trigger names (50-60 characters), **When** the page renders at 1280px width, **Then** labels, inputs, and save/cancel actions remain fully visible without overlapping or off-screen clipping.
2. **Given** an author resizes the browser from 1920px down to 1280px, **When** sticky headers or side panels are present, **Then** they do not cover form actions and vertical scrolling remains possible without horizontal scrollbars.

---

### User Story 6 - Operator monitors execution states clearly (Priority: P6)

Operators need to start, stop, and observe automation execution with clear visual cues for state and progress.

**Why this priority**: Ambiguous status styling causes misreads of running/failed tasks, leading to delayed interventions.

**Independent Test**: Open the execution UI with mixed run states; verify color/status chips, progress indicators, and action buttons are aligned, legible, and consistently sized so state is recognizable at a glance.

**Acceptance Scenarios**:

1. **Given** a list of runs in pending, running, succeeded, and failed states, **When** the operator views the execution screen, **Then** each state has a distinct label and color with sufficient contrast and none of the chips or icons overlap adjacent text.
2. **Given** a run is selected for details, **When** loading states occur, **Then** the panel shows a consistent skeleton/placeholder without layout jump or text clipping, and action buttons remain in stable positions.

---

### User Story 7 - Consistent look-and-feel across surfaces (Priority: P7)

Users expect authoring and execution areas to share consistent spacing, typography, and button hierarchy to reduce cognitive load.

**Why this priority**: Aligning visual patterns lowers training cost and prevents mistaken clicks when switching contexts.

**Independent Test**: Compare primary/secondary buttons, headings, form spacing, and table row density across authoring and execution screens to ensure they use the same scale and spacing tokens without visual drift.

**Acceptance Scenarios**:

1. **Given** any page in authoring or execution, **When** primary and secondary actions are displayed, **Then** they share consistent height, padding, and typography so buttons align in a row without jitter or truncation.
2. **Given** content with nested cards or panels, **When** viewed side by side across modules, **Then** heading sizes, section spacing, and divider styles match and no component appears visually misaligned relative to its siblings.

---

### Edge Cases

- Long names or descriptions (60+ characters) in tables, chips, or buttons must wrap or truncate gracefully without causing horizontal scroll.
- Browser zoom or OS text scaling at 125%-150% must not hide action buttons, tooltips, or input labels.
- Missing or slow-loading assets (icons, screenshots) must leave placeholders that keep layouts stable.
- Error and empty states should retain the same spacing and alignment as loaded content to avoid jumpy layouts.
- Mixed-theme elements from older styles must not clash (no mismatched backgrounds or unreadable text due to contrast).
- Detection section must preserve entered values when validation errors occur elsewhere in the form (no silent clearing of target or parameters).
- Cached session ID usage must fail safely when the cache is empty or stale, preserving command inputs and prompting to start a session.
- Session banner must not duplicate or repeat the cached session ID; it should render once with a single stop control.
- Running sessions list must not show duplicate entries for the same game/emulator; a new session for that pair replaces the old one after auto-stop.
- If auto-stop fails while replacing a session for the same game/emulator, remove the prior session entry from the running list and do not retry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Execution MUST default to the cached session ID from the latest "Start a session" when executing a command without an explicitly provided session ID.
- **FR-002**: When no cached session ID exists, the UI MUST block execution and prompt to start a session or supply an ID, while preserving all entered command parameters.
- **FR-003**: When a cached session ID is stale/invalid, execution MUST fail safely with a clear message and retain command inputs so the user can retry after starting a new session.
- **FR-004**: The Execution UI MUST display the cached session ID once in a header/banner with a single adjacent stop/end control when a session is cached.
- **FR-005**: The stop/end control MUST terminate the cached session, clear the banner, and require a new session before further executions proceed.
- **FR-006**: The Execution UI MUST list all running sessions with their game/emulator pair and provide a stop control for each.
- **FR-007**: Starting a new session for a game/emulator pair MUST automatically stop any existing session for that pair before listing the new session; if the stop fails, remove the prior session from the running list without retrying and proceed with the new session.
- **FR-008**: Detection settings entered in the command create/edit UI MUST persist on save and be reloaded identically on reopen or subsequent API reads.
- **FR-009**: Detection target selection MUST be required; when missing, inline validation MUST block save while preserving all previously entered detection fields.
- **FR-010**: Editing an existing command MUST pre-populate the Detection section with stored values so authors can review/adjust without data loss.
- **FR-011**: Authoring forms (including modal dialogs) MUST render without overlapping or clipped inputs/actions at common desktop widths (1280-1920px) using datasets with long command/trigger names up to 60 characters.
- **FR-012**: Form layouts MUST avoid horizontal scrolling; vertical scrolling MUST preserve fixed headers/footers without obscuring primary or destructive actions.
- **FR-013**: Execution UI lists and detail panels MUST present run states (pending, running, succeeded, failed) with distinct labels and contrast that remains legible when printed or viewed on standard monitors.
- **FR-014**: Loading, empty, and error placeholders MUST share consistent spacing, typography, and iconography so content swaps do not shift layout positions or misalign buttons.
- **FR-015**: Primary and secondary buttons across authoring and execution areas MUST share consistent height, padding, and typography so aligned button rows do not jitter between screens.
- **FR-016**: Text and control rendering MUST remain usable at 125%-150% browser zoom or OS text scaling without cutting off labels, tooltips, or input affordances.
- **FR-017**: Tables, cards, and chips MUST gracefully wrap or ellipsize long strings while keeping row/column alignment intact and preventing horizontal overflow.

### Key Entities *(include if feature involves data)*

- **Authoring workspace content**: Command/trigger definitions, labels, helper text, and action buttons displayed in forms and modals.
- **Execution run artifacts**: Run rows, status chips, progress indicators, and action buttons surfaced in execution dashboards and detail panels.
- **Shared visual patterns**: Typography scale, spacing tokens, button hierarchy, and placeholder treatments applied across authoring and execution surfaces.
- **Detection configuration**: Detection target selection and parameter values persisted with commands and pre-populated on edit.
- **Session cache reference**: Cached session ID from "Start a session" available to the Execute flow for default use and validation.
- **Session banner**: Single rendered header/banner showing the cached session ID with an adjacent stop/end control.
- **Running sessions list**: Collection of active sessions by game/emulator displayed with stop controls and enforcing single active session per game/emulator.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 10 consecutive create/edit/save/reopen cycles using varied detection targets and parameters, 100% of detection configurations persist exactly as entered with no silent resets.
- **SC-002**: In 10 consecutive command executions after starting a session, 100% auto-use the cached session ID when none is provided explicitly; zero executions proceed with a missing or stale session.
- **SC-003**: In 10 consecutive visits to the Execution UI with an active session, the session banner appears once with the correct session ID and a working stop control that clears the banner after stopping.
- **SC-004**: In 10 consecutive starts of a new session for a game/emulator that already has a running session, the prior session stops automatically and only the new session remains listed.
- **SC-005**: In QA walkthroughs at 1280x720 to 1920x1080, zero overlapping/clipped UI elements are observed across authoring forms using sample data with 60-character names and multi-section layouts.
- **SC-006**: 95% of observed users correctly identify run state (pending/running/succeeded/failed) within $2\text{ seconds}$ of viewing the execution list without hovering for text.
- **SC-007**: At 125%-150% zoom or OS scaling, all primary actions on authoring and execution screens remain reachable without horizontal scrollbars, and no critical control is hidden off-screen.
- **SC-008**: Release sign-off reports zero blocker/critical visual defects, and UI-related support tickets for authoring/execution decrease by at least 40% in the first two weeks post-release.

## Assumptions

- Target devices are desktop browsers (Chrome/Edge) at 1280px width or greater; mobile views are out of scope.
- Visual refinements do not introduce new workflows; functionality remains unchanged beyond fixing layout/readability issues.
- Existing color palette and typography tokens are available to standardize states without adding new design systems.
