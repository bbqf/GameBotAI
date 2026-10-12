# Research: Reject a parameters object in command requests

## R1. Real throw site of the 500 on POST

Decision: The throw site is `root.Deserialize<CreateCommandRequest>(WebJsonOptions)` in the POST handler
of `CommandsEndpoints.cs` (about line 372). It is not the unknown-field scanner.

Evidence:
- `UnknownFieldScanner.Find` runs first (line 366). For `parameters` as an object, `WalkObject` reads the
  member `parameters` and finds the property `Collection<ParameterDeclarationDto>`. It then calls `Walk`
  with `ValueKind.Object` and that property type. `WalkObject` returns at once because `IsDtoType` is false
  for a type that implements `IEnumerable`. The scanner adds nothing and does not throw.
- The scanner never throws a `JsonException`. It only reads `JsonElement` values.
- `Deserialize` then throws `JsonException` (the object cannot convert to a collection). Its `Path` is
  `$.parameters`.
- The only `catch` on that call filters on `forceRestart` in `Path`. The exception is not caught.
- `ErrorHandlingMiddleware` catches it as a general `Exception`. It answers 500 with code `internal_error`
  and puts the exception message in `hint`.

Alternatives considered: Fix the scanner. Rejected: the scanner is not the cause.

## R2. PATCH path

Decision: PATCH has the same defect in a different form.

Evidence: `ReadUpdateBodyAsync` catches `JsonException` and throws `BadHttpRequestException`. The handler
code throws it, not the framework binder. The error middleware has no special case for it, so it falls to the
general catch and answers 500 `internal_error`. The test in task 1 must confirm this baseline on unchanged
code. If the baseline is 400, the PATCH change is only a message change and the plan keeps the same edit.

Alternatives considered: Handle `BadHttpRequestException` in the middleware. Rejected: it changes every
endpoint, and FR-008 forbids that.

## R3. Which exceptions to convert

Decision: Convert a `JsonException` only when `Path` is `$.parameters` or is under it
(`$.parameters.` or `$.parameters[`). This covers an object, a string, a number, `true`, and a list item
that is not an object (`$.parameters[0]`). It also covers a wrong type inside an item (for example
`$.parameters[0].required`), and the message still names `parameters`.

Other type errors (for example `name` as a number) keep the answer they have on unchanged code (spec
clarification). A test records this baseline first. The `forceRestart` catch stays first and unchanged.

Alternatives considered: Catch all `JsonException` in POST. Rejected: it changes unrelated answers.

## R4. Unchanged cases

`parameters: null` and `parameters: []` bind without error. The property type is nullable. Tests record the
current answer and assert it again after the fix (FR-007).

## R5. Error format

Decision: Use code `invalid_request` and the shape `{ error: { code, message, hint } }`. This is the same
shape as the other 400 answers in the same handler. No new error code (spec clarification).

## R6. Test location and filter

Project: `C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj`. New file:
`tests\contract\Commands\ParametersTypeContractTests.cs`, class `ParametersTypeContractTests`, in the style
of `UnknownStepFieldContractTests` (WebApplicationFactory, bearer token `test-token`).
Run: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Commands"`.
Integration tests for commands: `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~Command"`.
Contract tests share a bin data dir, so use unique command names.

## R7. Docs

`docs\architecture.md` records the feature 135 rule in its "Last reviewed" line. Add one sentence for this
rule and refresh the date.
