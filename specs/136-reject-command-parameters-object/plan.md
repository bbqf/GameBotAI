# Implementation Plan: Reject a parameters object in command requests

**Branch**: `136-reject-command-parameters-object` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/136-reject-command-parameters-object/spec.md`

## Summary

`POST /api/commands` answers 500 `internal_error` when `parameters` is a JSON object, not a list. The
`JsonException` from `root.Deserialize<CreateCommandRequest>` is not caught, so the error middleware
turns it into a 500. `PATCH /api/commands/{id}` has the same defect in another form: `ReadUpdateBodyAsync`
converts the `JsonException` to a `BadHttpRequestException`, and the middleware also answers 500.

The fix has two edits in `src\GameBot.Service\Endpoints\CommandsEndpoints.cs`:

1. In POST, add a `catch (JsonException)` filter for a `Path` at or under `$.parameters`. It returns 400
   `invalid_request` with a message that names `parameters`. The `forceRestart` catch stays first.
2. In PATCH, `ReadUpdateBodyAsync` returns `(Request, Body, Error)`. For a `parameters` path error, it
   returns the same 400 result. The handler returns the error before the stored-command lookup.

No schema, no other endpoint, and no new error code change (FR-008). See [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET (ASP.NET Core minimal API), as in `src\GameBot.Service`
**Primary Dependencies**: `System.Text.Json`, existing `ErrorHandlingMiddleware`, `UnknownFieldScanner`
**Storage**: N/A (a rejected request stores nothing)
**Testing**: xUnit contract tests with `WebApplicationFactory` in `tests\contract\GameBot.ContractTests.csproj`; integration tests in `tests\integration\GameBot.IntegrationTests.csproj`
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: web-service
**Performance Goals**: No change. The new catch runs only on the error path.
**Constraints**: Warnings are errors, so an unused helper breaks the build. Add a helper in the task of its first use.
**Scale/Scope**: One file of production code, one new test file, one docs sentence.

## Constitution Check

*GATE: passed before Phase 0 and again after Phase 1.*

| Principle | Result |
|---|---|
| I. Code Quality | Pass. Small edit. A shared helper builds the 400 result for both endpoints. No dead code. CamelCase method names. |
| II. Testing | Pass. A bug fix needs a failing test first. Task 1 adds contract tests that fail on unchanged code and records the PATCH baseline. |
| III. UX Consistency | Pass. Same `{ error: { code, message, hint } }` shape and code `invalid_request` as other 400 answers. The message has a remediation hint. |
| IV. Performance | Pass. No hot-path change. Goal: none. |
| V. Living Documentation | Pass. Add one sentence to `docs\architecture.md` and refresh "Last reviewed". Set the spec Status and `specs\STATUS.md` when the feature is Done. |
| VI. STE | Pass. All new text in this plan and the artifacts uses STE. |
| Build and test gate | No known red build or test. The implementer runs the solution build and the tests before each commit. |

No violation. The Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/136-reject-command-parameters-object/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── commands-parameters-type-error.md
└── tasks.md          # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/CommandsEndpoints.cs   # POST catch, ReadUpdateBodyAsync, PATCH handler
└── Models/Commands.cs               # Unchanged (CreateCommandRequest, UpdateCommandRequest)

tests/contract/
└── Commands/ParametersTypeContractTests.cs   # New

docs/architecture.md                 # One sentence and the "Last reviewed" date
```

**Structure Decision**: Single existing service project. Edit points: the POST handler (about line 371),
`ReadUpdateBodyAsync` (about line 334), and its one caller, the PATCH handler (about line 473).

## Design Notes

- Path filter: convert a `JsonException` only when `Path` equals `$.parameters` or starts with
  `$.parameters.` or `$.parameters[`. Other type errors keep their current answer.
- The message is `parameters must be a list of parameter declarations`. The hint is
  `Send an array, even for one declaration.`
- PATCH order: read the body, return the `parameters` error if set, then look up the command. A bad
  type gives 400 for any id, and nothing changes.
- An empty body and other malformed bodies keep the current behavior in `ReadUpdateBodyAsync`.
- Tests record the baseline for `parameters: null`, `parameters: []`, and a wrong type in another
  field, then assert it again after the fix. Use unique command names. Check "no command stored" with
  `GET /api/commands` filtered by name.
- Build: `dotnet build C:\src\GameBot\GameBot.sln`. `ReadUpdateBodyAsync` has one caller: the PATCH handler.

## Complexity Tracking

No violations.
