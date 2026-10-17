# Feature Specification: Keep the command action of an Action step in gamepack install

**Feature Branch**: `151-fix-gamepack-action-command`  
**Created**: 2026-10-17  
**Status**: Draft  
**Input**: User description: "Fix the gamepack install: keep the command action of an Action step. GitHub issue #321 (B-034). Closes #321."

## Clarifications

### Session 2026-10-17

- Q: When a step has `commandId` as a member of the step, what does the stored step hold? → A: The install turns it into the stored form: `primitiveAction` of type `command` and a `commandReference` with the same command ID. A step that gives the stored form keeps it as given.
- Q: When a step has `commandReference` and `primitiveAction` that name different commands, what happens? → A: The dry run reports a problem of the structure category for the step. The install does not create the sequence.
- Q: Which problem category does the dry run use for a step field that the install cannot keep? → A: The structure category, with the sequence name, the step ID, and the field name in the message.
- Q: Does the install keep the ID of a command from the file? → A: Yes when no other command uses the ID. If the install gives the command a new ID, it also changes each step reference to the new ID.
- Q: Does the fix cover other primitive action types, such as wait-for-image? (analyze C1) → A: Only the command action is in scope. A test measures the other types. If the install loses one, the result goes into a new issue, not into this fix.
- Q: Which step types have an `elseBody`, and does the fix cover it? (analyze U2) → A: Every step list inside a step is in scope: the Loop `body`, and the `body` and `elseBody` of a conditional step. An Action step in any of them keeps its command action.
- Q: What if a step has both the stored `action` member and `primitiveAction`? (analyze U3) → A: The stored `action` wins. If `primitiveAction` names a different type or command, the dry run reports a structure problem for the step.
- Q: Which members count as unknown on a step? (analyze U1) → A: A member that is not a member of the stored step and not an accepted alias (`primitiveAction`, `commandId`, `commandReference`). Names match without case. The check covers each step, including nested steps, and not nested payloads.
- Q: Does a `commandReference` keep `commandName`? (analyze T1) → A: Yes. The reference keeps `commandId`, `commandName`, and `isResolved` as in the file.
- Q: Is `isResolved` stored? (loop 1 analyze I1) → A: No. The file may carry `isResolved`; the install accepts it and does not store it. `GET /api/sequences/{id}` computes it. "Equal to the file" for a `commandReference` means equal in `commandId` and `commandName`.
- Q: What happens when a step names a command that is not in the file and not on the system? (analyze C1) → A: The dry run reports a reference problem for that step. A test covers it for `commandReference`, `primitiveAction`, and `commandId`.
- Q: Which step types accept the aliases `primitiveAction`, `commandId`, and `commandReference`? (analyze U2) → A: Only an Action step. On other step types these members give an unknown-member structure problem.
- Q: Which tests must pass for FR-008 and for the performance goal? (analyze C3, C4) → A: The full unit, contract, and integration suites, including the `POST /api/sequences` tests and the existing gamepack performance tests.
- Q: Does the fix change `POST /api/sequences`? (analyze C3) → A: No. Existing tests for it must pass.
- Q: Does the fix change the other object types of a gamepack file (commands, images, seed templates)? → A: No. Only the read of sequence steps changes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a sequence that runs a command (Priority: P1)

A gamepack author puts a sequence in a gamepack file. An Action step of the sequence runs a command that is also in the file. The author installs the file. After the install, the sequence has the same Action step, and the step runs the installed command.

**Why this priority**: Today the install drops the command action of the step. The step does nothing. A gamepack cannot carry a sequence that runs a command, so release 1.0.0 of the reporter cannot be installed.

**Independent Test**: Install the reproduction file from issue #321. Read the installed sequence. The step `s1` has the `primitiveAction` and the `commandReference` of the file, and the command exists with the ID of the file.

**Acceptance Scenarios**:

