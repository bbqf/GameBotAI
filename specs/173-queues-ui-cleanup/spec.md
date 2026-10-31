# Feature Specification: Clean up the Queues page on the big screen

**Feature Branch**: `173-queues-ui-cleanup`  
**Created**: 2026-10-30  
**Status**: Implemented  
**Input**: User description: "Clean up the big screen UI/Queues part. Remove the Schedule button. Use icons for the row buttons. Replace the Emulator column with what runs now and when the next run is, like the phone UI. Make the Review changes button clear. Enable Edit on running queues (feature 171), and maybe move Review changes under the Edit page."

## Clarifications

### Session 2026-10-30

- Q: What does the Edit page show for a running queue with no linked template? → A: The monitor and a read-only view of the entries, with a message to link a template. The operator can link a template there.
- Q: What does the phone show for template changes? → A: The phone detail screen keeps the review and apply action. It shows the same notice and explanation as the big screen. The Review button shows only when changes wait.
- Q: How does a phone switch reach a running queue? → A: The switch changes the template only. The phone shows that a change waits, with the Review button. The operator pauses, reviews, and applies.
- Q: Which queue settings are editable on the Edit page of a running queue? → A: The emulator is read-only while the queue runs. All other settings stay editable. The page shows one note near Save: the settings apply at the next start.
- Q: How does the Edit page show when a setting applies? → A: One note near Save. It says that all settings apply at the next start.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit a running queue (Priority: P1)

Today the Edit button is disabled while a queue runs. The feature 171 flow has no clear entry point. After this feature, Edit is always available. For a running queue, the Edit page shows the live monitor and the template editor together. The operator changes the entries on this page.

**Why this priority**: The operator names this as the main problem. Feature 171 adds live changes, but the page blocks the way to them.

**Independent Test**: Start a queue. Check that Edit is enabled. Open it. Check that the page shows the monitor and the entry editor. Change one entry and save. Check that no queue entry call is sent and the run goes on.

**Acceptance Scenarios**:

1. **Given** a running queue, **When** the operator looks at its row, **Then** the Edit button is enabled.
2. **Given** a running queue, **When** the operator opens Edit, **Then** the page shows the live monitor and the entry editor.
3. **Given** a stopped queue, **When** the operator opens Edit, **Then** the page works as before.
4. **Given** a running queue, **When** the operator saves an entry change, **Then** the run does not stop or restart.

---

### User Story 2 - See the queue activity in the list (Priority: P1)

The Emulator column gives little value. Each row shows the activity of the queue in its place. The text is the same as on the phone: the running sequence, the next run time, or "Stopped".

**Why this priority**: The operator wants to see at a glance what each queue does. The phone already shows this text.

**Independent Test**: Show a queue that runs a long sequence, a queue that waits for a timer, and a stopped queue. Check that the rows show "Running: <sequence name>", "Next run at <time>", and "Stopped".

**Acceptance Scenarios**:

1. **Given** a queue that runs a sequence, **When** the operator looks at the list, **Then** the row shows "Running:" and the sequence name.
2. **Given** a running queue that waits, **When** the operator looks at the list, **Then** the row shows "Next run at" and the clock time.
3. **Given** a stopped queue, **When** the operator looks at the list, **Then** the row shows "Stopped".
4. **Given** a queue that the operator paused, **When** the operator looks at the list, **Then** the row shows "Paused" or "Pausing".
5. **Given** the list is open, **When** time passes, **Then** the activity text updates with no page reload.

---

### User Story 3 - Compact icon buttons (Priority: P2)

The Actions column uses text buttons and takes much space. The row uses icon buttons instead. The icons are play, pause, stop, delete, edit, and duplicate. Each icon has a text name for screen readers and a tooltip.

**Why this priority**: It saves space and makes the row easy to scan. It does not change what a button does.

**Independent Test**: Open the list. Check that each action is an icon button with a name and a tooltip. Check that each button does what the text button did.

