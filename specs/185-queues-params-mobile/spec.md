# Feature Specification: Sleeker Queue Entries and Parameters on the Phone

**Feature Branch**: `185-queues-params-mobile`
**Created**: 2026-11-09
**Status**: Implemented
**Input**: User description: "Make the Queues page sleeker by moving the scheduling under the parameters. Also make the parameters available on the mobile UI. Consider making parameters a separate page at least on the mobile UI, but think if it would be possible to integrate it smoothly in the desktop ui as well"

## Clarifications

### Session 2026-11-09

- Q: Does the phone entry page edit the schedule? → A: No. The schedule is read-only on the phone page. Only the parameter values can be changed.
- Q: What happens for a queue with no linked template? → A: Its rows are not tappable. No read-only page is built for it. An address to a missing entry shows "Entry not found".
- Q: What does the entry page show when the sequence does not load? → A: The text "The parameters did not load." Save is off. The stored values show as plain text.
- Q: What name does the collapsible part of a card have? → A: "Entry settings panel". The word "scheduling area" means one of the five areas of the editor.

## Background

The queue editor on the big screen shows each sequence entry as a card. The main row of a card holds many controls. These are the drag handle, the enable switch, the name with badges, the schedule type, the timer mode, the timer time, Run next, Parameters, and Remove. A Timer entry has up to 10 controls. The row wraps on a narrow window.

Each card already has a collapsible Parameters panel. The panel shows the values that the entry gives to its sequence.

On a phone, the queue detail shows the schedule as read-only rows. Each row has an enable switch and Run next. The phone shows no parameters. The phone has no control to change a schedule. The phone support level of Queues is `reduced`.

This feature has two goals:

1. Make the entry row on the big screen shorter. The schedule controls move into the same collapsible panel as the parameters.
2. Let the operator read and change the parameter values of an entry on a phone. The values have a page of their own.

## Decisions Made From the Request

- The collapsible part of a card is the **Entry settings panel**. It has two parts. The **Schedule** part comes first. The **Parameters** part comes second.
- "Move the scheduling under the parameters" means that the schedule controls leave the main row. They go into the same panel as the parameters. Schedule comes first because it applies to every entry.
- The Parameters part shows only when the sequence declares parameters or the entry has values.
- On the **big screen**, the panel opens inside the card. There is no separate page. The operator keeps the whole queue in view and can still drag cards. This is the smooth desktop integration that the request asks for.
- On the **phone**, an entry opens on **its own page**. The page has its own history entry and a Back control. It shows the entry name, the read-only schedule, and the parameter form.
- The phone does **not** edit the schedule. Editing schedules, changing the order, and adding or removing entries stay on the big screen.
- The phone support level of Queues stays `reduced`. The parameter values of an entry are `full` on the phone.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shorter entry rows on the big screen (Priority: P1)

An operator edits a queue on the big screen. Each card shows only the key controls on its main row. The schedule type, the timer mode, the timer time, and the relative offset are in the Entry settings panel. The operator opens the panel to change them.

**Why this priority**: This is the main visual change of the request. It makes the page sleeker for all operators. It also prepares the place for the parameters.

**Independent Test**: Open a stopped queue with entries of every schedule type. Check the main row of each card. Open the panel of a card. Change the schedule type and the timer. Save the template and open the queue again. The saved values show.

**Acceptance Scenarios**:

1. **Given** a queue with a Once Per Run entry, **When** the operator looks at the card, **Then** the main row shows the drag handle, the enable switch, the name with badges, a schedule summary, the settings button, and Remove. It shows no schedule select.
2. **Given** a queue with a Timer entry, **When** the operator looks at the closed card, **Then** the card shows a short schedule summary, for example "Daily at 14:30" or "Every 01:00:00". The words are the same as the words of the phone schedule rows.
3. **Given** a closed card, **When** the operator opens the Entry settings panel, **Then** the panel shows the schedule type. For a Timer entry it also shows the timer mode and the time or the relative offset.
4. **Given** an open panel, **When** the operator changes the schedule type, **Then** the card moves to the matching scheduling area. The panel of the card stays open.
5. **Given** an entry whose sequence declares parameters, **When** the operator opens the panel, **Then** the Parameters part shows below the Schedule part. It has the same fields and the same behavior as the current Parameters panel.
6. **Given** an entry whose sequence declares no parameters and has no values, **When** the operator opens the panel, **Then** the panel shows the Schedule part only. A button "Add a value" lets the operator add an ad-hoc value.
7. **Given** an entry that has parameter values, **When** the card is closed, **Then** the "Parameters" badge still shows on the card.

---

### User Story 2 - See and change entry parameters on the phone (Priority: P1)

An operator uses the phone. The operator taps an entry row in the queue detail. A separate page opens for that entry. The page shows the parameters and lets the operator change the values. The operator saves the values and goes back to the queue.

**Why this priority**: This is the second goal of the request. Today the phone cannot show or change the values of an entry. The operator must find a big screen to change one value, for example a target level.

