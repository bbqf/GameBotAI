# Feature Specification: Export and import queue settings by name

**Feature Branch**: `145-export-import-queue-settings`  
**Created**: 2026-10-15  
**Status**: Draft  
**Input**: User description: "Export and import queue settings by name, without copies of gamepack content. GitHub issue #306 (FR-023). Closes #306."

## Clarifications

### Session 2026-10-15

- Q: Does the export hold all non-gamepack artifacts, or only those that a queue uses? → A: All of them. The export holds every sequence, command, and queue template with no gamepack owner. (Rationale: a backup must be complete; FR-002 is simplified.)
- Q: Does the export cover all queues or a chosen set? → A: All queues. A choice of queues is out of scope. (Rationale: backup use case; smallest scope.)
- Q: What does "queue membership" mean? → A: The link between a queue and its queue template and its entries, by name. (Rationale: links must use names, FR-004.)
- Q: What if an import changes a queue that is running? → A: The import updates the stored settings and reports that the queue needs a stop and start to apply them. It does not stop or start the queue. (Rationale: matches the existing rule that a running queue keeps its start-time entries.)
- Q: Does a reference to a gamepack-owned object also appear when a hand-made artifact points to it? → A: Yes. Any reference from a queue or a hand-made artifact to a gamepack-owned object is listed as a name reference. (Rationale: FR-003 and FR-008 need a complete reference list.)

### Session 2026-10-15 (loop 1, from analyze findings)

- Q (A1): What is the uniqueness and matching rule for names? → A: A name is unique per object type inside the document (queues, queue templates, sequences, commands). Name matching ignores case. (Rationale: the instance matches names this way.)
- Q (A2): How is the link of a queue to a game stored? → A: By game name. Games are not exported; a game is only referenced by name. A missing game is an unresolved reference. (Rationale: games are not in the artifact types.)
- Q (A3): Is a missing user-owned image an unresolved reference? → A: Yes. Any reference by name that has no target, including an image, is reported (see FR-008).
- Q (A8): What happens with machine-specific queue fields such as the emulator serial? → A: They are exported as stored and imported as given. The import report does not check them. (Rationale: no check is possible without the device.)
- Q (A9): What do "reference" and "unresolved reference" mean? → A: A "reference" is an entry in the document that points to an object by type and name (no content). An "unresolved reference" is a reference, or a link inside an item, whose target is missing on the instance at import; it appears in the import report.

### Session 2026-10-15 (loop 2, from analyze findings)

