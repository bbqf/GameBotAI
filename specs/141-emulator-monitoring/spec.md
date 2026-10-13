# Feature Specification: Emulator Monitoring

**Feature Branch**: `141-emulator-monitoring`  
**Created**: 2026-10-13  
**Status**: Implemented (real-emulator check pending)  
**Input**: User description: "As an operator, I want to have a possibility to monitor the connected emulators. I need a special area Monitoring in both UIs, where I'll be able to connect to the running sessions and get the automatically obtained - live screenshots. I also need a possibility to restart the whole emulator from that area. If there's no API exposes this function, expose this."

## Clarifications

### Session 2026-10-13

- Q: Which sessions does the list show? → A: All sessions that the service holds, each with a state. A session with no live emulator shows "no answer". (Rationale: the operator needs to find broken sessions, not only healthy ones.)
- Q: What does "restart the whole emulator" mean? → A: Stop the emulator process and start it again, then reconnect the session. It is not an in-game restart. (Rationale: matches the user words "whole emulator".)
- Q: What does a restart do to a queue that uses the session? → A: The queue is not stopped by the restart. The queue sees a failed or held cycle and recovers by its normal rules. The confirmation warns about this. (Rationale: keeps the change small and uses the existing liveness hold.)
- Q: What is the default screenshot interval and can the operator change it? → A: Default 2 seconds. The operator cannot change it in this feature. (Rationale: simple, meets SC-002.)
- Q: Who may restart an emulator? → A: Any user who can use the web UI. No new role. (Rationale: single-operator tool; confirmation prevents mistakes.)
- Q (loop 1): Which sessions does the list show, and what does the log hold? → A: All sessions, not only running ones (FR-002, FR-008). The log holds restart steps and failed screenshots only (FR-017). Restart always reconnects the session (FR-015). Default interval 2 s is in FR-005. (Rationale: analyze findings I1, C1, C2, A2.)
- Q (loop 2): Which analyze gaps are spec text? → A: FR-012 now says all sessions and FR-002 lists the state stopping. FR-023 says user story. SC-005 says that only the selected session is requested. A failed screenshot MUST be logged and a test MUST prove it (FR-017). (Rationale: analyze findings I1, I2, C1, C2.)
- Q: What is the allowed restart time before failure? → A: 2 minutes, in line with SC-004. (Rationale: one measurable limit.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a live picture of each running emulator (Priority: P1)

The operator opens a new area, "Monitoring". The area lists all sessions that the service holds (the connected emulators), each with a state. The operator selects a session. The area shows the screen of the emulator and keeps it up to date. The operator does not press a refresh control. The picture changes by itself at a regular interval.

**Why this priority**: This is the core need. Today the operator cannot see what an emulator shows without a tool outside the application. The operator needs this to find a stuck or wrong screen.

**Independent Test**: Start one session. Open the Monitoring area. Select the session. Check that a screenshot shows and that it changes within a few seconds after the emulator screen changes.

**Acceptance Scenarios**:

1. **Given** at least one session, **When** the operator opens the Monitoring area, **Then** the area lists each session with an identifying name and state.
2. **Given** the session list, **When** the operator selects a session, **Then** the area shows a screenshot of that emulator.
3. **Given** a selected session, **When** the emulator screen changes, **Then** the screenshot updates by itself, with no operator action.
4. **Given** a selected session, **When** the operator selects another session, **Then** the area shows the screenshot of the other session and stops the updates for the first session.
5. **Given** no session, **When** the operator opens the Monitoring area, **Then** the area shows a clear "no sessions" message.
6. **Given** the operator leaves the Monitoring area or hides the page, **When** the area is no longer visible, **Then** the screenshot updates stop.

---

### User Story 2 - Restart the whole emulator from Monitoring (Priority: P1)

From the Monitoring area, the operator restarts the whole emulator of a session. This is for an emulator that is frozen, does not answer, or shows a screen that the automation cannot leave. The operator selects the session and uses a "Restart emulator" control. The system asks the operator to confirm. After the confirmation, the system stops the emulator process and starts it again. The area shows the progress and the result.

**Why this priority**: When an emulator is stuck, the operator must now restart it by hand on the host machine. A restart from the application removes that need.

**Independent Test**: Select a running session. Use "Restart emulator" and confirm. Check that the area shows "restarting", and that the emulator returns and the live screenshot works again.

**Acceptance Scenarios**:

1. **Given** a selected session, **When** the operator uses "Restart emulator", **Then** the area asks for confirmation and names the emulator that it will restart.
2. **Given** the confirmation, **When** the operator confirms, **Then** the system restarts the whole emulator and the area shows a "restarting" state.
3. **Given** a restart in progress, **When** the emulator is ready again, **Then** the area shows the result as success and the live screenshot continues.
4. **Given** a restart in progress, **When** the emulator does not return in the allowed time, **Then** the area shows a clear failure message with the reason.
5. **Given** a restart in progress for a session, **When** the operator tries to start a second restart for the same session, **Then** the system refuses the second request and the area explains why.
6. **Given** the operator cancels at the confirmation, **When** the dialog closes, **Then** nothing is restarted.
7. **Given** a queue that uses the session, **When** the emulator restart runs, **Then** the operator is warned before the confirmation that the queue work on this emulator will be interrupted.

---

### User Story 3 - Use Monitoring on the desktop and on the phone (Priority: P2)

The operator uses the Monitoring area in the desktop layout and in the phone layout of the web UI. Both layouts offer the same functions: the session list, the live screenshot, and the restart. The phone layout uses a single column and shows the screenshot at full width.

**Why this priority**: The operator checks emulators from a phone when away from the desk. The core functions work on desktop first, so the phone layout comes second.

**Independent Test**: Open the Monitoring area at a desktop width and at a phone width. Do the User Story 1 and 2 checks in each layout. Check that the phone layout needs no sideways scroll.

**Acceptance Scenarios**:

1. **Given** the phone layout, **When** the operator opens the application, **Then** the navigation includes a "Monitoring" area.
2. **Given** the desktop layout, **When** the operator opens the application, **Then** the navigation includes a "Monitoring" area.
3. **Given** the phone layout, **When** the operator selects a session, **Then** the screenshot shows at the full screen width, with no sideways scroll, and the restart control is easy to reach with a thumb.
4. **Given** the operator changes the layout size while the area is open, **When** the layout changes, **Then** the selected session stays selected.

---

### Edge Cases

- A session exists but the emulator does not answer: the area shows the last good screenshot with its age and a "no live picture" marker, not an empty picture.
- The first screenshot is slow: the area shows a "loading" state.
- A session ends while the operator watches it: the area shows "session ended" and removes it from the list.
- The connection to the service drops: the area shows an offline marker and resumes the updates when the connection returns.
- Many sessions are running: the operator watches only the selected session, so the load stays the same for any number of sessions.
- Two operators restart the same emulator at the same time: only one restart runs, and the other request is refused.
- A request uses the word "monitoring" as a session identifier: the list route must still return the list. The existing session routes must still treat a real identifier as an identifier (FR-021).
- A restart is requested for a session that has no emulator the system can control: the system refuses with a clear message and does nothing.
- The operator started the emulator outside the application: the restart works only if the system can identify and control that emulator. If not, the area states that the restart is not available for this session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The web UI MUST include a "Monitoring" area in the desktop layout and in the phone layout.
- **FR-002**: The Monitoring area MUST list all sessions that the service holds with an identifying name and a short state (for example, live, no answer, restarting, stopping).
- **FR-003**: The operator MUST be able to select one session in the Monitoring area to watch it.
- **FR-004**: For the selected session, the area MUST show a screenshot of the emulator screen and MUST refresh it automatically at a regular interval, with no operator action.
- **FR-005**: The automatic refresh interval MUST be 2 seconds by default (the operator cannot change it), and the refresh MUST NOT start a new request until the previous one has ended.
- **FR-006**: The automatic refresh MUST pause when the Monitoring area is not visible or the page is hidden, and MUST resume when the area is visible again.
- **FR-007**: The area MUST show the age of the current screenshot, and MUST mark the picture as not live when the emulator does not answer.
- **FR-008**: The area MUST show a clear message when there is no session.
- **FR-009**: The operator MUST be able to restart the whole emulator of a selected session from the Monitoring area.
- **FR-010**: The restart control MUST ask for confirmation before it acts, MUST name the emulator, and MUST warn when a running queue uses the session.
- **FR-011**: The system MUST expose the emulator restart as a public service function (API), if none exists today. The function MUST accept a session identifier and MUST report progress and result (restarting, success, failure with a reason).
- **FR-012**: The system MUST expose a way to get the list of all sessions and a current screenshot of a session for the Monitoring area, if the existing service functions do not give them.
- **FR-013**: The system MUST allow only one restart at a time for the same emulator and MUST refuse a second request with a clear message.
- **FR-014**: The system MUST refuse a restart for a session whose emulator it cannot control, with a clear message, and MUST NOT change the emulator.
- **FR-015**: After a restart, the system MUST make the session usable again. The live screenshot and the other session functions MUST work with no manual step. The restart itself MUST reconnect the session to the new emulator device. This MUST NOT depend on a manual check.
- **FR-016**: The area MUST show a failure message with the reason when a restart does not finish within 2 minutes.
- **FR-017**: The service MUST write each step of a restart to the service log. The steps are request, refusal, start, success, and failure. Each entry MUST show the session identifier and the result. The service MUST also log a failed screenshot request in the same way. The service MUST NOT log a successful screenshot request. This keeps the log small.
- **FR-018**: The new service functions MUST appear in the public API description, together with their documentation.
- **FR-019**: The feature MUST have the phone support level "full" and MUST follow docs/mobile-ui-principles.md (single column, no sideways scroll, touch targets of usable size).
- **FR-020**: The area MUST keep the selected session when the layout changes between desktop and phone.
- **FR-021**: The route that lists sessions for monitoring MUST NOT be read as a session identifier. A request for the list MUST return the list, and MUST NOT return "session not found". An automated test MUST prove this, and MUST also prove that a real session identifier still reaches the existing session routes.
- **FR-022**: The restart readiness check interval MUST be a service setting named `RestartPollIntervalMs`. It is separate from the fixed 2 second screenshot interval in FR-005.
- **FR-023**: Each user story (1, 2, and 3) MUST be complete and proven by automated tests alone. The restart reconnect (FR-015) MUST be proven by an automated test that uses a fake emulator control.
- **FR-024**: An automated timer test MUST prove the 2 second refresh interval and the rule that no request starts before the previous one ends (FR-005).
- **FR-025**: The manual checks on a real emulator (SC-001, SC-002, SC-004, SC-006) MUST be ONE final verification step. This step is separate from the code stories. No code story depends on a manual check.
- **FR-026**: The change MUST update `docs/architecture.md` in the same pull request (constitution Principle V). The update MUST cover the new API routes, the monitoring capability, and the emulator restart capability. The update MUST set a new "Last reviewed" date.
- **FR-027**: When the work ends, the `Status` line of this spec MUST show the final state. The change MUST also update `specs/STATUS.md` with a row for this feature. Use the status words in `specs/STATUS.md`. Spec 140 is an example.
- **FR-028**: The change MUST add an entry to `CHANGELOG.md` under `[Unreleased]` > `Added`. The entry MUST describe the Monitoring area, the new routes, and the new setting `RestartPollIntervalMs`. The entry MUST name this feature (141-emulator-monitoring). Follow the style of the existing entries.

### Key Entities *(include if feature involves data)*

- **Session**: A connection to one running emulator. Has an identifier, a name, a state, and a link to the emulator that it controls.
- **Screenshot**: A picture of the emulator screen at one time. Has the session it comes from and the time it was taken.
- **Emulator restart**: One request to stop and start the whole emulator of a session. Has a session, a start time, a state (restarting, success, failure), and a reason when it fails.

## Assumptions

- "Both UIs" means the desktop layout and the phone layout of the existing web UI.
- "Connect to running sessions" means to select an existing running session. The Monitoring area does not create new sessions.
- Access control is the same as for the rest of the web UI. This feature adds no new login or role.
- The restart stops and starts the emulator process. It does not change game accounts or game data.
- Screenshot refresh uses a poll with a default interval of about 2 seconds. A fixed video stream is out of scope.
- The operator needs the screen picture only. Touch input from the Monitoring area is out of scope.
- Test layout: contract tests for the new routes go in separate test files for each user story. Tasks that edit the same file MUST NOT be marked parallel ([P]). This applies to `SessionMonitoringContractTests.cs` and to the navigation test files.
- `LdInstanceMapper` belongs in the Foundational phase (task T2). Other tasks use it, so it is not part of one user story.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The operator sees the screen of a selected running emulator within 3 seconds of the selection.
- **SC-002**: A change on the emulator screen shows in the area within 5 seconds, with no operator action.
- **SC-003**: The operator restarts a stuck emulator in 3 steps or fewer (select the session, use restart, confirm), without access to the host machine.
- **SC-004**: After a restart, the emulator is usable again and the live picture returns within 2 minutes in at least 90% of restarts under normal conditions.
- **SC-005**: Watching one session causes no more load than one screenshot request at a time, for any number of sessions. Only the selected session is requested.
- **SC-006**: The Monitoring area works at a phone width of 360 px with no sideways scroll.
- **SC-007**: In no test does a restart request for one session change another session's emulator.

**Verification note**: SC-001, SC-002, SC-004, and SC-006 have no full automated check. The automated timer test (FR-024) covers the 2 second interval and the no-overlap rule. The final manual step on a real emulator (FR-025) confirms the four criteria.
