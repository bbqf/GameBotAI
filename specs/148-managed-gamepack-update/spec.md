# Feature Specification: Managed gamepack update

**Feature Branch**: `148-managed-gamepack-update`  
**Created**: 2026-10-16  
**Status**: Draft  
**Input**: User description: "FR-026: managed gamepack update. GitHub issue #314. Closes #314."

## Clarifications

### Session 2026-10-16

- Q: How long does the platform wait for a queue to stop? → A: The limit is a fixed 60 seconds for each queue. A queue that does not stop in this time fails the step (FR-010).
- Q: Which entry does a new sequence get in a template? → A: One entry for each new sequence. The entry has no schedule and no parameter values. The enabled flag is off. The user sets the schedule later (FR-007).
- Q: What is a "new sequence"? → A: A sequence that the new version has and the installed version does not have. A first install has no new sequence for booking, because no template references the gamepack (FR-007).
- Q: Does the restore return the queue settings from the backup? → A: Yes. The platform imports the backup (feature 145) for the affected templates. It also restores the previous version with the rollback data that exists (FR-010).
- Q: What is the HTTP result when the flow fails and the restore works? → A: The report has the status "failed", "restored: true", and a failure reason. A bad request (source, signature, scope) gets a client error. A concurrent call gets a conflict. A step that fails after the start gets a failed report with a restore (FR-016).

### Session 2026-10-16 (loop 1)

- Q: What happens when the new version equals the installed version? → A: The platform stops no queue and changes no template. It returns the result "done" with an empty list of stopped queues and no backup identifier. A test covers this case.
- Q: What does a restore do for a first install? → A: A first install can fail only before or during the install step. The platform returns to the state before the call: no gamepack installed. If the install step wrote files, the platform removes them. The report states the restore. A test covers this case (SC-004).
- Q: What status does a dry run give for a missing template in the list? → A: The same status as a normal call: a client error with the reason "template_not_found". The report also lists all other problems.
- Q: What if two managed calls for different gamepacks affect the same queue? → A: The platform allows only one managed call at a time on the instance. A second call gets the conflict reason "update_in_progress", for any gamepack (FR-015).
- Q: What scope does the backup have? → A: The backup holds the queue templates that the update affects (FR-005).
- Q: Which problems does a dry run report together? → A: At least source problems (file or catalog entry), signature problems, scope problems (missing template), and conflict problems (FR-013).
- Q: Does a test check that the report has all items? → A: Yes. A test checks that the report has every item of FR-011 (SC-006).
- Q: Where does the dry-run flag belong in the tasks? → A: The request model, the route parameters, and the `dryRun` flag exist from the foundation. Only the dry-run plan logic belongs to User Story 4 (see Assumptions).
- Q: Are route mapping and handler logic two tasks? → A: No. Both edit the same file. They are one unit of work (see Assumptions).

### Session 2026-10-16 (loop 2)

- Q: Which status does a dry run give for a bad file? → A: A dry run gives 200 for file, signature, and validation problems. It gives a client error only for a malformed request. A malformed request has a bad body, a missing part, or a missing `name` or `version`. A test covers this case (FR-013).
- Q: What status does a dry run give for a missing template and a signature problem together? → A: The client error "template_not_found" wins. The report keeps all problems.
- Q: What is the shape of an error body? → A: An error body has a "reason" field with one reason name. The report field "result" has only the values done, planned, failed, and rejected. The reason list includes "too_large" and "not_configured" (FR-016).
- Q: Is a call done when a queue does not restart? → A: Yes. The report result is "done". The report lists the queue in "notRestartedQueues". SC-002 counts a queue in this list as reported (FR-009).
- Q: What does the restore do when a managed rollback fails after the rollback worked? → A: The platform puts back the installed state that it recorded before the call. It also imports the backup. It does not call the rollback a second time (FR-010).
- Q: What backup does the platform write when the update affects no queue template? → A: No backup. The report has no backup identifier.
- Q: What is the backup identifier? → A: A time stamp and the validated gamepack name. It has no path character (FR-005).
- Q: How does the platform find "same version"? → A: It compares the version text of the file with the installed version text. The match must be exact. The result is "done" with no change.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Update a gamepack in one call (Priority: P1)