**Independent Test**: At a phone width, open a queue whose entries use sequences with parameters. Tap an entry. Change a value. Save. Go back. Open the entry again and check that the value stays. Open the queue on the big screen and check that the entry shows the same value.

**Acceptance Scenarios**:

1. **Given** the phone queue detail, **When** the operator taps an entry row, **Then** a page for that entry opens. It shows the entry name, the schedule as text, and the parameter form.
2. **Given** the entry page, **When** the page shows, **Then** it shows the effective value of each declared parameter. It marks the values that the entry gives itself.
3. **Given** the entry page, **When** the operator changes a value and presses Save, **Then** the value is stored in the linked template. A confirmation shows. The queue detail shows the "Parameters" mark on the entry.
4. **Given** a running queue, **When** the operator saves a value on the entry page, **Then** the save changes the template only. It does not pause the queue. The notice "N changes wait in the template." shows on the queue detail. This is the same rule as for the enable switch.
5. **Given** the entry page, **When** the operator presses the on-screen Back control or the phone back button, **Then** the queue detail shows at the same place in the list.
6. **Given** a phone address that points to an entry page, **When** the page loads, **Then** the entry page shows. When the queue or the entry does not exist, the page shows "Entry not found" or "Queue not found" and a way back.
7. **Given** an entry whose sequence declares no parameters, **When** the operator opens the page, **Then** the page shows the schedule and the text "This sequence has no parameters." The operator can still add an ad-hoc value.
8. **Given** a queue that has no linked template, **When** the operator looks at the schedule rows, **Then** the rows are not tappable. The existing text explains that no template is linked.
9. **Given** a template that has entries with no entry ID, **When** the operator looks at the schedule rows, **Then** these rows are not tappable. A hint says "Upgrade the template to open the entry settings."
10. **Given** the entry page, **When** the sequence does not load, **Then** the page shows "The parameters did not load." and the stored values as plain text. Save is off.

---

### User Story 3 - Phone rules for the new page (Priority: P2)

The new page follows the mobile UI principles. It is easy to use with one hand. It is safe when the connection is weak.

**Why this priority**: The page must not break the rules that other phone screens follow. It is less urgent than the function itself.

**Independent Test**: Open the entry page at 320, 360, 390, 480, 600, and 768 px, and in landscape at 640 x 360. Turn the connection off. Check each line of the mobile checklist.

**Acceptance Scenarios**:

1. **Given** any phone width from 320 px, **When** the entry page shows, **Then** there is no sideways scroll. Each control is at least 44 x 44 px.
2. **Given** the entry page, **When** the connection is lost, **Then** the connection-lost banner shows and Save is off. The app does not send a save when the connection returns.
3. **Given** unsaved changes on the entry page, **When** the operator presses Back, **Then** the page asks to keep or discard the changes before it leaves.
4. **Given** a screen wider than the phone limit, **When** the operator opens the queue editor, **Then** the desktop layout is used. No separate entry page opens, and the phone layout shows no inline panel.

---

### Edge Cases

- A Timer entry has a relative offset. The closed card shows the offset in the same words as the phone rows ("Every 01:00:00").
- A Timer entry is in relative mode but keeps an old time of day. The summary follows the timer mode, not the old time.
- A disabled entry still has settings. The operator can open the panel and the page. The card keeps its "Off" badge.
- Many panels are open at the same time on the big screen. Each card keeps its own open state. A drag of a card does not close its panel.
- The queue starts while the operator edits it. The editor keeps the unsaved edits (the rule of feature 173).
- Two operators change the same entry on the phone and on the big screen. The last save wins. This is the same rule as for the enable switch.
- A parameter value is not valid. The form shows the error at the field. The server refuses an invalid save, and the page shows the error text and keeps the typed values.
- The entry page is open and someone removes the entry on the big screen. The next read shows "Entry not found" and a way back.
- The operator rotates the phone. The page keeps its state and the layout adapts.

## Requirements *(mandatory)*

### Functional Requirements

**Big screen (queue editor)**

- **FR-001**: The main row of an entry card MUST show only these items:
  - the drag handle,
  - the enable switch,
  - the name with badges,
  - a schedule summary,
  - Run next (when it applies),
  - the settings button,
  - Remove.
- **FR-002**: These controls MUST move from the main row to the Entry settings panel: the schedule type, the timer mode, the time of day, and the relative offset. The panel MUST show the Schedule part first. The Parameters part MUST follow.
- **FR-003**: The settings button MUST open and close the panel of its own card. It MUST show its state (expanded or not) to assistive technology. A new card MUST be closed.
- **FR-004**: A closed card MUST show a one-line schedule summary. The summary MUST use the same words as the phone schedule rows.
- **FR-005**: The Parameters part MUST show only when the sequence declares parameters or the entry has values. It MUST keep all current behavior: declared fields, effective values, ad-hoc names, and the save with the template. When the part does not show, the panel MUST give a button "Add a value" for an ad-hoc value.
- **FR-006**: A change of the schedule type in the panel MUST move the card to the matching scheduling area. The select on the main row does this today. The panel of the card MUST stay open.
- **FR-007**: Drag and drop, the area headings, add, remove, enable, and Run next MUST keep their current behavior. The saved template entries MUST NOT change in shape or meaning.
- **FR-008**: The existing badges (Off, stale, area names, Timer, Parameters) MUST stay on the card.