1. **Given** a gamepack file with an Action step that has `commandReference` and `primitiveAction` (type `command`) and a command in the same file, **When** the user installs the file, **Then** the installed sequence step has the same `primitiveAction` and `commandReference` as the file.
2. **Given** an Action step that has only `primitiveAction` (type `command`), **When** the user installs the file, **Then** the installed step has the `primitiveAction` of the file.
3. **Given** an Action step that has only `commandReference`, **When** the user installs the file, **Then** the installed step has the `commandReference` of the file.
4. **Given** an Action step that has `commandId` as a member of the step, **When** the user installs the file, **Then** the installed step keeps the command action for that command.
5. **Given** an Action step inside the body of a Loop step, **When** the user installs the file, **Then** the installed Action step keeps its command action.
6. **Given** a step that names a command from the file, **When** the user installs the file, **Then** the step names the installed command. If the install gives the command a new ID, the step reference uses the new ID.
7. **Given** an Action step inside the `body` of a Loop step, or inside the `body` or `elseBody` of a conditional step, and the install gives the command a new ID, **When** the user installs the file, **Then** the nested step reference uses the new ID.
8. **Given** an Action step inside the `body` or `elseBody` of a conditional step, **When** the user installs the file, **Then** the installed Action step keeps its command action.
9. **Given** an Action step that has `commandId` and `primitiveAction` (type `command`) for the same command, and no `commandReference`, **When** the user installs the file, **Then** the installed step has one consistent command action: `primitiveAction` of type `command` and a `commandReference` with that command ID.
10. **Given** an Action step that has `commandId` and `primitiveAction` for different commands, or `commandReference` and `primitiveAction` for different commands, **When** the user runs the dry run, **Then** the result reports a structure problem for the step, and the install does not create the sequence.
11. **Given** an Action step that names a command that is not in the file and not on the system (through `commandReference`, `primitiveAction`, or `commandId`), **When** the user runs the dry run, **Then** the result reports a reference problem for the step.
12. **Given** a step that is not an Action step and has `primitiveAction`, `commandId`, or `commandReference`, **When** the user runs the dry run, **Then** the result reports an unknown-member structure problem for the step.

---

### User Story 2 - No silent loss of step fields (Priority: P1)

A gamepack author sends a step that has a field the install cannot keep. The author sees a problem for that field in the dry run. The install does not drop the field without a message.

**Why this priority**: The reporter saw `valid: true` and `created: 3` when the install dropped the action. The author had no signal.

**Independent Test**: Run the dry run with a step that has a field that the install cannot keep. The result lists a problem that names the field and the step.

**Acceptance Scenarios**:

1. **Given** an Action step with a field that the install cannot keep, **When** the user runs the dry run, **Then** the result reports a problem for that field and the dry run is not valid.
2. **Given** the same step, **When** the user runs the install, **Then** the install does not create the sequence with the field dropped without a message.

---

### User Story 3 - Know the JSON of an Action step in a gamepack file (Priority: P2)

A gamepack author reads the install description in the OpenAPI text. The description shows a sample of an Action step that runs a command, in a gamepack file.

**Why this priority**: The reporter had to guess the JSON of such a step.

**Independent Test**: Read the OpenAPI description of the install endpoint. It has a sample Action step that runs a command.

**Acceptance Scenarios**:

1. **Given** the OpenAPI document, **When** the author reads the install endpoint, **Then** the description has a sample of an Action step that runs a command, with the matching command in the file.

---

### User Story 4 - Export then install keeps the command action (Priority: P2)

A user exports a gamepack that has a sequence with a command Action step. The user installs the exported file on a clean system. The installed sequence keeps the command action.

**Why this priority**: The export and the install must agree on the JSON of a step.

**Independent Test**: Export a gamepack with a sequence that runs a command. Install it. Compare the step.

**Acceptance Scenarios**:

1. **Given** an exported gamepack with a command Action step, **When** the user installs it, **Then** the installed step has the same command action as the source step.

---

### Edge Cases

