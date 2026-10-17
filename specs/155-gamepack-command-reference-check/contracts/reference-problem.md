# Contract: reference problem for a command

The API shape does not change. The dry run response and the install response already carry a list of problems.

## New cases

For a sequence step that names a command that is in no list, the problem list has an item:

```json
{
  "category": "reference",
  "objectType": "sequence",
  "name": "<sequence name>",
  "message": "Command '<id or name>' is not in the gamepack or in the installation."
}
```

- The dry run sets `valid: false`.
- The install refuses with the same item and writes nothing.
- A command that is in the gamepack or on the instance gives no item.
- Property names follow the existing `GamepackProblem` JSON form.
