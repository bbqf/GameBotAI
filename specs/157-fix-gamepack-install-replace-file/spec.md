# Feature Specification: Fix gamepack install file replace failure

**Feature Branch**: `157-fix-gamepack-install-replace-file`  
**Created**: 2026-10-18  
**Status**: Draft  
**Input**: GitHub issue #333 - a real gamepack install answers 500 "Unable to remove the file to be replaced". Closes #333.

## Clarifications

### Session 2026-10-18

- Q: What does "names the file" mean in the error? -> A: The error includes the full path of the file and the object kind and id. Reason: the operator can find the file at once.
- Q: Does the fix change the `partial_restore` result name? -> A: No. The name and the HTTP status stay. The response gains a list of files in an unknown state. Reason: no API break.
- Q: Must the install be atomic for all 259 objects? -> A: No new guarantee. The install keeps its present backup-and-restore model. Reason: the issue asks only for a fix and for hardening.

### Session 2026-10-18 (loop 1, from analyze)

- Q (E1): How is SC-001 checked at full scale? -> A: One automated test installs a pack with 259 objects (images, sequences, commands, templates) on top of the same 259 existing objects. Reason: SC-001 names 259.
- Q (E2): Which kinds does the install test assert? -> A: All four kinds: sequences, commands, images, templates.
- Q (E3): Are the read-only file and mixed new/existing cases tested? -> A: Yes. One test has a read-only target. One install mixes new and existing objects.
- Q (I2): What is the format of an unknown-state entry? -> A: The object key (kind and id) and the full file path, for example `image:abc (C:\data\images\abc.png)`. One format for all kinds.
- Q (U2): Which errors does the retry cover? -> A: Only sharing and lock violations. Other errors fail at once and name the file. Reason: no long wait for errors that retry cannot fix.
- Q (D1): Reuse of the existing atomic writer? -> A: The plan must state why a new helper is needed, or reuse the existing one. The two other `File.Replace` users (reference image store, log writer) are out of install scope.
- Q (C1): Status tracking? -> A: The task list updates the spec Status line and specs/STATUS.md.

### Session 2026-10-18 (loop 2, from analyze)

- Q (I1): Which kinds can the error text name? -> A: sequence, command, image, template, and also the registry kinds `registry` and `previous` for the pack record files. Same text format for all.
- Q (U2): Where does the full path of a restore key come from? -> A: The install code must get the path for each kind from the store that wrote the file. If a store has no way to give the path, the task list adds a small read-only method to give it. The entry then holds the key and the path.
- Q (U1): Is an image error wrapped twice? -> A: No. The error has one prefix only, and a test checks this for images.
- Q (C1): Windows-only tests? -> A: Tests that need Windows lock behavior skip on other systems. The task list states the CI system.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a valid gamepack with take-over (Priority: P1)

An operator sends a real install of a valid gamepack (for example PNS 1.0.0: 12 sequences, 130 commands, 117 images, 1 seed template) with take-over turned on. The pack has 259 objects that already exist on the host. The install replaces all of them. The host runs Windows.

**Why this priority**: Release 1.0.0 cannot be installed now. There is no workaround.

**Independent Test**: Install a valid pack with take-over on a Windows host that already holds the same objects. The install must answer success, and every taken-over object must hold the pack content.

**Acceptance Scenarios**:

1. **Given** a valid pack whose objects already exist on the host, **When** the operator installs it with take-over on, **Then** the install succeeds and all taken-over objects hold the pack content.
2. **Given** the same pack and the same host, **When** the dry run runs first and then the real install runs, **Then** both agree: the dry run reports no problems and the real install succeeds.
3. **Given** a successful install, **When** the operator reads the lists of sequences, commands, images and templates, **Then** the counts match the pack and no stale or duplicate object remains.

---

### User Story 2 - Clear error when a file cannot be replaced (Priority: P1)

When a file really cannot be replaced (for example another process holds it), the operator needs to know which file failed.

**Why this priority**: The generic message "Unable to remove the file to be replaced" gives no way to find the cause.

**Independent Test**: Block one target file. Run the install. The error must name that file.

**Acceptance Scenarios**:

1. **Given** one target file that cannot be replaced, **When** the install runs, **Then** the error response names that file (its path or its object identity).
2. **Given** that failure, **When** the install ends, **Then** all other objects stay as they were before the install.

---

### User Story 3 - A failed install does not leave a partial state unreported (Priority: P2)

If a write fails and the restore step also fails, the operator must know which files are in an unknown state. A failed install must try to leave every object as before.

**Why this priority**: The first attempt of the issue left 13 sequences, 130 commands, 121 images and 7 templates. This is a risk on a production host. This story is a hardening goal only.

**Independent Test**: Force a write failure, and force a restore failure for a known file. The response must list that file as in an unknown state.

**Acceptance Scenarios**:

1. **Given** a write failure after some objects were already replaced, **When** the restore succeeds, **Then** all objects hold their old content and the result is `failed`.
2. **Given** a write failure and a restore failure, **When** the install ends, **Then** the response lists each file whose state is unknown.

---

### Edge Cases

