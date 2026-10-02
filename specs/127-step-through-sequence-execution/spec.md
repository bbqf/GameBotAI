# Feature Specification: Step-Through Sequence Execution

**Feature Branch**: `127-step-through-sequence-execution`
**Created**: 2026-10-02
**Status**: Implemented
**Input**: User description: "As an author I want to be able to execute a given sequence step by step while observing the results. I want a special view in the authoring UI, in an open sequence, to step through the execution as if in a real run. I will observe the results in the emulator itself. I only need to execute the steps and see their status as in logs. In addition to one button that executes one step at a time, as if run automated, I need to be able to select the next step manually. This feature must only work on a saved sequence. Orient at the "Record steps" of the command authoring, but choose whatever is appropriate."

## Clarifications

### Session 2026-10-02

- Q: Do steps that change real schedules (for example reschedule-self) apply their effect in a step-through? → A: Never. The history shows the intended effect, and real state does not change.
- Q: What is "one step" for loops and branches? → A: Step into everything. Each action inside a loop iteration or an if branch is one step. The list shows nesting. (A sequence step cannot call another sequence, so a called sequence does not exist. A command step runs as one step.)
- Q: What happens when a real run (for example a queue on a timer) uses the same session? → A: The author can pause the owning queue from the step-through view. The queue resumes when the step-through ends.
- Q: May the author start a step-through at a middle step? → A: Yes. Any step can be the first step. Values from skipped steps are "not set", unless the author enters them.
- Q: Which time limits apply in a step-through? → A: The sequence time limit does not apply. Each step keeps its own timeouts. The time between steps is not limited.
- Q: How does a `lastRun` condition behave in a step-through? → A: It is always false. The history entry says "lastRun is not evaluated". This is a known difference from a real run.
- Q: Where does the cursor go after a step that normally ends the sequence (break, fail, reschedule-self)? → A: The cursor goes to the step that follows by the normal order. The history entry shows the ending effect as a status, for example "sequence would end here". The author can select any step.
- Q: What happens to a running step when the view is lost? → A: The service cancels the running step when the step-through ends or the lease expires. The lease expires 90 seconds after the last read from the view, also during a step. The view keeps the lease alive with its regular reads.
- Q: When can the author set values for parameters and variables? → A: At any time when no step runs. A change keeps the history and the cursor. "Variables" means the step outcomes that conditions read.
- Q: What happens to actions with outside effects inside a command step? → A: The command executor gets a preview mode. In a step-through, these actions show a preview inside a command step and do not run.
- Q: What does "Restart" do to the queue pause? → A: Restart keeps the queue pause and keeps the parameter values. It clears the history, the cursor, and the outcomes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the next step automatically (Priority: P1)

An author opens a saved sequence in the authoring UI and opens the step-through view. The author selects a game session. The view shows the sequence steps in order, with a marker on the step that runs next. The author pushes one button ("Run next step"). The system runs exactly that one step on the emulator, in the same way as a real run would run it. The view then shows the status of the step (for example succeeded or failed) and moves the marker to the step that a real run would run next. The author looks at the emulator to see the effect of the step.

**Why this priority**: This is the core value. The author can test a sequence one step at a time without a full run, and can find the exact step that goes wrong.

**Independent Test**: Open a saved sequence with three steps, start a step-through, push "Run next step" three times. Each push runs one step, shows its status, and moves the marker. After the last step the view shows that the sequence is complete.

**Acceptance Scenarios**:

1. **Given** a saved sequence and a started step-through, **When** the author pushes "Run next step", **Then** the system runs only the next step and shows its status and result message in the step list.
2. **Given** a step that has a condition, branch, or loop, **When** the author pushes "Run next step", **Then** the next step is the one that a real run would select (the branch that the real condition result selects, the next loop iteration, and so on).
3. **Given** the last step ran, **When** the view updates, **Then** the view shows that the sequence is complete and the "Run next step" button is disabled.
4. **Given** a step fails, **When** the view updates, **Then** the view shows the failure status and message, and the author can choose what to do next (see User Story 2).

---

### User Story 2 - Choose the next step manually (Priority: P1)

The author does not want to follow the automatic order. The author selects any step in the list as the next step to run. The marker moves to that step. The next push of the run button runs the selected step. This lets the author repeat a step, skip a step, or jump back after a failure.

**Why this priority**: The user asks for this explicitly. It makes repeated tests of one step fast, and it lets the author recover from a failed step without a restart.

**Independent Test**: In a started step-through, select step 3, run it, then select step 1, run it. The view shows both runs in the history and the marker follows the manual selection.

**Acceptance Scenarios**:

1. **Given** a started step-through, **When** the author selects a step as the next step, **Then** the marker moves to that step and the next run executes that step.
2. **Given** the author ran a step that was selected manually, **When** the step ends, **Then** the marker moves to the step that follows by the normal run order, starting from the step that just ran.
3. **Given** a step failed, **When** the author selects the same step again and runs it, **Then** the system runs it again and records a new history entry.

