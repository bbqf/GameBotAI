# Data Model: Gamepack command reference check

No stored data changes. No file format change.

## Command reference forms of a sequence step

| Form | Field | Kind | Resolved against |
|------|-------|------|------------------|
| Step id | `SequenceStep.CommandId` | id | gamepack command ids and instance command ids |
| Reference id | `SequenceStep.CommandReference.CommandId` | id | same |
| Payload id | `SequenceStep.Action.Parameters["commandId"]` (type `command`) | id | same |
| Reference name | `SequenceStep.CommandReference.CommandName` | name | gamepack command names and instance command names |

## Rules

- Blank values are skipped.
- A name is checked only when the step has no id in any id form.
- Id match and name match are case-insensitive. A name is trimmed before the match.
- Commands that the plan removes are not targets.
- Each missing value gives one `reference` problem per sequence (de-duplicated, case-insensitive).

## Result item

`GamepackProblem(category = reference, type = sequence, name = <sequence name>, message = "Command '<x>' is not in the gamepack or in the installation.")`. Any such problem gives `valid: false`.
