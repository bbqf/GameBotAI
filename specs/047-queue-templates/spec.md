# Feature Specification: Queue Templates

**Feature Branch**: `047-queue-templates`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "I want you to create \"queue templates\" - this is the missing persistence of the queue elements. When I create a template in the UI, I want to be able to persist the queue elements, but unbound from the queue itself, so that I can share the template among different queues. Saving and loading of the template should happen in the UI whenever I edit the queue. I also want to be able to delete the templates, think of a appropriate solution in the UI, where to put this function. I don't want an explicit editing of a template, if needed, I will load a template into the queue, edit the sequence steps there and save it, overwriting whatever is persisted - ask me if I am sure overwriting."

## Clarifications

### Session 2026-05-31

- Q: When loading a template into a queue that already has sequence entries, what happens to the existing entries? → A: Replace the queue's current entries with the template's; if the queue is non-empty, ask the operator to confirm the replacement first.
- Q: Where should the "delete template" control live in the UI? → A: In the template load picker — the chooser that lists templates (opened from the queue editor) shows a delete affordance per template, with a confirmation prompt.
- Q: Is loading a template allowed while the target queue is in the "running" state? → A: No — loading (a bulk replacement of all entries) is blocked while the queue is running; the queue must be stopped first. Saving a template from a running queue remains allowed.
- Q: When saving, should the editor remember the name of the template the queue was loaded from? → A: Yes — the editor remembers the loaded template's name and pre-fills it in the save dialog as a convenience default; the operator may change it. This is UI-state only and does not create a live data link (FR-015 still holds).
- Q: Is template-name uniqueness case-sensitive? → A: No — uniqueness is case-insensitive (names differing only in case are the same template and trigger the overwrite prompt); the casing the operator entered is preserved for display.
- Q: Should template save/load/delete actions be logged? → A: No — no logging of template actions this iteration, consistent with the queue feature logging execution (start/stop) only.
- Q: What are the template name constraints? → A: Trimmed of surrounding whitespace and non-blank after trimming; restricted to letters, digits, spaces, hyphen (-), and underscore (_); other characters are rejected with a message; maximum 100 characters.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Save the current queue's entries as a reusable template (Priority: P1)

While editing a queue, an operator who has arranged an ordered list of sequence entries wants to keep that arrangement so it survives service restarts and can be reused. From the queue editor they save the queue's current entries as a named template. The template captures only the ordered sequence entries — not the queue's name, bound emulator, or cycle-execution setting — so it is independent of any single queue. If a template with the chosen name already exists, the operator is asked to confirm before it is overwritten.

**Why this priority**: This is the core of the feature — "the missing persistence of the queue elements." Without the ability to save, there is nothing to load or delete. It delivers immediate value on its own: an arrangement of sequence entries that would otherwise be lost on restart becomes durable.

**Independent Test**: In the queue editor, arrange two or more sequence entries, save them as a template named "Daily Farm", restart the service, and confirm the template still exists and contains the same ordered entries — even though the queue's own (non-persistent) entries are gone.

**Acceptance Scenarios**:

1. **Given** a queue being edited with sequence entries A, B, C in order, **When** the operator saves them as a template named "Daily Farm", **Then** a template "Daily Farm" exists containing entries A, B, C in that order and is not tied to the queue it was saved from.
2. **Given** a template named "Daily Farm" already exists, **When** the operator saves the current queue's entries under the name "Daily Farm", **Then** the operator is asked to confirm overwriting, and only on confirmation is the existing template replaced with the current entries.
3. **Given** the overwrite confirmation is shown, **When** the operator cancels, **Then** the existing template is left unchanged and nothing is saved.
4. **Given** a saved template, **When** the service is restarted, **Then** the template and its ordered entries are still available.

---

### User Story 2 - Load a template into a queue (Priority: P1)

While editing any queue, an operator picks a previously saved template and loads its ordered sequence entries into the queue they are editing. The same template can be loaded into different queues, letting the operator share one arrangement across many queues. Because loading replaces the queue's current entries, if the queue already has entries the operator is asked to confirm the replacement first. After loading, the operator may freely edit the entries in the queue (add, remove, reorder as the queue editor allows) and optionally save them again as a template (US1).

