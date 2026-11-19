# Research: Parameter Condition (193)

All items of the Technical Context are known. This file records the design decisions.

## R-001: Domain shape of the condition

- **Decision**: Add the sealed class `ParameterStepCondition : SequenceStepCondition` in `src/GameBot.Domain/Commands/SequenceStepCondition.cs`, with the discriminator `parameter`. The fields are `Name` (string), `Operator` (string), and `Value` (`ParameterConditionValue?`). `ParameterConditionValue` is a record with `Text` (string) and `IsNumber` (bool), and a `JsonConverter` that reads a JSON number or a JSON string and writes the same JSON kind. The converter refuses all other JSON kinds.
- **Rationale**: The domain type is stored with System.Text.Json polymorphism, the same as the other condition kinds. A small value record keeps the JSON kind (clarification 4) and is simple to compare. The gamepack export and import, the step-through, and the file repository copy the condition through this JSON, so FR-016 needs no other change.
- **Alternatives considered**: `JsonElement Value` (rejected: a `JsonElement` holds a reference to a parsed document and is hard to compare and to copy). `string Value` (rejected: a read gives `"0"` for `0`).

## R-002: Run-time value resolution

- **Decision**: `SequenceStepConditionResolver` resolves a `ParameterStepCondition` against the `ParameterScope`, the same as it resolves an `imageVisible` placeholder. `HasPlaceholder` returns `true` for each `ParameterStepCondition`, so the fast path does not skip it. The resolver makes a new `ParameterStepCondition` with two run-time properties that have `[JsonIgnore]`: `ResolvedValue` (string) and `CompareAsNumber` (bool). `CompareAsNumber` is true when the declaration has the type `number`. The resolver uses the new method `ParameterScope.TryResolveEntered(name, out ParameterValue)`. This method gives the value before a number `pattern` changes it (FR-012).
- **Rationale**: All condition slots (step condition, `if.condition`, `loop.condition`, `breakCondition`, and the step-through) already call `SequenceRunner.ResolveCondition`, so one change covers FR-002 and FR-003. The evaluator stays free of the scope.
- **Failure rules**:
  - No value in the scope, or no declaration in the scope: `ParameterResolutionReasons.Unresolved`. `SequenceRunner.ToFailureKind` gives `ParameterUnresolved` (FR-010).
  - A `number` declaration with a value that is not a whole number: the current reason `ParameterResolutionReasons.NotANumber`. `ToFailureKind` changes so that `NotANumber` gives `ParameterValueInvalid` (FR-011).
  - A value that came from a parent whole-placeholder binding and that a parent pattern already changed (`Mapped`): the method gives that text. If it is not a whole number, the rule above applies. This case is rare and is documented in the data model.
- **Alternatives considered**: Give the scope to the evaluator (rejected: four call sites change, and the evaluator is pure today).

## R-003: Compare rules

- **Decision**: New static class `ParameterConditionRules` in `src/GameBot.Domain/Commands/ParameterConditionRules.cs`. It holds the operator names, the shape checks, and `Compare(resolvedValue, compareAsNumber, value, operator)`.
  - Number compare: parse both texts with `long.TryParse(NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)` after a trim, then compare.
  - Text compare: `string.Compare(runValue, value.Text, StringComparison.OrdinalIgnoreCase)`. No trim (edge case "max ").
  - The operator names are case-sensitive at save, the same as the JSON discriminators: `equals`, `notEquals`, `greaterThan`, `lessThan`.
- **Rationale**: Feature 078 declares `number` as a whole number. `long` covers all values of `Min` and `Max` (`int`). Ordinal compare is the same in all cultures.
- **Alternatives considered**: `decimal` compare (rejected: the declarations hold whole numbers only). Culture-aware compare (rejected: the result changes with the machine culture).

## R-004: Save checks and dryRun