**Acceptance Scenarios**:

1. **Given** a stopped queue, **When** the operator looks at the row, **Then** the row shows the play, edit, duplicate, and delete icons.
2. **Given** a running queue, **When** the operator looks at the row, **Then** the row shows pause, stop, edit, and duplicate. The delete icon is disabled.
3. **Given** a paused queue, **When** the operator looks at the row, **Then** the play icon resumes the queue. The row does not show the pause icon.
4. **Given** any icon button, **When** the operator moves the pointer over it, **Then** a tooltip shows its name.
5. **Given** any icon button, **When** a screen reader reads it, **Then** it reads the action and the queue name, for example "Stop Daily queue".

---

### User Story 4 - Remove the Schedule button (Priority: P2)

The Schedule button opens a small form in a running row. The operator finds it irrelevant and confusing. The list no longer has it.

**Why this priority**: The page has less clutter. The change is a removal only.

**Independent Test**: Run a queue. Check that its row has no Schedule button and that no schedule form opens from the list.

**Acceptance Scenarios**:

1. **Given** a running queue, **When** the operator looks at its row, **Then** there is no Schedule button.
2. **Given** the list, **When** the operator uses any control, **Then** no live-schedule form opens.

---

### User Story 5 - A clear way to review template changes (Priority: P2)

The "Review changes" button in the list row is confusing. It compares the running queue with its linked template. It lets the operator apply the differences to a paused queue. The row gives no hint of this. The button also shows when nothing can be applied. The review moves to the Edit page.

**Why this priority**: The feature works, but the operator cannot find out how to use it.

**Independent Test**: Edit the template of a running queue. Check that the Edit page shows a notice of waiting changes. Pause, review, apply, and resume. Check that the notice goes away.

**Acceptance Scenarios**:

1. **Given** a running queue that matches its template, **When** the operator looks at the list and the Edit page, **Then** nothing shows that changes wait.
2. **Given** a running queue whose template changed, **When** the operator opens the review, **Then** the page lists the differences. It explains in one sentence what Apply does and that the queue must be paused first.
3. **Given** the review list, **When** the operator applies the changes, **Then** the behavior of feature 171 does not change. The queue is paused, all selected changes go in or none do, and schedules stay.

---

### User Story 6 - The big screen and the phone agree (Priority: P2)

The operator uses the big screen and the phone for the same queues. A queue shows the same activity text, the same pause state, and the same action names on both screens. The operator learns one vocabulary only.

**Why this priority**: The operator asks for this directly. The phone changes a little, because its list does not show the pause state today.

**Independent Test**: Put one queue in each state: stopped, running a sequence, waiting for a timer, pausing, and paused. Open the big screen list and the phone list. Check that the activity text is the same on both.

**Acceptance Scenarios**:

1. **Given** a queue in any state, **When** the operator compares the two lists, **Then** both show the same activity text.
2. **Given** a queue that the operator paused, **When** the operator opens the phone list, **Then** the card shows "Paused" or "Pausing".
3. **Given** the big screen icons, **When** the operator compares them with the phone buttons, **Then** the names match (Start, Stop, Pause, Resume, Cancel pause).
4. **Given** a running queue whose template changed, **When** the operator opens it on both screens, **Then** both use the same words for the notice. Both let the operator review and apply the changes.

---

### User Story 7 - Turn sequences on and off from the phone (Priority: P2)

The operator turns a sequence (an entry) on or off with one tap on the phone. The phone already has a switch for each entry. It saves the change to the linked template. For a running queue, the change does not reach the run at once, and the phone does not say so. After this feature, the switch works the same way for stopped and running queues. The phone shows when a change waits.

**Why this priority**: The operator wants to control the sequences from the phone. A switch with no visible effect on a running queue is confusing.

