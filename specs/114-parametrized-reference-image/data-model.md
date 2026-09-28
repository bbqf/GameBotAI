# Data Model: Let a parameter choose the reference image

The stored JSON of commands, sequences and queue templates does not change. No migration is necessary. The changes are in accepted values, in validation rules and in run-time behavior.

## CommandStep.FieldTemplates (changed rules)

`src/GameBot.Domain/Commands/CommandStep.cs`

| Key | Target | Value rule | New? |
|-----|--------|------------|------|
| numeric keys of feature 078 (`swipe.startX`, `primitiveTap.detectionTarget.offsetX`, and others) | `int` or `double` | as before (checked at run time) | no |
| `primitiveTap.detectionTarget.referenceImageId` | `string` (image id) | one whole placeholder `{{name}}` (checked at save) | yes |
| `waitForImage.detectionTarget.referenceImageId` | `string` (image id) | one whole placeholder `{{name}}` (checked at save) | yes |

Rules:
- A key that is not in the table gives `unknown_field_template_path` (400). The message says "is not a parametrizable field".
- An image key with a value that is not one whole placeholder gives `invalid_field_template_value` (400). The message names the step order and the key.
- At dispatch, the value of an image key replaces the inline `referenceImageId`. The inline value is not resolved when the key is present.
- The inline `referenceImageId` stays required on a `PrimitiveTap` step (a fallback id or a placeholder).

## CommandStepFieldPaths (changed type)

| Member | Kind | Description |
|--------|------|-------------|
| `SupportedNumericPaths` | existing | numeric keys, value = "is integer" |
| `SupportedImagePaths` | new, `IReadOnlySet<string>` | the two image keys |
| `IsSupported(path)` | changed | true for a numeric key or an image key |
| `IsImagePath(path)` | new | true for an image key |

## ImageVisibleStepCondition.ImageId (changed rule)

`src/GameBot.Domain/Commands/SequenceStepCondition.cs`

- `ImageId` can hold an inline placeholder, for example `{{novaOption}}` or `nova-{{option}}`.
- Positions: `SequenceStep.Condition`, `IfConfig.Condition`, `WhileLoopConfig.Condition`, `RepeatUntilLoopConfig.Condition`, `SequenceStep.BreakCondition`, and each child of `all`, `any` and `none` in these positions.
- Save: a parametrized `ImageId` skips the image existence check and gives the warning `static_check_skipped`. Each name must be declared by the sequence, be a queue built-in, or be `iteration` inside a loop. Otherwise the save gives `unresolvable_parameter_reference`.
- Run: the runner resolves `ImageId` against the scope of the call site before the evaluation (see the table in "SequenceStepConditionResolver").

## ParameterReference (changed record)

`src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`

| Member | Change |
|--------|--------|
| `ParameterName`, `FieldPath`, `StepLabel`, `InsideLoop`, `DefeatsStaticCheck` | no change |
| `SourceText` (`string?`, default `null`) | new: the full text of the field that holds the reference |

New scanned fields:
- `fieldTemplates` image keys, with `DefeatsStaticCheck: true`.
- Each `imageVisible.imageId` in the positions above, with `DefeatsStaticCheck: true`. Field paths: `condition.imageId`, `if.condition.imageId`, `loop.condition.imageId`, `breakCondition.imageId`, with `children[i].` for a composite child.

## SequenceStepConditionResolver (new)

`src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs`

```text
static bool TryResolve(
    SequenceStepCondition condition,
    ParameterScope scope,
    string fieldPathPrefix,
    out SequenceStepCondition resolved,
    out ParameterResolutionError? error,
    out IReadOnlyList<ResolvedParameter> used)
```

- A tree with no placeholder: `resolved` is the same instance, `used` is empty.
- An `imageVisible` leaf: a new leaf with the substituted `ImageId`, and the same `MinSimilarity` and `Negate`.
- A composite: a new composite of the same rule, with resolved children and the same `Negate`.
- Other leaves: the same instance.
- An unknown name or an empty result: `false`, with a `ParameterResolutionError` (reason `unresolved`). The field path is `fieldPathPrefix`, then `.children[i]` for each composite level, then `.imageId`, for example `condition.children[1].imageId`.

`fieldPathPrefix` for each call site in `SequenceRunner` (six call sites):

