# Feature Specification: Take over same-named user objects on gamepack install

**Feature Branch**: `149-gamepack-takeover-user-objects`  
**Created**: 2026-10-16  
**Status**: Draft  
**Input**: User description: "FR-027: take over same-named user objects when a gamepack is installed. GitHub issue #315. Closes #315."

## Clarifications

### Session 2026-10-16

- Q: Where does the backup live, and how does the client restore it? → A: The platform stores the backup in its data folder, next to the other gamepack data. The client restores it with one restore operation that takes the backup identifier. An unknown identifier gets a "not found" client error (FR-008, FR-009).
- Q: Does a take-over of an object that the same gamepack already owns count? → A: No. Such an object follows the normal upgrade path. Only user-owned objects count (FR-012).
- Q: Is a take-over allowed when the install is a first install and when it is an upgrade? → A: Yes, both. In an upgrade, a new object of the new version can take over a user object of the same type and name (FR-003).
- Q: Does the take-over change the rollback of the gamepack? → A: No. A rollback of the gamepack does not give an object back to the user. The user restores the take-over backup for that (FR-009).
- Q: What does the dry run show when the option is off? → A: The conflict, as today, and no take-over list (FR-010).

### Session 2026-10-16 (loop 1)

- Q: Do the catalog install and catalog update routes get the option? → A: Yes. The catalog install and the catalog update are install paths and the managed update path. They accept the same option "takeOver" with the same default and meaning. The managed rollback does not accept it (FR-002).
- Q: How is the upgrade take-over tested? → A: A test covers an upgrade where an object of the new version takes over a user object. A test checks that the count excludes objects that the gamepack already owns (FR-003, FR-012).
- Q: What is the "force" restore? → A: It is an internal mode. Only the managed update uses it, to undo a take-over after a later step failed. The public restore route never uses it. Force skips the check "owner_changed" only. It does not skip "object_missing" or "object_changed" (FR-009).
- Q: What happens on a second restore of the same backup? → A: The backup stays after a restore. A second restore gets the conflict reason "owner_changed" and changes nothing (FR-009).
- Q: Is the 13 sequences and 131 commands case tested? → A: Yes. A test installs a gamepack over 13 sequences and 131 commands with the option on. It checks that the count is 144, every link by name is valid, and no queue changes its run state (SC-001, SC-008).
- Q: What is the HTTP status for a conflict with another gamepack when the option is on? → A: The same status as with the option off, as today. A dry run gives the same result as today for a conflict (FR-005).

### Session 2026-10-16 (loop 2)

- Q: Does the catalog install result have the new fields? → A: Yes. The catalog install returns the same install result with the count of objects taken over and the backup identifier. A client of the catalog install can then restore (FR-012, FR-013).
- Q: What is the time limit for the 144-object take-over? → A: The take-over is at most 2 times the time of a normal install of the same gamepack on the same machine. The test compares the two times. It has no fixed number of seconds (SC-001).
- Q: Which checks can refuse a restore? → A: Exactly three: "object_missing", "object_changed", and "owner_changed". There is no other check (FR-009).
- Q: Does the force restore use the gate? → A: Yes. The force restore runs under the same gate as the managed update that calls it (FR-016).
- Q: What is the name of the managed step that undoes a take-over? → A: "takeover_restore". It appears in the managed report when a later step fails (FR-009).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install over hand-made objects (Priority: P1)

A user made some objects by hand (for example sequences and commands). Later, the user gets a gamepack that has objects of the same type and the same name. Today the install reports a conflict and refuses. The user must delete the objects first. A deleted object breaks every link to it (queue templates, other sequences) while it is missing. The user sends the install request with the option "takeOver" on. The gamepack takes over each object that conflicts only with an object of the user. The object keeps its name, so all links by name stay valid. The object gets the content of the gamepack, and the gamepack owns it.

**Why this priority**: This is the core value. A buyer with hand-made objects cannot install a gamepack without a break in the links. The requester has 13 sequences and 131 commands in this state.

**Independent Test**: Make a sequence and a command by hand. Make a queue template that references the sequence by name. Install a gamepack that has a sequence and a command with the same type and name, with the option on. The install succeeds. The objects have the gamepack content and the gamepack owns them. The queue template still references the sequence by name.

**Acceptance Scenarios**:

