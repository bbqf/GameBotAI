# Feature Specification: Emulator Execution Queue

**Feature Branch**: `046-emulator-execution-queue`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "I need a completely new execution object: emulator execution queue or queue. There should be an API and the UI for the new queues. In the UI there should be a new tab Queues, showing a list of queues as well as standard CRUD operations. Every queue is bound exactly to one emulator at creation time. A queue should have a name and checkbox attribute: cycle execution. The queue entries in the queue are sequences. Adding objects to the queue by default places them at the end of the queue. From the list of queues I should see queue execution status, as well as be able to start execution and stop execution (both placeholder for now, just change status to running). The content - list of sequence of the queue should not be persistent across service restarts, however the rest of the queue configuration should be persisted."

## Clarifications

### Session 2026-05-31

- Q: Can more than one queue be bound to the same emulator? → A: Allow multiple — no uniqueness constraint on the emulator binding.
- Q: How should the emulator a queue binds to be identified and selected at creation time? → A: Pick from the currently connected ADB devices (by serial); the serial is persisted as the binding.
- Q: What does the "cycle execution" checkbox mean (stored now, enforced when real execution is built)? → A: A stored flag only; its runtime semantics are intentionally deferred (TBD) until the real execution engine is specified.
- Q: While a queue is running, which configuration changes are allowed? → A: Allow content edits only — adding/removing sequences is permitted while running; rename, cycle-execution toggle, and deletion are blocked until the queue is stopped.
- Q: What happens to a queue entry when its referenced sequence is deleted from the sequence store? → A: Keep the entry but flag it as a stale/unresolved reference (operator removes it manually); mirrors how the app flags deleted image references.
- Q: What scale should the feature be designed and tested against? → A: Small / operator-scale — up to ~50 queues total and ~100 sequence entries per queue.
- Q: Should starting a queue be allowed when its bound emulator (ADB serial) is not currently connected? → A: Yes — start just sets status to "running" regardless of connectivity (placeholder behavior); connectivity enforcement is deferred to the real engine.
- Q: Should queue actions be recorded in the application's logs? → A: Log execution only — emit log entries for start/stop status changes; CRUD and content edits are not required to be logged this iteration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Manage queues (CRUD) (Priority: P1)

An operator opens the new **Queues** tab and manages a collection of execution queues. They can create a queue (giving it a name, choosing the emulator it is bound to, and setting the "cycle execution" option), view the list of all queues, edit a queue's name and cycle-execution setting, and delete a queue they no longer need.

**Why this priority**: Without the ability to create and see queues, no other capability has anything to operate on. This is the foundational slice that makes the feature visible and usable.

**Independent Test**: Open the Queues tab, create a queue bound to an available emulator, confirm it appears in the list with the correct name, emulator, and cycle-execution value, edit it, and delete it — all without touching sequences or execution controls.

**Acceptance Scenarios**:

1. **Given** the operator is on the Queues tab with no queues, **When** they create a queue named "Daily Farm" bound to an available emulator with cycle execution enabled, **Then** the queue appears in the list showing its name, bound emulator, cycle-execution state, and a default (not-running) status.
2. **Given** an existing queue, **When** the operator edits its name and toggles cycle execution off, **Then** the updated values are shown in the list and persist after the page is reloaded.
3. **Given** an existing queue, **When** the operator deletes it and confirms, **Then** the queue is removed from the list and is no longer returned by the API.
4. **Given** the operator is creating a queue, **When** they attempt to save without a name or without selecting an emulator, **Then** the system prevents creation and explains which field is required.

---

### User Story 2 - Add and view sequences in a queue (Priority: P2)

An operator opens a queue and adds one or more sequences to it. Newly added sequences are appended to the end of the queue, preserving the order in which they were added. The operator can see the ordered list of sequences that make up the queue's work.

**Why this priority**: A queue's purpose is to hold an ordered set of sequences to run. This slice gives the queue meaningful content, building directly on the queue objects from US1.

**Independent Test**: Open a previously created queue, add two sequences, and confirm they appear in the queue in the order added (each new one at the end).

**Acceptance Scenarios**:

1. **Given** an empty queue, **When** the operator adds sequence "A", **Then** the queue shows "A" as its only entry.
2. **Given** a queue containing "A", **When** the operator adds sequence "B", **Then** the queue shows entries in order "A", "B" with "B" at the end.
3. **Given** a queue containing sequences, **When** the operator removes an entry, **Then** that entry is removed and the remaining entries keep their relative order.
4. **Given** a queue with sequences added during the current service session, **When** the service is restarted, **Then** the queue still exists with its name, bound emulator, and cycle-execution setting, but its list of sequences is empty.

---

### User Story 3 - Start and stop queue execution (Priority: P3)

