# Data Model: Honour dryRun on sequence updates and reject unresolvable command references

No persisted shape changes. This feature only changes which writes are allowed to reach storage.

## Sequence write request (POST / PUT / PATCH `/api/sequences[/{id}]`)

| Field | Type | Rule |
|---|---|---|
| `dryRun` | boolean, optional | `true` → validate only, never persist (create already; update and patch now). Absent/`false` → real write. |
| `version` | int, optional (update/patch) | Mismatch → `409`, identically for dry run and real write. |
| `steps[]` | per-step objects | Unchanged shape. |
| `steps[].primitiveAction.payload.commandId` | string | On a `command` step, anywhere in the tree: MUST name an existing command, unless (update/patch only) the stored sequence already references the same id. |

## Validation outcome

| Outcome | Status | Body |
|---|---|---|
| Dry run, all checks pass | `200` | `{ valid: true, dryRun: true, errors: [] }` |
| Real write, all checks pass | `201` (create) / `200` (update, patch) | sequence response (unchanged) |
| Unknown sequence id (update/patch) | `404` | unchanged |
| Version conflict | `409` | `SequenceSaveConflictDto` (unchanged) |
| Structural / image / command-reference errors | `400` | `{ message: "Invalid sequence payload", errors: string[] }` |
| Parameter errors | `400` | existing parameter error body (unchanged) |

## Tolerated command ids (update/patch)

`toleratedIds` = every explicit `commandId` on a command-backed step anywhere in the *stored*
sequence (top level, `Body`, `ElseBody`), case-insensitive. Computed before the stored steps are
replaced. For create it is empty.

## State transitions

```text
stored(vN) --PUT/PATCH real, valid-->            stored(vN+1)
stored(vN) --PUT/PATCH dryRun:true, valid-->     stored(vN)   + 200 envelope
stored(vN) --any write, invalid (incl. dryRun)--> stored(vN)   + 400/409
```