**Phone**

- **FR-009**: Each schedule row that has an entry ID MUST open a separate entry page when the operator taps it. The enable switch and Run next MUST keep working in the row. They MUST NOT open the page.
- **FR-010**: The entry page MUST show the entry name, the schedule as text (read-only), and the parameter form of the sequence.
- **FR-011**: The operator MUST be able to change and save the parameter values of an entry on the entry page. The values MUST be stored in the linked template, as on the big screen. Both screens MUST show the same values.
- **FR-012**: A save on a running queue MUST change the template only. It MUST NOT pause the queue or apply the change. The notice of waiting changes MUST show on the queue detail after the save.
- **FR-013**: The entry page MUST have its own history entry. The address MUST identify the queue and the entry. Back, forward, a reload, and an address shared between devices MUST show the same page. A missing queue or entry MUST show a message and a way back.
- **FR-014**: The entry page MUST show a confirmation after a successful save. It MUST show a clear error after a failed save and keep the typed values.
- **FR-015**: When the operator leaves the entry page with unsaved changes, the page MUST ask to keep or discard them. This applies to the on-screen Back control and to the phone back button.
- **FR-016**: The phone MUST NOT add a control to change a schedule type, a timer, the order of entries, or the list of entries. The text "Use a larger screen to edit schedules." MUST stay.
- **FR-017** *(web UI change)*: The feature MUST state its phone support level and follow `docs/mobile-ui-principles.md`. The Queues area stays `reduced`. The parameter values of an entry are `full` on the phone. The schedule editor stays `desktop-only`. Each line of the checklist in that document MUST pass for the entry page. The document MUST list the new page.
- **FR-018**: On the entry page, each control MUST be at least 44 x 44 px. Each text input MUST use a font of at least 16 px. There MUST be no sideways scroll from 320 px. No function MUST depend on hover.
- **FR-019**: When the connection is lost, the entry page MUST show the connection-lost banner and turn Save off. The app MUST NOT send a save again when the connection returns.
- **FR-020**: All new text MUST be in Simplified Technical English.
- **FR-021**: The entry page MUST NOT mount in desktop mode. The inline panel MUST NOT mount in phone mode.
- **FR-022**: The parameter form MUST be one shared component for both screens. The validation rules and the shown values are then the same on the phone and on the big screen.
- **FR-023**: A row that has no entry ID MUST NOT be tappable. For a template that needs an upgrade, the row MUST show the hint "Upgrade the template to open the entry settings."
- **FR-024**: The sequence of an entry can fail to load. The entry page MUST then show "The parameters did not load." It MUST show the stored values as plain text. It MUST turn Save off.

### Key Entities *(include if feature involves data)*

- **Queue entry**: One sequence in a queue. It has a schedule (type, timer time or offset, enabled) and a list of parameter values. This feature does not change this data.
- **Parameter value**: A name and a value that an entry gives to its sequence. It can be for a declared parameter or an ad-hoc name. It is stored in the linked template.
- **Entry settings panel**: The part of an entry card on the big screen that holds the Schedule part and the Parameters part.
- **Entry page**: The separate screen on the phone for one entry. It shows the schedule (read-only) and the parameter form.

## Assumptions

- The stored data and the programming interface do not change. The change is in how the two UIs show and edit the data that exists today.
- The phone saves parameter values to the linked template with the same call as the enable switch.
- The phone does not get a schedule editor. The request asks for parameters on the phone, not for scheduling.
- The Entry settings panel is closed at first on the big screen. The operator can open several panels together.
- The closed card summary comes from the current schedule. It changes at once when the schedule changes.
- Feature 184 (Run next, icon buttons) is in place. This feature keeps its buttons and rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The main row of an entry card has at most 5 controls for every schedule type. Today a Timer entry has up to 10.
- **SC-002**: At a 1024 px wide window, the main row of every entry card fits on one line for names of up to 30 characters.
- **SC-003**: An operator can change a parameter value on a phone and save it in under 30 seconds, from the queue detail.
- **SC-004**: 100% of the parameter values saved on the phone show unchanged on the big screen, and the other way round.
- **SC-005**: The entry page has no sideways scroll and no control smaller than 44 x 44 px. This holds at 320, 360, 390, 480, 600, and 768 px, and at 640 x 360.
- **SC-006**: Every function of the big screen editor still works: drag, add, remove, enable, schedule change, parameter values, Run next, and template save. The existing desktop tests pass. Only the tests that look for the schedule controls on the main row change.
- **SC-007**: With the connection lost, the operator can still read the entry page. The app sends no save request.
