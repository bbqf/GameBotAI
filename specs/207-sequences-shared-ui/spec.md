# Feature Specification: Move the Sequences list, form page, and delete to the shared UI components

**Feature Branch**: `207-sequences-shared-ui`  
**Created**: 2026-11-28  
**Status**: Draft  
**Input**: User description: "Move the Sequences page (list, form page, delete) to the shared UI components and to docs/ui-guidelines.md, with the same patterns as feature 206." The full description is in the pipeline call. This is PR 6 of the UI consistency work, part 2a of Authoring. On 2026-11-28 the user chose to split Sequences into 2a (this PR) and 2b (the new step editor with 56 px step rows and the 480 px side panel).

## Background

The Sequences page (`SequencesPage.tsx`, about 2,900 lines) has the old layout that features 206 replaced on Commands and Games:

- An `<h2>`, a plain "Create Sequence" button, a hint "No sequences yet. …", and an old table with the filter in the table header and the name as a link button. The badges "No success notifications" and "Ignores smart scheduling" are next to the name.
- Two near-copies of the form (create and edit) with the sections Basics, Parameters, and Steps, `hr` separators, "← Back to list", "Edit Sequence", `FormActions`, a "Step through" button in the actions, and a Delete button with the classes `btn btn-danger`.
- The old `ConfirmDeleteModal`.
- The add-step toolbar under the step list uses plain buttons.

Feature 206 made the shared parts for this: the filter bar, the message area, the card form (`ui-form`), `useDeleteFlow`, `DeleteReferences`, and `ConfirmDialog.confirmDisabled`.

## Clarifications

### Session 2026-11-28

- Q: Does part 2a change the step rows? → A: No. The step rows, the loop and if blocks, and their inline editors stay. Only the add-step toolbar uses the shared button. Rationale: the user chose the split; part 2b changes the step editor.

The other answers are from the pipeline with no manual review. Each is the most reasonable option, chosen from the guideline, feature 206, and the code.

- Q: How wide is the Sequences form? → A: Max. 880 px, centered (the step editor width of guideline section 2), with a modifier class of the card form. Rationale: the form holds the step editor; 720 px is too narrow for the loop and if blocks.
- Q: Where does "Step through" go? → A: A secondary button with the icon `play` in the page header (rule H1: other page actions are secondary buttons). Its disabled reason shows in the message area under the header. In create mode the button shows and is off with the reason "Save the sequence first. …" (as now). Rationale: it is a page action, not a form action.
- Q: What does the Options column show for a sequence with both options? → A: Both texts on one line, separated by a comma and a space, in the muted color, with "…" when the text does not fit; the tooltip holds the help text of each option, one on each line. With no option it shows "—". Rationale: rules L3 and L8; the badges do not fit a fixed row with a chip look.
- Q: What does the delete flow do on the edit page when the delete fails with another error? → A: The dialog closes and the error shows as the form error (as feature 206). Rationale: the same pattern as Commands and Games.
- Q: Does the merged form keep the current element ids (for example `sequence-basics` and `sequence-edit-basics`)? → A: One set of ids for both modes, the create ids (`sequence-basics`, `sequence-parameters`, `sequence-steps`, and the field ids of the create form). The tests that use the edit ids change to the create ids. Rationale: one form has one set of ids; the ids are only used by labels and tests.
- Q: What does the page title show while the name loads in edit mode? → A: "Loading…" until the sequence loads, then the name as loaded. Rationale: the title must not be empty.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a sequence in the shared list (Priority: P1)

The author opens Authoring > Sequences. The header shows "Sequences" and "Create sequence". The filter bar is under the header. The list has the columns Name, Options, and Steps with 56 px rows. A click on a row opens the sequence.

**Why this priority**: The list is the entry to the page.

**Independent Test**: Open Sequences at 1280 px with 3 sequences (one with both options, one with a long name, one with no option). Measure and filter.

**Acceptance Scenarios**:

1. **Given** 3 sequences, **When** Sequences opens, **Then** the columns Options (360 px ±1) and Steps (96 px ±1, numbers on the right) have these widths, each row is 56 px (±1), and the rows are sorted by name.
2. **Given** a sequence with both options, **When** the list shows, **Then** its Options cell shows "No success notifications, Ignores smart scheduling" on one line. A sequence with no option shows "—".
3. **Given** the list, **When** the author types in "Filter by name", **Then** only the matching sequences show.
4. **Given** no sequence, **When** the page opens, **Then** the list shows "No sequences." and no other hint. **Given** a failed list call, **Then** the list shows the error and **Try again**.
5. **Given** a row, **When** the author clicks the row or "Edit <name>", **Then** the edit page of the sequence opens.

---

### User Story 2 - Create and edit on one form page (Priority: P1)

The author creates or edits a sequence on one form page: a header with a back arrow, the title, and "Step through"; the cards Basics, Parameters, and Steps (max. 880 px); and a footer bar with Delete (edit mode), Cancel, and Save.