- Q (C1): How is SC-006 (two requests) checked? → A: The round-trip test makes exactly one export request and one import request and checks that the target state matches. (Rationale: makes SC-006 testable.)
- Q (C2): How does the export list a gamepack-owned image? → A: Like any gamepack-owned object: one entry in the same reference list, with type `image` and the image name (its id). Its content is not copied. (Rationale: FR-003 applies to all owned types.)
- Q (D1): How does a user recover after a failed import (HTTP 500 with a partial report)? → A: The user fixes the cause and sends the same document again. Earlier writes stay, and the import is idempotent (FR-011). The user documentation states this. (Rationale: no rollback, see Non-goals.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Export queue settings and own artifacts (Priority: P1)

A user wants a backup of the local settings. The user asks for an export. The system writes one document. The document holds the settings of the queues (entry options, disabled switches, schedules, queue membership) and the artifacts that no gamepack owns (the artifacts the user made by hand). For an object that a gamepack owns, the document holds only a reference by name, with the gamepack name and version when known.

**Why this priority**: Without an export there is nothing to import. The export also sets the main rule: no copy of gamepack content.

**Independent Test**: Install a gamepack, make a queue that uses a gamepack sequence and a hand-made sequence, and export. Check that the document has the queue settings and the hand-made sequence, and that the gamepack sequence appears only as a name reference.

**Acceptance Scenarios**:

1. **Given** a queue with entries, disabled switches, and schedules, **When** the user exports, **Then** the document holds these settings for the queue.
2. **Given** a queue entry that points to a sequence owned by a gamepack, **When** the user exports, **Then** the document holds only the name of the sequence, with the gamepack name and version, and none of the sequence content.
3. **Given** a hand-made sequence (no owner), **When** the user exports, **Then** the document holds the full definition of that sequence.
4. **Given** any export, **When** the document is inspected, **Then** it holds no identifier that the instance assigned.

---

### User Story 2 - Import settings by name (Priority: P1)

A user restores a backup, on the same instance after a gamepack update or on a new machine. The user sends the document to the import. The system creates or updates the queue settings and the non-gamepack artifacts, matched by name. It never creates, changes, or deletes an object that a gamepack owns.

**Why this priority**: This is the restore path that gives the export its value.

**Independent Test**: Export on instance A. Import on an empty instance B. Check that the queues and hand-made artifacts exist on B by name. Import again on an instance with a newer gamepack version and check that the gamepack objects did not change.

**Acceptance Scenarios**:

1. **Given** an empty instance with the gamepack installed, **When** a document from another instance is imported, **Then** the queues and hand-made artifacts exist, matched by name.
2. **Given** a queue and a hand-made sequence that exist with the same name, **When** the document is imported, **Then** they are updated to the document values and no duplicate is made.
3. **Given** a document and a gamepack-owned object with a different version than the document names, **When** the document is imported, **Then** the gamepack object is not created, changed, or deleted.
4. **Given** a document that names an object with the same name as a gamepack-owned object but with full content, **When** it is imported, **Then** the system does not change the gamepack object and reports that item as skipped.

---

### User Story 3 - Partial import with a list of unresolved references (Priority: P2)

A document can point to targets that do not exist on the target instance: a gamepack that is not installed, or a sequence that was removed. The import does not fail as a whole. It applies all that it can and returns the list of references without a target.

**Why this priority**: It makes the restore robust on instances that are not identical.

**Independent Test**: Import a document that points to a gamepack sequence on an instance without that gamepack. Check that the import succeeds for the rest, and the response lists the missing reference.

**Acceptance Scenarios**:

1. **Given** a document with a reference to a gamepack that is not installed, **When** it is imported, **Then** the rest is applied and the response lists the reference with its type, name, and gamepack name and version.
2. **Given** a document with a reference to a removed sequence, **When** it is imported, **Then** the rest is applied and the response lists that reference.
3. **Given** a document with no missing targets, **When** it is imported, **Then** the list of unresolved references is empty.

---

### Edge Cases

- A document is not valid (not the expected format, or an unknown format version): the import is refused with a clear error, and nothing changes.
- A queue in the document has the name of an existing queue: the existing queue is updated by name.
- A hand-made artifact in the document has the name of a gamepack-owned object on the target: the item is skipped and reported, the gamepack object stays.
- The export is run when no queue and no hand-made artifact exist: the document is valid and holds empty lists.
- A queue entry references a hand-made artifact that is in the same document: the reference resolves after the artifacts are applied.
- An import runs twice with the same document: the second run changes nothing.
- A name that is not unique inside the document: the import refuses the document with an error that names the duplicate.
- A gamepack install runs, or holds the gamepack gate lock, when an import starts: the import waits until the lock is free and applies no write before that. An install that starts while an import runs also waits. Install and import never run at the same time (testable by holding the lock in a test).
- A repository fails during an import (for example a write throws): the import returns HTTP 500 with a partial report. The report lists the items that were applied, the item that failed, and the items that were not applied. Items already written stay written, and no object is deleted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide an export that writes one document with the settings of all queues: entry options, disabled switches, schedules, and queue membership.
- **FR-002**: The export MUST include the full definition of every artifact that no gamepack owns. The artifact types are the types that a gamepack can own (sequences and commands) plus queue templates.
- **FR-003**: The export MUST NOT include the content of any object that a gamepack owns. Such an object MUST appear only as a reference by type and name, with the gamepack name and version when known.
- **FR-004**: The export document MUST NOT contain any identifier that the instance assigned. All links inside the document MUST use names.
- **FR-005**: The system MUST provide an import that applies a document by name. It MUST create an object that does not exist and update an object that exists with the same type and name, for queue settings and non-gamepack artifacts only.
- **FR-006**: The import MUST NOT create, change, or delete an object that a gamepack owns. A document item that has the type and name of a gamepack-owned object MUST be skipped and reported.
- **FR-007**: The import MUST NOT delete any object. It only creates or updates.
- **FR-008**: The import MUST NOT fail as a whole when a reference has no target. It MUST apply all valid items and return the list of unresolved references. Each item in the list MUST have the reference type, the name, the place of use, and the gamepack name and version when the document has them. A missing game, image, sequence, command, or gamepack is an unresolved reference.
- **FR-009**: The import MUST refuse a document that is not valid or has an unsupported format version. It MUST change nothing in that case and return a clear error.
- **FR-010**: The import response MUST report what happened for each item: created, updated, unchanged, or skipped, with the reason for a skip.
- **FR-011**: The import MUST be idempotent: the same document applied twice leaves the same state.
- **FR-012**: A document exported on one instance MUST be importable on another instance, because all matching uses names.
- **FR-013**: The feature MUST NOT change the behavior of gamepack install, list, rollback, or the API version endpoint.
- **FR-014**: The document MUST carry a format version so that later changes can be detected.
- **FR-015**: The export MUST cover all queues. The export MUST list, as a name reference, each reference from a queue or a non-gamepack artifact to a gamepack-owned object.
- **FR-016**: The import MUST NOT stop or start a queue. When it updates a queue that runs, the import report MUST say that the queue needs a stop and start to apply the new settings.
- **FR-017**: The import and a gamepack install MUST exclude each other. The import MUST wait while the gamepack gate lock is held, and MUST hold the lock while it applies items. A test MUST prove this by holding the lock and checking that the import makes no write until the lock is released.
- **FR-018**: When a repository fails during an import, the import MUST return HTTP 500 with a partial report (applied, failed, and not-applied items). A test MUST use a failing repository to prove this.

### Key Entities

- **Settings document**: One file that holds a format version, the queue settings, the non-gamepack artifacts, and the references to gamepack-owned objects.
- **Queue settings**: For one queue: name, entry options, disabled switches, schedules, and queue membership (which entries belong to the queue).
- **Non-gamepack artifact**: A sequence, command, or queue template with no gamepack owner record (owned by the user).
- **Name reference**: A pointer to a gamepack-owned object by type and name, with an optional gamepack name and version.
- **Import report**: The result of an import: per-item outcome and the list of unresolved references.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of tested exports, the document holds no content of a gamepack-owned object and no instance-assigned identifier.
- **SC-002**: In 100% of tested imports, the gamepack-owned objects (content and owner records) are the same before and after the import.
- **SC-003**: A document exported on one instance imports on a second instance with the same gamepack installed, and the queue settings match by name, in 100% of tested cases.
- **SC-004**: An import with missing targets finishes successfully for all other items, and the list of unresolved references names every missing target, in 100% of tested cases.
- **SC-005**: Importing the same document twice gives the same state as importing it once.
- **SC-006**: A user can complete an export and a restore in two requests (one export, one import).

## Assumptions

- A gamepack-owned object is a sequence, command, or image with an owner record, as defined by the gamepack install feature (specs/143-gamepack-install). An object with no owner record belongs to the user.
- The user-owned artifact types in the document are sequences, commands, and queue templates. Images hold binary data, so the document does not carry them. A reference to a user-owned image that is missing on the target is reported as unresolved.
- Objects are matched by type and name. Queues are matched by name.
- The export and import are API operations. Whether the web UI gets buttons is decided in planning; a UI is not required for the first release.
- The document is a single JSON file.

## Non-goals

- No signature verification of the document (FR-024, issue #307).
- No remote catalog (FR-025, issue #308).
- No change to gamepack install, list, rollback, or API version behavior (FR-021, FR-022).
- No copy of gamepack-owned content in the export.
- No versioning, owner tracking, or rollback of imported settings.
