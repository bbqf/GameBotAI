# Feature Specification: Gamepack Install

**Feature Branch**: `143-gamepack-install`  
**Created**: 2026-10-15  
**Status**: Draft  
**Input**: User description: "Issue #304: Install a gamepack as one atomic unit. A gamepack is a named, versioned set of sequences, commands, and images, with a manifest. The platform installs it all-or-nothing, records which gamepack and version own each object, lists the installed gamepacks, rolls one back to the previous version, and can check a gamepack with a dry run that changes nothing."

## Clarifications

### Session 2026-10-15

- Q: Is a gamepack object found by type and name, or by id? → A: By type and name. Names are unique per type inside a gamepack and in the installation for the conflict check. (Rationale: the issue says each object is addressed by name.)
- Q: What does a rollback do with an object that the newer version added? → A: It removes the object. An object that the newer version changed returns to its previous content. (Rationale: the rollback must equal the state after the previous version.)
- Q: What does an image reference mean in a gamepack? → A: A sequence or command can refer to an image by name. The reference is valid if the image is in the gamepack or in the installation. (Rationale: same rule as other references, FR-013.)
- Q: What is the status of a dry run in the response? → A: The response has a `dryRun` flag, a `valid` flag, and a list of problems, each with category (structure, reference, conflict), object type, name, and message. (Rationale: one shape for dry run and install, FR-014.)
- Q: What happens to a stored object when a user deletes an object that a gamepack owns? → A: The delete works as today. The next install of the gamepack re-creates it, and a rollback restores it. (Rationale: FR-015, no change to manual handling.)

### Session 2026-10-15 (loop 1, from analyze)

- Q (E1): Does a rollback check conflicts? → A: Yes. A rollback runs the same conflict and reference check as an install, on the kept previous content. If the rollback would overwrite an object with no owner or an object of another gamepack, or a reference is broken, it is refused with the problem list, and nothing changes. See FR-018. (Rationale: FR-016 must hold for rollback.)
- Q (I1): Does a same-version re-install replace the kept previous version? → A: No. FR-010 has this exception. (Rationale: a re-install must not lose the real previous version.)
- Q (U1): What content does the kept previous version hold? → A: The install captures the full content of the current version from the live objects (including hand edits) before it changes them, and stores it with the registry. A rollback restores that captured content. The capture is part of the atomic unit. (Rationale: v1 exists only as live objects; hand edits are lost at the next install by the assumption, but the capture keeps the state that was live.)
- Q (U2): What if a gamepack object has the same name as an owned object but another id? → A: The identity is type and name. The install keeps the id of the existing object and updates its content. (Rationale: keeps references by id stable.)
- Q (F1): Is "same id, other name" a conflict? → A: No. The conflict rule is by type and name only (FR-006). The design must not add more conflict kinds.
- Q (F2): Is the required API major version required? → A: It is optional in the manifest. The platform stores it when present. FR-002 is changed.
- Q (T3): Which gamepack names are valid? → A: 1 to 64 characters from letters, digits, `_`, `-`, and `.`, not starting with `.`. A name that is not valid is a structure error. See FR-019.
- Q (R1): What if the undo restore itself fails? → A: The platform logs an error with the failed object and returns a failure result that says the state may be partly restored. See FR-003 note.
- Q (D1): Is US1 alone releasable? → A: No. US1 and US2 ship together. The MVP is US1 plus US2.
- Q (C1): Project docs? → A: Add `docs/architecture.md` (with the "Last reviewed" date) and `specs/STATUS.md` updates, and the spec Status change, to FR-017.

### Session 2026-10-15 (loop 2, from analyze)

- Q (F1): What is the name of an image? → A: The image id is its name. An image in a gamepack has an `id` that serves as the name for conflict, ownership, and id-retention rules. (Rationale: images are stored by id today.)
- Q (F2): What happens to the owner entry when a user deletes an owned object by hand? → A: The owner entry is removed at once, so a later hand-made object with the same name belongs to the user. The owner read also checks that the object exists. (Rationale: FR-005 and FR-015.)
- Q (F3): Is the gamepack name case-sensitive, and are reserved Windows names allowed? → A: Name matching ignores case. Names that are reserved Windows device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9, with or without extension) are a structure error. (Rationale: the name is used as a file name.) FR-019 is extended by this answer.
- Q (F4): Which size rules apply? → A: An image MUST have a content type of an image type and valid base64 data, and MUST be at most 10,000,000 bytes decoded. The total request limit is 100 MB; a larger request gives a structured 413 problem. These are structure rules. (Rationale: bound memory use.)
- Q (F5): What about a hand create, edit, or delete during an install? → A: The hand-edit routes do not take the install gate, because FR-015 forbids changes to manual handling. The accepted limit is: a hand change that runs during an install can be lost or can fail. The docs say to avoid hand changes during an install. (Rationale: FR-015 forbids changes to manual handling.)
- Q (F6): Are references from user objects to a dropped object checked? → A: No. Only references inside the gamepack and from the gamepack to existing objects are checked. The docs say this.
- Q (F7): Where does the test seam live? → A: As a testability constraint in the plan; the spec Assumption stays.
- Q (F8): What does SC-004 mean with hand edits? → A: The state after rollback equals the live state captured just before the v2 install, including hand edits made before it. Hand edits made after the v2 install are lost.
- Q (F9): What does a failed restore return? → A: HTTP 500 with result `partial_restore`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a gamepack as one unit (Priority: P1)