---

### User Story 3 - See the run history and status (Priority: P2)

The author sees a list of the steps that ran in this step-through, in time order. Each entry shows the step, its status, the message, and the time, in the same form as the execution logs. The author can clear the history and restart the step-through.

**Why this priority**: The author watches the emulator for the visual effect, and uses the history for the status of each step. It supports the P1 stories but is not needed for the first working run.

**Independent Test**: Run four steps, one of them with a failure. The history shows four entries with the correct statuses and messages, in order. "Restart" clears the history and resets the marker to the first step.

**Acceptance Scenarios**:

1. **Given** steps ran in a step-through, **When** the author looks at the history, **Then** each entry shows step name, status, message, and time.
2. **Given** a step-through with history, **When** the author pushes "Restart", **Then** the history is cleared and the next step is the first step.

---

### User Story 4 - Saved sequences only (Priority: P2)

The step-through view is available only for a sequence that is saved. If the author has unsaved changes or the sequence is new, the system does not allow a step-through and tells the author to save first.

**Why this priority**: It keeps the step-through result tied to the exact stored sequence. It prevents the author from testing a different version than the one that real runs use.

**Independent Test**: Open a new, unsaved sequence: the step-through control is disabled with a reason. Save it: the control is enabled. Edit a step without saving: the control is disabled again until the author saves.

**Acceptance Scenarios**:

1. **Given** a new sequence that is not saved, **When** the author looks at the authoring UI, **Then** the step-through entry is disabled and shows that the sequence must be saved first.
2. **Given** a saved sequence with unsaved edits, **When** the author tries to start a step-through, **Then** the system does not start it and asks the author to save first.
3. **Given** a step-through is active and the stored sequence changes (for example the author saves a new version), **When** the author pushes "Run next step", **Then** the system tells the author that the sequence changed and asks the author to restart.

---

### Edge Cases

