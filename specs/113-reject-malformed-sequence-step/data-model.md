# Data Model: Reject a malformed sequence step on create

This feature adds no entity and no stored field. It changes only how the endpoint selects the body shape and which bodies it rejects.

## Body shapes of `POST /api/sequences`

| Shape | Selection rule (after this feature) | Result |
|-------|-------------------------------------|--------|
| Legacy branching | Body has `entryStepId` or `links` | 400 (no change) |
| Per-step | `steps` has one or more object items, and the body has no `blocks`; or a step object has `stepType` or `primitiveAction` | Reader checks each step; shape error gives 400 with `steps[<i>] (stepId '<id>'): ...` |
| Old (string ids) | `name` is a string, no `blocks`, each item of `steps` is a string (or `steps` is absent) | 201; 400 for a non-null `parameters`; `dryRun` gives 200 and stores nothing |
| Domain | All other bodies (for example, a body with `blocks`) | Current `blocks` validation; `dryRun` gives 200 and stores nothing |

## Shape errors (per-step reader)

| Condition | Error text |
|-----------|-----------|
| Step is not an object | `steps[<i>]: each step must be an object.` |
| No string `stepId` | `steps[<i>]: each step must include string stepId.` |
| Action step without `primitiveAction` object | `steps[<i>] (stepId '<id>'): each action step must include primitiveAction object.` |

## Old-body errors

| Condition | Error text |
|-----------|-----------|
| Item of `steps` is not a string | `steps[<i>]: each step must be a string command id or a step object.` |
| `parameters` is not `null` | `parameters requires the per-step body shape (steps as step objects).` |

Note: an old body with an item that is not a string and not an object (for example, a number) reaches the old branch, because only object items select the per-step shape.