A distributor has a set of automations: sequences, commands, and images. The distributor sends them to the platform as one gamepack file. The platform creates or updates all the objects, or none of them. Each object shows which gamepack and which version own it.

**Why this priority**: This is the core of the request. Without it, a client that restores objects one call at a time can leave a partly updated set.

**Independent Test**: Send a valid gamepack to an empty installation. Check that all its sequences, commands, and images exist, and that each shows the gamepack name and version as owner.

**Acceptance Scenarios**:

1. **Given** an installation with no gamepack, **When** a valid gamepack is installed, **Then** all its sequences, commands, and images exist and each shows the gamepack name and version as owner.
2. **Given** an installed gamepack at version 1, **When** version 2 of the same gamepack is installed, **Then** all objects of version 2 are created or updated, and each shows version 2 as owner.
3. **Given** a gamepack with one invalid object (for example, a sequence that points to a command that does not exist in the gamepack or in the installation), **When** it is installed, **Then** the install is refused, no object is created or changed, and the response lists each problem.
4. **Given** an installed gamepack at version 1 and a version 2 that drops an object of version 1, **When** version 2 is installed, **Then** the dropped object is removed, because it is owned by the same gamepack.

---

### User Story 2 - Protect user objects and objects of other gamepacks (Priority: P1)

A user makes some objects by hand. Those objects have no owner, so they belong to the user. A gamepack install must never overwrite them, and must never overwrite an object of another gamepack.

**Why this priority**: A gamepack install that destroys user work is not safe. This rule is part of the atomic guarantee.

**Independent Test**: Create a sequence by hand. Install a gamepack that has a sequence with the same name. Check that the install is refused and the response names the conflict.

**Acceptance Scenarios**:

1. **Given** a sequence made by hand with no owner, **When** a gamepack with a sequence of the same name is installed, **Then** the install is refused, nothing changes, and the response reports the conflict with the object type, the name, and the current owner (user).
2. **Given** an object owned by gamepack A, **When** gamepack B with an object of the same name is installed, **Then** the install is refused and the response reports the conflict with the owner A.
3. **Given** a gamepack with three conflicts, **When** it is installed, **Then** the response reports all three conflicts, not only the first.
4. **Given** an object owned by a gamepack, **When** a user edits the object by hand, **Then** the edit is accepted and the owner record does not change. Tests MUST cover this.
5. **Given** an object owned by a gamepack, **When** a user creates by hand an object of the same type with the same name, **Then** the platform refuses the request and the owner record of the gamepack object does not change. Tests MUST cover this.
6. **Given** an object owned by a gamepack, **When** a user deletes the object by hand and then makes an object of the same type with the same name by hand, **Then** the owner entry is removed at the delete, the new object shows the user as owner, and a read of the owner does not show the gamepack. Tests MUST cover this.

---

### User Story 3 - List installed gamepacks (Priority: P2)

An operator wants to know which gamepacks are installed and at which version.

**Why this priority**: Visibility is needed to manage updates, but the install works without it.

**Independent Test**: Install two gamepacks. Request the list. Check that each shows name, version, and changelog text.

**Acceptance Scenarios**:

1. **Given** two installed gamepacks, **When** the operator requests the list, **Then** the response has one entry for each gamepack with name, version, and changelog text.
2. **Given** no installed gamepack, **When** the operator requests the list, **Then** the response is an empty list.

---

### User Story 4 - Roll back to the previous version (Priority: P2)

A new version of a gamepack has a fault. The operator goes back to the version that was installed before it.

**Why this priority**: A safe update must be reversible. This is the second half of the stated need.

**Independent Test**: Install version 1, then version 2. Roll back. Check that the objects and owner records are the same as after version 1.

**Acceptance Scenarios**:

1. **Given** version 1 then version 2 installed, **When** the operator rolls back the gamepack, **Then** the objects are restored to version 1, objects added by version 2 are removed, and the list shows version 1.
2. **Given** a gamepack installed once, **When** the operator rolls it back, **Then** the result says that no previous version exists and nothing changes.
3. **Given** a rollback that cannot complete, **When** it fails, **Then** the state stays as before the rollback.
4. **Given** a gamepack that is rolled back once, **When** the operator rolls it back again, **Then** the result says that no previous version exists.

---

### User Story 5 - Check a gamepack with a dry run (Priority: P3)

A distributor wants to know if a gamepack will install, before any change is made.

**Why this priority**: It is a convenience on top of the install. The install already refuses bad gamepacks.

**Independent Test**: Run a dry run on a gamepack with a missing reference and a name conflict. Check that both problems are reported and that no data changes.

**Acceptance Scenarios**:

1. **Given** a valid gamepack, **When** a dry run is requested, **Then** the response says the install would succeed, and no object, owner record, or installed gamepack list entry changes.
2. **Given** a gamepack with a broken reference, a structure error, and a conflict, **When** a dry run is requested, **Then** the response reports each problem by category, and no data changes.

---

### Edge Cases

- The gamepack file is not readable, or the manifest is missing a name or version: the install is refused with a structure error and nothing changes.
- Two objects of the same type and name in one gamepack: refused as a structure error.
- The same version of an installed gamepack is installed again: the install is accepted as an update with the same content, and it does not replace the kept previous version. [Assumption, see below.]
- A failure while the platform writes objects (for example, a full disk): all objects return to the previous state.
- A reference from a gamepack object to an object outside the gamepack: valid only if that object exists in the installation. It is checked in the dry run and in the install.
- An object that a user makes by hand with the name of a gamepack object after the install: the platform refuses it as it refuses any duplicate name; the owner record of the gamepack object does not change.
- A user edits an object that a gamepack owns: the object stays owned by the gamepack until the next install or rollback replaces it. [Assumption, see below.]
- The manifest has a required API major version: the platform stores and shows it, but does not check it (see issue #305).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept a gamepack file as one install request. The file has a manifest and the gamepack objects: sequences, commands, and images. Each object is addressed by name.
- **FR-002**: The manifest MUST hold the gamepack name, the version, a changelog text, and the required API major version. The required API major version is optional. The system MUST store it when present, and MUST NOT check it.
- **FR-003**: The system MUST install all objects of a gamepack, or none. If any step fails, the objects, owner records, and installed gamepack records MUST stay in the state from before the request.
- **FR-004**: The system MUST record the owning gamepack name and version for each object that an install creates or updates.
- **FR-005**: An object with no owner record MUST be treated as owned by the user. When the system reads the owner of an object, it MUST also check that the object exists. An owner entry without an object MUST NOT be shown as an owner.
- **FR-006**: The system MUST refuse an install that would overwrite an object with no owner, or an object of another gamepack. The response MUST report every conflict, each with the object type, the object name, and the current owner.
- **FR-007**: The system MUST show the owner (gamepack name and version, or user) when a sequence, a command, or an image is read.
- **FR-008**: The system MUST list the installed gamepacks, each with name, version, and changelog text.
- **FR-009**: When a new version of an installed gamepack is installed, the system MUST remove the objects that the previous version owned and the new version does not contain.
- **FR-010**: The system MUST keep exactly one previous version of each gamepack for rollback. A new install of a different version replaces the kept previous version with the version that was current before the install. An install of the same version as the current one does not replace the kept previous version.
- **FR-011**: The system MUST roll back a named gamepack to its kept previous version as one atomic unit: objects, owner records, and the list entry. After a rollback, no previous version is kept.
- **FR-012**: If a gamepack has no kept previous version, a rollback request MUST give a clear result that says so, and MUST change nothing. If the gamepack is not installed, the result MUST say that the gamepack is not found.
- **FR-013**: The system MUST support a dry run for an install request. A dry run MUST check structure (manifest and object shape), references (between objects of the gamepack and to existing objects), and conflicts. It MUST report every problem by category, and MUST NOT change any data.
- **FR-014**: A dry run and a real install MUST give the same validation result for the same file and the same installation state.
- **FR-015**: The system MUST NOT change how a user creates, edits, or deletes an object by hand, except that the owner record is present and shown. When a user deletes an owned object by hand, the system MUST remove the owner entry of that object at once. A later object that the user makes by hand with the same name then belongs to the user.
- **FR-016**: The system MUST NOT delete or change any object with no owner during an install or a rollback.
- **FR-017**: New endpoints and changed object responses MUST be in the OpenAPI document and in the user docs. The feature MUST also update `docs/architecture.md` (API surface, persistence layout, and the "Last reviewed" date) and `specs/STATUS.md`, and MUST set the spec Status when done.
- **FR-018**: A rollback MUST run the same conflict and reference check as an install on the kept previous content. If it finds a problem, the rollback MUST be refused with the problem list and MUST change nothing.
- **FR-019**: A gamepack name MUST be 1 to 64 characters from letters, digits, `_`, `-`, and `.`, and MUST NOT start with `.`. A name that is not valid is a structure error. Name matching MUST ignore case, so two names that differ only in case are the same gamepack name. A name that is a reserved Windows device name (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, with or without an extension) MUST be a structure error, because the name is used as a file name.
- **FR-020**: An install that updates a gamepack MUST keep the id of each existing object that has the same type and name, and update its content.
- **FR-021**: If the restore after a failed install or rollback itself fails, the system MUST log the error with the failed object and MUST return a failure result that says the state can be partly restored. The response MUST use HTTP status 500 and the result `partial_restore`.
- **FR-022**: The kept previous version MUST hold the full content of the current version, captured from the live objects before the install changes them. The capture MUST be part of the atomic unit.
- **FR-023**: An image in a gamepack MUST have a content type of an image type and valid base64 data. The decoded data MUST be at most 10,000,000 bytes. The total size of an install request MUST be at most 100 MB. A request that is larger MUST get a structured 413 problem response. Each of these rules is a structure rule, and a dry run MUST check them too.
- **FR-024**: The id of an image is its name. An image in a gamepack MUST have an `id`, and the system MUST use this `id` as the image name for the conflict rule (FR-006), the owner records (FR-004), and the id-keeping rule (FR-020).

### Key Entities

- **Gamepack**: A named, versioned set of sequences, commands, and images. It has a manifest.
- **Manifest**: The name, version, changelog text, and required API major version of a gamepack.
- **Owner record**: A mark on a sequence, command, or image. It holds the gamepack name and version. No mark means the user owns the object.
- **Installed gamepack record**: The current version of a gamepack, its changelog text, and the list of objects it owns. It also holds the one kept previous version.
- **Conflict**: A gamepack object whose type and name match an object that the user or another gamepack owns.

## Assumptions

- A gamepack name identifies a gamepack. Installing a file with an installed name is an update of that gamepack.
- A downgrade or a re-install of the same version is allowed and works like an update. The platform does not compare version order.
- The kept previous version holds the full objects of that version, so a rollback needs no second file.
- An edit by hand to a gamepack-owned object is allowed and is lost at the next install or rollback of that gamepack. The owner record stays.
- The objects of a gamepack are not a security boundary. Access control is the same as for other API calls.
- The gamepack file format is one JSON document with the manifest and the object lists. The exact field names are a design choice of the plan.
- Image data in a gamepack is carried in the file, so the file is self-contained.
- Testability constraint: the design MUST provide a test seam for fault injection into object writes (for example, a wrapper over the stores that throws at the Nth write). Without it, the failure tests for SC-001 cannot run.
- A hand create, edit, or delete does not take the install gate, because FR-015 forbids changes to manual handling. A hand change that runs during an install can be lost or can fail. The user docs MUST tell users to avoid hand changes during an install.
- The system does not check references from user objects to an object that an install drops. It checks only references inside the gamepack and from the gamepack to existing objects. The user docs MUST say this.
- A problem report (conflict) names the current owner as text (for example, `user` or `PackName 1.2.0`). The object read shows the owner as a structured field with kind, name, and version. The plan MUST document both shapes.
- The performance goal for an install of 100 objects is a soft check with a generous threshold. It is not a hard CI gate.

## Out of Scope

- Check or reject by API version (issue #305).
- Export or import of queue settings (issue #306).
- Signature check (issue #307).
- Remote catalog (issue #308).
- More than one kept previous version.
- Uninstall of a gamepack.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In tests with a gamepack that fails at any one object, 100% of failed installs leave the installation identical to its state before the request. Note: SC-001 is verified by fault injection at each object write (see the test seam Assumption).
- **SC-002**: After a successful install, 100% of the gamepack objects show the gamepack name and version as owner.
- **SC-003**: In tests with N conflicts, the response reports all N conflicts in one answer.
- **SC-004**: After an install of version 2 followed by a rollback, the objects and owner records equal the live state that the install captured just before the version 2 install, in 100% of tested cases. This state includes hand edits made before the version 2 install. Hand edits made after the version 2 install are lost.
- **SC-005**: A dry run changes no stored data in 100% of tested cases, and its validation result equals the real install result for the same input.
- **SC-006**: An operator can see name, version, and changelog text of every installed gamepack in one list request.