From the list of queues, an operator can see each queue's execution status and can start or stop execution. In this iteration, starting and stopping are placeholders: starting sets the queue's status to "running" and stopping returns it to the not-running state. No sequences are actually executed yet.

**Why this priority**: Execution control is the visible payoff of the feature, but it depends on queues (US1) and ideally their contents (US2) existing first. The placeholder behavior is deliberately thin so the surrounding workflow can be validated before real execution is built.

**Independent Test**: From the Queues list, start a queue and confirm its status changes to "running"; stop it and confirm the status returns to not-running.

**Acceptance Scenarios**:

1. **Given** a queue in the not-running state, **When** the operator starts it, **Then** the queue's status changes to "running" and the status is reflected in the list.
2. **Given** a queue in the running state, **When** the operator stops it, **Then** the queue's status returns to the not-running state.
3. **Given** a running queue, **When** the operator views the Queues list, **Then** the running status is visible without needing to open the queue.

---

### Edge Cases

- **No emulators available**: When no emulator can be selected, queue creation cannot bind to an emulator; the system communicates that an emulator must be available/selected to create a queue.
- **Bound emulator no longer present**: If a queue's bound emulator is no longer available (e.g., disconnected), the queue still exists and is listed; its bound emulator is shown even if currently unavailable. The queue can still be started (status only) while the emulator is disconnected.
- **Starting an empty queue**: Starting a queue that has no sequences still sets the status to "running" (placeholder behavior); no work is performed.
- **Start an already-running queue / stop an already-stopped queue**: The action is idempotent — the status remains "running" / not-running respectively without error.
- **Editing or deleting a running queue**: Adding/removing sequence entries is allowed while running; renaming, toggling cycle execution, and deleting are blocked until the queue is stopped, with a message indicating the queue must be stopped first.
- **Stale sequence reference**: If a sequence referenced by a queue entry is deleted from the sequence store, the entry remains and is shown as a stale/unresolved reference that the operator can remove manually.
- **Duplicate queue names**: Two queues may share the same name; queues are distinguished by identity, not by name. (See Assumptions.)
- **Service restart with running queues**: After a restart, queues return in the not-running state with empty sequence lists, since runtime state and queue contents are not persisted.

## Requirements *(mandatory)*

### Functional Requirements

#### Queue lifecycle & configuration

- **FR-001**: System MUST provide a queue as a distinct execution object, separate from sequences and commands.
- **FR-002**: System MUST allow an operator to create a queue with a name (required) and a "cycle execution" setting (boolean, defaulting to off). The cycle-execution setting is a stored flag only in this iteration; its runtime meaning is deferred (see Assumptions / Out of Scope).
- **FR-003**: System MUST require each queue to be bound to exactly one emulator, chosen at creation time. The emulator is identified by its ADB device serial.
- **FR-003a**: System MUST allow more than one queue to be bound to the same emulator (no uniqueness constraint on the emulator binding).
- **FR-004**: System MUST treat a queue's emulator binding as fixed after creation (the bound emulator cannot be changed by editing the queue).
- **FR-005**: System MUST allow an operator to edit a queue's name and cycle-execution setting after creation, while the queue is not running.
- **FR-005a**: System MUST block renaming, toggling cycle execution, and deleting a queue while it is in the "running" state; these actions require the queue to be stopped first.
- **FR-006**: System MUST allow an operator to delete a queue that is not running.
- **FR-007**: System MUST allow an operator to retrieve the list of all queues and the details of an individual queue.
- **FR-008**: System MUST reject creation of a queue that has no name or no bound emulator, with a message indicating the missing field.

#### Queue contents (sequences)

- **FR-009**: System MUST allow an operator to add a sequence as an entry in a queue.
- **FR-010**: System MUST append newly added sequences to the end of the queue, preserving insertion order.
- **FR-011**: System MUST allow an operator to view the ordered list of sequence entries in a queue.
- **FR-012**: System MUST allow an operator to remove a sequence entry from a queue.
- **FR-013**: System MUST allow the same sequence to appear in a queue more than once. (See Assumptions.)
- **FR-013a**: System MUST allow adding and removing sequence entries while the queue is running.
- **FR-013b**: System MUST retain a queue entry whose referenced sequence has been deleted from the sequence store and present it as a stale/unresolved reference, allowing the operator to remove it manually.

#### Execution status (placeholder)

- **FR-014**: System MUST track and expose an execution status for each queue, with at least the states "not running" and "running".
- **FR-015**: System MUST allow an operator to start a queue, which sets its status to "running".
- **FR-016**: System MUST allow an operator to stop a queue, which returns its status to "not running".
- **FR-017**: System MUST make start and stop idempotent: starting a running queue keeps it running, and stopping a not-running queue keeps it not running, without error.
- **FR-018**: System MUST surface each queue's current execution status in the Queues list view.
- **FR-019**: In this iteration, starting/stopping a queue MUST NOT actually execute any sequences; it only changes status (placeholder behavior).
- **FR-019a**: System MUST allow starting a queue regardless of whether its bound emulator is currently connected; connectivity is not enforced in this iteration.
- **FR-019b**: System MUST record a log entry when a queue is started and when it is stopped (execution-status changes). Logging of CRUD and content-edit actions is not required this iteration.

