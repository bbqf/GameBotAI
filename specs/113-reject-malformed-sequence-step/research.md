# Research: Reject a malformed sequence step on create

## R-001: Why the service stores an empty sequence

**Finding**: `CreateSequenceAsync` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` calls `IsPerStepRequestCandidate(root)`. That method looks only at the first item of `steps`, and it returns `true` only when that item has `primitiveAction` or `stepType`. For the reproduction body, the first step has only `stepId` and `commandReference`, so the method returns `false`.

The endpoint then uses the old branch "Authoring shape: { name: string, steps?: string[] }". The condition of that branch is only "`name` is a string and there is no `blocks`". The branch reads only the string items of `steps`, so it drops both object steps. It does not read `parameters`, and it does not read `dryRun`. It calls `repo.CreateAsync` and returns 201.

With `"stepType":"Action"` on the first step, `IsPerStepRequestCandidate` returns `true`, and `TryReadPerStepRequest` returns the error `each action step must include primitiveAction object.` This is the 400 of the control case.

**Decision**: Select the per-step shape when one or more items of `steps` is a JSON object.

**Alternatives rejected**:
- Look at each step for `primitiveAction` or `stepType`. A body whose steps have none of the two still goes to the old branch and loses data.
- Map `commandReference.commandId` to a command step. The code has no documented mapping for this shape. `commandReference` is a name label that the service computes from `primitiveAction.payload.commandId` (see `MapCommandReference` and `EnrichCommandReferences`). The spec clarification selects a rejection.

## R-002: The `blocks` bodies

**Finding**: A body with a `blocks` property goes to the domain branch (`JsonSerializer.Deserialize<CommandSequence>`). The tests in `tests/integration/Sequences/BlocksValidationErrorTests.cs` send `blocks` bodies. Their block steps are objects such as `{ order, commandId }`, but they are inside `blocks`, not in `steps`. A `blocks` body can also have object steps in `steps` in the domain form.

**Decision**: For a body with `blocks`, keep the current rule (a step object with `stepType` or `primitiveAction` selects the per-step shape). Only the `blocks` validation and `dryRun` apply to the domain branch. This keeps the current `blocks` bodies unchanged.

## R-003: `dryRun` on create

**Finding**: The per-step branch reads `perStepRequest.DryRun`. The old branch and the domain branch do not read `dryRun`. `PUT` and `PATCH` use `IsDryRunRequested(root)`, which reads the raw body (feature 091).

**Decision**: In the old branch and in the domain branch, call `IsDryRunRequested(root)` after all checks and before `repo.CreateAsync`. A valid body gets the current dry-run body `{ valid: true, dryRun: true, errors: [] }`.

## R-004: The old branch and data loss

**Finding**: The old branch keeps only string items. It ignores `parameters`, `interStepDelayRangeMs` and `watchdogTimeoutMs`.

**Decision**: The old branch rejects with 400 an item of `steps` that is not a string (`steps[<i>]: each step must be a string command id or a step object.`) and a `parameters` value that is not `null` (`parameters requires the per-step body shape (steps as step objects).`). The issue names steps and parameters. The delay range and the watchdog in the old body are out of scope; no client sends them.

## R-005: The error prefix

**Finding**: The shape errors of `TryReadPerStepRequest` do not name the step. The issue asks for an error that names the step by its id or position. No test compares these texts.

**Decision**: A new private helper `DescribeStepPosition(int index, JsonElement step)` returns `steps[<i>] (stepId '<id>')` when `stepId` is a string, otherwise `steps[<i>]`. Each shape error in the loop starts with this text and `: `.

## R-006: `PUT` and `PATCH`

**Finding**: `UpdateSequenceAsync` and `PatchSequenceAsync` use the same `IsPerStepRequestCandidate` and `TryReadPerStepRequest`. Today, a body with a malformed first object step skips the per-step branch and also the string-steps branch, so the service keeps the stored steps and returns 200 with a new version.

**Decision**: The shape-selection change applies to them. The per-step reader rejects the step with 400. Their `dryRun` code does not change (issue #177 is out of scope).

## R-007: Documentation

**Decision**: Change `SequenceCreateDescription` in `SwaggerConfig.cs`: `dryRun` applies to each body shape, and a step that breaks the shape rules gets 400 that names the step. Update the "Dry-run / validate-only sequence mode" section of `docs/architecture.md`, add a "Fixed" entry to `CHANGELOG.md`, and add row 113 to `specs/STATUS.md`. Check the OpenAPI snapshot tests in `tests/contract` for the old description text.
