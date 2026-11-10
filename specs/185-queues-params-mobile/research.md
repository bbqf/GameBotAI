# Research: Sleeker Queue Entries and Parameters on the Phone

All unknowns of the Technical Context are resolved here. No `NEEDS CLARIFICATION` remains.

## R-1: Where does the open state of a panel live?

- **Decision**: In `QueueSchedulingAreas`, as a set of entry IDs (`openEntryIds`). The card gets `open` and `onToggleOpen`.
- **Rationale**: A card moves to another `SchedulingArea` component when its schedule type changes. React creates the card again, so a local `useState` is lost. FR-006 needs the panel to stay open after a type change.
- **Alternatives considered**: (a) Local state in the card. It fails FR-006. (b) One flat list with CSS areas, so that no card moves. This is a large rewrite of the drag code and is out of scope.

## R-2: Big screen: inline panel or separate page?

- **Decision**: An inline panel inside the card.
- **Rationale**: The operator keeps the queue in view. Drag and drop still works. No history entry or route is needed. The editor holds all edits in memory until the template save. A separate page would need a new place for this draft.
- **Alternatives considered**: A side panel or a modal. Both hide the list. A separate page loses the unsaved edits of the editor.

## R-3: What does the summary show on a closed card?

- **Decision**: One short text from the shared helper `scheduleSummary.ts`. It gives the words of the phone rows. They are "Daily at 14:30", "Every 01:00:00", "Once each run", "Every step", "At queue start", "Before each run", and "not scheduled". In a Timer entry, the timer mode decides which field the helper reads.
- **Rationale**: FR-004 asks for the same words as the phone rows. One helper avoids two copies. The editor keeps an old time of day when the operator switches to relative mode. The mode must therefore win.
- **Alternatives considered**: New words such as "Timer · 14:30". Two sets of words would drift apart.

## R-4: Parameters part when the sequence declares nothing

- **Decision**: The part shows when the sequence declares parameters or the entry has values. In other cases the panel shows the Schedule part and the button **Add a value**. The button shows the ad-hoc add field of the shared form.
- **Rationale**: Today an operator can add an ad-hoc value to any entry. The spec requires no empty Parameters part. It must also keep this function (FR-005).
- **Alternatives considered**: Always show the part (it conflicts with spec scenario 6 of US1). Remove ad-hoc values on empty sequences (this loses a function).

## R-5: Unsaved edits when the phone user leaves the entry page

- **Decision**: A small module `phoneLeaveGuard`. The page registers a function that returns true when there are unsaved edits. The on-screen Back button and the `popstate` handler in `App.tsx` call it. When it returns true, the handler undoes the step with `history.go(1)`. It also raises the counter prop `leaveRequest`, and the page opens a keep-or-discard dialog.
- **Rationale**: A browser cannot cancel a back step. A step forward restores the entry and the address. The history stays intact. FR-015 asks for a question before the page leaves.
- **Risk**: Two fast back steps can skip the guard. The handler ignores the `popstate` that its own `go(1)` causes (a flag). A test covers this. A tab close is out of scope, because the browser has its own prompt.
- **Alternatives considered**: A draft in `sessionStorage` (more state and more cases). A save on every change (many writes, and a half-typed value could reach a running queue).

## R-6: How does the phone page know the entry?

- **Decision**: The address holds the entry ID: `?area=queues&queue=<queueId>&entry=<entryId>`. The position type gets an optional `entryId`. The page finds the entry in the template by this ID.
- **Rationale**: An entry ID is stable (feature 171). A list index changes when someone edits the template on the big screen.
- **Edge**: A template that needs an upgrade has entries with no ID. Such a row is not tappable. It shows the hint "Upgrade the template to open the entry settings." A queue with no linked template also has no entry IDs. Its rows are not tappable and show no extra hint. The existing text about the missing template is enough. An address to an entry that is not in the template shows "Entry not found".
- **Alternatives considered**: The template index in the address (it is not stable). The sequence ID (it is not unique, because two entries can use one sequence).

## R-7: How does the phone save the values?

- **Decision**: Use `saveQueueTemplate` with `overwrite: true`. A new helper `setTemplateEntryParameters(template, index, values)` builds the request. It uses the same mapper as `toggleTemplateEntry`. The mapper is exported from `queueEntryToggle.ts`.
- **Rationale**: This is the path of the enable switch (FR-011, FR-012). A save on a running queue changes the template only. The existing notice shows the waiting change. No new API is needed.
- **Edge**: The mapper drops bindings with a null value (the "Use default" rows). The big screen does the same today.
- **Alternatives considered**: A new endpoint for one entry. It is a service change and is out of scope.

## R-8: Which data does the entry page read?

- **Decision**: `PhoneQueuesView` already loads the queue detail and the template. It gives both to the page. The page reads the sequence with `getSequence(sequenceId)` once for the declarations. The effective values come from `effectiveParameters` of the template entry. When the sequence read fails, the page shows "The parameters did not load." and the stored values as plain text. Save is off. A reload of the page reads the sequence again.
- **Rationale**: No extra poll. One small read.
- **Alternatives considered**: `listSequences` (a large answer for one sequence). Editable ad-hoc rows after a failed read (the operator would edit without the hints of the declarations).

## R-9: Button name and icon

- **Decision**: A new icon `settings` in `IconButton`. The label is `Entry settings for <name>`. The title is `Entry settings`.
- **Rationale**: One button opens the whole panel. The old label `Parameters for <name>` no longer fits. About 46 test references change. They change in the same task as the card. The tests open the panel first.
- **Alternatives considered**: Keep the old label. It would be wrong when the panel also shows the schedule.

## R-10: Layout measure for the big screen

- **Decision**: Update `tests/e2e/editor-row-height.spec.ts`. It already measures the row at 1024 px. Add a Timer entry to the mock. Check that the closed row is at most 57 px high (half of the old 114 px). Check the new button name. Count the controls of the main row (at most 5).
- **Rationale**: SC-001 and SC-002 are layout facts. Only a real browser measures them.
