# Feature Specification: Edit Queue Page Layout

**Feature Branch**: `048-edit-queue-layout`  
**Created**: 2026-06-01  
**Status**: Implemented (iterated by 061, 062)
**Input**: User description: "let's refine the edit queue page a bit more. I want the controls be raw by raw like this: 1. Name, editable. Emulator read-only. Remove the text that 'bound emulator cannot be exchanged...' 2. Template name; Save Template; Reload Template (new button). Reload should ask for confirmation. Template name is a button, which is clickable, when clicked it opens the current Load Template section. Both Load and Save template sections open between row 2 and 3, and closed. 3. Cycle execution 4. Sequences (as many rows as there are) 5. Save and Cancel buttons for the edit page."

## Clarifications

### Session 2026-06-01

- Q: When the operator clicks Reload Template, when should the confirmation prompt appear? → A: Only when entries would actually change — skip the prompt when there is nothing to discard (empty queue, or no edits since the template was loaded); confirm only when the queue's current entries differ from the template's.
- Q: Do template Load/Reload (and add/remove) actions on the queue's sequence entries apply immediately, or are they part of the page-level Save/Cancel? → A: They apply immediately and are independent of Save/Cancel. The template defines only the sequence entries, which are not persisted with the queue, so they have no relation to the page-level Save/Cancel (which govern the queue's name and cycle-execution setting only).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit a queue through a clear, ordered layout (Priority: P1)

While editing a queue, an operator sees the edit page organized as an ordered set of rows, top to bottom: (1) the queue name (editable) alongside its bound emulator (read-only), (2) the template controls, (3) the cycle-execution setting, (4) the queue's sequence entries, and (5) the Save and Cancel actions for the edit page. Each concern lives in its own row in this order, so the operator always knows where to find a given control.

**Why this priority**: The whole request is a layout refinement of the existing edit queue page. This row-by-row ordering is the backbone everything else hangs on; without it the other refinements have no home. It delivers immediate value by making the edit page predictable and scannable.

**Independent Test**: Open a queue for editing and confirm the controls appear, top to bottom, in the order: name + emulator, template controls, cycle execution, sequences, then Save/Cancel — and that name is editable while emulator is read-only.

**Acceptance Scenarios**:

1. **Given** an operator opens a queue for editing, **When** the edit page renders, **Then** the rows appear in order: (1) name + emulator, (2) template controls, (3) cycle execution, (4) sequences, (5) Save/Cancel.
2. **Given** the edit page is open, **When** the operator views row 1, **Then** the queue name is an editable field and the bound emulator is shown read-only.
3. **Given** the edit page is open, **When** the operator views the bound emulator, **Then** no explanatory text stating the bound emulator cannot be changed/exchanged after creation is shown.
4. **Given** the operator changes the name and cycle-execution setting and clicks Save (row 5), **Then** the queue is saved with the new values; clicking Cancel discards the in-progress edits and closes the edit page.

---

### User Story 2 - Manage templates from a single inline row (Priority: P1)

The template controls occupy row 2 as a single row containing: the current template name (rendered as a clickable button), a **Save Template** action, and a **Reload Template** action. Clicking the template name opens the Load Template section. The Save Template and Load Template sections expand inline between row 2 and row 3; they are collapsed by default and only one is shown at a time. The operator can therefore save, load, or reload a template without leaving the edit page or opening a separate modal.

**Why this priority**: Templates are the primary working tool on this page (from the Queue Templates feature). Consolidating them into one row with inline, collapsible sections is the core of the refinement and is what the operator interacts with most.

**Independent Test**: On the edit page, confirm row 2 shows the template name as a button plus Save Template and Reload Template buttons; click the template name and confirm the Load Template section opens inline below row 2; trigger Save Template and confirm the Save section opens in the same place; confirm both are closed by default.

**Acceptance Scenarios**:

1. **Given** the edit page is open, **When** the operator views row 2, **Then** it shows the current template name as a clickable button, a Save Template button, and a Reload Template button, with both the Save and Load sections collapsed.
2. **Given** row 2 is shown, **When** the operator clicks the template-name button, **Then** the Load Template section opens inline between row 2 and row 3.
3. **Given** row 2 is shown, **When** the operator clicks Save Template, **Then** the Save Template section opens inline between row 2 and row 3.
4. **Given** the Load Template section is open, **When** the operator opens the Save Template section, **Then** the Load Template section closes (only one inline section is open at a time).
5. **Given** an inline template section is open, **When** the operator completes or dismisses it, **Then** the section collapses and row 2 returns to its default (both sections closed).
6. **Given** no template has been loaded or saved yet in this editing session, **When** the operator views the template-name button, **Then** it shows a neutral "no template" placeholder and still opens the Load Template section when clicked.

---

### User Story 3 - Reload the current template with confirmation (Priority: P2)

