# Feature Specification: Remove an installed gamepack record

**Feature Branch**: `156-remove-gamepack-record`
**Created**: 2026-10-17
**Status**: Draft
**Input**: User description: "FR-031 (issue #325): remove an installed gamepack record. Closes #325."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remove the record of a trial install (Priority: P1)

An operator installs a gamepack for a trial. The operator deletes the content items of the pack by hand. The list of installed gamepacks still shows the pack, with its last version. The operator needs one request that removes this record.

**Why this priority**: Today no route removes a record. A first install cannot be rolled back (`409 no_previous_version`). The record stays for ever. This is the full gap of issue #325.

**Independent Test**: Install a gamepack. Send the remove request for its name. Read the list of installed gamepacks. The pack is not in the list.

**Acceptance Scenarios**:

1. **Given** a gamepack is installed, **When** the operator removes the record of this gamepack, **Then** the request succeeds and `GET /api/gamepacks` does not list the gamepack.
2. **Given** a gamepack is installed and the operator deleted all its content items by hand, **When** the operator removes the record, **Then** the request succeeds and the list does not show the gamepack.
3. **Given** two gamepacks are installed, **When** the operator removes the record of one, **Then** the other gamepack stays in the list with no change.

---

### User Story 2 - Install the same name again as a first install (Priority: P1)

After the record is removed, the operator installs a gamepack with the same name. The install must not start from the removed version.

**Why this priority**: The owner link of the old record makes a new install start from a version that does not exist. This is the harm named in the issue.

**Independent Test**: Install a pack, remove its record, install a pack with the same name. The install is a first install: it has no previous version and it does not report an update.

**Acceptance Scenarios**:

1. **Given** the record of a gamepack was removed, **When** the operator installs a gamepack with the same name, **Then** the install is a first install and the list shows the new version.
2. **Given** the record of a gamepack was removed, **When** the operator installs a gamepack with the same name and an object of the same type and name exists as an object of the removed gamepack, **Then** the object belongs to the user (FR-013), so the existing conflict rule for user objects applies (a conflict, or a take-over with `takeOver=true`).
3. **Given** the record was removed, **When** the operator asks for a rollback of the same name, **Then** the answer is `404 not_found`.

---

### User Story 3 - Clear error for an unknown gamepack (Priority: P2)

The operator sends the remove request for a name that is not installed, or sends the same request twice.

**Why this priority**: The route must give a clear error and not a server error.

**Independent Test**: Send the remove request for a name that is not installed. The answer is `404` with the same error model as the other gamepack routes.

**Acceptance Scenarios**:

1. **Given** no gamepack has the name, **When** the operator removes this name, **Then** the answer is `404` with `result` `not_found` and nothing changes.
2. **Given** a record was removed, **When** the operator sends the same request again, **Then** the answer is `404` with `result` `not_found`.

---

### Edge Cases

- A content item of the pack (sequence, command, image) still exists and has the owner link at removal time. The removal keeps the item and removes its owner link. The item belongs to the user after the removal (FR-013).
- The removal and an install or rollback of the same gamepack run at the same time. The gamepack operations that change data must not overlap.
- A write fails while the record is removed. The record and the owner link must stay as before, and the answer is a server error with the same form as other gamepack routes.
- The name has unusual characters or letter case. The name matches as for the rollback route.
- A kept previous version of the pack exists. The removal also removes this kept copy.
- A take-over backup exists for the pack. The removal keeps the backup file. A restore of the backup then gives `409` with `owner_changed`, because the gamepack is not installed (existing restore rule, no change).

## Clarifications

### Session 2026-10-17

