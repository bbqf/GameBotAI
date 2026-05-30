# Data Model: Preserve Sequence Step Command Names

## Entity: PersistedSequenceStep

- Purpose: Canonical saved representation of a linear sequence step used by authoring round-trip and runtime execution.
- Fields:
  - `stepId` (string, required, immutable within a sequence)
  - `label` (string, optional but preserved when supplied)
  - `stepType` (enum: `Action`, `Loop`, `Break`; defaults to `Action`)
  - `primitiveAction` (object, optional)
  - `action` (object, optional for compatibility)
  - `condition` (object, optional)
  - `loop` (object, optional)
  - `body` (`PersistedSequenceStep[]`, optional for loop steps)
  - `breakCondition` (object, optional)
- Validation rules:
  - `stepId` values are unique within a sequence.
  - Command-backed action steps preserve command reference fields across save and reload.
  - Loop and break-only fields are omitted for non-matching step types.

## Entity: CommandReference

- Purpose: The executable command target associated with a sequence step.
- Fields:
  - `commandId` (string, required for command-backed steps)
  - `commandNameSnapshot` (string, optional but persisted when known)
- Validation rules:
  - `commandId` is stable across save/load unless the user explicitly changes the step.
  - `commandNameSnapshot` mirrors the selected command's display name at save time when the command exists.

## Entity: UnresolvedCommandReference

- Purpose: A saved command reference whose target command can no longer be resolved from the command repository.
- Fields:
  - `commandId` (string, required)
  - `commandNameSnapshot` (string, optional)
  - `resolutionStatus` (enum: `resolved`, `unresolved`; required)
  - `displayState` (enum: `assigned`, `unresolved`, `blank`; required)
- Validation rules:
  - `displayState=unresolved` is used only when saved command data exists but live command lookup fails.
  - `displayState=blank` is used only when no command was intentionally assigned.

## Entity: SequenceAuthoringViewModel

- Purpose: UI-facing projection of a saved step used to populate authoring controls.
- Fields:
  - `stepId` (string, required)
  - `stepLabel` (string, required display identity)
  - `selectedCommandId` (string, optional)
  - `selectedCommandName` (string, optional)
  - `commandResolutionStatus` (enum: `resolved`, `unresolved`, `blank`; required)
  - `unresolvedMessage` (string, optional)
- Validation rules:
  - Resolved steps populate dropdown selection from `selectedCommandId`.
  - Unresolved steps do not masquerade as blank and surface the snapshot name when present.

## Entity: SequenceExecutionLogStep

- Purpose: Human-readable step-level log representation derived from sequence execution.
- Fields:
  - `sequenceId` (string, required)
  - `sequenceLabel` (string, required)
  - `stepId` (string, required when known)
  - `stepLabel` (string, required)
  - `commandId` (string, optional)
  - `commandName` (string, optional for command-backed steps)
  - `stepType` (string, required)
  - `status` (string, required)
  - `message` (string, required)
- Validation rules:
  - Command-backed step messages include both `stepLabel` and `commandName`.
  - Non-command steps continue using their existing specialized wording.

## Terminology Note

- `stepLabel` is the canonical authoring-facing display field across persistence, UI reload, and execution logs.
- Any log-facing "step name" wording refers to this same authoring step label rather than a separate field.

## State Transitions

- Authoring command-reference state: `blank -> resolved -> unresolved -> resolved`
  - `blank -> resolved`: user selects a command and saves.
  - `resolved -> unresolved`: saved command id no longer resolves on reload.
  - `unresolved -> resolved`: command becomes available again or user reassigns the step.
- Execution-log message state: `step-only wording -> step-plus-command wording`
  - Applies only to command-backed step log entries after the fix is implemented.

## Relationships

- `PersistedSequenceStep (1) -> (0..1) CommandReference`
- `CommandReference (1) -> (0..1) UnresolvedCommandReference`
- `PersistedSequenceStep (1) -> (1) SequenceAuthoringViewModel`
- `PersistedSequenceStep (1) -> (0..1) SequenceExecutionLogStep`