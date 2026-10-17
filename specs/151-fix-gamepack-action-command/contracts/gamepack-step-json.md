# Contract: Action step JSON in a gamepack file

The endpoints do not change. This contract shows the step JSON that `POST /api/gamepacks/install` accepts.

## Sample: Action step that runs a command

```json
{
  "manifest": { "name": "demo-pack", "version": "1.0.0" },
  "sequences": [
    {
      "name": "Run the demo command",
      "steps": [
        {
          "stepId": "s1",
          "stepType": "Action",
          "primitiveAction": { "type": "command", "payload": { "commandId": "cmd-1" } },
          "commandReference": { "commandId": "cmd-1", "commandName": "Demo command" }
        }
      ]
    }
  ],
  "commands": [ { "id": "cmd-1", "name": "Demo command", "steps": [] } ]
}
```

## Accepted forms of an Action step

| Form | Result after install |
|------|----------------------|
| `primitiveAction` only | `primitiveAction` of the file. |
| `commandReference` only | `commandReference` of the file. |
| `commandId` as a member of the step | `primitiveAction` of type `command` and a `commandReference` with the same ID. |
| All three together | One consistent command action. |
| Stored form (`action` with `parameters`) | Kept as given. This is the export form. |
| Any form inside a Loop `body`, or the `body` or `elseBody` of a conditional step | Same result as at the root. |

## Result after install

`GET /api/sequences/{id}` returns step `s1` with `primitiveAction.type` `command`, `payload.commandId`, and
`commandReference` (`commandId`, `commandName`, and the computed `isResolved`). If the install gives the
command a new ID, all command ID fields of the step show the new ID, also in nested steps.

## Dry run problems

| Case | Category | Message content |
|------|----------|-----------------|
| Step member that the install cannot keep | `structure` | Sequence name, step ID, field name |
| `commandReference` and `primitiveAction` name different commands | `structure` | Sequence name, step ID, both IDs |
| `action` and `primitiveAction` differ in type or command | `structure` | Sequence name, step ID, both values |
| Step names a command that is not in the file or on the system | `reference` (as before) | Unchanged |

A step problem makes the dry run not valid. The install does not create the sequence.

## Unchanged

`POST /api/sequences`, the export form (`action` with `parameters`), conditions, loops, `breakCondition`
(`imageVisible`), and the other object types of a gamepack file (commands, images, seed templates).

## Alias scope and unresolved commands

| Case | Category | Message content |
|------|----------|-----------------|
| `primitiveAction`, `commandId`, or `commandReference` on a step that is not an Action step | `structure` | Sequence name, step ID, field name |
| Action step names a command (by any of the three forms) that is not in the file or on the system | `reference` | Command ID |

The install description in `GamepacksEndpoints.cs` (`WithDescription` of `InstallGamepack`) gets the sample of the
Action step above and a one-sentence rule for the aliases.