The operator clicks **Reload Template** to re-apply the template the queue was most recently loaded from or saved as, restoring the queue's sequence entries to that template's persisted contents. Because reloading can discard edits made since the template was loaded, the operator is asked to confirm — but only when the reload would actually change the queue's entries; if there is nothing to discard (the queue is empty or its entries already match the template), the reload proceeds without a prompt.

**Why this priority**: Reload is the one genuinely new action in this refinement. It builds on the existing save/load round trip (the queue must already be associated with a template name) and is a convenience over re-opening the Load Template picker, so it ranks below the layout and the consolidated template row.

**Independent Test**: Load a template into a queue, edit its entries, click Reload Template, confirm the prompt, and verify the queue's entries are restored to the template's persisted contents.

**Acceptance Scenarios**:

1. **Given** the queue is associated with a template name (loaded or saved this session) and its entries have been edited away from the template, **When** the operator clicks Reload Template and confirms, **Then** the queue's entries are replaced with the named template's current persisted entries.
2. **Given** the reload confirmation is shown, **When** the operator cancels, **Then** the queue's current entries are left unchanged and nothing is reloaded.
3. **Given** the queue is associated with a template and its entries already match the template (or the queue is empty), **When** the operator clicks Reload Template, **Then** the reload proceeds without a confirmation prompt (there is nothing to discard).
4. **Given** no template is associated with the queue (no template name yet), **When** the operator views row 2, **Then** Reload Template is disabled (there is nothing to reload).
5. **Given** the queue is in the "running" state, **When** the operator views row 2, **Then** Reload Template is disabled (reloading is a bulk replacement of all entries), consistent with the existing block on loading into a running queue.
6. **Given** the associated template no longer exists (it was deleted or renamed since), **When** the operator triggers a reload, **Then** the operator is told the template is no longer available and the queue's entries are left unchanged.

---

### Edge Cases

- **No template associated yet**: The template-name button shows a neutral placeholder, Reload Template is disabled, and Save/Load still work (Save lets the operator name a new template; Load opens the picker).
- **Running queue**: Loading and reloading are disabled while the queue is running (consistent with the existing block); saving a template from a running queue remains allowed. The name field and cycle-execution editing follow the existing running-queue rules from the queue feature.
- **Switching inline sections**: Opening one inline template section closes the other; only one is open at a time, and they always open in the same place (between row 2 and row 3).
- **Reload target missing**: If the associated template was deleted or renamed, triggering a reload reports that the template is unavailable and changes nothing.
- **Reload with nothing to discard**: If the queue is empty or its entries already match the associated template, Reload proceeds without a confirmation prompt.
- **Cancel on the edit page**: Cancel (row 5) discards in-progress edits to the queue's name and cycle-execution setting and closes the edit page. It does not affect the queue's sequence entries: add/remove/load/reload act on the queue immediately and independently of the page-level Save/Cancel, because a queue's sequence entries are runtime-only (not persisted with the queue) and Save/Cancel govern only the persisted name and cycle-execution fields.
- **Empty sequences**: When the queue has no sequence entries, row 4 shows the existing empty state for sequences; the layout order is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

#### Layout & ordering

- **FR-001**: The edit queue page MUST arrange its controls as ordered rows, top to bottom: (1) name + emulator, (2) template controls, (3) cycle execution, (4) sequence entries, (5) Save/Cancel actions.
- **FR-002**: Row 1 MUST present the queue name as an editable field and the bound emulator as read-only.
- **FR-003**: The edit page MUST NOT display explanatory text stating that the bound emulator cannot be changed/exchanged after creation.
- **FR-004**: Row 5 MUST present the edit page's Save and Cancel actions. Save persists the queue's name and cycle-execution changes; Cancel discards in-progress edits to those fields and closes the edit page. Save/Cancel MUST govern only the name and cycle-execution fields — they MUST NOT gate or revert changes to the queue's sequence entries (which are runtime-only and not persisted with the queue).
- **FR-004a**: Actions on the queue's sequence entries — add, remove, load (template), and reload (template) — MUST apply to the queue immediately and independently of the page-level Save/Cancel.
- **FR-005**: Row 3 MUST present the cycle-execution setting, and row 4 MUST present the queue's sequence entries (one row per entry, as many rows as there are entries), preserving the existing add and remove behavior.

#### Template controls row

