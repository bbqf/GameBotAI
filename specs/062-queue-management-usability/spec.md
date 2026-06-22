# Feature Specification: Queue Management Usability

**Feature Branch**: `062-queue-management-usability`  
**Created**: 2026-06-22  
**Status**: Draft  
**Input**: User description: "let's improve the usability of queues management. first I want you to remove the sequences column in the overview - it doesn't bring anything, especially if many of them contain 0. second: save template should work as one click, if I didn't change the name, no need to ask for overwrite or anything. third: confirmations MUST be shown wherever I clicked Save - either for the queue itself or for the template"

## Clarifications

### Session 2026-06-22

- Q: When saving a template under a name that matches a *different* existing template, should the overwrite confirmation still be shown? → A: Yes — confirmation only on collision with a different existing template; unchanged or brand-new names save in one click.
- Q: With the confirmation now shown at the Save action's location, should the existing top-of-page status banner still appear for queue/template saves? → A: Move it — show the confirmation only at the Save action's location and drop the top-of-page banner for these saves.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One-click template save when the name is unchanged (Priority: P1)

A user editing a queue that is already associated with a template wants to push their latest
changes back into that same template. Because they are not renaming it, they expect the save to
complete in a single action without any extra "a template with this name already exists —
overwrite?" prompt.

**Why this priority**: This is the most frequent template interaction (iterating on an existing
template) and the current multi-step overwrite confirmation is the biggest friction point the user
called out. Removing it delivers the clearest day-to-day time savings.

**Independent Test**: Load a queue that is linked to an existing template, make an entry change,
click Save Template without altering the pre-filled name, and confirm the template is updated in
one action with no overwrite prompt.

**Acceptance Scenarios**:

1. **Given** a queue associated with a template named "Daily Farm" and the save name still reads
   "Daily Farm", **When** the user saves the template, **Then** the template is overwritten
   immediately with no overwrite confirmation step.
2. **Given** the same queue, **When** the user changes the name to a brand-new name that does not
   match any existing template, **Then** the template is saved as a new template in one action with
   no overwrite prompt.
3. **Given** the same queue, **When** the user changes the name to one that matches a *different*
   existing template, **Then** an overwrite confirmation is shown before that other template is
   replaced.

---

### User Story 2 - Save confirmation appears where the user clicked Save (Priority: P1)

A user clicks a Save action — either to save the queue itself or to save a template — and needs
immediate, visible confirmation at the place they acted, rather than having to scroll up or hunt
for a status message elsewhere on the page.

**Why this priority**: Without local feedback the user cannot tell whether their save succeeded,
which undermines trust in every save interaction. It applies to both save paths the user named.

**Independent Test**: Click Save on the queue form and confirm a success message appears adjacent
to that form; separately click Save Template and confirm a success message appears adjacent to the
template controls.

**Acceptance Scenarios**:

1. **Given** a user editing a queue, **When** they save the queue successfully, **Then** a success
   confirmation is shown at the queue form where the Save action lives.
2. **Given** a user saving a template, **When** the save succeeds, **Then** a success confirmation
   is shown at the template controls where the Save Template action lives.
3. **Given** a save that fails, **When** the error occurs, **Then** an error message is shown at the
   same location so the user knows the save did not complete.

---

### User Story 3 - Remove the Sequences column from the queues overview (Priority: P2)

A user scanning the queues overview table is distracted by a Sequences count column that is
frequently zero and adds no decision-making value. They want the overview cleaner and more focused.

**Why this priority**: A pure decluttering improvement; valuable but lower risk and lower urgency
than the save-flow fixes.

**Independent Test**: Open the queues overview and confirm the Sequences column is gone while every
other column and action still works.

**Acceptance Scenarios**:

1. **Given** the queues overview, **When** it is displayed, **Then** no Sequences (entry count)
   column is present.
2. **Given** the queues overview after the column is removed, **When** the user views any queue
   row, **Then** Name, Emulator, Cycle, Status, and Actions remain present and functional.

---

### Edge Cases

- Saving a template when the queue is not yet associated with any template and the typed name is
  new: saves in one action, no overwrite prompt.
- Saving a template with a name that, after trimming, matches an existing template's name
  case-insensitively: treated as the same name (overwrite path), consistent with how templates are
  matched elsewhere.
- A save confirmation from one action should not be mistaken for another; the most recent save's
  result is what the user sees at the relevant location.
- Removing the Sequences column must not break the layout of expandable rows (e.g. the live
  schedule row) that previously spanned the full set of columns.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The queues overview MUST NOT display a Sequences (entry-count) column.
- **FR-002**: The queues overview MUST continue to display the queue Name, Emulator, Cycle, Status,
  and Actions, and all existing row actions (Start, Stop, Schedule, Edit, Delete) MUST continue to
  function.
- **FR-003**: When the user saves a template using a name that matches the template the queue is
  currently associated with, the system MUST persist (overwrite) the template in a single action
  without showing an overwrite confirmation.
- **FR-004**: When the user saves a template using a name that does not match any existing template,
  the system MUST save it in a single action without showing an overwrite confirmation.
- **FR-005**: When the user saves a template using a name that matches a *different* existing
  template (one the queue is not currently associated with), the system MUST request explicit
  confirmation before overwriting that template.
- **FR-006**: Upon a successful queue save, the system MUST display a success confirmation at the
  location of the queue Save action, and MUST NOT rely on the top-of-page status banner to convey
  that result.
- **FR-007**: Upon a successful template save, the system MUST display a success confirmation at the
  location of the template Save action, and MUST NOT rely on the top-of-page status banner to convey
  that result.
- **FR-008**: Upon a failed queue or template save, the system MUST display an error indication at
  the same location as the corresponding Save action so the user knows the save did not complete.
- **FR-009**: Template name validation (required, length, allowed characters) MUST continue to apply
  before any save is attempted, including the one-click path.

### Key Entities *(include if feature involves data)*

- **Queue**: A configured run target shown as a row in the overview (Name, Emulator, Cycle, Status)
  with associated entries and an optional associated template.
- **Queue Template**: A named, saved snapshot of a queue's ordered entries and their schedules; a
  queue may be associated with one template and saved back to it or saved under a new name.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Saving changes back to the already-associated template takes exactly one user action
  (down from the current multi-step flow that includes an overwrite confirmation).
- **SC-002**: 100% of successful and failed saves (queue and template) surface their result at the
  location where the user clicked Save, with no need to scroll to a different part of the page.
- **SC-003**: The queues overview presents one fewer column, and users can identify a queue's key
  attributes (name, emulator, cycle, status) and act on it without the removed column.
- **SC-004**: Overwriting a *different* existing template still requires an explicit confirmation,
  so no template is unintentionally replaced.

## Assumptions

- "Wherever I clicked Save" means an inline confirmation co-located with the relevant Save control
  (the queue form for the queue, the template controls for the template). This replaces — rather
  than supplements — the page-top status banner currently used for these saves.
- Template name matching is case-insensitive and trimmed, consistent with existing template
  reload/matching behavior.
- The one-click rule is keyed on whether the typed name resolves to the queue's currently
  associated template (unchanged → overwrite silently) versus a different existing template
  (changed-into-collision → confirm). Saving under a genuinely new name always proceeds directly.
- Removing the Sequences column does not change any underlying data; entry counts remain available
  inside the queue editor where entries are managed.
