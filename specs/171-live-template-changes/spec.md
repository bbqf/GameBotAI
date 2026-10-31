# Feature Specification: Apply template changes to a running queue

**Feature Branch**: `171-live-template-changes`  
**Created**: 2026-10-30  
**Status**: Implemented (iterated by 173)  
**Input**: User description: "As an operator I want to be able to apply template changes without interrupting the queue run. Explicitly I want to be able to enable/disable individual sequences, change their parameters, adding and removing the sequences. Changing their order of execution is desired, but not required if it increases complexity by a large amount. What is important is that the sequences schedules do not change without a direct instruction to do so. In order to make the changes atomic, we will need to introduce a queue pause in between sequences, i.e. if paused, the queue has to finish a running sequence if any and then pause and wait to be resumed."

## Clarifications

### Session 2026-10-30

- Q: What does "pause" mean for the queue? → A: A pause request is a soft pause. The queue lets the firing group that runs now finish. Then the queue starts no new firing group until the operator resumes it (FR-001, FR-002).
- Q: What happens when the operator asks for a pause while the queue waits in a "Pause when idle" hold? → A: The queue is paused at once. The hold stops early and the game stays backed out. On resume, the system brings the game to the front and goes on (FR-036).
- Q: Which word does the operator see for a firing group? → A: "Sequence". The UI text says "sequence". The spec uses "firing group" for the exact rule (FR-003, FR-033).
- Q: Is the pause a stop of the run? → A: No. The run stays active. The queue keeps its run state, its cycle position, and all schedules. Resume continues the same run (FR-005, FR-006).
- Q: What does "schedules do not change" mean? → A: A schedule is the next due time and the schedule type of an entry. An apply action never changes the schedule of an entry that the operator did not change. The apply action never resets a timer (FR-008, FR-009).
- Q: Is the order change in scope? → A: It is a stretch goal (P3). The feature is complete and useful without it. If it adds large complexity, the team may defer it (User Story 4).
- Q: How does the system match a running-queue entry to a template entry? → A: Each template entry gets a stable entry ID (option A). The ID is created once and the running queue keeps it (FR-017). The upgrade of existing templates is a manual, explicit action, not a silent change (FR-025 to FR-029). The release increases the minor version (FR-031).
- Q: Does a time limit end a paused run? → A: No. The time limit of feature 170 belongs to one sequence (`watchdogTimeoutMs`). It is not a limit for a queue run. A paused queue has no running sequence, so no limit applies. A pause has no end of its own (FR-032). (An earlier answer said that the limit ends a paused run. It was replaced after a check of feature 170.)
- Q: What does the pause request do to the sequence time limit? → A: Nothing. The limit still applies to the sequence that runs when the pause is requested. The pause starts after that sequence ends, also when the limit ends it (FR-032).
- Q: What happens to the schedule state when an upgrade gives IDs to the entries of a running queue? → A: The system renames the keys of the consumed state. All state stays (FR-027).
- Q: Does the pause stop the device recovery? → A: No. The liveness watch and the device recovery keep running. The pause only stops the start of new firing groups (FR-034).
- Q: What happens when the operator asks for a pause while a failure-policy pause is in force? → A: The system accepts the request. Health reports `failurePolicy` first. Resume releases the operator pause first (FR-035).
- Q: Where does the system record pause, resume, and apply? → A: In the run log, as structured log lines. The apply line lists the applied differences. No new storage and no new UI (FR-022).
- Q: What happens to a pause when the service restarts while the queue is "pausing" or "paused"? → A: The restart works as today (option A). The run ends and the pause is dropped. The queue is Stopped, or it starts a fresh run that is not paused if `ResumeOnServiceStart` is on (FR-007). (An earlier answer, "comes up paused", was replaced because a run does not survive a restart in the product today.)
- Q: What name does the operator see for the new pause, given the existing "Pause when idle" setting (feature 073)? → A: "Pause" and "Resume" (option A). The new state shows as "Paused (between sequences)". The old setting keeps its name "Pause when idle" and is a different thing (FR-033).
- Q: Which templates can the operator apply? → A: The template that the queue is linked to. The operator edits the template, then applies it to the running queue. The operator chooses which differences to apply (FR-010, FR-011).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pause the queue between sequences (Priority: P1)