| Call site | Method | Prefix | Scope | Failure message |
|-----------|--------|--------|-------|-----------------|
| step guard of an action step | `EvaluateStepGuardAsync` | `condition` | the scope of the step (`scope`, or `iterScope` in a loop body) | `ParameterResolutionError.ToMessage(stepKey)` exactly |
| step guard of a loop step | `EvaluateStepGuardAsync` | `condition` | the scope of the step (`scope`, or `iterScope` in a loop body) | `ParameterResolutionError.ToMessage(stepKey)` exactly |
| while condition | `EvaluateLoopConditionAsync` | `loop.condition` | `scope.WithIteration(iterations + 1)`: the iteration that is about to run (the condition runs before `iterCtx` exists) | `Loop '<key>' condition evaluation failed: ` + resolution message |
| repeat-until condition | `EvaluateLoopConditionAsync` | `loop.condition` | `iterCtx`: the iteration that just ran | `Loop '<key>' exit condition evaluation failed: ` + resolution message |
| `If` condition | `EvaluateLoopConditionAsync` | `if.condition` | the scope of the step (`iterScope` argument of the `If` path) | `If '<key>' condition evaluation failed: ` + resolution message |
| break condition | `EvaluateLoopConditionAsync` | `breakCondition` | `iterScope` | "No break", with the resolution message as the error detail in the log |

These paths are the same as the field paths of the save scan. With these scopes, `{{iteration}}` in a while condition gives the number of the next iteration (1 before the first iteration), and in a repeat-until condition it gives the number of the iteration that just ran.

## ConditionEvaluationFailureKind (changed enum)

`src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`

- New value `ParameterUnresolved`: a placeholder in the condition has no value in scope. `Detail` holds the parameter name.

## ParameterValidationCodes (changed)

| Code | Kind | Where |
|------|------|-------|
| `invalid_field_template_value` | new, error (400) | command create and update |
| `unknown_image_reference` | new, error (400) | `POST /api/queue-templates` |
| `unknown_field_template_path` | message text changes | command create and update |

## ImageValueCandidate (new record)

`src/GameBot.Domain/Services/ParameterValidationService.cs`

| Member | Type | Description |
|--------|------|-------------|
| `EntryIndex` | `int` | index of the template entry |
| `ParameterName` | `string` | the parameter whose known value goes to the image field |
| `ImageId` | `string` | the image id after substitution |
| `FieldPath` | `string` | the image field, for example `primitiveTap.detectionTarget.referenceImageId` |

`ParameterValidationService.FindImageValueCandidates(entry, entryIndex, sequence, reachableCommands)` returns the distinct candidates of one entry. A field text with a name that has no known value gives no candidate.

Scanned fields: each reference that the scanner marks `DefeatsStaticCheck` in the sequence or in a reachable command. This includes `imageVisible.imageId` in each condition position, the inline image fields of command steps (also an inline `ensureGameRunning.readinessImage.referenceImageId` placeholder), the two `fieldTemplates` image keys, and `detection.referenceImageId` of a command.

Known value rules (for each call path):
- A call path is: sequence step, then command, then each nested command step. The method rebuilds the paths from the sequence and the `Command` objects. A field in the sequence has the path "sequence" only.
- Binding rule: at each call site on the path (the sequence step and each nested command step above the field), a non-null `parameterBindings` entry for the name covers the name. The path then gives no candidate for it (the binding outranks the entry at run time). A literal binding is not checked. A binding to `{{otherName}}` is not followed. This is a known limit; the run-time check (FR-009) applies.
- Else, if the entry supplies the name, the known value is the entry value.
- Else, the known value is the default of the innermost declaration layer outward along the path: the nested command, then the command that calls it, and so on, and then the sequence.
- A field gets one candidate for each path that reaches it. The candidates have no duplicates. Example: a command that two sequence steps reach, where one step binds `novaOption` and the other step does not, gets a candidate from the path of the step with no binding.

## Sequence write response (changed)

- `POST`, `PUT` and `PATCH /api/sequences` responses to a per-step-shape body get an optional `warnings` array (same item shape as the command response: `code`, `message`, `fieldPath`, `parameterName`, `entryIndex`). The member is present only when there are warnings. The responses to an old-shape body and to a domain-shape body do not change.
