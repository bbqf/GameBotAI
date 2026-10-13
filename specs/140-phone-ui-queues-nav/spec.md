# Feature Specification: Phone UI Queue Activity and Navigation

**Feature Branch**: `140-phone-ui-queues-nav`  
**Created**: 2026-10-13  
**Status**: Draft  
**Input**: User description: "I need some changes in the newly developed phone version of the UI. 1) remove the execution area. 2) Add the possibility to see what each queue is doing when it's running and what is scheduled for when, similar to the desktop version. 3) adjust the navigation, so that browser back arrow gets to the previous page within the application, not to the previous page before the application was first started."

## Clarifications

### Session 2026-10-13

- Q: How often does the queue activity refresh? → A: Every 5 seconds while the screen is visible and the connection is up. It pauses when the page is hidden. (Rationale: meets the 15-second goal in SC-002 with low load.)
- Q: Which entries does the schedule list show? → A: All entries of the queue. Enabled entries come first, in order of next run time. Disabled entries come last and are marked "disabled". (Rationale: the operator can see and enable an entry in one place.)
- Q: What happens on back after a direct link to a queue detail? → A: The first screen of the session is the queue detail. Browser back leaves the application as normal. The on-screen "back" control of the detail shows the queue list. (Rationale: matches FR-011 and avoids a fake history.)
- Q: Does the redirect from the old Execution link add a history entry? → A: No. The redirect replaces the current entry, so back does not return to the removed area. (Rationale: avoids a back loop.)
- Q: How do automated tests check "back leaves the application"? → A: By the first-screen marker and no in-app change (the test browser cannot leave the page). A manual real-phone check confirms it. (Rationale: accepted test limit.)
- Q: Does a layout change add a history entry? → A: No. It keeps the area and queue, and a resize test checks it. (Rationale: FR-013 must be testable.)
- Q: Does the phone redirect from Execution stay if the layout changes to desktop? → A: Yes. The redirect is permanent for the session, because it replaces the address. (Rationale: simple and predictable.)
- Q: Does the activity view use data that the service already gives? → A: Yes, if possible. The plan checks the existing queue detail and the schedule data used by the desktop monitor, and adds service data only if a data point is missing. (Rationale: keeps the change in the web UI.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what each queue does and what is scheduled (Priority: P1)

The operator opens a queue on the phone. The operator sees what the queue does now: its state, the sequence that runs, and the last result. The operator also sees what is scheduled: the entries of the queue and the time of the next run of each entry. The view is like the desktop queue monitor, but in a phone layout.

**Why this priority**: This is the main gap. Today the phone shows only queue status and start/stop. The operator cannot tell what a running queue does, or when the next task runs. The operator needs this to decide if a queue needs help.

**Independent Test**: On a phone-size screen, open a running queue. Check that the screen shows the current activity and the next run time of each scheduled entry. Check that no sideways scroll is needed.

**Acceptance Scenarios**:

1. **Given** a running queue, **When** the operator opens the queue on the phone, **Then** the screen shows what the queue does now (the running sequence or "waiting") and the last result.
2. **Given** a queue with scheduled entries, **When** the operator opens the queue, **Then** the screen shows each entry with its next scheduled time, in time order.
3. **Given** a stopped queue, **When** the operator opens the queue, **Then** the screen shows that the queue is stopped and shows its entries without false "running" data.
4. **Given** the queue detail is open and the queue state changes, **When** the service reports the change, **Then** the screen updates without a manual reload.
5. **Given** the queue list on the phone, **When** the operator looks at a queue card, **Then** the card shows a one-line summary of the activity (for example the running sequence name or the next run time).

---

### User Story 2 - Use the back arrow inside the application (Priority: P1)

The operator moves between the areas and the detail screens of the phone UI. The operator presses the browser back arrow (or the phone back gesture). The UI goes to the previous screen inside the application. It does not leave the application to the page that was open before the application started. At the first screen of the application, back leaves the application as normal.

**Why this priority**: Today back leaves the application at once. This breaks the app feel and loses the operator's place. It affects every phone screen.

**Independent Test**: On a phone-size screen, open the UI. Go to the Queues area, open one queue, then open the Notifications area. Press back three times. The UI shows the queue, then the Queues area, then the first screen. A fourth back leaves the application.

**Acceptance Scenarios**:

1. **Given** the operator opened a queue detail from the queue list, **When** the operator presses back, **Then** the queue list shows.
2. **Given** the operator moved from one area to another with the tab bar, **When** the operator presses back, **Then** the previous area shows.
3. **Given** the operator is on the first screen, **When** the operator presses back, **Then** the browser leaves the application as normal.
4. **Given** the operator pressed back to a screen, **When** the operator presses forward, **Then** the UI shows the screen that the operator left.
5. **Given** a link that points to an area or a queue, **When** the operator opens the link, **Then** the UI shows that screen directly.
6. **Given** the desktop layout, **When** the operator uses the application, **Then** the desktop navigation works as it did before.

---

### User Story 3 - Phone UI without the Execution area (Priority: P2)

The phone UI does not show the Execution area. The tab bar and any menu do not list it. The operator sees queue activity in the Queues area (User Story 1) instead.

**Why this priority**: The operator asked for it. The queue activity view replaces the need for this area on a phone. It makes the phone UI simpler.

**Independent Test**: On a phone-size screen, check the tab bar and menu. Execution is not there. Open the old Execution link. The UI shows a valid screen (not a blank or broken one).

**Acceptance Scenarios**:

1. **Given** a phone-size screen, **When** the operator looks at the navigation, **Then** Execution is not listed.
2. **Given** a phone-size screen and a saved link to the old Execution area, **When** the operator opens the link, **Then** the UI shows the Queues area.
3. **Given** a desktop-size screen, **When** the operator opens the UI, **Then** the Execution area is available as before.

---

### Edge Cases

- A queue has no entries: the screen shows an empty state with a clear text, not a blank list.
- An entry has no next time (for example it ran its last time, or the queue is stopped): the screen shows a clear text such as "not scheduled".
- The connection is lost: the existing "connection lost" banner shows, control actions are disabled, and the last data stays visible but is marked as old. After the connection returns, the UI reloads the real state.
- A link points to a queue that no longer exists: the UI shows a "queue not found" message with a way back to the queue list.
- The operator resizes the window or rotates the phone between layouts: the UI keeps the current area and queue, and the back history stays valid.
- The phone redirect from Execution to Queues changes the address for the rest of the session. If the operator then moves to the desktop layout, the desktop shows Queues (not Execution). The operator can open Execution from the desktop navigation.
- A very long sequence or entry name: the name is shortened with a way to see the full text.
- Many queues or entries: the screens stay usable and scroll only vertically.
- Times are shown in the local time of the operator, and a relative time (for example "in 25 min") is accepted as an addition.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The phone UI MUST NOT show the Execution area in the tab bar, in any menu, or in any list of areas.
- **FR-002**: On a phone-size screen, an old link to the Execution area MUST show the Queues area. It MUST replace the current history entry, so back does not return to the removed area. It MUST NOT show a blank or broken screen.
- **FR-003**: The desktop UI MUST keep the Execution area and all other desktop functions unchanged.
- **FR-004**: The phone queue detail MUST show what the queue does now: its state (running, stopped, or waiting), the sequence that runs now (if any), and the result of the last run.
- **FR-005**: The phone queue detail MUST show the scheduled entries of the queue. For each entry it MUST show the name, whether the entry is enabled, and the time of the next run (or a clear text when none is planned). The list MUST be in time order, with the next entry first.
- **FR-005a**: Disabled entries MUST show after the enabled entries and MUST be marked "disabled".
- **FR-006**: The phone queue list MUST show, on each queue card, a one-line summary of the activity: the running sequence name, or the next scheduled run.
- **FR-007**: The queue activity and schedule MUST refresh by itself every 5 seconds while the screen is open, visible, and the connection is up, without a manual reload. The refresh MUST stop when the screen closes or the page is hidden.
- **FR-008**: The queue activity view MUST need no sideways scroll on screens from 320 to 768 CSS pixels wide and MUST keep tap targets of at least 44 x 44 CSS pixels.
- **FR-009**: The queue activity view MUST show only read-only data and the controls that exist today on the phone (start, stop, enable or disable an entry). Schedule creation, reorder, and other editing stay desktop-only. The activity view MUST NOT show any control that edits a schedule.
- **FR-010**: Each screen of the phone UI (each area, and the detail of a queue) MUST have its own browser history entry. A move to a new screen MUST add an entry. It MUST NOT replace the current entry.
- **FR-011**: The browser back and forward actions MUST move between the screens of the application, in the order that the operator visited them. Back from the first screen of the application MUST leave the application as normal.
- **FR-011a**: When the first screen of a session is a queue detail (a direct link), browser back MUST leave the application as normal. The on-screen back control of the detail MUST show the queue list.
- **FR-012**: The address of the page MUST show the current area and, when open, the selected queue. A link to this address MUST open that screen directly.
- **FR-013**: The back history MUST stay correct when the layout changes between phone and desktop, and when the page reloads. A layout change MUST NOT add a history entry and MUST keep the current area and selected queue. An automated test MUST check this with a resize from phone to desktop and back.
- **FR-014**: A link to a queue that does not exist MUST show a "queue not found" message with a way back to the queue list.
- **FR-015**: The existing "connection lost" behavior MUST apply to the new views: the banner shows, control actions are disabled, and the UI reloads the real state after the connection returns. On a failed refresh, a queue card and the queue detail MUST keep the last data and mark it as old.
- **FR-016**: The phone support level of each area MUST stay recorded in one place. The record MUST reflect that Execution is not part of the phone UI.
- **FR-017**: The design-principles document for small screens and the area table in the documentation MUST be updated to match these changes.
- **FR-018**: Existing automated UI tests for the desktop MUST pass. Tests that assert the phone Execution area MUST be removed or changed to match this feature.

### Key Entities

- **Queue activity**: What a queue does now: state, running sequence, and last result.
- **Queue schedule entry**: One scheduled item of a queue, with its name, enabled state, and next run time.
- **Screen history entry**: One position in the browser history that matches one screen (area and optional selected queue).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From the first phone screen, the operator sees what a given queue does now and when its next entry runs in at most 2 taps (an automated test counts the taps).
- **SC-002**: The queue activity data on screen is no more than 15 seconds older than the real state while the connection is up.
- **SC-003**: In a test path of at least 3 screen changes, pressing back the same number of times returns to each previous screen in order, and the next back leaves the application. 100% of tested paths pass. In automated tests, "leaves the application" is checked by the first-screen marker and by the absence of an in-app screen change. A manual check on a real phone confirms it.
- **SC-004**: 100% of phone areas and queue screens have a working direct link.
- **SC-005**: The phone navigation shows 0 references to the Execution area.
- **SC-006**: On a 360 x 640 screen, the queue activity view shows no sideways page scroll, and at least 95% of its controls have a tap target of 44 x 44 CSS pixels or more.
- **SC-007**: 100% of existing desktop automated UI tests pass after the change.

## Assumptions

- "The execution area" means the Execution area of the phone UI (live run view). The Execution Logs area is already desktop-only and is not changed.
- "Similar to the desktop version" means the same facts as the desktop queue monitor (current activity and scheduled entries with next run times). It does not mean the same layout or the same editing functions.
- The service already gives the data that the desktop queue monitor uses. If a data point is missing, the plan decides how to provide it.
- The existing feature 139 rules stay: layout by screen size, desktop-only authoring, and "connection lost" behavior.
- Times use the local time zone of the operator's device.
- The refresh interval is 5 seconds (see FR-007).
- Tasks and plan name exact file paths after reading the code.
- The phone queue styles go in `src/components/phone/phone.css`. The navigation type test is `src/types/__tests__/navigation.test.ts`. The "back" state uses `idx` in `history.state` (no module flag).
- Tasks that edit the same file are not marked parallel [P]. They run in order.
- The `tab` address rule of the desktop stays as it is. Contract rules T-01 to T-04 in `contracts/phone-queues-nav-contract.md` state it exactly. Tasks name these rule IDs.
- The queue detail takes the selected queue id from `App.tsx` from the first task. No task first builds it with local state.
- SC-006 evidence is the CSS assertions plus the manual 360 x 640 check in the final verification task.