A client must update a gamepack on an instance. The client sends one request. The source is a gamepack file or a catalog entry (`name` and `version`). The platform does the full flow in this order. It stops the affected queues. It backs up the queue settings. It installs the version. It books disabled entries for new sequences. It restarts the queues that it stopped. The platform returns one report of all that it did.

**Why this priority**: This is the core value. Today the client must do each step by hand, in the right order. A failure in the middle can leave queues stopped.

**Independent Test**: Install version 1 of a gamepack. Make two queue templates that reference its sequences. Start one queue. Send an update to version 2 that adds one sequence. The report shows the stopped queue, the restarted queue, the backup identifier, and the new disabled entries. Version 2 is installed.

**Acceptance Scenarios**:

1. **Given** a queue that runs and whose template references a sequence of the gamepack, **When** the client sends an update with a gamepack file, **Then** the platform stops the queue, stores a backup, installs the version, and starts the queue again.
2. **Given** a catalog is configured and lists the version, **When** the client sends an update with `name` and `version`, **Then** the platform gets the file from the catalog and does the same flow.
3. **Given** a queue that does not run and whose template references a sequence of the gamepack, **When** the update runs, **Then** the platform does not start that queue. The platform books the new entries in its template.
4. **Given** a queue that runs and whose template does not reference a sequence of the gamepack, **When** the update runs, **Then** the platform does not stop that queue.
5. **Given** the client gives a list of queue templates, **When** the update runs, **Then** the platform stops only the affected queues in that list. It changes only the affected templates in that list.
6. **Given** the signature policy is "required" and the file has no valid signature, **When** the client sends the update, **Then** the platform rejects it with the signature reason. The queues and the installed version stay as they were.

---

### User Story 2 - New entries stay disabled (Priority: P1)

For each new sequence of the new version, the platform adds an entry to each affected queue template. The entry is always disabled. The platform keeps all entries that exist, with their enabled flag, schedule, and parameter values. The platform never enables an entry by itself. Only the user knows what an account can run.

**Why this priority**: An enabled entry can run an action that the account must not run. This rule protects the user.

**Independent Test**: Update to a version with one new sequence. In each affected template, the new entry exists with the enabled flag off. All old entries are the same as before.

**Acceptance Scenarios**:

1. **Given** the new version adds a sequence, **When** the update ends, **Then** each affected template has one new disabled entry for it. The report lists each new entry.
2. **Given** a template has entries that are enabled, with a schedule and parameter values, **When** the update ends, **Then** those entries have the same enabled flag, schedule, and parameter values.
3. **Given** a template already has an entry for the new sequence, **When** the update ends, **Then** the platform does not add a second entry. It does not change the entry that exists.
4. **Given** the new version removes a sequence that a template entry references, **When** the update ends, **Then** the platform keeps that entry unchanged. The report lists it as an entry whose sequence was removed.

---

### User Story 3 - Safe failure and restore (Priority: P1)

If a step fails, the platform restores the previous gamepack version and the previous queue state. The report says that a restore happened. The report shows the result of the restore.

**Why this priority**: The requester updates installations that it does not control. A failed update must not leave queues stopped. It must not leave a template without its entries.

**Independent Test**: Make the install step fail after the queues stop (for example, with a file that is not valid). Check that the old version is installed. Check that the queue templates are the same as before. Check that the stopped queues run again. The report states the failure and the restore.

**Acceptance Scenarios**:

1. **Given** the install fails, **When** the update ends, **Then** the previous version is installed. The queue templates are as before. The queues that the platform stopped run again.
2. **Given** the step that books entries fails, **When** the update ends, **Then** the platform restores the previous version. It restores the queue settings from the backup. It restarts the queues that it stopped.
3. **Given** a restore step also fails, **When** the update ends, **Then** the report lists each step that failed. It lists each object that the platform could not restore.
4. **Given** the update failed and the platform restored the state, **When** the client reads the report, **Then** the report states that a restore happened.
5. **Given** a queue does not stop in the time limit, **When** the update runs, **Then** the update fails before any change to the gamepack or the templates. The queues that the platform stopped run again.

---

### User Story 4 - Dry run (Priority: P2)

The client sends the same request with `dryRun`. The platform returns the plan and all problems. It changes nothing. It stops no queue, stores no backup, and installs nothing.

**Why this priority**: The client can check the effect before it takes a risk. It depends on the flow of story 1.

**Independent Test**: Send an update as a dry run. The report lists the queues that the update would stop. It lists the objects that the update would create, update, and remove. It lists the entries that the update would book. Then check that the installed version, the queues, and the templates did not change. Check that no backup exists.

**Acceptance Scenarios**:

1. **Given** a valid request, **When** the client sends it with `dryRun`, **Then** the report lists the planned stops, installs, and new disabled entries. Nothing changes.
2. **Given** a request with problems (for example, a missing catalog entry or an invalid signature), **When** the client sends it with `dryRun`, **Then** the report lists all problems that the platform finds.

---

### User Story 5 - Managed rollback (Priority: P2)

The client sends a rollback with a `managed` option. The platform does the same flow as an update. The install step is the rollback to the previous version. The report has the same items. A rollback without the `managed` option works as before.

**Why this priority**: A rollback also replaces sequences that queues use.

**Independent Test**: After an update, send a managed rollback. The queues stop. The backup is stored. The previous version returns. The queues run again. The report has the same items as an update report.

**Acceptance Scenarios**:

1. **Given** a gamepack with a previous version, **When** the client sends a rollback with `managed`, **Then** the platform stops the affected queues and stores a backup. It rolls back. It books disabled entries for sequences that the previous version adds. It restarts the queues.
2. **Given** the rollback fails, **When** the platform ends the call, **Then** it restores the state before the call. The report says so.
3. **Given** a rollback without the `managed` option, **When** the client sends it, **Then** the behavior that exists does not change.
4. **Given** a managed rollback with `dryRun`, **When** the client sends it, **Then** the platform changes nothing. It returns the plan.

---

### Edge Cases