**Why this priority**: Loading is the other half of the round trip that makes templates useful and is what enables sharing an arrangement across queues. Together with US1 it forms the minimum viable feature.

**Independent Test**: Save a template from one queue, open a different queue, load the template, and confirm the second queue now contains the template's ordered entries.

**Acceptance Scenarios**:

1. **Given** a saved template "Daily Farm" with entries A, B, C and an empty queue being edited, **When** the operator loads "Daily Farm", **Then** the queue's entries become A, B, C in that order.
2. **Given** a queue being edited that already contains entries X, Y, **When** the operator loads a template, **Then** the operator is asked to confirm replacing the existing entries, and only on confirmation are X, Y replaced by the template's entries.
3. **Given** the replacement confirmation is shown, **When** the operator cancels, **Then** the queue keeps its existing entries X, Y and nothing is loaded.
4. **Given** a template loaded into a queue, **When** the operator edits the queue's entries and saves them under the same template name (confirming the overwrite), **Then** the template reflects the edited entries — this is the only supported way to "edit" a template.
5. **Given** the same template, **When** the operator loads it into two different queues, **Then** both queues independently contain the template's entries, and later edits to one queue's entries do not change the template or the other queue.

---

### User Story 3 - Delete a template (Priority: P2)

An operator who no longer needs a saved template removes it. The delete control lives in the same template picker used to load templates (reachable from the queue editor): each listed template offers a delete action, and the operator is asked to confirm before the template is removed. Deleting a template never changes any queue's current entries — it only removes the saved, shareable arrangement.

**Why this priority**: Housekeeping that keeps the template list manageable. It depends on templates existing (US1) but is not required for the core save/load round trip, so it ranks below US1/US2.

**Independent Test**: Save a template, open the template picker, delete the template, confirm the deletion, and verify it no longer appears in the picker and is gone after a restart.

**Acceptance Scenarios**:

1. **Given** one or more saved templates listed in the template picker, **When** the operator chooses delete on a template and confirms, **Then** that template is removed and no longer appears in the list.
2. **Given** the delete confirmation is shown, **When** the operator cancels, **Then** the template is retained.
3. **Given** a template currently loaded into a queue being edited, **When** the operator deletes that template, **Then** the queue's already-loaded entries are unaffected (deletion removes only the saved template).
4. **Given** a deleted template, **When** the service is restarted, **Then** the template does not reappear.

---

### Edge Cases

- **No templates saved yet**: When the operator opens the template picker and no templates exist, the picker communicates that there are no templates to load or delete (empty state), and there is nothing to load.
- **Saving with an empty queue**: Saving a template when the queue has no entries produces an empty template (zero entries); loading it later replaces the target queue's entries with an empty list. (See Assumptions.)
- **Loading into a non-empty queue**: The operator is warned that the current entries will be replaced and must confirm before the replacement happens.
- **Loading into a running queue**: Loading is blocked while the queue is running; the operator is told to stop the queue first. (Saving from a running queue is still allowed.)
- **Overwriting an existing template**: Saving under a name that already exists prompts for confirmation; canceling leaves the existing template untouched.
- **Empty or blank template name**: Saving requires a non-blank name (after trimming whitespace); the system prevents saving and indicates a name is required.
- **Invalid template name**: A name containing characters outside letters/digits/spaces/`-`/`_`, or longer than 100 characters, is rejected with a message identifying the constraint.
- **Stale sequence reference inside a template**: If a sequence referenced by a template entry has been deleted from the sequence store, the template entry is retained and presented as a stale/unresolved reference once loaded into a queue (consistent with how queues flag stale entries today); the operator can remove it manually.
- **Concurrent edits to the same template name**: If two save operations target the same template name, the last confirmed save wins (the template reflects the most recently saved entries). (See Assumptions.)
- **Deleting a template that another queue has loaded**: Allowed; the other queue's already-loaded entries are not affected because loading copies the arrangement rather than linking to the template.

## Requirements *(mandatory)*

### Functional Requirements

#### Template definition & persistence