An operator has a queue that runs. The operator wants to make a change with no sequence that runs at the same time. The operator asks the queue to pause. If a sequence runs, it finishes first. Then the queue waits. The operator sees that the queue is paused. When the operator resumes, the queue continues the same run.

**Why this priority**: The pause makes the change atomic. Without it, the other stories are not safe. It also has value alone: the operator can hold the queue for any reason.

**Independent Test**: Start a queue with a long running sequence. Request a pause. Check that the running sequence finishes, that no new sequence starts, and that the queue shows "paused". Resume. Check that the queue starts the next due sequence.

**Acceptance Scenarios**:

1. **Given** a queue that runs a sequence, **When** the operator requests a pause, **Then** the sequence finishes without interruption, the queue starts no other sequence, and the queue shows the state "paused".
2. **Given** a queue that waits between sequences, **When** the operator requests a pause, **Then** the queue is paused at once.
3. **Given** a paused queue, **When** the operator resumes it, **Then** the queue continues the same run and starts the next due sequence.
4. **Given** a queue that has a pause request but the sequence still runs, **When** the operator cancels the pause request, **Then** the queue continues with no pause.
5. **Given** a paused queue, **When** the operator looks at the queue, **Then** the queue shows how long it has been paused.

---

### User Story 2 - Change the entries of a running queue (Priority: P1)

An operator edits the template of a running queue. The operator wants the running queue to use the change without a restart. While the queue is paused, the operator applies the change. The operator can enable or disable an entry, change the parameter values of an entry, add an entry, and remove an entry. After the apply action, the operator resumes the queue.

**Why this priority**: This is the main value of the feature. The operator no longer stops the run to change a template.

**Independent Test**: Run a queue with three entries. Pause it. Disable one entry, change the parameters of a second entry, add a fourth entry, and remove the third entry. Apply the change. Resume. Check that the queue runs the new set of entries and that the run did not restart.

**Acceptance Scenarios**:

1. **Given** a paused queue and a template where an entry is now disabled, **When** the operator applies the change, **Then** the queue does not run that entry after the resume.
2. **Given** a paused queue and a template where a disabled entry is now enabled, **When** the operator applies the change, **Then** the queue runs that entry according to its schedule after the resume.
3. **Given** a paused queue and a template where the parameter values of an entry changed, **When** the operator applies the change, **Then** the next run of that entry uses the new values.
4. **Given** a paused queue and a template with a new entry, **When** the operator applies the change, **Then** the queue adds the entry with the schedule that the template states.
5. **Given** a paused queue and a template that no longer has an entry, **When** the operator applies the change, **Then** the queue removes the entry and does not run it again.
6. **Given** a queue that is not paused, **When** the operator tries to apply a change, **Then** the system refuses the action and tells the operator to pause the queue first.
7. **Given** a template with several differences, **When** the operator applies the change, **Then** the queue gets all the selected differences together or none of them.

---

### User Story 3 - Schedules stay the same unless the operator says otherwise (Priority: P1)

An operator has entries with schedules that are already in progress (for example, "every 4 hours" with 3 hours gone, or "daily at 06:00"). The operator applies a change to a different entry. The schedules of the other entries do not change. If the operator wants a schedule change, the operator states it directly.

**Why this priority**: An unwanted schedule reset can cause an entry to run too early, too late, or twice. The operator names this as the most important rule.

**Independent Test**: Run a queue with an interval entry and a daily entry. Let time pass so that both have a partial wait. Pause. Change the parameters of another entry and apply. Resume. Check that the two entries keep the same next due times as before the pause (a pause that is long can make an entry due on the resume, but the due time does not move).

**Acceptance Scenarios**:

1. **Given** an entry with a schedule in progress that the change does not touch, **When** the operator applies the change, **Then** the next due time of the entry is the same as before.
2. **Given** an entry whose parameters change but whose schedule does not change, **When** the operator applies the change, **Then** the next due time of the entry stays the same.
3. **Given** a template where the schedule of an entry changed (type, time of day, or offset), **When** the operator applies the change and chooses to include the schedule change, **Then** the entry gets the new schedule and a new next due time.
4. **Given** a template where the schedule of an entry changed, **When** the operator applies the change and does not choose the schedule change, **Then** the entry keeps its current schedule and the next due time.
5. **Given** an entry that is due while the queue is paused, **When** the operator resumes, **Then** the entry runs at once as an entry that is late. The due time is not moved.
6. **Given** a new entry, **When** the operator applies the change, **Then** the schedule of the new entry starts as the template states and the schedules of old entries do not change.

---

### User Story 4 - Change the order of the entries (Priority: P3)

An operator wants to move an entry earlier or later in the run order of a running queue. The operator changes the order in the template and applies it while the queue is paused. The schedules do not change.

**Why this priority**: The operator wants this but does not require it. If it adds large complexity, the team can defer it. The other stories do not depend on it.

**Independent Test**: Run a queue with three entries in one order. Pause. Move one entry. Apply. Resume. Check that the queue runs the entries in the new order and that no schedule changed.

**Acceptance Scenarios**:

1. **Given** a paused queue and a template with a new entry order, **When** the operator applies the change, **Then** the queue runs the entries in the new order after the resume.
2. **Given** an order change, **When** the operator applies it, **Then** the next due times of all entries stay the same.

---

### User Story 5 - Review the change before it is applied (Priority: P2)

An operator wants to see what the apply action does before it does it. The system shows the list of differences between the running queue and the template: entries to add, remove, enable, disable, parameter changes, schedule changes, and order changes. The operator selects which differences to apply.

**Why this priority**: The operator cannot be sure about the result without a preview. This also supports the rule that a schedule changes only on a direct instruction.

**Independent Test**: Pause a queue. Edit the template in several ways. Open the apply action. Check that the list shows each difference with its type. Select a part of them and apply. Check that only the selected differences are in the queue.

**Acceptance Scenarios**:

1. **Given** a template that differs from the running queue, **When** the operator opens the apply action, **Then** the system lists each difference with its type and the entry name.
2. **Given** a list of differences, **When** the operator clears a difference and applies, **Then** the queue does not get that difference.
3. **Given** a template that does not differ from the running queue, **When** the operator opens the apply action, **Then** the system shows that there is nothing to apply.
4. **Given** a schedule change in the list, **When** the operator views it, **Then** the list shows it as a schedule change that is not selected by default.

---

### Edge Cases

