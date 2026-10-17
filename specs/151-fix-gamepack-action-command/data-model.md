# Data Model: Action step in a gamepack file

No stored data changes. This file shows how the file form of a step maps to the stored form. The mapping
applies to each step, including each step in a Loop `body` and in the `body` or `elseBody` of a conditional step.

## File form of an Action step (accepted)

| File member | Stored member | Rule |
|-------------|---------------|------|
| `stepId` | `StepId` | As given. |
| `stepType` | `StepType` | `Action` for a command action. |
| `action` (stored form) | `Action` | Kept as given. Export writes this form. It wins over `primitiveAction`. |
| `primitiveAction.type` | `Action.Type` | As given. |
| `primitiveAction.schemaVersion` | `Action.SchemaVersion` | As given. |
| `primitiveAction.payload` | `Action.Parameters` | Each pair is copied. |
| `primitiveAction.payload.commandId` | `CommandId` | Set when the type is `command`. |
| `commandReference.commandId` | `CommandReference.CommandId` | Also sets `CommandId` when no other source gives it. |
| `commandReference.commandName` | `CommandReference.CommandName` | Kept as given. |
| `commandReference.isResolved` | none | Accepted and not stored. `GET /api/sequences` computes it. |
| `commandId` (member of the step) | `CommandId` | Gives `Action` of type `command` (payload `commandId`) and a `CommandReference` with the same ID. |

## Rules

- The command ID sources (`commandReference`, `primitiveAction.payload.commandId`, `commandId`) MUST name
  the same command. If they differ, the read gives a `structure` problem and the sequence is not created.
- If a step has `action` and `primitiveAction`, `action` wins. If `primitiveAction` names a different type or
  command, the read gives a `structure` problem for the step.
- A step member that is not a member of the stored step and not an accepted alias (`primitiveAction`,
  `commandId`, `commandReference`) gives a `structure` problem. The message has the sequence name, the step ID,
  and the field name. Names match without case. The check covers each step, including nested steps, and not
  nested payloads.
- When the install gives a command a new ID, the rewrite changes `CommandId`, `CommandReference.CommandId`, and
  `Action.Parameters["commandId"]` together, in every step list.
- Other primitive action types are out of scope. A test measures them.

## Entities

- **Gamepack file**: unchanged (manifest, sequences, commands, images, seedTemplates).
- **Sequence step**: unchanged type `SequenceStep`.
- **Command reference**: unchanged type `SequenceCommandReference` (`CommandId`, `CommandName`).
- **Problem**: unchanged type `GamepackProblem` with category `structure`.
- **Step reader** (new, no stored state): input is the `JsonObject` of a sequence. Output is the same object in
  the stored form, and a list of `structure` problems.

## Alias scope

Only an Action step accepts the aliases `primitiveAction`, `commandId`, and `commandReference`. On a step of
another type these members give an unknown-member `structure` problem (sequence name, step ID, field name).
A step that names a command that is not in the file and not on the system gives a `reference` problem from the
existing validator check. `isResolved` is never stored.
