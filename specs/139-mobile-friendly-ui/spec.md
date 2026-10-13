# Feature Specification: Mobile-Friendly UI

**Feature Branch**: `139-mobile-friendly-ui`  
**Created**: 2026-10-13  
**Status**: Implemented  
**Input**: User description: "I want to access the UI from my phone. Now it is very poorly optimized for the small screen. Optimized web page or app are the options. Compare approaches (including the effort to implement new vs. optimize based on the current codebase), bring the arguments, and I will decide. Afterwards document the principles to follow for future features, so that these are accessible on the small screen. Not all features make sense, for example Execution logs will not be usable. If UI rework is needed in general, do it. I want an 'app look and feel' - little scrolling, few controls, etc."

## Clarifications

### Session 2026-10-13

- Q: What phone level do the Authoring pages (Commands, Games, Sequences, Images, Backup and Restore) get? → A: All Authoring pages are desktop-only on a phone.
- Q: How much queue editing does the phone allow? → A: Status and control, plus small changes to settings (for example, enable or disable an entry). Reorder and the creation of schedules are desktop-only.
- Q: How does the UI choose the phone layout? → A: By screen size, with no manual switch. A screen 768 px wide or less gets the phone layout. A wider screen gets the desktop layout.
- Q: What layout does a phone in landscape get? → A: The phone layout also applies to short screens (about 500 px high or less), so a landscape phone keeps the phone layout.
- Q: What does the UI do when the connection is lost? → A: It shows a "connection lost" banner and disables control actions. It never repeats an action by itself. After reconnect, it reloads the real state.
- Analysis fixes (same day): the phone limit is 768 px wide, so small tablets get the phone layout. The tab bar uses the name "Notifications". The guide only warns that the token travels in clear text over HTTP (no UI warning). The load-time goal stays and has a task that measures it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Monitor and control queues from a phone (Priority: P1)

The operator holds a phone and opens the GameBot UI. The operator sees which queues run, which sequence runs now, and any problem. The operator can start or stop a queue with one tap. The screen feels like a phone app: one clear task per screen, large tap targets, no sideways scroll, and little vertical scroll.

**Why this priority**: This is the main reason to use a phone. The operator wants to check the bots and react while away from the PC.

**Independent Test**: Open the UI on a phone-size screen (360 x 640 CSS pixels). Find the status of each queue, then start and stop one queue. The task needs no zoom and no sideways scroll.

**Acceptance Scenarios**:

1. **Given** a phone-size screen, **When** the operator opens the UI, **Then** the first screen shows queue status and the main navigation. It needs no sideways scroll and no zoom.
2. **Given** a running queue, **When** the operator taps its stop control, **Then** the queue stops and the screen shows the new state.
3. **Given** a phone-size screen, **When** the operator moves between the main areas, **Then** the operator reaches the navigation with one thumb. The current area is clear.

---

### User Story 2 - Use the other monitor-and-adjust areas on a phone (Priority: P2)

The operator opens Execution, Notifications, Configuration (connection and token), and Update on a phone. Each area is usable on a small screen. Lists become tap-to-open cards. Forms show one group of fields at a time. Secondary controls are behind a single menu.

**Why this priority**: These areas support the P1 flow. They are simple enough to work well on a phone.

**Independent Test**: On a phone-size screen, do one typical task in each area. Examples: see a live run, change a notification level, enter the access token, check for an update. No task needs sideways scroll or zoom.

**Acceptance Scenarios**:

1. **Given** a phone-size screen, **When** the operator opens a list, **Then** each item shows only its key facts. A tap opens the detail view. The detail view has a clear way back.
2. **Given** a form on a phone-size screen, **When** the operator edits a value, **Then** the field, its label, and the primary action are visible together. The on-screen keyboard does not hide the field.
3. **Given** a table in the desktop view, **When** the operator opens the same data on a phone, **Then** the data shows as cards or rows. There is no sideways scroll.

---

### User Story 3 - Clear handling of areas that do not fit a phone (Priority: P2)

Some areas do not make sense on a small screen. These are Execution logs and the whole Authoring area (Commands, Games, Sequences, Images, and Backup and Restore). On a phone, the UI shows these areas as "best on a larger screen". It does not show a broken layout. Where a read-only or reduced view is useful and cheap, the UI provides it.

**Why this priority**: The operator accepted that not all features fit. A clear message is better than a broken screen.

**Independent Test**: On a phone-size screen, open each desktop-only area. The UI shows a clear notice and a way back. No area shows a broken or unusable layout.

**Acceptance Scenarios**:

1. **Given** a phone-size screen, **When** the operator opens a desktop-only area, **Then** the UI shows a short notice that names the area. A control goes back.
2. **Given** the same area on a desktop-size screen, **When** the operator opens it, **Then** the full feature works as before.
3. **Given** the list of areas, **When** a new area is added, **Then** its phone support level (full, reduced, or desktop-only) is recorded in one place.