- **FR-001**: System MUST provide a "queue template" as a named, persisted, ordered list of sequence entries that is independent of (not bound to) any queue, emulator, or cycle-execution setting.
- **FR-002**: System MUST persist queue templates so that they survive service restarts (unlike queue entries themselves, which are not persisted).
- **FR-003**: A template MUST capture only the queue's ordered sequence entries; it MUST NOT capture the queue's name, bound emulator, execution status, or cycle-execution setting.
- **FR-004**: System MUST identify a template by a human-readable name that is unique across all templates; a saved name can be reused to overwrite the existing template of that name.
- **FR-004a**: Template-name uniqueness MUST be case-insensitive (names differing only in letter case refer to the same template and trigger the overwrite prompt). The system MUST preserve and display the casing the operator entered.
- **FR-005**: System MUST preserve the order of sequence entries within a template, matching the order they had in the queue when saved, and reproduce that order when loaded.
- **FR-006**: System MUST allow the same sequence to appear multiple times within a template (mirroring queue entry behavior).

#### Saving templates

- **FR-007**: System MUST allow an operator, while editing a queue, to save the queue's current ordered sequence entries as a template under a name the operator provides.
- **FR-007a**: When the queue was populated by loading a template earlier in the editing session, the save dialog MUST pre-fill that template's name as a convenience default (the operator may change it). This is editor UI state only and does not create a live link between the queue and the template (see FR-015).
- **FR-008**: System MUST reject saving a template with a blank or missing name, indicating that a name is required. The name MUST be trimmed of surrounding whitespace and is considered blank if empty after trimming.
- **FR-008a**: System MUST restrict template names to letters, digits, spaces, hyphen (`-`), and underscore (`_`), and MUST reject names containing other characters with a message identifying the constraint.
- **FR-008b**: System MUST reject template names longer than 100 characters (measured after trimming) with a message indicating the limit.
- **FR-009**: When the chosen template name already exists, the system MUST ask the operator to confirm overwriting before replacing the existing template, and MUST leave the existing template unchanged if the operator cancels.
- **FR-010**: On a confirmed overwrite, the system MUST replace the named template's entries with the queue's current entries (full replacement, not a merge).
- **FR-011**: System MUST allow saving a template from a queue that has zero entries, producing an empty template. (See Assumptions.)

#### Loading templates

- **FR-012**: System MUST allow an operator, while editing a queue, to load a saved template's ordered sequence entries into the queue being edited.
- **FR-013**: Loading a template MUST replace the queue's current entries with a copy of the template's entries (full replacement), preserving order.
- **FR-014**: When the queue being edited already contains one or more entries, the system MUST ask the operator to confirm the replacement before loading, and MUST leave the queue's entries unchanged if the operator cancels.
- **FR-014a**: System MUST block loading a template into a queue that is in the "running" state (loading is a bulk replacement of all entries); the operator must stop the queue first, and the system MUST indicate that the queue must be stopped. Saving a template from a running queue remains allowed.
- **FR-015**: Loading MUST copy the template's entries into the queue such that subsequent edits to the queue's entries do not modify the template, and subsequent changes to the template do not modify the previously loaded queue (no live link between a queue and the template it was loaded from).
- **FR-016**: System MUST allow the same template to be loaded into multiple different queues independently.
- **FR-017**: A template entry whose referenced sequence no longer exists MUST be loaded into the queue as a stale/unresolved reference (consistent with existing queue stale-entry handling) rather than being silently dropped.

#### Deleting templates

- **FR-018**: System MUST allow an operator to delete a saved template.
- **FR-019**: System MUST ask the operator to confirm before deleting a template, and MUST retain the template if the operator cancels.
- **FR-020**: Deleting a template MUST NOT alter the current entries of any queue, including a queue that had previously loaded that template.

#### UI

