# Research: commandStepOutcome condition

No item in the Technical Context stays as NEEDS CLARIFICATION. The decisions follow.

## D1 - Where the step states come from

- **Decision**: Read `PrimitiveTapStepOutcome.Status` from `CommandForceExecutionResult.StepOutcomes` in `SequenceExecutionService` (the same list that `ClassifyDispatch` reads). Keep only the entries with status `executed`, `skipped_detection_failed`, `completed_timeout`, or `image_detected`. Put them in a new optional field `StepStates` (`IReadOnlyDictionary<int, string>`) of `CommandDispatchOutcome`.
- **Rationale**: The states exist already (`CommandExecutor.cs` lines 578, 688, 702; `executed` for taps). `CommandDispatchOutcome` is the one object that goes from the service to the runner for each command run. A new optional field with a default of null changes no existing caller (FR-009, FR-010).
- **Alternatives**: (a) Give the runner the full `PrimitiveTapStepOutcome` list: it is an `internal` Service type, and the Domain project cannot see it. (b) Read the execution log after the run: slow, and the log may be off. (c) Change `executeCommandAsync` to return a value: breaks all callers.

## D2 - Nested commands (a command step that calls a command)

- **Decision**: The flat outcome list repeats step orders when a command calls a nested command. Only the steps of the command that the sequence step names are readable. `CommandExecutor.cs` marks each outcome with its nesting depth. `PrimitiveTapStepOutcome` (in `ICommandExecutor.cs`) gets a new optional field `Depth` (default 0). The recursion passes depth + 1. This is the only change to the command run path (FR-009a). It adds a value and changes no behavior. A test MUST show that a nested step with the same order does not change the result. `SequenceExecutionService` keeps only depth 0 entries. If one order appears two times at depth 0 (it cannot: orders are unique in one command), the first one wins.
- **Rationale**: `commandStepOrder` is the order in the command that the sequence step runs, as the issue says. A nested order has no clear meaning in that command.
- **Alternatives**: Path keys such as `3/2`: more complex, not requested.

## D3 - Run-scoped storage and the latest-run rule

- **Decision**: `SequenceRunner` adds a map `commandStepStates`: sequence step key -> `IReadOnlyDictionary<int, string>`. After each command run of a step, it sets the entry to the new `StepStates` (or removes it when the value is null). A new command run replaces the older entry, even if the new run has no readable states. So the latest run counts. The map is separate from `stepOutcomes`. A step that is skipped, fails, or does not run in a dry run keeps the older entry. The older entry stays until the same step runs a command again (same as `commandOutcome`).
- **Rationale**: Matches the `commandOutcome` loop rule (spec Clarifications). A separate map keeps `commandOutcome` unchanged (FR-010).
- **Alternatives**: Encode as a string in `stepOutcomes` (for example `step12=executed`): would break the `Matches` logic and mix two records.

## D4 - Evaluation and the missing-outcome case

- **Decision**: Add a case to `EvaluateLeafAsync`. It looks up `commandStepStates[stepRef][order]`. Found: compare with `expectedState` (case-insensitive). Not found: return false and do not throw. `negate` applies after, in the existing code path. The evaluator receives the map through a new optional parameter of the evaluation entry points (same way as `stepOutcomes`). Every evaluation writes one Debug/Information log line `commandStepOutcome(stepRef=..., order=..., expected=..., actual=<state|none>)`, and the existing condition trace text (`Describe`) shows the same fields.
- **Rationale**: FR-002, FR-004, FR-005. A missing outcome must be a normal case ("the recall did not run"). `commandOutcome` throws `CommandOutcomeUnavailable`; the new type must not.
- **Alternatives**: Throw and let the run fail: rejected by the spec.

## D5 - Validation

- **Decision**:
  - `stepRef`: empty, unknown, or not before the condition: the same messages as `commandOutcome` (reuse `ValidateStepReference` and the per-step branch), with the type name `commandStepOutcome`.
  - `stepRef` must name a step with a `commandId` (a command step). Otherwise: error "Step 'X' commandStepOutcome stepRef 'Y' must reference a step that runs a command."
  - `commandStepOrder` missing or below 1: error.
  - `expectedState`: one of the four states (case-insensitive), else error that lists the four states (`CommandStepOutcomeStates.AllText`).
  - Order exists in the command: checked ONLY in the save validation and the save dry run in `SequencesEndpoints` through the command repository. If the command cannot be loaded, skip the check (FR-007). Error names the command and the order.
  - The run dry run and the run check only the `stepRef` rules. A missing order at run time gives "no outcome recorded".
  - API schema: `commandStepOrder` has `minimum: 1`.
- **Rationale**: FR-006, FR-007. The Domain validators have no repository access, so the order check lives in the endpoint layer, where the command repository is available.
- **Alternatives**: Check the order at run time only: rejected (SC-004 needs an error before the run).

## D6 - Position rules (loops, if, break, all/any/none)

- **Decision**: Use the same code paths as `commandOutcome`: the new type is a `SequenceStepCondition` leaf. Wherever the six condition slots call `CompositeConditionValidator`, `SequenceStepValidationService`, and the evaluator, the new type gets the same treatment. Add the type to the gate in `CompositeConditionValidator.Validate` so a leaf at the root is walked.
- **Rationale**: FR-008, one model.

## D7 - Web UI

- **Decision**: Add the type to the condition type list in `ConditionFields.tsx` and `BreakStepRow.tsx`. Fields: `stepRef` (the same prior-step picker as `commandOutcome`, limited to command steps), a number input for the order, and a select with the four states. Validation in `lib/validation.ts` gives the same errors as the service (empty order, no state). Phone support level: reduced, as the other condition fields.
- **Rationale**: FR-012, User Story 5.

## D8 - Schema

- **Decision**: Add the type to the OpenAPI schema through the existing schema filters (`ConditionReferenceScopeSchemaFilter`, `PrimitiveActionSchemaFilter`, `ConditionalFlowSchemaDocumentFilter`) and the `SwaggerConfig` examples, with a description of the four states.
- **Rationale**: FR-011.

## D9 - Out of scope (confirmed)

Option B (a run-scoped flag and a step to set it), changes to `commandOutcome`, and recall or shield logic stay out (FR-013).
