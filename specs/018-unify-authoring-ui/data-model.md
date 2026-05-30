# Data Model: Unified Authoring Object Pages

## Entities

### Action
- **Attributes**: id, name, description, parameters (array of {name, type, default, required}), executionMetadata (type/category), tags.
- **Constraints**: name unique per catalog; parameters require unique names; required parameters must have type.
- **Relationships**: Referenced by Commands and Triggers.

### Command
- **Attributes**: id, name, description, detectionTargets (ordered array of targets), steps (ordered array referencing Actions or primitive steps), bindings/inputs, metadata (gameId, category).
- **Constraints**: detectionTargets order preserved; steps order preserved; at least one Action/step.
- **Relationships**: References Actions; associated to Game Profile.

### Trigger
- **Attributes**: id, name, description, conditions (array), actions (ordered array referencing Actions or Commands), scope/gameId.
- **Constraints**: at least one condition and one action; order preserved for actions.
- **Relationships**: References Actions/Commands; linked from Game Profile.

### GameProfile
- **Attributes**: id, name, description, defaultCommands (array of Command ids), defaultTriggers (array), inputMappings (array), game metadata.
- **Constraints**: name unique; references must resolve.
- **Relationships**: References Commands and Triggers.

### Sequence / StepCollection
- **Attributes**: id, name, steps (ordered array of step descriptors referencing Actions or primitives), metadata (loop/retry flags as applicable).
- **Constraints**: steps order preserved; steps require type and parameters.
- **Relationships**: Steps may reference Actions; may be embedded in Commands.

## Shared Validation Rules
- All referenced ids must resolve; UI must prevent save otherwise.
- Array sections must persist explicit order as saved.
- Required fields: name, at least one behavior element (Action/step/action list) per object type.
- Live save requires success confirmation and error surfacing with actionable messages.