- **FR-021**: System MUST expose save-template and load-template actions within the queue editing experience (where the operator edits a queue's sequence entries), not as a separate top-level area.
- **FR-022**: System MUST present a template picker (reachable from the queue editor when loading) that lists the available templates by name for the operator to choose from.
- **FR-023**: System MUST place the delete-template control within that template picker, offering a delete action per listed template, each guarded by a confirmation prompt.
- **FR-024**: System MUST communicate an empty state in the template picker when no templates exist.
- **FR-025**: System MUST NOT provide a separate, explicit template editor; the only supported way to change a template's contents is to load it into a queue, edit the queue's entries, and save back over the template (confirming the overwrite).

### Key Entities *(include if feature involves data)*

- **Queue Template**: A named, persisted, ordered collection of sequence entries, independent of any queue. Attributes: identity, unique name, ordered list of template entries, and timestamps (created/updated). It does not reference an emulator or carry execution status or cycle-execution settings.
- **Template Entry**: A reference to a sequence with a position determining order within the template. Mirrors a queue entry but exists as part of a persisted template rather than a runtime queue. May become a stale/unresolved reference if its sequence is deleted.
- **Queue**: The existing emulator execution queue (from the Emulator Execution Queue feature) whose runtime sequence entries are the source for saving a template and the destination for loading one. Unchanged by this feature except for the new save/load/replace interactions on its entries.
- **Sequence**: An existing authored command sequence referenced by template entries. This feature does not modify sequences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

> Note: SC-002 expresses the persistence requirement (FR-002) as a concrete pass/fail acceptance test rather than a separate requirement.

- **SC-001**: An operator can save the current queue's entries as a named template and load them into a different queue entirely from within the queue editor, without visiting any other tab, in under 30 seconds.
- **SC-002**: 100% of saved templates retain their name and exact ordered entries after a service restart.
- **SC-003**: Loading a template reproduces the template's sequence entries in the same order 100% of the time.
- **SC-004**: 100% of overwrite-save attempts and 100% of load-into-non-empty-queue attempts present a confirmation the operator can cancel, and canceling leaves the prior state unchanged.
- **SC-005**: After deleting a template and confirming, the template is absent from the picker immediately and remains absent after a restart in 100% of cases, with no change to any queue's current entries.
- **SC-006**: Edits made to a queue after loading a template do not alter the template, and edits to a template do not alter previously loaded queues, in 100% of cases (templates and loaded queues are independent copies).
- **SC-007**: The template picker (list, load, delete) remains responsive — interactions reflected within 1 second — at the target scale of up to ~50 templates each with up to ~100 entries.

## Assumptions

- **Source of truth for the "queue elements"**: The "queue elements" a template persists are the queue's ordered sequence entries as defined by the Emulator Execution Queue feature (the in-memory, non-persistent content). Templates provide the durable, shareable persistence those entries otherwise lack.
- **Template scope**: A template stores only the ordered sequence entries. It deliberately excludes emulator binding, cycle-execution flag, execution status, and queue name, so a single template can be loaded into queues bound to different emulators.
- **Unique names, overwrite by name**: Templates are addressed by a unique name. Saving under an existing name overwrites that template after confirmation; there is no separate "rename" or in-place template editor (FR-025).
- **Load is a copy, not a link**: Loading copies entries into the queue. There is no ongoing relationship between a queue and the template it was loaded from, so later changes on either side do not propagate.
- **Empty templates allowed**: Saving from a queue with no entries yields a valid empty template; loading it replaces the target queue's entries with an empty list. This keeps the save action unconditional and predictable.
- **Stale references**: Template entries follow the same stale/unresolved-reference convention as queue entries when a referenced sequence is deleted; the feature does not validate or repair references at save time.
- **Concurrency**: Single-operator desktop tool; concurrent saves to the same name resolve last-write-wins. No multi-user locking is required.
- **Scale**: Up to ~50 templates, each with up to ~100 entries, consistent with the surrounding queue feature's operator scale.
- **UI conventions**: Save/load/delete affordances and confirmation prompts follow the existing authoring UI conventions used elsewhere in the app (e.g., the existing delete-confirmation pattern), and live within the queue editing experience rather than a new top-level tab.

## Out of Scope

- A dedicated, explicit template editor (creating or editing template contents outside of loading into a queue and saving back).
- Persisting queues' runtime sequence entries or execution status themselves (this feature persists templates, not the live queue contents — that remains non-persistent by the queue feature's design).
- Capturing emulator binding, cycle-execution, or execution status within a template.
- Renaming a template in place, duplicating/exporting/importing templates, folders/tags/categorization, or sharing templates between machines/users.
- Versioning or history of template changes; only the latest saved state of each template is kept.
- Logging or audit trail of template save/load/delete actions (consistent with the queue feature, which logs execution status changes only).
- Live linkage between a queue and a template (syncing edits in either direction after a load).
- Reordering capabilities beyond those the queue editor already provides for queue entries.