1. **Given** a gamepack object that conflicts only with an object of the user, **When** the client installs with "takeOver" off, **Then** the install is refused with the conflict, as today.
2. **Given** the same state, **When** the client installs with "takeOver" on, **Then** the install succeeds. The object has the gamepack content, the gamepack owns it, and its name does not change.
3. **Given** a queue template that references a taken-over sequence by name, **When** the install ends, **Then** the reference is still valid.
4. **Given** a gamepack object that conflicts with an object of another gamepack, **When** the client installs with "takeOver" on, **Then** the install is refused with the conflict.
5. **Given** a user object whose type and name are not in the gamepack, **When** the client installs with "takeOver" on, **Then** that object does not change.
6. **Given** the client sends no "takeOver" value, **When** the client installs, **Then** the behavior is the same as "takeOver" off.

---

### User Story 2 - Backup and restore of replaced objects (Priority: P1)

Before the install replaces an object of the user, the platform keeps a backup of that object. The backup has an identifier. The user can restore the backup. If any step of the install fails, no object changes.

**Why this priority**: A take-over replaces user content. The user must be able to undo it. A partial result is not acceptable.

**Independent Test**: Install with "takeOver" on over three user objects. Read the backup identifier from the result. Restore the backup. The three objects have their old content again and are objects of the user again. Then force a failure in the middle of an install. Check that no object changed.

**Acceptance Scenarios**:

1. **Given** an install with "takeOver" on that replaces objects, **When** the install ends, **Then** the result has the identifier of one backup that holds every replaced object.
2. **Given** a backup identifier, **When** the client restores the backup, **Then** each replaced object has its old content and is an object of the user again.
3. **Given** a failure during the install, **When** the install ends, **Then** no object has changed and no backup remains that the client must use.
4. **Given** an install that replaces no object, **When** the install ends, **Then** the result has no backup identifier.

---

### User Story 3 - Preview with dry run (Priority: P2)

The user wants to know what a take-over does before it runs. The client sends the install request with "dryRun" and "takeOver" on. The result lists each object that the install would take over. For each object, it says whether the user content is identical to the gamepack content or different. A dry run changes nothing.

**Why this priority**: The user can decide with data. It is useful, but the install works without it.

**Independent Test**: Make two user objects: one identical to the gamepack object and one different. Send a dry run with "takeOver" on. The result lists both, and marks one identical and one different. No object changed and no backup exists.

**Acceptance Scenarios**:

1. **Given** user objects that conflict with gamepack objects, **When** the client sends a dry run with "takeOver" on, **Then** the result lists each object (type and name) with the mark "identical" or "different".
2. **Given** the same state, **When** the dry run ends, **Then** no object, no owner, and no backup has changed.
3. **Given** a dry run with "takeOver" off, **When** a user-object conflict exists, **Then** the result reports the conflict, as today, and lists no take-over.

---

### User Story 4 - Take-over in the managed update (Priority: P2)

The managed update (feature 148) accepts the same "takeOver" option. It passes the option to its install step. The managed update stops and starts queues as before. The option itself does not stop or start a queue.

**Why this priority**: Clients use the managed update as the normal path. They need the option there too.

**Independent Test**: Send a managed update with "takeOver" on over a gamepack with a conflicting user object. The update succeeds, and the report has the count of objects taken over.

**Acceptance Scenarios**:

1. **Given** a managed update with "takeOver" on and a user-object conflict, **When** the update runs, **Then** the update succeeds and the report has the count of objects taken over.
2. **Given** a managed update with "takeOver" off and a user-object conflict, **When** the update runs, **Then** the behavior is the same as today.
3. **Given** a managed dry run with "takeOver" on, **When** it ends, **Then** the report lists the objects to take over with the mark "identical" or "different". Nothing changes.

---

### Edge Cases

