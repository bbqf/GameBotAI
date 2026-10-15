# Contract: API version and install refusal

## GET /api/version

Read-only. It uses the same API token rule as the other `/api` routes. It has no query and no body.

Response 200:

```json
{ "major": 1, "minor": 0, "patch": 0, "schemaFormat": 1 }
```

| Field | Type | Meaning |
|-------|------|---------|
| `major` | integer | API major version. A gamepack `requiredApiMajor` must equal it. |
| `minor` | integer | API minor version. |
| `patch` | integer | API patch version. |
| `schemaFormat` | integer | Format version of the sequence and command schema. |

Two calls give the same body. Other statuses: the same as other `/api` routes (for example 401 without a valid token).

## POST /api/gamepacks/install (changed)

A file with `manifest.requiredApiMajor` that differs from the running major version gives 422 (200 with `valid: false`
for `dryRun=true`). The body is the existing install response. The list `problems` has an entry:

```json
{
  "category": "structure",
  "objectType": "manifest",
  "name": "pns-daily",
  "owner": null,
  "message": "The gamepack needs API major version 2. This platform runs API major version 1."
}
```

A value below 1 gives the message `The required API major version must be a whole number of 1 or more.`
Nothing changes in the data when the problem list is not empty.

## POST /api/gamepacks/{name}/rollback (unchanged)

The rollback does not check the API major version.

## Docs line (single definition)

`docs/gamepacks.md` holds exactly one line in this format, on its own line:

```text
API version: 1.0.0, schema format 1
```

The format is `API version: {Major}.{Minor}.{Patch}, schema format {SchemaFormat}`. The docs test reads this line and
compares it with `ApiVersion`. Other files refer to this section and do not repeat the format.

## OpenAPI

- The route has the name `GetApiVersion`, the tag `Version`, and a summary and a description in STE.
- The swagger document has the path `/api/version`. A contract test checks this path and the `requiredApiMajor` text
  (the text says that the install refuses a value that differs from the running major version).
- The descriptions of `ApiVersionResponse` are in `Swagger/ApiVersionSchemaFilter.cs`, a separate filter from
  `GamepackSchemaFilter`.
- The gamepack file description names `requiredApiMajor` and says that the install refuses a value that differs from the
  running major version.
