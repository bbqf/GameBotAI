# Contract: sequence writes — dry run and command-reference existence

Applies to `POST /api/sequences`, `PUT /api/sequences/{sequenceId}`, `PATCH /api/sequences/{sequenceId}`.

## C-1 Dry run never persists

For any request with top-level `"dryRun": true`:

- No sequence is created (POST, per-step body), and the target sequence reads back with the same
  `version` and content (PUT, PATCH); nothing is written, so stored timestamps are untouched too.
- If a real write of the same body would fail, the response is that same failure: same status
  (`400`, `404`, `409`) and same body.
- Otherwise the response is `200 OK`:

```json
{ "valid": true, "dryRun": true, "errors": [] }
```

POST keeps its documented scope: `dryRun` is recognised on the per-step body shape. PUT and PATCH
recognise it on every body shape.

## C-2 Nonexistent command references are rejected

A per-step body whose `command` step (at any depth: top level, loop body, if body, else body) has a
`primitiveAction.payload.commandId` that matches no existing command (case-insensitive) is rejected:

```http
400 Bad Request
{
  "message": "Invalid sequence payload",
  "errors": [ "Command reference 'does-not-exist' does not exist (used by: step-1, step-4)." ]
}
```

- One error per missing id, ids ordered case-insensitively; each lists the distinct step ids using it.
- Nothing is persisted.
- Identical with and without `dryRun` (so dry-run validation reports it).

## C-3 Carried-over unresolved references are allowed on update

On PUT/PATCH, a `commandId` that no longer resolves but is already referenced anywhere in the stored
sequence is **not** rejected. The step reads back as before: `commandReference.isResolved: false`
with its last-known `commandName`. A nonexistent id not present in the stored sequence is rejected
by C-2.

## C-4 Unchanged

- Real writes that reference existing commands: request and response shapes unchanged.
- A command step with no payload `commandId`: keeps its existing single B-003 error.
- Execution of stored sequences with unresolved references: unchanged.
- Legacy body shapes (string step lists): no existence check.