#### Persistence

- **FR-020**: System MUST persist queue configuration (name, bound emulator, cycle-execution setting) across service restarts.
- **FR-021**: System MUST NOT persist a queue's sequence entries across service restarts; after a restart, every queue's sequence list is empty.
- **FR-022**: System MUST NOT persist runtime execution status across restarts; after a restart, every queue is in the "not running" state.

#### UI

- **FR-023**: System MUST present a new top-level "Queues" tab in the authoring UI.
- **FR-024**: The Queues tab MUST display a list of all queues, each showing at least its name, bound emulator, cycle-execution setting, and current execution status.
- **FR-025**: The Queues tab MUST provide controls to create, edit, and delete queues (standard CRUD).
- **FR-026**: The Queues tab MUST provide controls to start and stop execution for each queue from the list.
- **FR-027**: The UI for selecting the emulator at creation time MUST present the currently connected ADB devices (by serial) for the operator to choose from, consistent with the device list already used elsewhere in the app.

### Key Entities *(include if feature involves data)*

- **Queue (Emulator Execution Queue)**: A named execution object bound to exactly one emulator. Persisted attributes: identity, name, bound emulator, cycle-execution flag. Runtime-only attributes (not persisted): ordered list of sequence entries, execution status.
- **Queue Entry**: A reference to a sequence placed in a queue, with a position determining run order. Entries are ordered (new entries appended at the end) and exist only in memory for the current service session.
- **Emulator**: An existing device/emulator the system can target. A queue is bound to one emulator; an emulator may be referenced by queues. (Identity matches the system's existing emulator/device concept.)
- **Sequence**: An existing authored command sequence. Sequences are referenced by queue entries; this feature does not modify sequences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can create a queue bound to an emulator and see it in the Queues list in under 30 seconds, without consulting documentation.
- **SC-002**: 100% of created queues retain their name, bound emulator, and cycle-execution setting after a service restart.
- **SC-003**: 100% of queue sequence lists are empty immediately after a service restart, and every queue is in the not-running state after a restart.
- **SC-004**: Sequences added to a queue appear in insertion order with the most recently added at the end in 100% of cases.
- **SC-005**: Starting a queue reflects a "running" status in the list view within 2 seconds; stopping returns it to not-running within 2 seconds.
- **SC-006**: An operator can complete the full create → add sequences → start → stop → delete workflow for a queue entirely from the Queues tab without using any other tab.
- **SC-007**: The Queues list and queue detail views remain responsive (interactions reflected within 1 second) at the target scale of 50 queues with up to 100 sequence entries each.

## Assumptions

- **"Cycle execution" meaning**: A stored boolean flag only. It is persisted and displayed in this iteration but has no defined runtime behavior; its execution semantics are intentionally deferred until the real execution engine is specified (see Out of Scope).
- **Emulator identity**: "Emulator" maps to a connected ADB device, identified by its device serial — the same device list already surfaced elsewhere in the app. The serial is persisted as the queue's binding. A persisted binding may reference a serial that is not currently connected.
- **Multiple queues per emulator**: More than one queue may be bound to the same emulator; there is no uniqueness constraint on the emulator binding.
- **Duplicate sequences and names**: A sequence may appear multiple times in a queue, and multiple queues may share the same name; queues and entries are distinguished by identity rather than name.
- **Execution status states**: The minimal status model is "not running" (idle/stopped) and "running". Richer states (paused, error, completed) are out of scope for this iteration.
- **Authorization/UI conventions**: The Queues tab and its API follow the same authentication, styling, and CRUD conventions as the existing authoring tabs (Commands, Games, Sequences, Images).
- **Reordering**: Beyond append-on-add and remove, arbitrary reordering of queue entries is out of scope for this iteration ("by default places them at the end" implies append is the only insertion behavior required now).
- **Scale**: The feature targets a single-operator desktop tool — up to ~50 queues total and ~100 sequence entries per queue. This bounds performance expectations and test data sizing.

## Out of Scope

- Actual execution of sequences within a queue (real run engine, scheduling, progress, per-sequence results).
- Defining the runtime behavior of the "cycle execution" flag (stored/displayed only this iteration).
- Reordering or inserting queue entries at arbitrary positions.
- Changing a queue's bound emulator after creation.
- Persisting queue contents or runtime status across restarts.
- Advanced execution states (paused, completed, error) and concurrency rules between queues on the same emulator.