---

### User Story 4 - Reach the UI from a phone safely (Priority: P2)

The operator follows a short guide to open the UI from a phone on the home network or through a private remote link. The guide explains how to expose the service, how to set the access token, and how to add the UI to the phone home screen. The service does not accept phone connections by default.

**Why this priority**: The UI is only useful on a phone if the phone can reach it. The service is local-only by default, and this is a security boundary that the guide must keep.

**Independent Test**: A person who has not seen the guide follows it and opens the UI on a phone in under 10 minutes. The access token is required for the connection.

**Acceptance Scenarios**:

1. **Given** default settings, **When** a phone tries to connect from the network, **Then** the connection is refused.
2. **Given** the guide steps are done, **When** the phone opens the address, **Then** the UI loads and asks for the access token.
3. **Given** an open service with no token, **When** the operator reads the guide, **Then** the guide warns that anyone on the network has access.
4. **Given** the guide, **When** the operator reads the security section, **Then** it states that the token travels in clear text over plain HTTP. It advises a private VPN link outside the home network.

---

### User Story 5 - Follow phone-first design principles for new features (Priority: P3)

A developer who adds a new UI feature reads one short document of mobile design principles. The document states the rules and how to classify the feature. Example rules: tap target size, no sideways scroll, one primary action for each screen, cards instead of tables. The classes are full, reduced, and desktop-only. The review checklist for UI changes includes these rules.

**Why this priority**: Without a rule, each new feature will break the phone experience again.

**Independent Test**: A reviewer applies the checklist to one existing screen and one new screen. The checklist gives a clear pass or fail on each rule.

**Acceptance Scenarios**:

1. **Given** the principles document, **When** a developer plans a new screen, **Then** the document tells the developer how to choose the phone support level. It also states which rules apply.
2. **Given** a UI change in review, **When** the reviewer uses the checklist, **Then** each phone rule gives a pass, a fail, or "not applicable". The reviewer gives a reason for "not applicable".

---

### User Story 6 - Desktop use does not get worse (Priority: P3)

The operator who works on a desktop sees no loss of function or density after the change. Existing desktop workflows work as before.

**Why this priority**: The desktop is the main authoring tool. The phone work must not break it.

**Independent Test**: Run the existing UI test suites and walk through the main desktop flows. All pass with the same result as before.

**Acceptance Scenarios**:

1. **Given** a desktop-size screen, **When** the operator uses any existing area, **Then** the layout and functions match the state before this feature.

---

### Edge Cases

