# Feature Specification: Preserve Sequence Step Command Names

**Feature Branch**: `041-fix-sequence-step-names`  
**Created**: 2026-05-29  
**Status**: Draft  
**Input**: User description: "There's a bug (or two - I don't know) I want you to fix. When creating the steps of a sequence, their names appear properly in the UI. However when the sequence is saved and reopened, the step names disappear, the step names are also missing from the execution logs, they appear like \"command: executed — Step 'body-step-2' executed.\" so I don't know which command was that and I cannot even check it in the UI, as UI is showing steps, however all of them show the drop down box with \"Select command\" in them. Can you fix this?"

## Clarifications

### Session 2026-05-29

- Q: What execution log context should be shown for each sequence step run? → A: Log the authoring step label and command name.
- Q: How should the editor represent a saved step whose command can no longer be resolved? → A: Show an unresolved state with the last saved command name when available.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reopen a Sequence Without Losing Step Identity (Priority: P1)

As a sequence author, I can save a sequence and reopen it later without losing the command selected for each step, so I can continue editing with confidence.

**Why this priority**: Losing the selected command after save makes saved sequences unreliable and prevents further editing.

**Independent Test**: Create a sequence with multiple named steps that reference different commands, save it, reopen it, and confirm each step still shows the correct command selection and label.

**Acceptance Scenarios**:

1. **Given** a sequence author has assigned commands to sequence steps, **When** the sequence is saved and reopened, **Then** each step shows the same command selection that was saved.
2. **Given** a saved sequence contains multiple steps with different commands, **When** the author returns to the sequence editor, **Then** no step reverts to an empty "Select command" state unless that step was intentionally left unassigned.

---

### User Story 2 - Identify Executed Commands in Logs (Priority: P2)

As an operator reviewing execution history, I can tell which command each sequence step ran, so I can understand what happened without cross-referencing internal step identifiers.

**Why this priority**: Execution logs are part of troubleshooting and auditability; internal step identifiers alone do not provide enough context.

**Independent Test**: Execute a sequence with named steps and inspect the resulting execution log entries to confirm they include the user-recognizable command identity for each executed step.

**Acceptance Scenarios**:

1. **Given** a sequence step runs a selected command, **When** the execution is logged, **Then** the log entry includes both the step label and the command name so an operator can match it to the authoring UI.
2. **Given** a sequence contains repeated or similar step labels, **When** execution logs are reviewed, **Then** each logged step still includes the command name needed to distinguish what was executed.

---

### User Story 3 - Preserve Existing Sequences During Repair (Priority: P3)

As a user with previously saved sequences, I can reopen existing sequences without data loss or unexpected reassignment, so the fix does not force me to rebuild working automations.

**Why this priority**: A fix that only helps newly created sequences would leave existing saved content broken and reduce trust in the editor.

**Independent Test**: Open a sequence saved before the fix, verify the editor shows any recoverable command associations correctly, and verify sequences with already-valid step associations remain unchanged after save.

**Acceptance Scenarios**:

1. **Given** a previously saved sequence already contains valid step-to-command associations, **When** it is opened and saved again, **Then** those associations remain intact.
2. **Given** a previously saved sequence has missing or unresolved step associations, **When** it is opened, **Then** the system preserves remaining valid data and shows an unresolved state that includes the last saved command name when available without corrupting other steps.

### Edge Cases

- What happens when a saved step references a command that has since been deleted or is no longer available? The editor must preserve the step record and show an unresolved state with the last saved command name when available.
- How does the system handle multiple steps that point to the same command? Each step must retain its own identity while still showing the shared command correctly.
- What happens when a sequence is saved with a mix of assigned and intentionally unassigned steps? Only intentionally unassigned steps may appear empty when reopened.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST persist the command association for each sequence step so the same association is available when the sequence is reopened.
- **FR-002**: The sequence editor MUST display the saved command selection for each step when loading an existing sequence.
- **FR-003**: The system MUST preserve each step's user-visible name independently from the command associated with that step.
- **FR-004**: The system MUST distinguish between a step that was intentionally left without a command and a step whose saved command association failed to load.
- **FR-004a**: When a saved command association cannot be resolved, the sequence editor MUST show an unresolved state rather than the same empty state used for an intentionally unassigned step.
- **FR-004b**: When available from saved data, the unresolved state MUST include the last saved command name.
- **FR-005**: The system MUST include both the sequence step label and the selected command name in step execution logs.
- **FR-006**: The system MUST keep execution log wording consistent with the command selection shown in the sequence editor for the same step.
- **FR-007**: The system MUST preserve valid step-to-command associations when loading and re-saving sequences created before this fix.
- **FR-008**: The system MUST avoid reassigning a step to a different command unless the user explicitly changes that step in the editor.

### Key Entities *(include if feature involves data)*

- **Sequence Step**: A saved step within a sequence, including its own identity, display name, ordering, and associated command reference.
- **Command Reference**: The saved link from a sequence step to a command that should appear in the editor and be used during execution.
- **Unresolved Command Reference**: A saved step-to-command association whose target command cannot currently be loaded, but whose last known command name may still be shown to the user.
- **Execution Log Entry**: A human-readable record of step execution that includes the step label and command name a user recognizes in authoring.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In validation of saved sequences containing assigned steps, 100% of step-command selections remain visible after save and reopen.
- **SC-002**: In execution log review for validated sequence runs, 100% of step-executed entries include both the step label and the user-recognizable command name instead of only an internal step identifier.
- **SC-003**: Users can reopen and verify the command mapping of a five-step sequence in under 30 seconds without guessing which step maps to which command.
- **SC-004**: Previously valid saved sequences continue to reopen without losing any existing step-command associations during regression validation.
- **SC-005**: In validation of sequences with deleted or missing commands, 100% of unresolved steps are shown as unresolved rather than as intentionally blank, and include the last saved command name when that data exists.

## Assumptions

- Sequence steps already have a distinct saved identity and order separate from the command they invoke.
- Command names shown in the authoring UI are the same names users expect to see reflected in execution history.
- Existing sequences with truly missing command references should remain editable, but the fix should not invent replacements for data that no longer exists.