- Q: Which route shape and method? → A: `DELETE /api/gamepacks/{name}`, answer `204` (same family as `POST /api/gamepacks/{name}/rollback` and `DELETE keys/{fingerprint}`).
- Q: Does the removal delete the content items the pack installed? → A: No. It removes the record, the kept previous version, and the owner links only. Rationale: smallest scope, matches the issue, and it never destroys user data.
- Q: What happens to items that still carry the owner link? → A: They stay and become user-owned (the owner entry is removed). A later install of the same name then sees them as user objects, so the existing user-object conflict rule (or take-over) applies.
- Q: (Analyze F1, U1) Order of FR-013 and test of SC-004? → A: FR-013 now follows FR-012. SC-004 is met by FR-001 and checked by the contract test of FR-011. The other findings (F2, C1, U2, C2, D1) are LOW and plan or task level.
- Q: (Analyze loop 1: C1, I1, I2, A1) How to close the findings? → A: The task list MUST have its own task that checks that the diff against `origin/master` lists no web-ui file (FR-012), before the version bump task. The final test task runs the unit and contract gamepack tests only (the same projects as the plan). The domain unit test of the removal service method needs only the service task, not the endpoint task, so it can run in parallel with the route task. The unit test project is `tests\unit`.
- Q: Does the removal keep take-over backups? → A: Yes. The restore route then gives `409 owner_changed`, which is the existing rule.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST give the operator one route that removes the installed record of one gamepack, found by its name. The route is `DELETE /api/gamepacks/{name}`. A successful removal gives `204` with no body, as the other DELETE routes do.
- **FR-002**: After a successful removal, `GET /api/gamepacks` MUST NOT list the gamepack.
- **FR-003**: A removal MUST also remove the kept previous version of the gamepack, so that no data of the record stays.
- **FR-004**: A removal for a name that is not installed MUST give `404` with `result` `not_found`, in the error model of the other gamepack routes. It MUST NOT give a server error and MUST NOT change data.
- **FR-005**: After a successful removal, the owner link of the gamepack MUST be gone. A later install of the same name MUST be a first install and MUST NOT start from the removed version.
- **FR-006**: A removal MUST work when the content items of the pack no longer exist.
- **FR-007**: A removal MUST change all or nothing: if a write fails, the record and the owner link stay as before.
- **FR-008**: The route MUST use the same lock as install and rollback, so a removal does not overlap another gamepack change.
- **FR-009**: The route MUST NOT change install, dry run, signature, catalog, or rollback behaviour. A rollback of a gamepack with no kept previous version MUST still give `409 no_previous_version`.
- **FR-010**: The OpenAPI document MUST list the route with its summary, description, and the status codes it can give.
- **FR-011**: A contract test MUST cover the route: success, unknown name, list after removal, and a new install after removal.
- **FR-012**: The feature MUST NOT add UI, unless the existing gamepack UI needs a control to use the route. Any such control needs a reason in this spec. No such control is needed, so a final review of the diff MUST check that no UI file changed.
- **FR-013**: A removal MUST NOT delete or change any content item (sequence, command, image). It MUST remove every owner entry of the gamepack, also an entry whose item no longer exists. An item that still exists belongs to the user after the removal.

### Key Entities *(include if feature involves data)*

- **Installed gamepack record**: Name, version, change text, required API major version, kept previous version, and install time. The list route shows it.
- **Owner link**: The link between a content item (sequence, command, image) and the gamepack that installed it. It decides which gamepack owns an item and if an install is a conflict.
- **Take-over backup**: A stored copy of the old content of items that an install with `takeOver=true` replaced.

## Assumptions

- The operator is the single user of the service API. The route needs no new access rule beyond the other gamepack routes.
- The gamepack name is the key of the record, as for the rollback route.
- The route does not stop or start queues.
- Domain unit tests for the removal go in the unit test project (`tests\unit`, folder `Gamepacks`), in a class for the removal service method. The test for the conflict of a take-over or user-object install goes in the existing validator test class, not in the removal service test class.
- A unit test of the gate wait covers the lock use of FR-008: a removal waits while an install or rollback holds the lock.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of tests, a removed gamepack is absent from the list of installed gamepacks.
- **SC-002**: In 100% of tests, an install of the same name after a removal is a first install with no previous version.
- **SC-003**: A request for a name that is not installed gives a `404` answer, never a server error, in 100% of tests.
- **SC-004**: The operator removes a trial record in one request (FR-001), with no other step. Contract test (FR-011) shows this with one DELETE request.
- **SC-005**: The existing gamepack tests (install, dry run, rollback, catalog, signature) pass with no change.
