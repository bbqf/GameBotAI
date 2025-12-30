# Feature Specification: Connect to game action

**Feature Branch**: `020-connect-game-action`  
**Created**: 2025-12-30  
**Status**: Draft  
**Input**: User description: "I need a new Action Type "Connect to game". It has to reference a game, like the other actions and apart from that have one parameter: adbSerial, as needed for POST /api/sessions. This has to be implemented both in backend and frontend. On the frontend side, when creating or editing an action, the game has to be selectable from the list of games only, however for adbSerial choices have to presented from the /api/adb/devices but the field has to be modifiable as well. 
When the Action is executed over the command execution capabilities, is has to call POST /api/sessions in a syncronous call (timeout 30s) and if successful, the session ID has to be returned back to the UI and stored in local storage for future use with other calls to /api/command/... Incidentally, this has to be the first call in a row, but as this one returns session ID, the parameter sessionId to /api/commands/{id}/force-execute and /api/commands/{id}/evaluate-and-execute has to be made optional (but semantically required for all but the first call)"

## Clarifications

### Session 2025-12-30

- Q: Should stored sessionIds be reused across different games/devices or scoped? → A: Scope stored sessionIds to the combination of game + adbSerial.

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

### User Story 1 - Author configures Connect to game action (Priority: P1)

An author creates or edits an action of type "Connect to game", selects an existing game from the catalog, and supplies an adbSerial (from suggested devices or manual entry) so the action can be saved for later use.

**Why this priority**: Without the new action type and required fields, no one can prepare the connection step needed ahead of command execution.

**Independent Test**: Create a new action with this type, choose any available game, pick or type an adbSerial, save, and re-open to confirm selections persist.

**Acceptance Scenarios**:

1. **Given** the author is on create action, **When** they choose "Connect to game", select a listed game, and choose an adbSerial suggestion, **Then** the action saves with both values preserved.
2. **Given** the device suggestions are empty, **When** the author types an adbSerial manually, **Then** the action still validates and saves with that value.

---

### User Story 2 - Execute action to open a session (Priority: P1)

An operator runs the "Connect to game" action; the system requests a session using the selected game and adbSerial, waits for completion (up to 30 seconds), and receives a sessionId that is surfaced to the UI and stored for later calls.

**Why this priority**: Establishing the session is a prerequisite for any subsequent command execution flow; failure blocks downstream automation.

**Independent Test**: Execute the action alone via command execution, verify a sessionId is returned within the timeout, surfaced in the UI, and persisted client-side.

**Acceptance Scenarios**:

1. **Given** a valid game and adbSerial, **When** the action executes, **Then** POST /api/sessions completes within 30s and returns a sessionId visible to the user and stored for reuse.
2. **Given** the session request fails or times out, **When** the action executes, **Then** the user sees a clear failure status and no subsequent commands run.

---

### User Story 3 - Use stored session across commands (Priority: P2)

After creating a session with the "Connect to game" action, an operator executes additional commands in sequence; the system treats sessionId as optional in subsequent command calls, automatically supplying the stored sessionId when it is not provided.

**Why this priority**: Enables smooth multi-step command runs without forcing users to retype or re-fetch session identifiers.

**Independent Test**: Run a sequence where the connect action executes first, then trigger force-execute and evaluate-and-execute calls without specifying sessionId; confirm they succeed by using the stored value.

**Acceptance Scenarios**:

1. **Given** a stored sessionId from the latest connect action, **When** a force-execute call omits sessionId, **Then** the call uses the stored value and runs successfully.
2. **Given** no stored sessionId is present, **When** a non-connect command attempts to run without sessionId, **Then** the user is prompted to establish a session first and the call does not proceed.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- No games exist: authoring should block save and explain that a game must be added before creating this action.
- Device list empty or fetch fails: adbSerial remains editable and can be saved without suggestions.
- Session request exceeds 30 seconds or errors: surface failure, do not cache any sessionId, and halt subsequent commands in the run.
- Stored sessionId is outdated or superseded by a new connect action: the most recent successful connect replaces the stored session and is used for following commands.
- Sequence starts without running the connect action: subsequent commands that rely on sessionId must refuse to run and direct the user to connect first.
- Stored sessionId from a different game or adbSerial must not be reused; only a matching game + adbSerial can auto-fill sessionId.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: Provide a new action type "Connect to game" that requires selecting an existing game from the game catalog; the action cannot be saved without a valid game reference.
- **FR-002**: Expose an adbSerial field for this action that loads selectable device values from /api/adb/devices while also allowing manual entry; the value is required to save the action.
- **FR-003**: When creating or editing the action, persist the chosen game and adbSerial so they re-display accurately on re-open or edit flows.
- **FR-004**: When the action executes via command execution, submit a synchronous POST /api/sessions request using the action’s game and adbSerial, enforcing a 30-second timeout.
- **FR-005**: On successful session creation, surface the returned sessionId to the UI and persist it client-side for reuse in later command calls until replaced by a newer successful connect action with the same game and adbSerial.
- **FR-006**: Treat sessionId as optional inputs for /api/commands/{id}/force-execute and /api/commands/{id}/evaluate-and-execute; when omitted, the system must supply the latest stored sessionId that matches the command’s target game and adbSerial and reject the call if no matching sessionId is available.
- **FR-007**: If the connect action fails or times out, clearly report the failure, avoid persisting any sessionId, and prevent subsequent commands in the same run from executing without a valid session.
- **FR-008**: Refresh device suggestions for adbSerial at action authoring time so the list reflects currently connected devices without blocking manual entry.

### Key Entities *(include if feature involves data)*

- **Connect to game action**: An action configuration that holds a required game reference and a required adbSerial value used to start sessions.
- **Session context**: A sessionId returned from POST /api/sessions, exposed to the user and stored client-side for subsequent command execution calls.
- **Game**: A predefined game record selectable from the existing games list and referenced by the connect action.

## Assumptions

- A game catalog already exists and exposes the same games list used by other actions.
- /api/adb/devices responds with the currently connected devices at authoring time.
- A client-side storage mechanism is available to persist the latest sessionId across command invocations in the same browser environment.
- Command sequences can be ordered so that the connect action runs before any command requiring a session.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: 95% of authors can create or edit a "Connect to game" action with required game and adbSerial on the first attempt without validation errors unrelated to their input.
- **SC-002**: 95% of successful connect action executions return a sessionId and present it to the user within 30 seconds.
- **SC-003**: 0 unexpected missing-session errors occur in subsequent command executions when a valid sessionId was stored from the latest connect action.
- **SC-004**: Device suggestions for adbSerial load within 2 seconds when available, and manual entry succeeds even when suggestions are unavailable.