- No game session is available or the selected game session is disconnected: the system does not run the step and shows a clear message.
- A real run of the same game or the same queue is active at the same time: the step-through and the real run must not send input to the emulator together. The system refuses the step and tells the author why. It also offers to pause the owning queue (FR-012a).
- The step-through ends without notice while the queue is paused (browser closes, network loss): the queue resumes (FR-012b).
- A step takes a long time (for example wait-for-image or a long loop): the view shows that the step is running, disables the run button while it runs, and offers a way to cancel the step.
- The author closes the view or leaves the page during a step: the service cancels the running step. It cancels when the view sends a close signal, or when the view sends no read for 90 seconds (FR-012b). No step keeps running with no view.
- A step refers to a command or sequence that was deleted: the step fails with the same message as in a real run.
- A step that normally ends the whole sequence (break, fail, reschedule-self): the view shows the effect as a status, for example "sequence would end here". The system does not change any real queue or schedule. The cursor goes to the step that follows by the normal order. A failed step follows the same rule. A break outside a loop shows "no loop is active".
- A step or branch with a `lastRun` condition: the condition is always false in a step-through, because it has no queue run history. The history entry says "lastRun is not evaluated".
- A command step that contains an action with an outside effect (for example notify): the step runs in preview mode for that action. The history entry lists the previewed effect (FR-015a).
- The sequence has zero steps: the view shows that there is nothing to run.
- Parameters and variables: steps that use sequence parameters or variables use the values that the author gives for the step-through. Values from earlier steps in this step-through stay available for later steps. The author can change a value between steps (FR-017).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The authoring UI MUST give the author a step-through view for an open sequence.
- **FR-002**: The step-through view MUST be available only for a sequence that is saved and has no unsaved edits. For other sequences, the entry MUST be disabled and MUST show the reason.
- **FR-003**: The author MUST be able to select the game session on which the steps run.
- **FR-004**: The view MUST show the steps of the sequence in order and MUST mark the step that runs next. The view MUST show nesting: the actions inside loop bodies and if branches appear indented under their parent step.
- **FR-004a**: One step is one action. A loop and an if branch are not steps that run as one unit. A command step runs as one step. Each action inside them is a separate step. Each loop iteration is shown as its own entry in the history, with the iteration number.
- **FR-005**: The author MUST be able to run exactly one step with one action ("Run next step"). The step MUST run with the same behavior as in a real run, including its conditions, timeouts, and outcome rules. One exception: a `lastRun` condition is always false (see Edge Cases).
- **FR-005a**: The sequence time limit (watchdog) MUST NOT apply to a step-through. Each step MUST keep its own timeouts, so a hung step still ends with a failure status. The time between steps MUST NOT be limited.
- **FR-006**: After a step ends, the system MUST select the next step with the same rules that a real run uses (including branches, loops, and jumps), unless the author selects another step.
- **FR-007**: The author MUST be able to select any step as the next step to run, at any time when no step is running. This includes a step inside a loop body or an if branch. The system uses the current values (variables, loop counters) for that step.
- **FR-008**: The view MUST show for each step run: the step, its status, its message, and the time, in a form that matches the execution logs.
- **FR-009**: The view MUST keep a history of step runs for the current step-through and MUST let the author clear it and restart from the first step. A restart MUST NOT resume the queue (FR-012b).
- **FR-010**: The view MUST show when the sequence is complete and MUST disable "Run next step" in that state, but the author MUST still be able to select a step manually and run it.
- **FR-011**: While a step runs, the view MUST show that it runs, MUST prevent a second step from starting, and MUST let the author cancel the running step.
- **FR-012**: The system MUST NOT run a step-through step at the same time as a real run that uses the same session. It MUST refuse the step with a clear message that names the queue that owns the session.
- **FR-012a**: If a queue owns the selected session, the view MUST let the author pause that queue. The system MUST NOT pause a queue without an explicit author action. While the queue is paused by the step-through, no real run starts on that session.
- **FR-012b**: The system MUST resume the queue that the step-through paused when the step-through ends (the author leaves the view or closes it). A restart does not end the step-through. It keeps the queue pause. The resume MUST also happen if the view closes without notice (for example the browser closes). In that case, the system MUST cancel a running step first. It MUST do this when the view sends no read for 90 seconds. The service checks the lease every 10 seconds, so the cancel happens at most 100 seconds after the last read. If the queue was already paused before the step-through, the system MUST leave it paused.
- **FR-012c**: If a real run is active when the author asks to pause the queue, the view MUST tell the author that the run is active and MUST NOT run a step until that run ends or the author cancels it.
- **FR-013**: The system MUST run the stored (saved) version of the sequence. If the stored version changes during a step-through, the system MUST refuse to continue and MUST ask the author to restart.
- **FR-013a**: The author MUST be able to select any step as the first step, before any step has run. Values that skipped steps would set are "not set", unless the author enters them (FR-017). A step that reads a "not set" value MUST behave as in a real run when that value is missing.
- **FR-014**: The system MUST keep values (variables, step outputs) across steps of one step-through, so later steps see the results of earlier steps. "Restart" MUST reset the step outcomes. "Restart" MUST keep the parameter values that the author entered.
- **FR-015**: A step-through MUST NOT change queue schedules, daily run records, or other state that real runs use. Steps that normally change such state (for example reschedule-self) MUST show their intended effect in the history (for example "would reschedule at 14:30") and MUST NOT apply it. There is no option to apply it.
- **FR-015a**: This rule MUST also apply to the actions inside a command step. The command executor MUST have a preview mode. In a step-through, an action with an outside effect (for example notify or reschedule-self) MUST show its intended effect in the history entry of the command step and MUST NOT run. All other actions inside the command step run as in a real run.
- **FR-016**: Step-through step runs MUST be written to the execution log, marked as step-through runs, so the author can find them later.
- **FR-017**: The author MUST be able to give values for sequence parameters and for variables (the step outcomes that conditions read) before the step-through starts and at any time when no step runs. A change of a value MUST keep the history and the cursor. A restart is not necessary.

### Key Entities

- **Step-through run**: One author's step-through of one saved sequence on one game session. Has the sequence version, the next-step marker, the carried values, and the history. In this spec, "session" alone always means the game session.
- **Step run entry**: One executed step in the history. Has the step, status, message, start time, and duration.
- **Next-step marker**: The step that runs on the next push. It is set by the system (normal run order) or by the author (manual selection).

## Assumptions

- The author watches the emulator directly. The view does not need to show screenshots or detection overlays.
- The step-through view is placed in the sequence authoring page as a separate panel or mode, with a style like "Record steps" in command authoring, but with a step list and a history.
- A step-through is for one author at a time on one session. Multi-author use is out of scope.
- Steps run on the real game session, so the steps do real taps on the emulator. The author is responsible for the effect (the same as with a real run).
- If the author wants the real schedule effect of a step, the author uses a real run (FR-015).
- Step-through state lives only while the view is open. The history is not kept after the author leaves the view, except for the execution log entries (FR-016).
- Unsaved sequences are out of scope. The author saves first (FR-002).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can start a step-through of a saved sequence and run its first step in under 30 seconds.
- **SC-002**: For every sequence, the order of steps run by repeated "Run next step" (with no manual selection) is the same as the order of steps in a real run with the same inputs.
- **SC-003**: After each step, the view shows the step status within 1 second of the step ending.
- **SC-004**: An author can repeat a chosen step, or jump to any step, in 2 actions or fewer (select, run).
- **SC-005**: In the UI, 100% of attempts to start a step-through on an unsaved or edited sequence are refused with a message that tells the author to save. The service refuses a run when the stored sequence changed after the start (`sequence_changed`).
- **SC-006**: A step-through never changes queue schedules or daily run records (zero changes in all tested cases).