- A pause is requested but the running sequence takes a long time: the queue shows "pausing" until the sequence ends, and the operator can cancel the pause request.
- The queue run ends (a stop, or an error) while a pause is requested: the pause request clears and the queue does not remain "paused" for a run that is not active.
- The operator removes an entry that is the running sequence: the pause request waits for the sequence to finish, so this case does not occur in a paused queue. The removed entry never runs again.
- The operator disables an entry that is due: the entry does not run. If the operator enables it again later, it keeps its stored due time, and the queue runs it as late if the time passed.
- An entry that has the same sequence twice: the system matches entries by a stable identity, not only by sequence name, so the operator can change one and keep the other.
- A new parameter value is not valid for the sequence: the apply action refuses the whole change and the queue stays unchanged (atomic).
- The template is edited again while the operator reviews the list of differences: the system detects that the template changed, discards the old list, and asks the operator to review again.
- The service restarts while the queue is "pausing" or "paused": the run ends, as it does for any restart. The pause is not stored. The queue is Stopped, or it starts a fresh run that is not paused if `ResumeOnServiceStart` is on. An apply action that is not yet done is lost, and the operator must start it again.
- Two operators apply a change at the same time: the system accepts one change and refuses the other with a clear message.
- The sequence time limit (feature 170) passes while a pause is requested: the limit ends the running sequence as it does today. Then the pause starts.
- The operator pauses a queue and forgets: the queue stays paused with no limit of its own. No time limit ends a paused run, because the time limit of feature 170 belongs to one sequence. The system shows the paused time so the operator can see it.
- The device is not live, or a device recovery runs, while the queue is paused: the liveness watch and the recovery keep running. The pause only stops the start of new firing groups.
- The operator asks for a pause while a failure-policy pause (feature 087) is in force: the system accepts the request. Health reports `failurePolicy` first. A resume releases the operator pause first. A second resume releases the failure-policy pause.
- The queue has a "cycle" setting (run again from the start): a pause between sequences keeps the cycle position, and the apply action keeps the position of the cycle.
- A queue has "Pause when idle" on and is paused between sequences: the two do not interact, except in the case that follows. The "Pause when idle" setting acts only on idle gaps between firings. While the queue is paused between sequences, the system does not start sequences and does not change the game state on its own.
- The operator asks for a pause while the queue waits in a "Pause when idle" hold: the queue is paused at once. The hold stops early. The game stays backed out, and the system does not bring it to the front while the queue is paused. When the operator resumes, the system brings the game to the front and the queue goes on.
- A pause request for a queue that does not run: the system refuses it with a clear message.
- A template that "needs upgrade" is used by a running queue: the queue keeps running as before. The operator pauses it only if the operator wants to apply a change. The apply action stays refused until the operator upgrades the template.
- The running queue has entries that do not match the template at upgrade time (for example, the operator edited the queue by hand): the system shows the entries that it cannot match and does not guess. The operator decides before the upgrade is applied.
- The operator imports or exports queue settings (feature 145): the export carries the entry IDs. An import of a document with no entry IDs creates a **new** template with new IDs. An import over a stored template that "needs upgrade" creates no ID, and an import over a template that has IDs keeps the ID of the entry at the same position and sequence. A difference in the IDs alone is not a change.
- A gamepack seed template (feature 150) has no entry IDs: the system creates IDs when it creates the seed template. An existing seed template is never changed by the gamepack (rule of feature 150), so it uses the manual upgrade.

## Requirements *(mandatory)*

### Functional Requirements

**Pause**

- **FR-001**: The operator MUST be able to request a pause of a running queue.
- **FR-002**: When a pause is requested, the queue MUST let the running firing group finish without interruption and MUST NOT start any new firing group until the operator resumes. If no firing group runs, the queue MUST pause at once. A firing group is one scheduled sequence and the helper sequences of that firing (before-each-run and every-step sequences). The pause waits for the whole group to end.
- **FR-003**: The queue MUST show one of these states: running, pausing (a pause is requested and a firing group still runs), and paused. The UI text says "sequence" for a firing group, for example "Pausing (waiting for the running sequence)".
- **FR-004**: The operator MUST be able to cancel a pause request while the queue is "pausing".
- **FR-005**: The operator MUST be able to resume a paused queue. The resume continues the same run and does not restart it.
- **FR-006**: A pause MUST NOT change the run state, the cycle position, the schedules, or the history of the queue.
- **FR-007**: The pause state belongs to the run. A pause request and the states "pausing" and "paused" MUST clear when the run ends for any reason, including a service restart. The restart rules of the product (including `ResumeOnServiceStart`) MUST NOT change. A queue that starts again after a restart MUST NOT be paused.