**Why this priority**: Create and edit are the main work of the page. One form removes the two copies.

**Independent Test**: Create a sequence with the options on, edit it, save, and check the payloads; edit a sequence that changed on the server.

**Acceptance Scenarios**:

1. **Given** the list, **When** the author selects **Create sequence**, **Then** the header shows a back arrow, "New sequence", and "Step through" (off, with "Save the sequence first. …" in the message area); the form is 880 px wide (±1) and centered; the footer bar shows Cancel and Save and no Delete.
2. **Given** a new sequence with no parameters and all options off, **When** the author saves, **Then** the create call sends no `parameters`, no `excludeFromSuccessNotifications`, no `ignoreSmartScheduling`, and no `onTimeLimit`; with the options on and "End the run as success", it sends `true`, `true`, and `'end'`. The list shows "Sequence created.".
3. **Given** an edit page, **When** the author saves, **Then** the update call always sends `parameters`, `excludeFromSuccessNotifications`, `ignoreSmartScheduling`, and `onTimeLimit` (as now). The list shows "Sequence updated.".
4. **Given** the server has a newer version, **When** the update returns the version conflict, **Then** the form error shows "Sequence has changed on the server (version N). Reloaded latest version." and the form loads the latest version.
5. **Given** an edit page of "Daily rewards", **When** it opens, **Then** the title is "Daily rewards", the footer bar shows Delete on the left and Cancel and Save on the right, and "Step through" is on when there are no unsaved changes.
6. **Given** unsaved changes, **When** the author selects the back arrow, Cancel, or the active sub-tab, **Then** the unsaved-changes prompt shows.
7. **Given** a long form, **When** the author scrolls, **Then** the footer bar stays visible at the bottom.
8. **Given** the Steps card, **When** it shows, **Then** the step rows and their editors are the same as before, and the add-step buttons use the shared button style with the same texts.

---

### User Story 3 - Delete a sequence with the shared dialog (Priority: P2)

The author deletes a sequence from the row menu or the footer bar. The shared dialog asks "Delete sequence <name>?". If a queue or another item uses the sequence, the dialog shows the references and Delete is off.

**Why this priority**: Delete works now; this story moves it to the shared parts.

**Independent Test**: Delete a sequence from the row menu; try to delete a sequence that a queue uses.

**Acceptance Scenarios**:

1. **Given** a row, **When** the author selects Delete in "⋮", **Then** the dialog shows "Delete sequence <name>?", "You cannot undo this.", Cancel, and a red Delete.
2. **Given** the dialog, **When** the delete succeeds, **Then** the list reloads and shows "Sequence deleted.".
3. **Given** a 409 answer, **When** the delete fails, **Then** the dialog stays open with the message and "Referenced by:" with the names, Delete is off, and Cancel closes it.
4. **Given** the edit page, **When** the author deletes from the footer bar and confirms, **Then** the list shows with "Sequence deleted.".

### Edge Cases

- A name of 200 characters: the row stays 56 px; the title is one line with "…".
- The address opens an edit page (`initialEditId`) and the load fails: the form error shows the load error (as now).
- A sequence with 60 steps: the footer bar stays visible; the page has no sideways scroll at 1024 px.
- The author opens "Step through" and then makes a change: the button turns off and the message area shows "Save your changes first. …" (as now).
- A 409 with an empty message: the dialog shows "Cannot delete: the sequence is in use. Remove the links first.".

## Requirements *(mandatory)*

### Functional Requirements

**List**

- **FR-001**: The page MUST use `PageHeader` "Sequences" with one primary button "Create sequence" (icon `add`), the message area, and the filter bar "Filter by name".
- **FR-002**: The list MUST use `ListView` (label "Sequences table") with the columns Name (flexible), Options (360 px), and Steps (96 px, right aligned, tabular digits), sorted by name.
- **FR-003**: The Options cell MUST show the texts "No success notifications" and "Ignores smart scheduling" for the options that are on, separated by ", ", on one line in the muted color, with the help texts as the tooltip; "—" when no option is on.
- **FR-004**: Each row MUST have Edit ("Edit <name>") and the menu "More actions for <name>" with Delete (red, last). A row click or Edit MUST open the edit page with the unsaved-changes check.
- **FR-005**: The list MUST show "Loading…", "No sequences.", and the list error with **Try again**. The old hint "No sequences yet. …", the `<h2>`, the old table, and the link button MUST go away.

**Form page**