- A name has one owner. If the owner of a same-named object is another gamepack, the install is refused.
- The same gamepack is installed again (an upgrade): objects that it already owns are not "taken over". They follow the normal upgrade path. The count of objects taken over does not include them.
- A gamepack has two objects of different types with the same name: each object is matched by type and name. Only the matching pairs are taken over.
- The user content is identical to the gamepack content: the object is still taken over (owner changes). The dry run marks it "identical".
- The backup store cannot be written: the install fails and no object changes.
- A restore of a backup whose object was changed after the install: the restore is refused with a reason, and nothing changes (see Assumptions).
- The option is on, but no conflict exists: the install works as a normal install. The count is 0 and there is no backup.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The install request MUST accept an option "takeOver". The default MUST be off. With the option off, all install behavior MUST stay as it is today.
- **FR-002**: The managed update request MUST accept the same option "takeOver" with the same default and the same meaning. The managed update MUST pass it to its install step. The catalog install and the catalog update MUST accept it too. The managed rollback MUST NOT.
- **FR-003**: With the option on, a gamepack object that conflicts only with an object of the user MUST NOT be a conflict. The install MUST make the object part of the gamepack: the gamepack owns it and it has the content of the gamepack.
- **FR-004**: A taken-over object MUST keep its type and name. Links by name (queue templates, other sequences) MUST stay valid.
- **FR-005**: A conflict with an object of another gamepack MUST stay a conflict, with the option on or off.
- **FR-006**: An object of the user whose type and name are not in the gamepack MUST NOT change.
- **FR-007**: The install with the option on MUST be atomic. If any step fails, no object, owner, or registry entry MUST change.
- **FR-008**: Before the install replaces an object of the user, the platform MUST keep a backup of the object. One install MUST create one backup with one identifier for all replaced objects. The identifier MUST contain no path character.
- **FR-009**: The platform MUST give a way to restore a backup by identifier. A restore MUST put back the old content and make the objects objects of the user again. A restore MUST be atomic.
- **FR-010**: A dry run with the option on MUST list each object that the install would take over, with its type and name. For each object it MUST say whether the user content is "identical" to the gamepack content or "different".
- **FR-011**: A dry run MUST NOT change any data and MUST NOT create a backup.
- **FR-012**: The install result MUST have the number of objects taken over. The number MUST be 0 when the option is off or no object was taken over.
- **FR-013**: The install result MUST have the backup identifier when at least one object was replaced.
- **FR-014**: The option MUST NOT stop or start a queue. The caller does that, or the managed update does that as it does today.
- **FR-015**: The API documentation (OpenAPI) MUST describe the option, the dry-run output (the list and the marks), the count in the result, the backup identifier, and the restore operation.
- **FR-016**: The install gate MUST cover a take-over, so that only one install, rollback, or managed update runs at a time, as today.

### Key Entities

- **Take-over option**: A request flag. Off by default. It lets a gamepack take over user objects of the same type and name.
- **Taken-over object**: An object of the user that the gamepack now owns. It has the same type and name and the content of the gamepack.
- **Take-over backup**: A stored copy of every object that one install replaced. It has an identifier. A restore uses it.
- **Take-over entry (dry run)**: Type, name, and the mark "identical" or "different".

## Assumptions

- "Identical" means the canonical content of the user object equals the canonical content of the gamepack object.
- The platform removes a backup only on request or by the existing retention rules; this feature adds no new retention rule.
- A restore is refused with a reason if a backed-up object was changed or deleted after the install, or if the gamepack no longer owns the objects. A restore has exactly three checks: object_missing, object_changed, owner_changed.
- A restore does not stop or start queues.
- The feature adds no web UI change.
- A conflict gets the same status as today: a real install gives 422 and a dry run gives 200 with `valid: false`. This holds for a conflict with a user object when the option is off, and for a conflict with an object of another gamepack with the option on or off. The status 409 is not used for an install conflict.
- A restore keeps the backup file. After a restore, the backup stays on disk until a user removes it or an existing retention rule removes it. A second restore of the same backup is refused with the reason `owner_changed`, because the gamepack no longer owns the objects. The public restore route never forces. An internal forced restore exists for the managed update only (see the clarifications and FR-009). It skips the check `owner_changed` only.
- The version bump of `installer\versioning\version.override.json` is not a feature task. The pipeline does it before the push.
- The applier constructor accepts the take-over backup store. The foundation phase adds this, before any story tests, so the tests build the applier in one way.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can install a gamepack over 13 sequences and 131 commands of the same names in one call, with no delete step, and with 0 broken links by name.
- **SC-002**: With the option off, 100% of the existing install and managed update tests give the same result as before.
- **SC-003**: For every conflict with another gamepack, the install is refused in 100% of tests, with the option on or off.
- **SC-004**: After a forced failure at any install step, 0 objects differ from the state before the call.
- **SC-005**: After a restore, 100% of replaced objects have their old content and have the owner "user".
- **SC-006**: A dry run lists 100% of the objects that the real install then takes over, and changes 0 objects.
- **SC-007**: The result count of objects taken over equals the number of objects that the install replaced in every test.
- **SC-008**: No queue changes its run state because of the option in any test.