- **FR-032**: The pause MUST NOT change the time limit of a sequence (feature 170). The limit still applies to the sequence that runs when the pause is requested. The pause starts after that sequence ends, also when the limit ends it. No time limit ends a paused run, because a paused queue has no running sequence.
- **FR-033**: The web UI, the service API, and the documents MUST use "Pause" and "Resume" for this feature, and MUST show the state as "Paused (between sequences)". They MUST NOT use these words for the "Pause when idle" setting of feature 073, which keeps its name. The documents MUST state the difference between the two in one short paragraph. The new pause MUST NOT change the behavior of "Pause when idle", except as FR-036 states. In the UI text, the word "sequence" means a firing group (FR-003).
- **FR-034**: While the queue is paused or pausing, the device liveness watch (feature 106) and the device recovery MUST keep running. The pause MUST only stop the start of new firing groups.
- **FR-035**: The system MUST accept a pause request while a failure-policy pause (feature 087) is in force. Health MUST report `failurePolicy` as the pause kind first. A resume MUST release the operator pause first, and a second resume MUST release the failure-policy pause.
- **FR-036**: A queue that waits in a "Pause when idle" hold has no running firing group. If the operator asks for a pause then, the queue MUST be paused at once. The hold MUST stop early and MUST NOT bring the game to the front. While the queue is paused, the system MUST NOT change the game state. When the operator resumes, the system MUST bring the game to the front before the queue goes on.

**Schedules**

- **FR-008**: An apply action MUST NOT change the schedule type or the next due time of any entry, unless the operator selected a schedule change for that entry.
- **FR-009**: A pause, an apply action, and a resume MUST NOT reset, move, or skip a timer. An entry that is due during a pause MUST run as late after the resume, with the original due time.
- **FR-010**: The operator MUST be able to choose a schedule change for an entry explicitly. A schedule change MUST NOT be selected by default.

**Apply changes**

- **FR-011**: The operator MUST be able to apply the template that the queue links to onto the paused queue. The apply action MUST NOT stop or restart the run.
- **FR-012**: The apply action MUST be possible only while the queue is paused. In other states, the system MUST refuse the action with a clear message.
- **FR-013**: The apply action MUST support these changes: enable an entry, disable an entry, change the parameter values of an entry, add an entry, and remove an entry.
- **FR-014**: The apply action SHOULD support a change of the order of entries. If this requires large added complexity, the team MAY defer it (User Story 4).
- **FR-015**: The apply action MUST be atomic. The queue MUST get all the selected differences, or none of them. If a part is not valid, the system MUST refuse the whole change and keep the queue as it was.
- **FR-016**: The system MUST validate the new parameter values and the new entries with the same rules as the template editor, before it changes the queue.
- **FR-017**: Each template entry MUST have a stable entry ID. The system creates the ID once, when the entry is created. The ID does not change when the operator edits, moves, or disables the entry. The running queue keeps the same ID for the entry. The system MUST match the entries of the queue to the entries of the template by this ID. Two entries with the same sequence MUST stay separate.
- **FR-018**: Before the apply action, the system MUST show the list of differences with a type and an entry name for each. The operator MUST be able to select which differences to apply.
- **FR-019**: If the template changed after the operator opened the list of differences, the system MUST refuse the apply action and ask for a new review.
- **FR-020**: The system MUST refuse an apply action that runs at the same time as another apply action on the same queue.
- **FR-021**: A removed entry MUST NOT run again in this run. A disabled entry MUST NOT run until the operator enables it. A new entry MUST follow the schedule that the template states.
- **FR-022**: The system MUST record each pause request, pause, cancel, resume, and apply action in the run log, as structured log lines with the time. The apply line MUST list the applied differences. This feature adds no new storage and no new UI for the record.

**Upgrade of existing templates**

- **FR-025**: A template that was saved before this feature has no entry IDs. The system MUST NOT add IDs to it in silence. The system MUST mark such a template as "needs upgrade" in the web UI and through the service API.
- **FR-026**: The operator MUST be able to upgrade a template with an explicit manual action, in the web UI and through the service API. The action gives each entry an ID and leaves all other data of the template unchanged. The action reports how many entries it changed.
- **FR-027**: The upgrade of a template that a running queue uses MUST also give the same IDs to the running entries. The system matches them by their position in the order and by sequence. The system MUST show the match to the operator before it applies it, and MUST NOT change any schedule or next due time. The system MUST keep all consumed schedule state of the running entries (fired timers, done once-per-run entries, pending retries) when it gives them IDs.
- **FR-028**: The system MUST refuse an apply action for a template that "needs upgrade", with a message that tells the operator how to upgrade it. All other queue functions with such a template MUST continue to work as before.
- **FR-029**: The upgrade action MUST be safe to repeat. A second run on an upgraded template changes nothing and reports 0 entries changed.
- **FR-030**: The project documentation MUST describe the manual upgrade steps, in plain steps that an operator can follow, including how to check the result.
- **FR-031**: The change adds a new stored field to templates, so the release MUST increase the minor version of the product.