- **FR-006**: Create and edit MUST use one form (one render path) with the sections Basics, Parameters, and Steps. Only the title, the save call, the conflict handling, and Delete differ by mode.
- **FR-007**: The create payload MUST send `parameters` only when the list is not empty, `excludeFromSuccessNotifications: true` and `ignoreSmartScheduling: true` only when on, and `onTimeLimit: 'end'` only for "end". The update payload MUST always send `parameters`, `excludeFromSuccessNotifications`, `ignoreSmartScheduling`, and `onTimeLimit`. The other members (`name`, `version`, `steps`, `interStepDelayRangeMs`) MUST stay as now.
- **FR-008**: A version conflict on update MUST show "Sequence has changed on the server (version N). Reloaded latest version." and load the latest version. Other save errors MUST show the parameter error summary or the error message (as now).
- **FR-009**: The form page MUST use `PageHeader` with the back arrow (the current "back to list" with the unsaved-changes check), the title "New sequence" (create) or the name as loaded ("Loading…" before the load ends), and the secondary button "Step through" (icon `play`, `data-testid="step-through-button"`) with the current disabled rules. The disabled reason MUST show in the message area with `id="step-through-reason"`.
- **FR-010**: The form MUST be in the card form style, max. 880 px wide and centered. "← Back to list", the `<h3>` "Edit Sequence", and the `hr` separators MUST go away.
- **FR-011**: A `FooterBar` inside the form MUST show Delete (`danger-outline`, edit mode only) and "Loading…" (while loading) on the left, and Cancel (secondary) and Save (primary, last) on the right. `FormActions` and the `btn btn-danger` button MUST go away from this page.
- **FR-012**: The step rows, the loop and if blocks, and their inline editors MUST NOT change. The add-step buttons ("Add to steps", "Count", "While", "Repeat‑Until", "If", "Add wait step", "Add reschedule step", "Add quiz lookup step", "Add quiz learn step", "Add wait for still step") MUST use the shared secondary button with the same texts, `data-testid` values, and disabled rules.
- **FR-013**: The `StepThroughPanel` MUST work as now.

**Delete**

- **FR-014**: Delete (row menu or footer bar) MUST use `useDeleteFlow` (new kind `sequence`), `DeleteReferences`, and `ConfirmDialog`: title "Delete sequence <name>?", message "You cannot undo this.", danger Delete, `confirmDisabled` after a 409, Cancel always on. The default 409 text is "Cannot delete: the sequence is in use. Remove the links first.".
- **FR-015**: A successful delete MUST show the list, reload it, and show "Sequence deleted.". Another error MUST close the dialog and show the error in the message area (list) or as the form error (edit page).
- **FR-016**: This page MUST NOT use `ConfirmDeleteModal`.

**Behavior that stays**

- **FR-017**: All other behavior MUST stay: `initialCreate` and `initialEditId` from the address, the reset signal, the unsaved-changes prompt, drag and drop of steps, the command options and the unresolved command labels, `validate()`, and `validatePerStepConditions`.
- **FR-018**: The success texts MUST be "Sequence created.", "Sequence updated.", and "Sequence deleted.".

**Cleanup, documents, tests**

- **FR-019**: CSS that no code uses after the change (`sequences-table`, `sequences-row`, `sequence-steps-separator`, the badge rules of the list, `link-button` and `actions-header` where unused) MUST go away. New CSS MUST use tokens only, with hover rules inside `@media (hover: hover)`.
- **FR-020**: `docs/ui-guidelines.md` section 13 MUST describe the wide card form modifier, and the "Last reviewed" line MUST name feature 207.
- **FR-021**: The Sequences unit tests MUST change only for the layout, the ids, and the names. No behavior check may be removed. New unit tests MUST cover the Options column, the exact create and update payload members, the conflict reload, and the row Delete flow with 409 references.
- **FR-022**: A browser test (desktop 1280 px) MUST check: fixed column widths and 56 px rows; the filter bar above the list; a row click opens the edit page with a back arrow and the name; the form is 880 px wide and centered; the footer bar is visible at the bottom with Delete on the left and Save last; "Step through" is in the header; Delete from the row menu opens the dialog; the 409 references show in the dialog; the back arrow shows the list; no sideways scroll at 1024 px.
- **FR-023**: The phone support level of Authoring stays `desktop-only`.

### Key Entities

- **Sequence**: name, version, steps, inter-step delay range, parameters, the options (exclude from success notifications, ignore smart scheduling), on time limit. No change.
- **Delete conflict (409)**: message and references. No change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the list rows are 56 px high (±1) and the fixed columns keep their widths (±1).
- **SC-002**: The page has 0 uses of `btn`, `btn-danger`, `link-button`, `FormActions`, `ConfirmDeleteModal`, "← Back to list", and "Edit Sequence".
- **SC-003**: The page has 1 form instead of 2 copies; the page file is at least 250 lines shorter.
- **SC-004**: The create and update payloads are the same as before for the same input (checked by tests for each optional member).
- **SC-005**: All behavior checks of the current Sequences tests pass (changed only for names, ids, and layout).

## Assumptions

- No backend or API change is needed.
- Part 2b changes the step rows and editors; this PR keeps them.
- The list has no paging (rule L12 comes later).