- **Decision**: Two levels, the same as for the other condition kinds.
  1. Shape (no declarations needed): `CompositeConditionValidator.Walk` gets a `ParameterStepCondition` case that calls `ParameterConditionRules.ValidateShape`. The root gate of `Validate` includes `ParameterStepCondition`, so a leaf directly in a slot is checked also. Messages: `parameter condition requires name.`, `parameter operator must be one of equals, notEquals, greaterThan, lessThan.`, `parameter condition requires value.` `FileSequenceRepository` gets the same last guard.
  2. Declarations: `ParameterValidationService.ValidateSequence` calls the new `AddParameterConditionIssues`. It walks each condition slot of each step (also nested bodies and composites) with the field paths that `ParameterReferenceScanner` uses (for example `if.condition.children[1]`). New codes in `ParameterValidationCodes`: `unknown_condition_parameter` (FR-006), `invalid_condition_value` (FR-008), and the current `list_in_single_field` (FR-009).
  - Path formats: the shape check keeps the `$`-rooted path of `CompositeConditionValidator` (for example `condition at $.children[0]`), the same as for the other condition kinds. The declaration check uses the slot field path of `ParameterReferenceScanner` (for example `condition at if.condition.children[0]`), the same as the other parameter issues. The two formats are intentional: each validator keeps its current format.
  3. The request check `CollectConditionRegionErrors` in `SequencesEndpoints` refuses a `value` that is not a JSON string or a JSON number (400, FR-007).
- **Rationale**: The sequence save path and the `dryRun` path already run both validators before the `IsDryRunRequested` return, so the `dryRun` refuses the same conditions (FR-006). The declaration check needs the declarations, so it goes into the parameter validation, not into the shape validator.
- **Alternatives considered**: Report the `name` as a `ParameterReference` of the scanner (rejected: the current check accepts the queue built-ins `queue.*`, and the clarification refuses them; also the reference check does not know the operator and the value).

## R-005: Execution log

- **Decision**: The evaluator writes one line for each evaluation to `CommandStepStateScope.Current?.AddLog`, the same as `matchedImage`: `parameter(name=fighterBatch, operator=equals, value=0) actual=0 compare=number`. `SequenceStepConditionEvaluator.Describe` renders `parameter(name=..., operator=..., value=...)` with the `NOT ` prefix.
- **Rationale**: FR-013. The format follows feature 167 and feature 181.

## R-006: OpenAPI

- **Decision**: Add `ParameterConditionContract` to `SequenceStepContracts.cs` (discriminator `parameter`; `Name`, `Operator`, `Value` as `JsonElement?`). Register it in `ConditionalFlowSchemaDocumentFilter` (generate and alias `ParameterCondition`). Add `ParameterConditionSchemaFilter` with the description, the field texts, an `enum` of the four operators on `operator`, and `oneOf` string or integer on `value`. Register the filter in `GameBotServiceSetup`.
- **Rationale**: FR-014. The same pattern as `MatchedImageConditionSchemaFilter`.

## R-007: Web UI

- **Decision**: Add the type `ParameterStepCondition` to `src/web-ui/src/types/sequenceFlow.ts` and `'parameter'` to `PerStepConditionType`. `ConditionFields.tsx` gets the option `parameter` and three controls: a select of the declared parameter names (new optional prop `parameterNames`), a select of the four operators, and a text input for the value. The value goes to the payload as a number when the selected parameter is a `number` parameter and the text is a whole number, and as a string in all other cases. `SequencesPage.tsx` maps the condition in the step form (load and save) and passes the parameter names. `validation.ts` adds the same shape rules and the unknown-name rule as the service.
- **Rationale**: FR-017. The sequence editor is in the Authoring area, so the phone level is `desktop-only`.

## R-008: The `sequenceStep` condition in the issue

- **Decision**: No action. The issue lists `sequenceStep` as a condition kind, but the service has no condition with that discriminator. The spec lists the real condition kinds.
