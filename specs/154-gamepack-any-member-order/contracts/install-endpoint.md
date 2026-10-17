# Contract: gamepack install routes

Routes: the manual install route and the catalog install route (both use `GamepacksEndpoints.ToInstallResult`). No route, field, or schema change.

| Input | Dry run | Real install |
|-------|---------|--------------|
| Polymorphic object, `type` first | 200, `valid:true` | 200 |
| Polymorphic object, `type` in the middle or last | 200, `valid:true` (was 500) | 200 (was 500) |
| `type` missing | 400, `InvalidJson`, problem with JSON path | 400, `InvalidJson`, problem with JSON path |
| `type` unknown, not a string, or duplicate | 400, `InvalidJson`, problem with JSON path | 400, `InvalidJson`, problem with JSON path |
| Other wrong shape | unchanged (200, `valid:false`) | unchanged (422) |

Status per error class (both routes):

| Error class | Dry run | Real install |
|-------------|---------|--------------|
| Type missing, unknown, not a string, or duplicate (also nested) | 400, `InvalidJson`, JSON path | 400, `InvalidJson`, JSON path |
| Other wrong shape | 200, `valid:false` | 422 |
| Text is not valid JSON | 400, `InvalidJson` | 400, `InvalidJson` |

The 400 body is the existing install response with `valid:false` and a `problems` list, and the existing `InvalidJson` error code. Each problem has category `structure`. The message holds the JSON path (for example `$.sequences[0].steps[0].breakCondition`) and a short reason.

The status 500 MUST NOT occur for any file content in these cases.

Classification of a type error uses the structure of the JSON and the type metadata. It does not use exception message text (FR-009).

Hash and signature: the same file content in a different member order gives the same content hash and the same signature status.
