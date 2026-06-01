# Feature Specification: Queue–Template Link with Auto-Load

**Feature Branch**: `049-queue-template-link`
**Created**: 2026-06-01
**Status**: Draft
**Input**: User description: "I want the queues to be linked to 0 or 1 templates, that should be loaded automatically when the queue is displayed in the UI. The relation is 0..1:0:n, meaning a queue can have 0 or 1 templates linked and a template can be linked to 0 to n queues. No visual changes are expected here, but the UI behaviour when opening the queue."

## Clarifications

### Session 2026-06-01

- Q: How is a queue's linked template established and cleared given "no visual changes"? → A: Reuse the existing load/save flow — loading a template into the queue, or saving the queue's entries to a template, sets that template as the queue's persisted link (replacing the 047/048 UI-only "remembered name"). No explicit unlink control this iteration; a queue is unlinked only if it was never linked or its link was auto-cleared (see broken-link answer).
- Q: When a linked queue is opened during a session where it already holds runtime entries (e.g. edited away from the template, no restart), should auto-load run? → A: No — auto-load populates entries only on the first display after a service start (when the queue has not yet been materialized this lifetime). Once materialized or edited, its entries are kept and never silently discarded — including a deliberately emptied queue, which is not re-filled; the manual Reload (048) remains the way to force a re-apply.
- Q: What happens on open when the linked template can no longer be resolved (deleted, or renamed if links are name-based)? → A: Auto-load does nothing and the now-broken link is cleared, so the queue becomes unlinked going forward; the queue still opens normally without error.
- Q: How does the link reference its template — by stable identity or by name? → A: By the template's stable identity (ID). Renaming the template keeps the link intact; only deleting the template breaks the link.
- Q: Does auto-load populate the queue's server-side runtime entries or only the client editor view? → A: The server-side runtime entries — auto-load writes the template's entries into the queue via the existing replace-entries operation, so both the editor and execution see them. Auto-clearing a broken link is likewise persisted.
- Q: Which UI surface triggers auto-load? → A: Only the queue's edit/detail page (the queue editor). Passive views such as the queue list do not trigger auto-load.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Linked template loads automatically when opening a queue (Priority: P1)

An operator who previously associated a queue with a template opens that queue in the editor. Without taking any manual "load" action, the queue's sequence entries are populated from its linked template, so the arrangement the operator expects is already there. This is especially valuable after a service restart, when the queue's runtime entries would otherwise be empty: the link gives the queue durable, restored contents derived from the template.

**Why this priority**: This is the core of the feature — the behavior the operator asked for ("loaded automatically when the queue is displayed"). It delivers the central value (queues regain their expected entries on open without a manual load step) and is independently demonstrable.

**Independent Test**: Link a queue to a template containing entries A, B, C; restart the service; open the queue in the UI and confirm its entries are A, B, C in order without any manual load action.

**Acceptance Scenarios**:

1. **Given** a queue linked to template "Daily Farm" (entries A, B, C) and a freshly started service with no runtime entries for that queue, **When** the operator opens the queue in the editor, **Then** the queue shows entries A, B, C in the template's order, loaded automatically.
2. **Given** a queue that is not linked to any template, **When** the operator opens the queue, **Then** no auto-load occurs and the queue's entries are exactly its current runtime entries (empty after a restart), with no error.
3. **Given** a queue linked to a template, **When** the operator opens the queue and the entries auto-load, **Then** the operator can immediately edit those entries (add, remove, reorder) as usual.

---

### User Story 2 - Associate a queue with a template (and replace or clear the association) (Priority: P1)

While editing a queue, an operator makes a template the queue's linked template so that future opens auto-load it. The association is persisted (it survives a service restart) and is a property of the queue, not of the template. The same template can be the linked template of many queues at once. A queue has at most one linked template; associating a different template replaces the prior link.

**Why this priority**: Without a way to establish the link, US1 has nothing to auto-load. Together with US1 it forms the minimum viable feature.

**Independent Test**: Associate queue Q1 with template T, restart the service, and confirm Q1 is still linked to T (US1 auto-loads T into Q1); associate the same template T with a second queue Q2 and confirm both Q1 and Q2 are independently linked to T.

**Acceptance Scenarios**:

1. **Given** a queue with no linked template, **When** the operator associates it with template T, **Then** the queue's linked template becomes T and remains T after a service restart.
2. **Given** a queue already linked to template T, **When** the operator associates it with a different template U, **Then** the queue's linked template becomes U (the prior link to T is replaced), and on next open U is auto-loaded.
3. **Given** template T already linked to queue Q1, **When** the operator associates T with queue Q2 as well, **Then** both Q1 and Q2 are linked to T independently, and T is unchanged.
4. **Given** a queue with no linked template, **When** the operator loads a template into it (or saves its entries to a template), **Then** that template becomes the queue's persisted linked template, with no new visual control involved.
5. **Given** a queue whose linked template was auto-cleared because it became unavailable, **When** the operator later loads a template into the queue, **Then** the queue is linked to the newly loaded template.

---

### Edge Cases

- **Linked template no longer exists**: The linked template was deleted before the queue is opened (the link is by stable ID, so a rename does not break it). On auto-load the queue loads nothing, opens normally without error, and the now-broken link is cleared so the queue becomes unlinked going forward.
- **Queue already materialized this session when opened**: During a single session (no restart), the operator re-opens a linked queue that has already been materialized — whether it still holds runtime entries it edited away from the template, or it was deliberately emptied. Auto-load does not run again — the current entries (including an empty list) are kept and never overwritten. The operator can use the manual Reload (048) to force re-applying the template.
- **Linked template is empty**: A queue linked to an empty template (zero entries) opens with zero entries; auto-load of an empty template is a valid no-content load, not an error.
- **Stale entry inside the linked template**: A template entry whose referenced sequence was deleted is auto-loaded as a stale/unresolved reference (consistent with existing queue stale-entry handling), not silently dropped.
- **Linked template changes between opens**: If the linked template's contents are saved/overwritten elsewhere, the next auto-load reflects the template's latest saved contents (the link resolves to the template, not to a frozen copy).
- **Auto-load while the queue is running**: Opening a running queue must not disrupt its execution. Auto-load follows the existing rule that bulk entry replacement is blocked while a queue is running; for a running queue, opening shows the current running entries and does not replace them.
- **Loading vs. linking divergence**: After auto-load, the operator edits the queue's entries without saving them back to the template; the link still points at the template, and the in-queue edits remain runtime-only (non-persistent) unless saved to the (or a) template.

## Requirements *(mandatory)*

### Functional Requirements

#### The link

- **FR-001**: A queue MUST be able to reference at most one template as its "linked template" (0 or 1), and a template MUST be allowed to be the linked template of any number of queues (0..n). The link is a property of the queue.
- **FR-001a**: The link MUST reference the template by its stable identity (ID), not by name; renaming the linked template MUST NOT break the link, and only deleting the template makes the link unresolvable.
- **FR-002**: The queue→template link MUST be persisted so that it survives service restarts (the queue's runtime sequence entries remain non-persistent, as today; only the link is persisted).
- **FR-003**: Associating a queue with a template MUST replace any prior linked template for that queue (a queue never has more than one linked template).
- **FR-004**: The link MUST be independent per queue: associating a template with one queue MUST NOT change any other queue's link, and MUST NOT modify the template itself.
- **FR-005**: Establishing or changing a queue's linked template MUST occur as a side effect of the existing load/save template actions (loading a template, or saving the queue's entries to a template, sets that template as the link) without introducing new visual controls or layout changes (behavioral change only).
- **FR-005a**: This iteration MUST NOT add an explicit "unlink" control; a queue's link is cleared only automatically when its linked template becomes unresolvable (FR-011).

#### Auto-load on display

- **FR-006**: When a queue with a linked template is opened on its edit/detail page (the queue editor), the system MUST automatically populate the queue's sequence entries from the linked template, preserving the template's order, without requiring a manual load action. Passive views (e.g. the queue list) MUST NOT trigger auto-load.
- **FR-006a**: Auto-load MUST write the template's entries into the queue's server-side runtime entries (reusing the existing entry-replacement logic that backs the load/replace flow), so that both the editor view and queue execution use the auto-loaded entries; it MUST NOT be a client-only view population. Likewise, auto-clearing a broken link (FR-011) MUST persist the cleared link.
- **FR-007**: When a queue with no linked template is displayed, the system MUST NOT perform any auto-load and MUST show the queue's current runtime entries.
- **FR-008**: Independent of *where* entries are written (FR-006a), auto-load MUST be a **copy** (consistent with existing load semantics): subsequent in-queue edits MUST NOT modify the template, and later template changes MUST NOT retroactively modify an already-materialized queue (the live link only re-resolves on the next first-display, never mid-session).
- **FR-009**: Auto-load MUST reproduce a template entry whose referenced sequence no longer exists as a stale/unresolved reference, consistent with existing queue stale-entry handling.
- **FR-010**: Auto-load MUST NOT replace the entries of a queue that is in the running state; for a running queue, display MUST show the current running entries unchanged.
- **FR-011**: If the linked template cannot be resolved (deleted or otherwise unavailable), auto-load MUST NOT block or error the display of the queue, MUST load nothing, and MUST clear the queue's now-broken link so the queue becomes unlinked going forward.
- **FR-012**: Auto-load MUST run only on the first display of a queue after a service start — that is, when the queue's runtime entries have not yet been materialized this service lifetime (no runtime state exists for it). Once a queue has been materialized or edited this session, auto-load MUST NOT run again, MUST leave its entries unchanged, and MUST NOT re-fill a queue the operator has deliberately emptied. (Operationally: the trigger is "no runtime state yet", not merely "zero entries", so clearing a queue's entries does not cause an immediate re-load.)