- Target file is new (no old file exists): the install writes it without error.
- Target file is read-only or locked by another process: the error names the file.
- Pack has objects with and without existing files in the same install.
- Restore fails for more than one file: all of them are listed.
- A file is locked for a short time (sharing or lock violation): the install retries a short, fixed number of times, then fails with the error that names the file.
- A file fails with an error that is not a sharing or lock violation (for example access denied): the install does not retry. It fails at once and names the file.
- Write fails and restore works: the result is `failed`, and every object holds its old content.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A real install with take-over on of a valid pack MUST succeed on a Windows host and MUST replace every taken-over object (259 for PNS 1.0.0).
- **FR-002**: The install MUST give the same result as the dry run predicts for a valid pack: a pack that the dry run accepts MUST NOT fail only because of how files are replaced.
- **FR-003**: When a file cannot be replaced, the error response MUST name that file. The generic message alone is not enough.
- **FR-004**: When an install fails, the system MUST restore all objects that it already changed, and the result MUST be `failed` when the restore works.
- **FR-005**: When the restore step fails, the response MUST list each file that is in an unknown state.
- **FR-006**: The dry run, the gamepack file format and the signature rules MUST NOT change.
- **FR-007**: An automated test MUST cover the root cause that the investigation finds, and it MUST fail without the fix.
- **FR-008**: The file error message MUST use one fixed text, the same in the API contract and in the tests: `Cannot replace file '<full path>' for <kind> '<id>': <reason>`. `<kind>` is one of sequence, command, image, template, registry, previous. The kinds `registry` and `previous` are for the pack record files. The contract and the tests MUST use this exact template for all kinds. An image error MUST have one prefix only (no double wrapping), and a test MUST check this for images.
- **FR-008a**: The change MUST cover every public registry write that calls `WriteAtomic` in `GamepackRegistryStore`: `Save`, `Update` (kind `registry`) and `WritePrevious` (kind `previous`). The task list MUST re-check this list with a search for `WriteAtomic` and `File.Replace` in the gamepack code, so that no write is missed.
- **FR-009**: Each entry in the list of files in an unknown state MUST use one format for all kinds: the object key (kind and id) and the full file path, for example `image:abc (C:\data\images\abc.png)`. The install code MUST get the full path of each restore key from the store that wrote the file. If a store has no way to give the path, the task list MUST add a small read-only method to give it. The `partial_restore` result name and its HTTP status MUST NOT change. The response gains this list only.
- **FR-010**: The install MUST retry a file replace only for sharing and lock violations, with a short, bounded wait. Other errors MUST fail at once and name the file (FR-003).
- **FR-011**: An automated test MUST install a pack of 259 objects (images, sequences, commands, templates) on top of the same 259 existing objects, and MUST assert the result for all four kinds. This test checks SC-001.
- **FR-012**: Automated tests MUST also cover: a read-only target file; an install that mixes new and existing objects; and a contract test for the result `failed` when a write fails and the restore works (all objects hold their old content). Tests that need Windows lock behavior MUST skip on other systems. The task list MUST state the CI system. The 259-object test (FR-011) MAY use a synthetic pack of 259 objects. It does not need the signed PNS 1.0.0 file.
- **FR-013**: The plan MUST state why the install needs a new file-replace helper, or MUST reuse the existing `AtomicFileWriter`. If it adds a new helper, the plan MUST give the reason.
- **FR-014**: The task list MUST include a task that updates the Status line of this spec and the feature entry in `specs/STATUS.md`.

### Key Entities

- **Gamepack**: A signed bundle of sequences, commands, images and templates.
- **Taken-over object**: An object that already exists on the host and that the pack replaces.
- **Install outcome**: The result of an install: success, `failed`, or `partial_restore`, with a message and the list of problem files.

## Assumptions

- The root cause is in the way the install replaces existing files on Windows. The investigation in the plan step confirms it.
- The "partial restore" result stays as a possible outcome, but only with the list of files in an unknown state (FR-005).
- No web UI change is in scope.
- Out of scope: `ReferenceImageStore` and `AtomicFileWriter` (and the log writer) also use `File.Replace`. This fix does not change them. If the plan reuses `AtomicFileWriter` without a change, that is allowed. A change to its behavior for other callers is not allowed.
- Task rule: tasks that touch the same file are not parallel. The task list MUST NOT mark such tasks `[P]`. A test task depends on the implementation tasks that it tests (for example, the 259-object test depends on the install changes). Tests marked `[P]` can be written in parallel, but they are red (they fail) until the implementation task is done. This is expected for test-first work.
- Retry numbers: the replace retry makes 5 tries in total, and the wait between tries grows from 50 ms to 800 ms (FR-010).
- Call sites: the task list re-checks all call sites of `WriteFailed` (the endpoint helper in `GamepacksEndpoints`) during the task, to make sure each one carries the new file list and message. This spec does not list them, because the list can change.
- Test scale: the 259-object test (FR-011) uses a synthetic pack of 259 objects. This is accepted as proof for SC-001.
- The install keeps its present backup-and-restore model. There is no new all-or-nothing guarantee for 259 objects.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real install of the PNS 1.0.0 pack with take-over on succeeds on a Windows host, and 259 of 259 taken-over objects are replaced.
- **SC-002**: In a forced failure test, 100% of error responses for a file that cannot be replaced name that file.
- **SC-003**: In a forced restore failure test, 100% of files in an unknown state are listed in the response.
- **SC-004**: The automated test for the root cause fails before the fix and passes after it.