- A step names a command that is not in the file and not on the system: the dry run reports a problem for that reference.
- A step has `commandReference` and `primitiveAction` that name different commands: the dry run reports a problem of the structure category for the step. The install does not create the sequence.
- A step has both the stored `action` member and `primitiveAction`: the stored `action` wins. If `primitiveAction` names a different type or command, the dry run reports a structure problem for the step.
- A step has a member that is not a member of the stored step and not an accepted alias (`primitiveAction`, `commandId`, `commandReference`): the dry run reports a structure problem. The message has the sequence name, the step ID, and the field name. Names match without case. The check covers each step, including nested steps, and not nested payloads.
- A step has a `commandReference`: the stored reference keeps `commandId` and `commandName` as in the file. The file can carry `isResolved`; the install accepts it and does not store it. `GET /api/sequences/{id}` computes `isResolved`.
- A step that is not an Action step has `primitiveAction`, `commandId`, or `commandReference`: the dry run reports an unknown-member structure problem. Only an Action step accepts these aliases.
- A step has `commandId` as a member of the step: the install stores `primitiveAction` of type `command` and a `commandReference` with the same command ID. A step that gives the stored form keeps it as given.
- The command action types other than `command` (for example wait-for-image) are out of scope. A test measures them. If the install loses one, the result goes into a new issue.
- A step has `commandId` and also `primitiveAction` for the same command, and no `commandReference`: the install keeps one consistent command action (`primitiveAction` of type `command` and a `commandReference` with that command ID). If the two name different commands, the dry run reports a structure problem and the install does not create the sequence.
- The install runs a second time (update or take over): the command action stays correct.
- Steps of type Condition, Loop, and `breakCondition` (`imageVisible`) install as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: After install, `GET /api/sequences/{id}` MUST return the `primitiveAction` and the `commandReference` of each Action step that runs a command. The values MUST equal the values in the gamepack file, except for a command ID that the install maps to a new ID. For a `commandReference`, "equal" means equal in `commandId` and `commandName`. The install does not store `isResolved`; `GET /api/sequences/{id}` computes it.
- **FR-002**: The install MUST keep the command action for each of these forms of an Action step: `primitiveAction` only; `commandReference` only; `commandId` as a member of the step; all three together; an Action step inside a Loop `body`; an Action step inside the `body` or `elseBody` of a conditional step. Every step list inside a step is in scope. Only an Action step accepts the aliases `primitiveAction`, `commandId`, and `commandReference`. On other step types these members give an unknown-member structure problem.
- **FR-002a**: If a step names a command that is not in the file and not on the system (through `commandReference`, `primitiveAction`, or `commandId`), the dry run MUST report a reference problem for the step.
- **FR-003**: The command that a step names MUST resolve to the installed command. The installed command keeps the ID of the file when no other command uses the ID. If the install gives the command a new ID, it MUST change each step reference to the new ID. This mapping MUST also apply to an Action step in a Loop `body` and in the `body` or `elseBody` of a conditional step.
- **FR-004**: If the install cannot keep a field of a step, the dry run MUST report a problem of the structure category for that field. The message MUST have the sequence name, the step ID, and the field name. The install MUST NOT create the sequence with the field dropped without a message. A `commandReference` and a `primitiveAction` that name different commands are such a problem.
- **FR-005**: The install description in the OpenAPI text MUST give a sample of an Action step that runs a command in a gamepack file.
- **FR-006**: Tests MUST cover the reproduction file of issue #321 and each form in FR-002. A test MUST cover an export then install round trip. Tests MUST cover: `commandId` with `primitiveAction` for the same command and no `commandReference`; the different-command conflict; the unresolvable command reference for `commandReference`, `primitiveAction`, and `commandId`; and an alias on a step that is not an Action step.
- **FR-007**: Conditions, loops, and `breakCondition` (`imageVisible`) MUST install as before.
- **FR-008**: The change MUST NOT change `POST /api/sequences` (existing tests for it MUST pass), and MUST NOT add new step types or new gamepack features. The full unit, contract, and integration suites, including the `POST /api/sequences` tests and the existing gamepack performance tests, MUST pass.

### Key Entities

- **Gamepack file**: JSON file with a manifest, sequences, commands, and images.
- **Sequence**: ordered steps. An Action step can run a command.
- **Action step**: has `stepId`, `stepType` `Action`, and the command action fields `primitiveAction`, `commandReference`, and `commandId`.
- **Command**: stored object with an ID, a name, and steps. It has an owner (`gamepack` after install).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reproduction file of issue #321 installs, and the installed step `s1` has a non-null `primitiveAction` and `commandReference` equal to the file in `commandId` and `commandName`.
- **SC-002**: 6 of 6 forms of an Action step in FR-002 keep their command action after install.
- **SC-003**: 0 install runs drop a step field without a dry run problem or a message.
- **SC-004**: The export then install round trip gives a step equal to the source step ("equal" means equal in `commandId` and `commandName` for the `commandReference`, and equal in `primitiveAction`).
- **SC-005**: All existing gamepack tests for conditions, loops, and `breakCondition` pass without a change in the expected values.

## Assumptions

- The web UI does not change, so no phone support level applies.
- The OpenAPI text is the only user documentation that changes.
- Other gamepack problems (#322 to #326) are out of scope.
- Only the read of sequence steps changes. The other object types of a gamepack file (commands, images, seed templates) do not change.

### Notes for planning

- A shared test fixture MUST be reachable from each test project that uses it.
- Tasks that write the same file MUST NOT be parallel.
- A step problem MUST prevent the creation of the sequence. A test for this MUST be in the foundational phase.
- The plan lists every file it changes, including validator, service, and applier files.
- The install description lives in `GamepacksEndpoints.cs`.
- A fix-if-needed task must state a done check. The first failing test of a phase may fail to compile until a stub exists.
