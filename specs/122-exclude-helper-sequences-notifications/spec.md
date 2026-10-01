# Feature Specification: Exclude Helper Sequences From Success Notifications

**Feature Branch**: `122-exclude-helper-sequences-notifications`  
**Created**: 2026-10-01  
**Status**: Implemented  
**Input**: User description: "as an operator I need a way to exclude certain sequences -helpers to excluded from the success notifications in UI"

## Clarifications

### Session 2026-10-01

- Q: Is the exclusion a property of the sequence, of the queue, or of one queue entry? → A: Of the sequence. One option on the sequence applies in every queue. Rationale: the request says "exclude certain sequences". A helper is a helper in every queue.
- Q: Does the system detect helpers by name (for example a "-helper" suffix) or does the operator choose? → A: The operator chooses with the explicit option. Rationale: name rules are fragile and hide behavior. The option is visible in the UI.
- Q: Does the exclusion also hide "recovered" messages? → A: No. Rationale: "recovered" follows a "failure" message that the operator already received. Hiding it would leave the failure open in the operator's view.
- Q: Does the option need a queue level change or a queue restart to apply? → A: No. The option is read when a result is sent, so a change applies to the next result with no queue restart. Rationale: the operator expects a UI toggle to work at once.

- Q (analyze T1): What text does the list marker show? → A: "No success notifications". The spec and UI use the term "notifications" only. Rationale: one term for one thing.
- Q (analyze U3): Does a change of the option count as a sequence change? → A: Yes. It changes the sequence version and the update time like any other change. Rationale: the operator and other clients see the edit in the normal history.
- Q (analyze U1, U2): Which cases must a test cover? → A: A cancel from an operator cancel and a cancel from a queue stop, both with the option on. Also each queue level ("None", "Failure", "Success+Failure") with the option on. Rationale: the edge cases and FR-002 name these cases.
- Q (loop 2, analyze U1): Which values of the option are valid in the API? → A: Only JSON true and false. A null, a string and a number return 400. An omitted option keeps the saved value. Rationale: in a partial update, null could mean "not set". A clear rule avoids this.
- Q (loop 2, analyze C1): What happens when the system cannot read the sequence at send time? → A: The system treats the option as off and writes the existing log event for the failed read. The notification is then sent as usual. Rationale: a send of a possible duplicate success is safer than a lost failure message.
- Q (loop 2, analyze U2): Is SC-004 separate work? → A: No. It is an outcome that follows from the worker tests of US1. Rationale: it is an observation over time, not a build item.
- Q (analyze C2): How does a clone keep the option? → A: The server has no clone action. A client clone reads the sequence and creates a new one with the same data, so the create call MUST accept the option. A round-trip test covers this. Rationale: FR-009 needs a testable path.
- Q (analyze D1): Is US2 independent of US1? → A: US2 is tested on top of the worker change of US1. It has its own tests and its own acceptance scenarios. Rationale: both stories change one decision point.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mark a sequence as a helper that sends no success message (Priority: P1)

The operator runs queues at level "Success+Failure". Some sequences are small helpers, for example a guard that runs before each run or a popup dismiss step. These sequences succeed very often. Each success sends a message and fills the chat with noise. The operator opens the sequence in the UI and turns on an option "Exclude from success notifications". After this, the queue sends no "success" message for this sequence. Messages for other sequences do not change.

**Why this priority**: This is the core request. Without it, the operator must lower the whole queue level to "Failure" and loses the success messages of the real tasks.

**Independent Test**: Set a queue to "Success+Failure". Put two sequences in it. Turn on the option for one of them. Run both so that they succeed. Check that only one "success" message arrives, for the sequence without the option.

**Acceptance Scenarios**:

1. **Given** queue "Farm-1" has level "Success+Failure" and sequence "PNS.GuardPopups" has the option on, **When** "PNS.GuardPopups" succeeds in "Farm-1", **Then** no message is sent.
2. **Given** the same queue, **When** "PNS.CollectResources" (option off) succeeds in "Farm-1", **Then** the target receives `Farm-1 : PNS.CollectResources : success`.
3. **Given** the option is off for a sequence, **When** the operator turns it on and saves, **Then** the next success of this sequence sends no message.
4. **Given** the option is on for a sequence, **When** the operator turns it off and saves, **Then** the next success of this sequence sends a message again (level allowing).

---

### User Story 2 - Failures of a helper still reach the operator (Priority: P1)

A helper that fails is a real fault. The option hides only the noise from success. The operator must still learn about a failure, a cancel and the recovery.

**Why this priority**: A silent helper that fails could hide a broken queue. This rule keeps the exclusion safe.

**Independent Test**: Turn on the option for a sequence in a queue with level "Failure" or "Success+Failure". Make the sequence fail, then succeed. Check that the operator gets "failure" and then "recovered", and no "success" message.

**Acceptance Scenarios**:

1. **Given** the option is on for sequence S in queue Q, **When** S fails in Q, **Then** the target receives the "failure" message as usual.
2. **Given** an open failure streak for S in Q, **When** S next succeeds, **Then** the target receives the "recovered" message.
3. **Given** the option is on for S, **When** S later succeeds again with no open streak, **Then** no message is sent.
4. **Given** the option is on for S, **When** the operator cancels S or a queue stop interrupts it, **Then** the "cancelled" message is sent as usual.

---

### User Story 3 - See and set the option in the sequence UI (Priority: P2)

The operator can see which sequences are excluded without opening each one. The sequence editor has the option. The sequence list shows a small marker for excluded sequences.

**Why this priority**: The core behavior works with only the editor option. The list marker saves time but is not needed for the first release.

**Independent Test**: Open the sequence list. Check that excluded sequences show a marker. Open one sequence and turn the option off. Check that the marker disappears after save.

**Acceptance Scenarios**:

1. **Given** sequence S has the option on, **When** the operator opens the sequence list, **Then** S shows a marker and other sequences do not.
2. **Given** the operator opens the sequence editor, **When** the editor loads, **Then** it shows the option with its saved state and a short help text that explains the effect.
3. **Given** the operator saves any other change to S, **When** the save ends, **Then** the option keeps its state.

---

### Edge Cases

- A sequence that another sequence calls as a step sends no message of its own (spec 120). The option does not change this.
- The queue level is "Failure" or "None": the option has no visible effect on success, because those levels send no success message already.
- A sequence that is not a queue entry (a manual run) sends no message (spec 120). The option does not change this.
- The operator clones, exports or imports a sequence: the option goes with the sequence data. A new sequence made without the option has the option off.
- The operator deletes a sequence: its option is removed with it.
- An existing sequence, saved before this feature, has no option value: the system treats it as off. Behavior does not change for it.
- The operator turns the option on while a failure streak is open: the streak stays open. The next success sends "recovered" (see FR-003).
- The same sequence runs in two queues: the option belongs to the sequence, so it applies in both queues.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let the operator set a boolean option "Exclude from success notifications" for each sequence. The default is off.
- **FR-002**: When a queue entry sequence with the option on succeeds, the system MUST send no "success" notification. This applies at every queue level.
- **FR-003**: The option MUST NOT hide "failure", "cancelled" or "recovered" notifications. The failure streak rules of spec 120 (FR-012, FR-019) MUST work as before. Only the plain "success" message is excluded.
- **FR-004**: The option MUST be a property of the sequence, not of the queue. It MUST apply in every queue that runs the sequence.
- **FR-005**: The system MUST keep the option after a service restart. The option MUST survive other changes to the sequence: an update that does not name the option MUST keep the saved value.
- **FR-006**: The UI sequence editor MUST show the option with a short help text. The help text MUST say that failures and recoveries are still sent.
- **FR-007**: The UI sequence list MUST show a marker with the text "No success notifications" on each sequence that has the option on.
- **FR-008**: The API MUST let a client read and write the option. An invalid value MUST return a 400 error, not a 500 error. A value is valid only if it is a JSON true or false. A null, a string and a number are invalid. A request that omits the option is valid and keeps the saved value.
- **FR-009**: The option MUST go with the sequence in clone, export and import. A sequence data set with no option value MUST set the option to off.
- **FR-010**: A change to the option MUST apply to the next sequence result with no service restart.
- **FR-011**: When the work ends, the Status line of this spec MUST change to "Implemented". The entry for this feature in `specs/STATUS.md` MUST also change to "Implemented" (constitution Principle V).
- **FR-012**: A change of the option MUST count as a sequence change. It MUST change the sequence version and the update time, as any other change does.

### Key Entities

- **Sequence**: An existing entity. It gets one new attribute: "Exclude from success notifications" (boolean, default off).
- **Notification Message**: An existing entity (spec 120). The status "success" is not created for a sequence with the option on. The other statuses are not changed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The operator can find and turn on the option for a sequence in under 1 minute, with no help text outside the UI.
- **SC-002**: A test run with one excluded and one normal sequence, both succeeding at level "Success+Failure", gives exactly 1 success message. The count is 100% correct.
- **SC-003**: An excluded sequence fails 10 times in a row and then succeeds. The operator receives exactly 2 messages: one "failure" and one "recovered".
- **SC-004**: After the option is on, the number of "success" messages from the excluded sequence is 0 over a full day of runs.
- **SC-005**: All existing sequences, saved before this feature, send the same messages as before.

## Assumptions

- "Helpers" means sequences that the operator chooses. The system does not detect helpers by name or by use.
- The option is per sequence (not per queue and not per queue entry). This is the simplest model that matches the request "exclude certain sequences". A per-queue exclusion list is out of scope.
- "Success notifications in UI" means the success messages that spec 120 sends to the configured targets. The operator sets the option in the UI. No new notification display in the UI is added.
- The "recovered" message stays, because it is tied to a "failure" message that the operator already received.
- A change of the message text or format is out of scope.
- Task rules: Tasks that write the same file MUST NOT carry the [P] mark. A task that adds to a file that another task creates MUST NOT carry the [P] mark.
- SC-001 (find and turn on the option in less than 1 minute) is checked by a manual step in the quickstart.
