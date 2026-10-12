# Data Model: Reject a parameters object in command requests

This feature adds no stored entity and changes no schema (FR-008).

## Command request (existing)

`CreateCommandRequest` and `UpdateCommandRequest` in `src\GameBot.Service\Models\Commands.cs`.
Field `Parameters` has type `Collection<ParameterDeclarationDto>?`. It is optional.

## Parameter declaration (existing, unchanged)

Fields: `name`, `type`, `required`.

## Validation rule (new)

| Value of `parameters` | Result |
|---|---|
| absent, `null`, `[]`, list of valid items | Same as before this change |
| object, string, number, boolean | 400 `invalid_request`, message names `parameters` |
| list with an item that is not an object | 400 `invalid_request`, message names `parameters` |

## State

No state change. A rejected request creates or changes no command.

## Internal type change

`ReadUpdateBodyAsync` returns `(UpdateCommandRequest? Request, JsonElement Body, IResult? Error)`.
