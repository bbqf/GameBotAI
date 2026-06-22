# Feature Specification: Queue Management Usability

**Feature Branch**: `062-queue-management-usability`  
**Created**: 2026-06-22  
**Status**: Draft  
**Input**: User description: "let's improve the usability of queues management. first I want you to remove the sequences column in the overview - it doesn't bring anything, especially if many of them contain 0. second: save template should work as one click, if I didn't change the name, no need to ask for overwrite or anything. third: confirmations MUST be shown wherever I clicked Save - either for the queue itself or for the template"

## Clarifications

### Session 2026-06-22

- Q: When saving a template under a name that matches a *different* existing template, should the overwrite confirmation still be shown? → A: Yes — confirmation only on collision with a different existing template; unchanged or brand-new names save in one click.
- Q: With the confirmation now shown at the Save action's location, should the existing top-of-page status banner still appear for queue/template saves? → A: Move it — show the confirmation only at the Save action's location and drop the top-of-page banner for these saves.

### Session 2026-06-22 (template-save controls refinement)

- Q: How is the template name edited and saved — via a separate "save as template" popup? → A: No popup. The template name is edited inside the area that opens from the template-name control (the same area that lists templates to load), and an explicit **Rename** button next to the name field saves under the typed name.
- Q: When the user clicks Rename and the typed name collides with a different existing template, where is the overwrite confirmation shown? → A: Next to the Rename button, inside that same area.
- Q: What does the separate bottom **Save Template** button do, versus Rename? → A: Save Template re-saves the current entries to the queue's already-associated template under its existing name, in one click, ignoring any unsaved edit in the name field. It is disabled when the queue has no associated template yet (the first template must be created via the name field + Rename).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One-click template save, with rename in the template area (Priority: P1)

A user editing a queue that is already associated with a template wants to push their latest
changes back into that same template in a single click. When they do want to save under a different
name, they edit the name in the template area and confirm with a dedicated **Rename** action — only
a collision with a *different* existing template adds an overwrite confirmation.

**Why this priority**: Iterating on an existing template is the most frequent template interaction,
and the previous multi-step overwrite confirmation was the biggest friction point the user called
out. A one-click Save Template plus an explicit Rename path delivers the clearest day-to-day time
savings while keeping name changes deliberate.

**Independent Test**: Open a queue linked to an existing template, change an entry, click Save
Template, and confirm the template is updated in one action with no overwrite prompt. Separately,
open the template area, type a different name, click Rename, and confirm the save/overwrite behavior.

**Acceptance Scenarios**:

1. **Given** a queue associated with a template named "Daily Farm", **When** the user clicks **Save
   Template**, **Then** the current entries are saved back to "Daily Farm" in one action with no
   overwrite confirmation.
2. **Given** a queue not yet associated with any template, **When** the user opens the template
   area, types a name that matches no existing template, and clicks **Rename**, **Then** a new
   template is created under that name in one action (no overwrite prompt) and the queue becomes
   associated with it.
3. **Given** a queue, **When** the user opens the template area, changes the name to one that
   matches a *different* existing template, and clicks **Rename**, **Then** an overwrite
   confirmation is shown next to the Rename button before that other template is replaced.
4. **Given** a queue associated with "Daily Farm", **When** the user edits the name field but does
   **not** click Rename and instead clicks **Save Template**, **Then** the template is saved under
   the old name "Daily Farm" and the typed-but-unconfirmed edit is ignored.
5. **Given** a queue with no associated template, **Then** the bottom **Save Template** action is
   disabled until a template is created via the name field + Rename.

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

- Creating the first template for a queue not yet associated with any template: done via the name
  field + **Rename**; saves in one action, no overwrite prompt. (The bottom **Save Template** button
  is disabled in this state.)
- Saving a template with a name that, after trimming, matches the associated template's name
  case-insensitively: treated as the same name (overwrite path), consistent with how templates are
  matched elsewhere.
- Editing the name field but not clicking **Rename**, then clicking **Save Template**: the save uses
  the old (associated) name; the unconfirmed edit is ignored. Reopening the template area resets the
  field to the current name so a stale edit does not linger.
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
- **FR-003**: Clicking **Save Template** MUST persist (overwrite) the queue's currently associated
  template under its existing name in a single action without showing an overwrite confirmation, and
  MUST ignore any unsaved edit in the template name field.
- **FR-004**: When the user clicks **Rename** with a name that matches no existing template (or
  matches the queue's currently associated template), the system MUST save in a single action
  without showing an overwrite confirmation.
- **FR-005**: When the user clicks **Rename** with a name that matches a *different* existing
  template (one the queue is not currently associated with), the system MUST request explicit
  confirmation, shown next to the Rename action, before overwriting that template.
- **FR-006**: Upon a successful queue save, the system MUST display a success confirmation at the
  location of the queue Save action, and MUST NOT rely on the top-of-page status banner to convey
  that result.
- **FR-007**: Upon a successful template save, the system MUST display a success confirmation at the
  template controls (the Save Template / Rename location), and MUST NOT rely on the top-of-page
  status banner to convey that result.
- **FR-008**: Upon a failed queue or template save, the system MUST display an error indication at
  the same location as the corresponding Save action so the user knows the save did not complete.
- **FR-009**: Template name validation (required, length, allowed characters) MUST apply before a
  Rename save is attempted.
- **FR-010**: The template name MUST be edited inside the area that opens from the template-name
  control (the same area that lists templates to load), with an explicit **Rename** action next to
  the name field; there MUST NOT be a separate "save as template" popup/dialog.
- **FR-011**: The **Save Template** action MUST be disabled when the queue has no associated template
  yet; in that state the first template is created via the name field + **Rename**.

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
- Two distinct save controls exist: **Save Template** (bottom) is a one-click re-save to the
  associated template under its existing name; **Rename** (next to the name field, in the template
  area) saves under the typed name. The overwrite confirmation can only arise from Rename, and only
  when the typed name collides with a *different* existing template; saving under the associated
  name or a genuinely new name proceeds directly.
- Removing the Sequences column does not change any underlying data; entry counts remain available
  inside the queue editor where entries are managed.