- Phone in landscape orientation: layout adapts, and no control becomes unreachable.
- Very small screen (320 px wide) and small tablet (768 px wide): both get the phone layout and stay usable.
- A screen that is 769 px wide gets the desktop layout. It stays usable.
- Phone with a notch or rounded corners: content and navigation are not cut off by safe areas.
- On-screen keyboard open: the focused field and the primary action stay visible.
- Network drops or the phone sleeps: the UI shows a "connection lost" banner and disables control actions. It never repeats an action by itself. When the connection returns, the UI reloads the real state from the service.
- The access token is missing or wrong: the UI shows an actionable message, not a blank screen.
- A user opens a deep link to a desktop-only area on a phone: the UI shows the notice from User Story 3.
- Long names (queue, sequence, game) do not break the layout; they are shortened with a way to see the full text.
- Touch only (no mouse): no function depends on hover, right-click, or precise pointer drag.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The UI MUST be usable on screens from 320 to 768 CSS pixels wide in all areas with full or reduced phone support. The page MUST NOT scroll sideways.
- **FR-001a**: The UI MUST choose the phone layout by screen size. A screen 768 CSS pixels wide or less gets the phone layout. A screen 500 CSS pixels high or less also gets the phone layout, so a phone in landscape keeps it. All other screens get the desktop layout. The layout MUST change when the window size or the phone orientation changes. It MUST NOT need a reload or a manual switch. A narrow or short desktop window therefore also gets the phone layout.
- **FR-002**: On phone-size screens, the UI MUST show a phone-style main navigation. The operator MUST reach it with one thumb. It MUST show the current area.
- **FR-003**: Interactive controls on phone-size screens MUST have a tap target of at least 44 x 44 CSS pixels. The space between controls MUST be large enough to prevent wrong taps.
- **FR-004**: Each phone screen MUST show one primary task and at most one primary action. Secondary actions MUST be grouped behind one menu or detail view.
- **FR-005**: On phone-size screens, list and table data MUST show as compact cards or rows with key facts only. A tap MUST open the detail view. The detail view MUST have a clear way back.
- **FR-006**: On phone-size screens, long forms MUST show in short groups so the operator does not need long vertical scroll. Labels and validation messages MUST stay next to their fields.
- **FR-007**: The operator MUST be able to see the status of all queues, and start and stop a queue, on a phone. The operator MUST also be able to change simple queue settings on a phone, such as enable or disable an entry. Reorder of entries and the creation or change of schedules are desktop-only. On a phone, the UI MUST show those controls as a notice that says "use a larger screen", not as broken controls.
- **FR-008**: Execution, Notifications, Configuration (connection and token), and Update MUST be usable on a phone.
- **FR-009**: Each area MUST have a recorded phone support level: full, reduced, or desktop-only. Execution logs MUST be desktop-only. The whole Authoring area (Commands, Games, Sequences, Images, and Backup and Restore) MUST be desktop-only unless a later feature changes this.
- **FR-010**: When an operator opens a desktop-only area on a phone-size screen, the UI MUST show a notice that names the area. The notice MUST offer a way back. The UI MUST NOT show a broken layout.
- **FR-011**: No function in a full or reduced area MAY depend on hover, right-click, keyboard shortcuts, or precise mouse drag.
- **FR-011a**: When the connection to the service is lost, the UI MUST show a "connection lost" banner and MUST disable control actions. The UI MUST NOT send an action again by itself. After the connection returns, the UI MUST reload the real state.
- **FR-012**: The UI MUST respect phone safe areas, screen rotation, and the on-screen keyboard.
- **FR-013**: The operator MUST be able to add the UI to the phone home screen with its own name and icon. Where the phone allows it, the UI MUST open without the browser address bar.
- **FR-014**: The delivery approach MUST be to optimize the current web UI (one code base, responsive, installable to the home screen). A separate mobile UI and a native app are out of scope. The operator chose this on 2026-10-13 (see approach-comparison.md, Option A).
- **FR-015**: The service MUST NOT accept connections from other devices by default. The documentation MUST describe how to allow phone access. It MUST require the access token for that setup. It MUST warn about open access without a token. It MUST warn that the token travels in clear text over plain HTTP, and advise a private VPN link outside the home network.
- **FR-016**: A short design-principles document for small screens MUST exist. It MUST define the rules, the three support levels, and how to choose a level for a new feature. The UI review checklist MUST include the rules.
- **FR-017**: All existing desktop functions MUST work as they did before. Existing automated UI tests MUST pass.
- **FR-018**: A comparison of the delivery approaches MUST exist before the approach is chosen. It MUST cover effort (new build versus optimize the current code), maintenance, capability, security, and risk.

### Key Entities

- **Area**: A main part of the UI (for example Queues, Execution, Notifications). It has a phone support level.
- **Phone support level**: One of full, reduced, or desktop-only. It decides what an area shows on a phone.
- **Design principle**: A rule for small screens with a pass/fail check. It is part of the review checklist.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a 360 x 640 screen, 100% of full and reduced areas show no sideways page scroll.
- **SC-002**: The operator sees the status of all queues with no tap after the UI opens. The operator starts or stops one queue with at most 3 taps.
- **SC-003**: At least 95% of interactive controls in full and reduced areas have a tap target of 44 x 44 CSS pixels or more.
- **SC-004**: For each full area, the main task fits in at most 2 screen heights of vertical scroll.
- **SC-005**: A person who follows the access guide opens the UI on a phone in under 10 minutes.
- **SC-006**: 100% of existing desktop automated UI tests pass after the change.
- **SC-007**: A reviewer can apply the design-principles checklist to a new screen in under 10 minutes. The reviewer gives a pass or a fail on every rule.
- **SC-009**: On a throttled mobile connection profile, the first screen of the built UI is ready for use in under 3 seconds.
- **SC-008**: The operator rates the phone experience as "app-like" in a hands-on check on a real phone (yes or no, recorded in the sign-off).

## Assumptions

- The operator uses a current phone browser (iOS Safari or Android Chrome) and one operator uses the UI. Tablets are a secondary target. They get the phone layout up to 768 px wide, and the desktop layout above that.
- The primary phone use is to monitor and control, with small changes to settings. It is not to author. All Authoring pages stay on the desktop (see FR-009). The image crop editor and the drag-and-drop lists therefore need no touch work in this feature.
- The phone reaches the service over the home network or a private remote link that the operator sets up. The project does not host a public service and does not add accounts.
- The existing optional access token stays the access control. A stronger scheme is out of scope.
- Encrypted connections (HTTPS) are not part of this feature. Some phone features, such as full app install and offline mode, need HTTPS. The comparison document describes this limit.
- Push notifications to the phone are out of scope. The existing notification features stay as they are.
- A real phone, or a phone emulator in the browser, is available for the final hands-on check.
- A later feature can add a thin native wrapper on top of the same UI if push alerts become important.