#### Relationship to existing template behavior (047/048)

- **FR-013**: This feature MUST reuse the existing save/load/delete template behavior (047) and the existing reload behavior (048); auto-load is the automatic counterpart of the manual reload, applied when the queue is displayed.
- **FR-014**: Deleting a template MUST NOT alter any queue's current runtime entries; the effect of deletion on linked queues is limited to the linked template becoming unresolved (per FR-011).
- **FR-015**: A template MUST remain independent of the queues that link to it: it stores only its own ordered sequence entries and does not record which queues link to it (the link lives on the queue side).

### Key Entities *(include if feature involves data)*

- **Queue**: The existing emulator execution queue. Gains one new persisted attribute: an optional reference to a single linked template (0 or 1). Its sequence entries remain runtime-only (non-persistent); the link does not persist the entries, only the association used to derive them on display.
- **Queue Template**: Unchanged from 047 — a named, persisted, ordered list of sequence entries, independent of any queue. It may be the linked template of 0..n queues but holds no back-reference to them.
- **Queue–Template Link**: The association from a queue to at most one template, referencing the template by its stable identity (ID). Persisted, owned by the queue, replaceable, and clearable. Resolves to the template's current saved contents at display time.
- **Sequence**: An existing authored command sequence referenced by template/queue entries; unchanged by this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After linking a queue to a template and restarting the service, 100% of the time opening that queue shows the template's exact ordered entries with no manual load action, and the queue is immediately runnable with those entries (execution uses the auto-loaded server-side entries).
- **SC-002**: A queue with no linked template opens with no auto-load and no error in 100% of cases.
- **SC-003**: Associating a different template with a queue replaces the prior link in 100% of cases, and the next open auto-loads the newly linked template.
- **SC-004**: The same template can serve as the linked template for multiple queues simultaneously, and editing one linked queue's entries leaves the template and the other linked queues unchanged in 100% of cases.
- **SC-005**: Opening a queue with auto-load reflects the queue's entries within 1 second at the established scale (up to ~50 templates each with up to ~100 entries).
- **SC-006**: Opening a queue whose linked template is unavailable never blocks or errors the queue's display (100% of attempts succeed in showing the queue).

## Assumptions

- **Link establishment reuses existing actions**: Establishing/changing the link reuses the existing load/save template flow rather than adding new UI controls (consistent with "no visual changes"); the persisted link replaces the UI-only "remembered template name" introduced in 047/048 (confirmed in Clarifications).
- **Entries remain non-persistent**: As in 047/048, a queue's sequence entries are runtime-only. The link is the durable thing; entries are derived from the linked template on display. This is how queues effectively "remember" their contents across restarts now.
- **Link resolves live**: The link points at the template (by identity), so auto-load always reflects the template's latest saved contents rather than a snapshot taken when the link was created.
- **Running-queue safety**: Auto-load honors the existing rule that bulk entry replacement is blocked while a queue is running.
- **Single-operator tool**: Concurrency follows the surrounding features' last-write-wins assumptions; no multi-user locking.
- **Scale**: Up to ~50 templates each with up to ~100 entries, consistent with 046/047.

## Out of Scope

- Any visual redesign or new on-screen controls in the queue editor (this iteration changes behavior only).
- Persisting a queue's runtime sequence entries directly (entries remain derived from the linked template / runtime edits).
- A back-reference from a template to the queues that link to it, or cascade behavior when a template is deleted beyond leaving the link unresolved.
- Multiple linked templates per queue, ordering/merging of several templates, or partial loads.
- Versioning/history of links or templates; renaming/duplicating/exporting templates (covered by/excluded in 047).