- **FR-006**: Row 2 MUST present, in a single row: the current template name (rendered as a clickable button), a Save Template action, and a Reload Template action.
- **FR-007**: The template-name button MUST open the Load Template section when clicked.
- **FR-008**: When no template is associated with the queue in the current editing session, the template-name button MUST show a neutral placeholder and MUST still open the Load Template section when clicked.
- **FR-009**: The Save Template and Load Template sections MUST appear inline between row 2 and row 3 when opened.
- **FR-010**: The Save Template and Load Template sections MUST be collapsed (closed) by default when the edit page opens.
- **FR-011**: At most one inline template section (Save or Load) MUST be open at a time; opening one MUST close the other.
- **FR-012**: An inline template section MUST collapse after the operator completes or dismisses it, returning row 2 to its default closed state.
- **FR-013**: The Save Template and Load Template sections MUST preserve the existing save and load behaviors from the Queue Templates feature (name validation, overwrite confirmation, template picker with per-template delete, empty state, replace-on-load confirmation, and the block on loading into a running queue).

#### Reload Template

- **FR-014**: The Reload Template action MUST re-apply the template the queue is currently associated with (the one most recently loaded or saved this session), replacing the queue's current sequence entries with that template's current persisted entries.
- **FR-015**: The system MUST ask the operator to confirm a reload before replacing the queue's entries ONLY when the reload would change the queue's entries (i.e., the current entries differ from the template's). When there is nothing to discard — the queue is empty or its entries already match the template — the reload MUST proceed without a confirmation prompt. When a confirmation is shown and the operator cancels, the queue's entries MUST be left unchanged.
- **FR-016**: Reload Template MUST be disabled when no template is associated with the queue (nothing to reload).
- **FR-017**: Reload Template MUST be disabled while the queue is in the "running" state, consistent with the existing block on loading into a running queue.
- **FR-018**: When a reload targets a template that no longer exists, the system MUST inform the operator that the template is unavailable and MUST leave the queue's entries unchanged.

### Key Entities *(include if feature involves data)*

This feature reorganizes the presentation of the existing edit queue page; it introduces no new persisted entities. It operates on the existing **Queue**, **Queue Entry**, and **Queue Template** entities defined by the Emulator Execution Queue and Queue Templates features.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On opening the edit queue page, 100% of the time the controls render in the order: name + emulator, template controls, cycle execution, sequences, Save/Cancel.
- **SC-002**: An operator can locate and trigger Save Template, Load Template (via the template-name button), and Reload Template from row 2 without scrolling past row 4 or opening any separate top-level area.
- **SC-003**: The Save and Load inline sections are closed on every fresh open of the edit page, and only one is ever open at a time, in 100% of interactions.
- **SC-004**: 100% of reloads that would change the queue's entries present a confirmation the operator can cancel (and canceling leaves the entries unchanged), while reloads with nothing to discard proceed without a prompt.
- **SC-005**: After a confirmed reload, the queue's entries match the associated template's persisted entries 100% of the time (when the template still exists).
- **SC-006**: The bound-emulator "cannot be changed" explanatory text is absent from the edit page in 100% of renders.

## Assumptions

- **Refinement, not rebuild**: This changes only the arrangement and the addition of Reload Template on the existing edit queue page. The underlying queue editing, template save/load/delete, validation, and running-queue rules from the Emulator Execution Queue (046) and Queue Templates (047) features are unchanged unless stated here.
- **Inline sections replace modals**: The Save Template and Load Template experiences, previously surfaced as dialogs, are now inline collapsible sections opening between row 2 and row 3. Their internal behavior (fields, validation, picker, per-template delete, confirmations) is preserved.
- **"Associated template"**: The queue is "associated" with a template name when it was loaded from a template or saved as one during the current editing session (the same UI-state association the Queue Templates feature already tracks for the save dialog's pre-filled name). It is not a persisted live link.
- **Reload semantics**: Reload re-fetches the associated template's current persisted entries by name and fully replaces the queue's entries — effectively "load again." It is guarded by confirmation only when it would actually discard changes (current entries differ from the template); when the queue is empty or already matches the template, it proceeds silently.
- **Entries independent of Save/Cancel**: The queue's sequence entries are runtime-only and not persisted with the queue, so all entry actions (add, remove, load, reload) apply to the queue immediately and are unrelated to the page-level Save/Cancel, which commit or discard only the name and cycle-execution fields.
- **Mutual exclusivity of sections**: Only one inline template section is open at a time to keep row 2's footprint predictable; opening the other closes the first.
- **Running-queue consistency**: Reload follows the same running-queue restriction as Load (blocked while running). Saving remains allowed while running.
- **Confirmation pattern**: Reload and existing replace/overwrite/delete confirmations use the app's established confirmation-prompt convention.

## Out of Scope

- Any change to template persistence, the template data model, or the template API beyond what already exists.
- Changing the queue's bound emulator after creation (still immutable; only the explanatory text is removed).
- Reordering or redesigning the Queues list/table view; this refinement targets the edit queue page only.
- A dedicated template editor or any new template management surface outside the edit page.
- Changes to start/stop execution behavior or to the cycle-execution flag's (still deferred) runtime semantics.
- Persisting the queue's sequence entries or the associated-template UI state across service restarts.