**Interfaces**

- **FR-023**: The operator MUST be able to do all these actions in the web UI and through the service API.
- **FR-024** *(web UI)*: The feature changes the web UI. The feature MUST state its phone support level as full, and MUST follow docs/mobile-ui-principles.md. The pause, resume, and apply actions MUST be usable on a phone.

### Key Entities

- **Queue run state**: The state of a queue run. New values: pausing and paused. Related data: the time when the pause started.
- **Firing group**: One scheduled sequence and the helper sequences of that firing (before-each-run and every-step sequences). The pause takes effect between two firing groups.
- **Pause request**: An instruction to pause the queue after the current firing group. The operator can cancel it before the queue is paused.
- **Queue entry (runtime)**: One sequence in the running queue, with its enabled flag, parameter values, schedule, next due time, and a stable identity.
- **Template entry**: One sequence in the template, with the same fields and the same stable entry ID.
- **Entry ID**: A stable identifier of one entry. It is created once, and it is the same in the template and in the running queue.
- **Template upgrade**: The manual action that gives entry IDs to a template that was saved before this feature.
- **Template difference**: One difference between a runtime entry and a template entry, with a type: added, removed, enabled, disabled, parameters changed, schedule changed, or order changed.
- **Apply action**: The operation that puts selected template differences into the paused queue as one atomic change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can change an entry of a running queue (enable, disable, parameters, add, remove) with no stop of the run. The run identity and the cycle position stay the same in 100% of tests.
- **SC-002**: After a pause, an apply action, and a resume, the next due time of every entry that the operator did not select for a schedule change is the same as before. This holds in 100% of tests.
- **SC-003**: After a pause request, the queue starts 0 new firing groups, and the running firing group is not interrupted. This holds in 100% of tests.
- **SC-004**: When an apply action fails for any reason, the queue is the same as before the action in 100% of tests.
- **SC-005**: An operator can finish the full flow (pause, review, apply, resume) in the web UI alone, with no restart and with no command line step.
- **SC-006**: The operator sees the state change to "paused" when the running firing group ends, with no other action.
- **SC-007**: After a service restart, no queue is in the state "pausing" or "paused", and the restart behavior of queues is the same as before this feature, in 100% of tests.
- **SC-008**: An operator can upgrade an existing template by the documented manual steps, with only the web UI or the service API. The upgrade changes 0 schedules, 0 parameter values, and 0 other template fields in 100% of tests.

## Assumptions

- The queue is linked to one queue template. The feature applies this template. Apply from another template is out of scope.
- An operator edits the template with the existing template editor. This feature adds no new template editor.
- A pause has no time limit of its own. The system shows the paused time but does not resume the queue by itself. No time limit ends a paused run (the limit of feature 170 belongs to one sequence).
- A pause is at the queue level. A pause of one sequence is out of scope.
- The apply action changes the running queue only. It does not change the template.
- A schedule that is "relative to now" (live relative schedule) is not part of the template and is not changed by the apply action.
- One operator role exists. This feature adds no new access rules.
- The upgrade of existing templates is manual by choice of the operator. The system shows the "needs upgrade" mark and the steps, and does not upgrade in the background.
- "Between sequences" means between firing groups. A firing group is one scheduled sequence and the helper sequences of that firing (before-each-run and every-step sequences). The pause waits for the whole group to end.
- The order change (User Story 4) is a stretch goal. The release can ship without it if it adds large complexity.
- Existing queue history and notification features can show the pause, resume, and apply events. This feature adds no new notification type.
