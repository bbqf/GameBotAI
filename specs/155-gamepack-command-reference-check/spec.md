# Feature Specification: Gamepack command reference check

**Feature Branch**: `155-gamepack-command-reference-check`  
**Created**: 2026-10-17  
**Status**: Draft  
**Input**: GitHub issue #323 (FR-029): "gamepack dry run should report an unresolved command reference in a sequence step".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dry run finds a dangling command reference (Priority: P1)

A gamepack author runs a dry run before publishing. One sequence has a step that runs a command. The command is not in the gamepack and not on the instance. The dry run MUST report a `reference` problem, in the same way as it does for a missing image. The result is not valid.

**Why this priority**: Today the dry run says `valid: true` for some such gamepacks. The step `commandId` form is already checked. The dry run misses `commandReference.commandId`, the Action payload `commandId`, and name-only steps. The error shows only at run time on the buyer instance. Scenario 1 (step `commandId`) stays as a regression scenario.

**Independent Test**: Run a dry run on a gamepack with one sequence whose step runs an unknown command. Check for a `reference` problem and `valid: false`.

**Acceptance Scenarios**:

1. **Given** a sequence step that runs a command by `commandId`, and that id is not in the gamepack and not on the instance, **When** the author runs a dry run, **Then** the result has a `reference` problem and `valid: false`.
2. **Given** a sequence step that runs a command by `commandName`, and that name is in no list (gamepack or instance), **When** the author runs a dry run, **Then** the result has a `reference` problem and `valid: false`.
3. **Given** a step whose command is in the gamepack, **When** the author runs a dry run, **Then** no new problem is reported.
4. **Given** a step whose command is already on the instance, **When** the author runs a dry run, **Then** no new problem is reported.
5. **Given** an unknown command id in each id form that the install rewrites (step `commandId`, `commandReference.commandId`, Action payload `commandId`), **When** the author runs a dry run, **Then** each form gives a `reference` problem.

---

### User Story 2 - Install refuses a dangling command reference (Priority: P2)

A user installs a gamepack with a dangling command reference. The install applies the same check and reports the same `reference` problem, as it does for images.

**Why this priority**: The install must not accept what the dry run rejects.

**Independent Test**: Install a gamepack with an unknown command in a step. Check that the install reports the `reference` problem.

**Acceptance Scenarios**:

1. **Given** a gamepack with an unresolved command reference in a sequence step, **When** the user installs it, **Then** the install reports the same `reference` problem as the dry run, and the install installs nothing.
2. **Given** a gamepack where all command references resolve, **When** the user installs it, **Then** the install works as before.

---

### Edge Cases

- A command that the gamepack contains under a different id but the same name: it does NOT resolve a missing id. The run time finds a command by id only.
- A name-only step whose command is in the gamepack, after the install renames a command id because of a conflict: it still passes. The name check runs on the file names.
- A command-to-command reference: it gives the same result as before this feature.
- A step with no command reference (for example a primitive step): it gives no problem.
- A step that has both an id and a `commandName`: only the id is checked. The name is a label.
- An install with a `reference` problem installs nothing.
- A command in the same gamepack that the install creates: it counts as present.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The dry run MUST report a `reference` problem for each sequence step that runs a command by `commandId` when the id is not in the gamepack and not on the instance. The id check MUST cover every id form that the install rewrites: the step `commandId`, the `commandReference.commandId`, and the Action payload `commandId`.
- **FR-002**: The dry run MUST report a `reference` problem for each sequence step that runs a command by `commandName` when the name is in no list (gamepack or instance). A name is checked only when the step has no id in any id form. The name check is a pack-consistency check: a name-only step whose name is in a list passes. Name match is case-insensitive and ignores leading and trailing white space.
- **FR-003**: The problem message MUST have the same form as the existing image message: "Command 'x' is not in the gamepack or in the installation."
- **FR-004**: A gamepack with a `reference` problem MUST give `valid: false`.
- **FR-005**: The install MUST apply the same check and report the same problem.
- **FR-006**: A command reference that the gamepack contains, or that the instance already has, MUST NOT give a problem. Existing valid gamepacks MUST stay valid.
- **FR-007**: The feature MUST NOT change the image check, add checks for other reference kinds (nested sequence, trigger), change the gamepack file format, or change the run-time behaviour of sequences.
- **FR-008**: Automated tests MUST cover: unresolved by id (each id form), unresolved by name, resolved in the gamepack, resolved on the instance, a name-only step that still passes after the install renames a command id because of a conflict (rename-after-conflict, name-only case), and an unchanged command-to-command reference (same result as before).
- **FR-009**: The release version in `installer\versioning\version.override.json` MUST be bumped. The pipeline does this before the push. There is no code task for it.

### Key Entities

- **Gamepack**: A file with commands, images, and sequences for install.
- **Sequence step**: A step in a sequence that can run a command by id or by name.
- **Reference problem**: A dry run or install result item of kind `reference` that names a missing target.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of gamepacks with a command reference that is in no list give `valid: false` in the dry run.
- **SC-002**: 0 existing valid gamepacks change to not valid.
- **SC-003**: The install and the dry run report the same problem for the same gamepack.
- **SC-004**: The author no longer needs a separate build-side reference check for commands to trust the dry run.

## Clarifications

### Session 2026-10-17

- Q: Does the gamepack validator already check some command references? -> A: Yes. It reports missing command ids for some step forms, so this feature MUST extend that existing check to the step forms it misses. The step `commandId` form is already checked. The missing forms are `commandReference.commandId`, the Action payload `commandId`, and name-only steps. This feature MUST NOT add a second parallel check. Rationale: same message form and same code path as images.
- Q: Which name match rule applies to `commandName`? -> A: Case-insensitive match on the command name, the same rule that the existing validator uses for other name lookups. Rationale: consistency.
- Q: Does the problem name the sequence and the step? -> A: It names the sequence and the missing command, as the existing image problem names its owner. No step index is required.

### Loop 2 clarifications (from analyze)

- A2: The step `commandId` form is already checked by the validator. The missing forms are `commandReference.commandId`, the Action payload `commandId`, and name-only steps. Q1 is read in this way.
- C1: A name-only step whose command is in the gamepack MUST still pass after the install renames a command id because of a conflict. The name check runs on the file names.
- C2: Command-to-command checks stay as they are; a test MUST show the same result as before.

### Loop 1 clarifications (from analyze)

- I1: The run time finds a command by id only. A `commandName` next to an id is a label and is not checked. A command with the same name but a different id does NOT resolve a missing id. A name is checked only when the step has no id in any id form. The edge cases above are replaced by this rule.
- I2: The id check covers every id form that the install rewrites: the step `commandId`, the `commandReference.commandId`, and the Action payload `commandId`. The check is an extension of FR-001.
- A1: The name check is a pack-consistency check. A name-only step whose name is in a list passes the check.
- U1: Name match is case-insensitive and ignores leading and trailing white space.

## Assumptions

- The dry run already collects the commands in the gamepack and on the instance, as it does for images.
- The gamepack and instance are the only two places that can provide a command.
- Tests go in one new test file for the validator unit tests, plus one integration test. No docs task is needed, unless a docs file lists the reference problem kinds.