**Independent Test**: Run a queue with three entries. Turn one entry off on the phone. Check that the switch shows "off" and the phone shows that one change waits. Apply the change (User Story 5). Check that the queue no longer runs the entry.

**Acceptance Scenarios**:

1. **Given** a stopped queue with a template, **When** the operator turns an entry off, **Then** the switch shows "off" and the entry shows "disabled". The next start skips the entry.
2. **Given** a running queue with a template, **When** the operator turns an entry on or off, **Then** the switch shows the new state. The phone shows the notice of waiting changes. The change reaches the run only after pause, review, and apply.
3. **Given** a queue with no linked template, **When** the operator looks at the entries, **Then** the switches are read-only and a message tells why.
4. **Given** a queue, **When** the operator turns an entry off on the phone, **Then** the big screen editor shows it as off after a refresh.

---

### Edge Cases

- A queue stops while the operator is on its Edit page. The page stays open and the monitor shows "Stopped". The editor keeps working.
- A queue starts while the operator edits it. The page shows the live monitor. It keeps the unsaved edits.
- A running queue has no linked template. FR-018 states what the Edit page shows.
- The activity data of a queue cannot load. The row shows the status only. The list shows no error.
- The list has many queues. A slow or failed monitor call for one queue does not block the other rows.
- The window is narrow. The icon buttons stay on one row. No button is hidden.

## Requirements *(mandatory)*

### Functional Requirements

**Schedule button**

- **FR-001**: The Queues list MUST NOT show a Schedule button. The list MUST NOT open the live-schedule form.

**Icon buttons**

- **FR-002**: The row actions MUST be icon buttons for play (start or resume), pause, stop, edit, duplicate, and delete.
- **FR-003**: Each icon button MUST have a tooltip. Its accessible name MUST include the action and the queue name.
- **FR-004**: Each icon button MUST keep the behavior and the enabled rules of the text button it replaces. Edit is the exception (FR-009).
- **FR-005**: The row MUST show only the buttons that fit the queue state. A stopped queue shows play, edit, duplicate, and delete. A running queue shows pause, stop, edit, duplicate, and a disabled delete. A pausing queue shows "Cancel pause" in place of pause. A paused queue shows play (Resume) in place of pause.
- **FR-006**: The destructive icons (stop and delete) MUST look different from the others. Delete MUST keep its confirmation dialog.

**Activity column**

- **FR-007**: The list MUST NOT show the Emulator column. The Activity column MUST take its place.
- **FR-008**: The Activity column MUST show one of these texts for each queue: "Running: <sequence name>", "Next run at <time>", "Nothing scheduled", "Waiting", "Stopped", "Paused", or "Pausing". The rules and the words MUST be the same as on the phone queue card (FR-020, FR-021). The text MUST update with no page reload.

**Edit on a running queue**

- **FR-009**: The Edit button and the queue name link MUST be enabled for a running queue.
- **FR-010**: The Edit page of a running queue MUST show the live monitor and the entry editor.
- **FR-011**: A save on the Edit page of a running queue MUST change the linked template only. It MUST NOT change the running queue. It MUST NOT stop or restart the run. The changes reach the queue only through the review and apply steps (FR-013 to FR-015).
- **FR-012**: The Edit page of a stopped queue MUST work as before.
- **FR-018**: A running queue with no linked template needs a different page. The Edit page MUST show the entries as read-only. It MUST show the message "Link a template to edit this queue while it runs." It MUST let the operator link a template. It MUST NOT change the running entries.
- **FR-019**: On the Edit page of a running queue, the emulator MUST be read-only. All other queue settings MUST stay editable. The page MUST show one note near Save: "Settings apply at the next start."

**Review of template changes**

