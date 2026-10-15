# Data Model: API Version and Gamepack Compatibility

No stored data changes. The registry file keeps its form. `GamepackRegistry` already stores `RequiredApiMajor`.

## ApiVersion (static class, `GameBot.Domain.Gamepacks`)

| Member | Type | Value | Meaning |
|--------|------|-------|---------|
| `Major` | int | 1 | Changes when a valid sequence or command can become invalid, or when a route breaks. |
| `Minor` | int | 0 | Changes when the API adds a feature in a compatible way. |
| `Patch` | int | 0 | Changes for a fix with no contract change. |
| `SchemaFormat` | int | 1 | The format version of the sequence and command JSON. |

Rule: inside one `Major`, a valid sequence stays valid.

## ApiVersionResponse (DTO, `GameBot.Service.Endpoints.Dto`)

| Field | Type | Source |
|-------|------|--------|
| `major` | int | `ApiVersion.Major` |
| `minor` | int | `ApiVersion.Minor` |
| `patch` | int | `ApiVersion.Patch` |
| `schemaFormat` | int | `ApiVersion.SchemaFormat` |

## Manifest field (existing)

`GamepackManifest.RequiredApiMajor` (`int?`). Absent: no check. Present and below 1: structure problem. Present and not
equal to `ApiVersion.Major`: structure problem.

## Validation rules

| Case | Result |
|------|--------|
| `requiredApiMajor` absent | No version problem |
| `requiredApiMajor` = `ApiVersion.Major` | No version problem |
| `requiredApiMajor` < 1 | Structure problem on `manifest`: "The required API major version must be a whole number of 1 or more." |
| `requiredApiMajor` >= 1 and != `ApiVersion.Major` | Structure problem on `manifest`: "The gamepack needs API major version N. This platform runs API major version M." |
| Rollback | No version check |

The problem uses the existing `GamepackProblem(category: structure, objectType: manifest, name: <gamepack name>, message)`.