- The gamepack is not installed yet. The platform installs it. It stops no queue and books no entries, because no template references its sequences.
- The new version equals the installed version. The platform returns "done" with no change. It stops no queue.
- A queue template in the optional list does not exist. The platform rejects the request with a clear reason before any change.
- A queue template in the optional list does not reference a sequence of the gamepack. The platform ignores it. The report lists it as not affected.
- Two managed calls start at the same time, for any gamepacks. The platform rejects the second call with a clear reason.
- The restart step fails for one queue. The report lists the queue as not restarted. The platform still restarts the other queues.
- The catalog is not configured, or it does not list the version. The platform rejects the request with the catalog reason before any queue stops.
- The user stopped a queue. The platform does not start it.
- The update affects no queue template. The platform writes no backup.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST give one update call. The call takes a source and a `dryRun` option. The source is a gamepack file in the request, or a catalog entry with `name` and `version`.
- **FR-002**: The platform MUST do the update steps in this order: find affected queues, stop them, store a backup, install, book disabled entries, restart.
- **FR-003**: The platform MUST stop only the queues that run and whose template references a sequence of the gamepack. The platform MUST wait until each queue is stopped. The platform MUST record which queues it stopped.
- **FR-004**: The platform MUST accept an optional list of queue templates. The list limits the stops and the entry changes to these templates.
- **FR-005**: The platform MUST store an export of the queue settings (feature 145) as a backup before the install. The backup holds the affected queue templates only. The platform MUST return the identifier of the backup. The identifier is a time stamp and the validated gamepack name. It has no path character. If the update affects no queue template, the platform MUST NOT write a backup.
- **FR-006**: The platform MUST install the version with the install flow that exists (feature 143). The platform MUST apply the signature policy of feature 146.
- **FR-007**: The platform MUST add one disabled entry to each affected queue template for each new sequence of the new version. The platform MUST NOT enable any entry.
- **FR-008**: The platform MUST keep each entry that exists, with its enabled flag, schedule, and parameter values. The platform MUST NOT add a second entry for a sequence that a template already references.
- **FR-009**: The platform MUST restart the queues that it stopped, and only these queues. A queue that does not restart goes in the report list "notRestartedQueues". The result stays "done".
- **FR-010**: If a step fails, the platform MUST restore the previous gamepack version and the previous queue state. The platform MUST restart the queues that it stopped. The platform MUST state the restore in the report. A failed managed rollback does not call the rollback a second time. The restore puts back the installed state that the platform recorded before the call. It also imports the backup.
- **FR-011**: The report MUST list these items: the stopped queues, the restarted queues, the backup identifier, the created, updated, and removed objects, the new disabled entries, and the template entries whose sequence the new version removed.
- **FR-012**: The platform MUST keep a template entry whose sequence the new version removed. The platform MUST list it in the report.
- **FR-013**: With `dryRun`, the platform MUST return the plan and all problems that it finds. The platform MUST NOT stop a queue, store a backup, install, or change a template. A dry run gives 200 for file, signature, and validation problems. It gives a client error only for a malformed request or a missing template.
- **FR-014**: The platform MUST give a managed rollback in the same form. The rollback call takes a `managed` option. With the option, the flow of FR-002 to FR-013 applies, and the install step is the rollback. Without the option, the behavior that exists stays.
- **FR-015**: The platform MUST reject a second managed call while another managed call runs on the instance, for any gamepack.
- **FR-016**: The platform MUST give each failure a clear reason name. An error body has one field "reason". The call MUST NOT end with an unhandled server error for a known failure.
- **FR-017**: The platform MUST NOT host a catalog. The platform MUST NOT change the catalog protocol of feature 147.
- **FR-018**: The platform finds "same version" with an exact match of the version text of the file and the installed version text.

### Key Entities *(include if feature involves data)*

- **Managed update request**: the source (file, or catalog `name` and `version`), the `dryRun` flag, and the optional list of queue templates.
- **Managed update report**: the result of the call (done, planned, failed, or rejected), the stopped and restarted queues, the queues not restarted, the backup identifier, the created, updated, and removed objects, the new disabled entries, the entries with a removed sequence, the problems, and the restore state.
- **Affected queue**: a queue whose template references at least one sequence of the gamepack.
- **Backup**: the stored export of the affected queue templates that the platform made before the install.
- **Disabled entry**: a queue template entry for a new sequence, with the enabled flag off.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A client updates a gamepack with one call. No other call is necessary for the steps in the flow.
- **SC-002**: After a successful update, 100% of the queues that the platform stopped run again or are listed as not restarted. 0 queues that the platform did not stop are changed.
- **SC-003**: After any update, 0 new entries are enabled. 100% of the entries that existed keep their enabled flag, schedule, and parameter values.
- **SC-004**: After a failed update, the installed version and the queue templates equal the state before the call. The report states the restore.
- **SC-005**: A dry run changes nothing. The installed version, the templates, and the queue states are the same before and after.
- **SC-006**: Each report contains all the items in FR-011.

## Assumptions

- The install, rollback, export, import, signature, and catalog features that exist are the building blocks. This feature does not change their rules.
- A queue "references a sequence" when an entry of its template refers to that sequence.
- The API token rule that exists protects the new calls. No new role is added.
- The wait time for a queue to stop has a fixed limit. A queue that exceeds it counts as a failed step.
- The request model, the route parameters, and the `dryRun` flag exist from the foundation. Only the dry-run plan logic belongs to User Story 4.
- Route mapping and handler logic are one unit of work, not two.
- The update does not run in the web interface. This feature adds no web interface change.