- **FR-013**: The list row MUST NOT have a Review button. The review MUST move to the Edit page of the running queue.
- **FR-014**: The review MUST explain in plain words what it compares (the running queue and its template). It MUST explain what Apply does. It MUST say that the queue must be paused first.
- **FR-015**: The review and apply behavior of feature 171 MUST NOT change.
- **FR-017**: A running queue can differ from its linked template. Then the Edit page MUST show a notice with the number of changes and a Review button. The page MUST NOT show the notice when the queue matches its template or has no linked template.
- **FR-028**: The Edit page of a running queue MUST show the pause controls (Pause, Cancel pause, Resume) and the pause state by the notice. The operator then pauses, applies, and resumes on one page. The controls MUST use the names and states of the list (FR-005, FR-022).

**Phone**

- **FR-016**: The feature MUST state its phone support level: reduced. The phone can view, start, stop, pause, resume, switch entries, and review template changes. The phone view changes only where FR-020 to FR-027 say. The phone gets no icon buttons, no Edit page, and no duplicate or delete action. It follows docs/mobile-ui-principles.md.

**Big screen and phone agree**

- **FR-020**: A queue in one state MUST show the same activity text on the big screen and on the phone card, character for character. Both screens MUST get the text from one shared rule.
- **FR-021**: The activity text MUST include the pause states of feature 171. A paused queue shows "Paused". A queue that waits for the running sequence to end shows "Pausing". Both screens MUST show them. A paused queue MUST NOT show "Running: <sequence name>" or "Next run at <time>".
- **FR-022**: The action names MUST be the same on both screens: Start, Stop, Pause, Resume, Cancel pause. The big screen icon buttons MUST use these names as the tooltip and the accessible name. The phone buttons keep their text.
- **FR-023**: The notice of waiting changes and its explanation (FR-014, FR-017) MUST use the same words on both screens. The phone detail screen of a running queue MUST keep the review and apply action. It MUST show the notice and the Review button only when the queue differs from its template.
- **FR-024**: A test MUST check that both screens show the same activity text. The test covers six states: stopped, running a sequence, waiting for a timer, nothing scheduled, pausing, and paused.

**Enable and disable entries on the phone**

- **FR-025**: The phone detail screen MUST show an on/off switch for each entry of a queue that has a linked template. This holds for stopped and running queues. A queue with no linked template MUST show read-only switches and a message that tells why.
- **FR-026**: A phone switch MUST change the same entry state that the big screen template editor shows. A change on one screen MUST show on the other screen after a refresh.
- **FR-027**: The phone MUST show that a change waits after the operator changes a switch for a running queue. A switch on a running queue MUST change the template only. It MUST NOT pause the queue, apply the change, or change the running entries. The change reaches the queue only through the review and apply steps (FR-011, FR-023).

### Key Entities

- **Queue row**: One line of the list. It shows name, cycle, status, activity, and the action icons.
- **Queue activity**: The short text for a queue: the running sequence, the next run time, paused, or stopped. It comes from the data that the phone uses.
- **Template difference**: One difference between a running queue and its linked template (feature 171). This feature does not change it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The operator sees what each queue runs now, or when it runs next, from the list alone. No click is needed.
- **SC-002**: The Actions column uses at least 50% less width than today at the same window size.
- **SC-003**: The operator opens the editor of a running queue in one click from the list.
- **SC-004**: An operator who has not used feature 171 finds and applies a template change to a running queue in under 2 minutes. No outside help is needed.
- **SC-005**: Every action in the list works with the keyboard and has a text name for screen readers.
- **SC-006**: The two screens show the same activity text in 100% of the checked cases. The cases are the six states of FR-024.

## Assumptions

- The review moves to the Edit page, and a save on a running queue changes the template only (Clarifications).
- The list shows the activity text from the monitor data that the phone already reads. The plan decides how to load it for many queues.
- All queue settings apply at the next start. Task T006 checks this. If a setting applies at once, the note changes for that setting.
- Icon names: play = start or resume, pause = pause, stop = stop, pencil = edit, copy = duplicate, bin = delete.
- The live-schedule service call stays in the product. Only its list control goes away.
