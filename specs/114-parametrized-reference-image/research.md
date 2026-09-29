# Research: Let a parameter choose the reference image

All items below come from a read of the code on branch `ccr-843497ee-pjzcth`. No item stays "NEEDS CLARIFICATION".

## R-001: Why `fieldTemplates` rejects the image key

**Finding**: `ParameterValidationService.ValidateFieldTemplateKeys` (`src/GameBot.Domain/Services/ParameterValidationService.cs`) accepts a key only when `CommandStepFieldPaths.IsSupported(path)` is true. `CommandStepFieldPaths.SupportedNumericPaths` (`src/GameBot.Domain/Commands/CommandStep.cs`) holds only numeric paths. Other keys get `unknown_field_template_path` with the text "is not a parametrizable numeric field".

**Decision**: Add a second set, `CommandStepFieldPaths.SupportedImagePaths`, with `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`. `IsSupported` returns true for a key in one of the two sets. A new method `IsImagePath(path)` tells the image keys. The message of `unknown_field_template_path` becomes "Step {order}: '{path}' is not a parametrizable field." (FR-012).

**Alternatives rejected**:
- Put the image keys into `SupportedNumericPaths` with a third value. The map value is "is integer". A third meaning in the same map is not clear.
- Add `ensureGameRunning.readinessImage.referenceImageId`. The spec puts it out of scope.

## R-002: The value rule for an image key (FR-002)

**Finding**: Today no save check looks at a `fieldTemplates` value. A numeric value that is not one whole placeholder fails only at run time, in `CommandStepResolver.TryOverlayValue`, with the reason `not_a_number`.

**Decision**: `ValidateFieldTemplateKeys` checks the value of each image key. The value must be exactly `{{name}}` after a trim: `TemplateSubstitutor.ExtractKeys(value)` gives one key, and the trimmed value equals `{{key}}`. Other values get the new code `invalid_field_template_value` (400) with the message "Step {order}: the value of '{path}' must be one whole placeholder, for example {{name}}." The numeric keys keep their current behavior (FR-013), so the check does not apply to them.

**Alternatives rejected**: Apply the check to the numeric keys too. The result is better, but FR-013 and SC-005 do not permit a change of numeric behavior.

## R-003: Resolution at dispatch (FR-003, FR-004, FR-011)

**Finding**: `CommandStepResolver.TryDetection` resolves the inline `referenceImageId` with `TryText` and the numeric fields with `TryOverlayValue`. `TryOverlayValue` adds each used value to the `used` list. `CommandExecutor.ExecuteCommandRecursiveAsync` puts that list into `PrimitiveTapStepOutcome.ResolvedParameters`, and `ExecutionLogService` writes it to the log. The fast path already runs the resolver when `FieldTemplates` has one or more keys.

**Decision**: In `TryDetection`, look for the overlay key `{prefix}.referenceImageId` first. When the key is there, resolve it with `TryOverlayValue` and do not resolve the inline value. When the key is not there, resolve the inline value with `TryText` as today. A malformed stored overlay value (possible only by a manual edit of the file) gives a `ParameterResolutionError` with the reason `unresolved`. The current empty-id check stays after the two paths. The used value goes into `used`, so the execution log records it with no other change.

**Alternatives rejected**: Resolve both and let the overlay value replace the inline value. An inline placeholder that no scope can supply then fails a step that does not use it.

## R-004: The inline `referenceImageId` stays required

**Finding**: `CommandsEndpoints.ValidateStep` requires a non-empty `primitiveTap.detectionTarget.referenceImageId`. The `DetectionTarget` constructor throws on an empty id.

**Decision**: No change. With the `fieldTemplates` key, the author writes a fallback image id or the same placeholder in the inline field. The overlay value wins at dispatch. The quickstart shows this.

**Alternatives rejected**: Let the inline field be empty when the overlay key is present. This changes the domain type, the stored form and the web UI tap editor. The issue does not need it.

## R-005: The save scan of a parametrized image

**Finding**: `ParameterReferenceScanner.ScanCommandStep` marks the inline image fields with `DefeatsStaticCheck: true`, and `AddReferenceIssues` adds the warning `static_check_skipped` for them. The `fieldTemplates` loop adds references without this flag. `ScanSequenceSteps` does not look at conditions.

**Decision**:
- In the `fieldTemplates` loop, set `defeatsStaticCheck: CommandStepFieldPaths.IsImagePath(path)`.
- In `ScanSequenceSteps`, scan each `imageVisible` leaf, also inside composites, in these positions: `step.Condition`, `step.If.Condition`, `WhileLoopConfig.Condition`, `RepeatUntilLoopConfig.Condition` and `step.BreakCondition`. The field paths are `condition.imageId`, `if.condition.imageId`, `loop.condition.imageId` and `breakCondition.imageId`, with the composite path in the middle (for example `condition.children[1].imageId`). Each reference has `DefeatsStaticCheck: true`.
- `insideLoop` for a condition: a loop condition and a break condition are inside the loop, so `{{iteration}}` is legal there. A step condition and an `If` condition use the value of the enclosing body.
- `ParameterReference` gets a new optional member `SourceText`, the full text of the field. The queue template check (R-008) needs it.

**Alternatives rejected**: A separate scanner for conditions. The two scans then give two lists that the validators must merge.

## R-006: The image existence check of a sequence

**Finding**: `SequencesEndpoints.ValidatePerStepImageReferencesAsync` checks only the top-level `step.Condition` (feature 088, decision D-006). It runs before the parameter check. For `"imageId": "{{novaOption}}"` it looks for an image with that literal id and rejects the sequence.

**Decision**: Skip an `imageId` when `TemplateSubstitutor.ContainsPlaceholder(imageId)` is true. The parameter check then gives `static_check_skipped` or `unresolvable_parameter_reference`. Other positions stay without an existence check, as today.

## R-007: Resolution of a condition at run time (FR-005, FR-007, FR-008, FR-009)

**Finding**: `SequenceRunner.ApplyScope` substitutes only `CommandId` and the action payload. It copies `Condition`, `If`, `Loop` and `BreakCondition` without a change. All condition positions go through two methods: `EvaluateStepGuardAsync` (step guard of an action step and of a loop step) and `EvaluateLoopConditionAsync` (while, repeat-until, break and `If`). The scope in effect is available at each call site (`scope` or `iterScope`). A missing image already fails an `imageVisible` leaf: `SequenceExecutionService.EvaluateImageConditionAsync` throws `InvalidOperationException("image_unavailable")` when `IImageRepository.ExistsAsync` is false.

**Decision**:
- A new static class `SequenceStepConditionResolver` in `src/GameBot.Domain/Parameters/`. `TryResolve(condition, scope, fieldPathPrefix, out resolved, out error, out used)` returns a copy of the tree with each `imageVisible.ImageId` substituted with `TemplateSubstitutor.TrySubstitute`. The inline form accepts text around the placeholder, the same as the inline image field of a command step. A tree with no placeholder comes back as the same instance (fast path). An empty result gives the reason `unresolved`, the same as `TryDetection`. The field path of an error is `fieldPathPrefix`, then `.children[i]` for each composite level, then `.imageId` (for example `condition.children[1].imageId`). These paths are the same as the paths of the save scan (R-005).
- `EvaluateStepGuardAsync` and `EvaluateLoopConditionAsync` get a `ParameterScope scope` argument and a `fieldPathPrefix` argument, and resolve the condition before the evaluation. The six call sites in `SequenceRunner` give these prefixes: the action-step guard `condition`, the loop-step guard `condition`, the while condition `loop.condition`, the repeat-until condition `loop.condition`, the `If` condition `if.condition`, and the break condition `breakCondition`. The scope of each call site: the step guard of an action step or a loop step and the `If` condition use the scope of the step (`scope`, or `iterScope` inside a loop body); the while condition uses `scope.WithIteration(iterations + 1)`, because it runs before the iteration and `iterCtx` does not exist yet (`ExecuteWhileLoopAsync`); the repeat-until condition uses `iterCtx`, the iteration that just ran; the break condition uses `iterScope`. Thus `{{iteration}}` in a while condition resolves to the number of the iteration that is about to run. A failure throws `ConditionEvaluationException` with the new kind `ConditionEvaluationFailureKind.ParameterUnresolved`. The message is `ParameterResolutionError.ToMessage(stepKey)`. The step guard (`FailGuard`) gives this message exactly. The while, repeat-until and `If` paths keep their current prefix (`Loop '<key>' condition evaluation failed: `, `Loop '<key>' exit condition evaluation failed: `, `If '<key>' condition evaluation failed: `) in front of this message.
- Each position handles this kind in the same way as its other evaluation errors. The step guard gives a failed step (`FailGuard`). The `If` path and the while and repeat-until paths already fail the step on an exception. A break condition keeps the rule of feature 066 FR-002a and FR-010: an evaluation error gives "No break", the error detail stays in the log, and the run does not fail. A break step sends no input to the device, so FR-008 ("no device input") holds. This feature does not change the rule of feature 066.
- The description of a condition in the log (`DescribeBreakCondition`, `SequenceStepConditionEvaluator.Describe`) uses the resolved tree, so the log shows the resolved image id.
- An image that does not exist gives the current result of each position (FR-009): the existing `image_unavailable` path of an `imageVisible` leaf.

**Alternatives rejected**:
- Fail the run when a break condition has an unresolved parameter. This supersedes feature 066 for one error type. Two rules for one position are hard to explain. The loop limit stops a loop that never breaks.
- Substitute conditions in `ApplyScope`. Loop and `If` steps do not go through `ApplyScope`, and a loop condition must use the scope of each iteration.
- Give the scope to `SequenceStepConditionEvaluator`. The evaluator then needs a parameter dependency, and its unit tests need a scope.

## R-008: The save check of a template entry value (FR-010)

**Finding**: `POST /api/queue-templates` (`QueueTemplatesEndpoints.cs`) is the only route that saves a queue template. It creates or overwrites by name. No validation route exists. The route checks only the value names (`invalid_parameter_value_name`). `ParameterValidationService.ValidateTemplateEntry` exists, but no route calls it. `QueuesEndpoints.CollectReachableDeclarationsAsync` finds the commands that a sequence can reach, but it returns only their declarations.

**Decision**:
- A new internal method `QueuesEndpoints.CollectReachableCommandsAsync(sequence, commands)` returns the reachable `Command` objects. `CollectReachableDeclarationsAsync` uses it.
- A new domain method `ParameterValidationService.FindImageValueCandidates(entry, entryIndex, sequence, reachableCommands)`. It scans the sequence and each reachable command, and keeps each reference with `DefeatsStaticCheck: true`. This includes each image field that the scanner marks: `imageVisible.imageId` in each condition position, the inline image fields of command steps (also an inline `ensureGameRunning.readinessImage.referenceImageId` placeholder), the two `fieldTemplates` image keys, and `detection.referenceImageId` of a command. The method rebuilds the call paths from the sequence and the `Command` objects: sequence step, then command, then each nested command step (cycles stop as in the current reach walk). For a field in the sequence, the path is the sequence only.
- Known value rule for each path and each name: `ParameterScope.TryResolve` looks at the bound values of all layers first, and then at the declaration defaults from the innermost layer outward. `SequenceRunner` puts the `parameterBindings` of a sequence step into a command layer, and `CommandExecutor` (`src/GameBot.Service/Services/CommandExecutor.cs`) does the same for a nested command step. Thus:
  1. At each call site on the path (the sequence step and each nested command step above the field), a non-null `parameterBindings` entry for the name covers it. The path then gives no candidate for the name. A literal binding is not checked. A binding to `{{otherName}}` is not followed. This is a known limit; the run-time check (FR-009) applies.
  2. Else, if the entry supplies the name, the known value is the entry value.
  3. Else, the known value is the default of the innermost declaration layer outward along the path: the nested command, then the command that calls it, and so on, and then the sequence.
- When each key of the text has a known value on a path, the result is a candidate `(EntryIndex, ParameterName, ImageId, FieldPath)`. A field gets one candidate for each path. A text with a queue built-in or an unknown name has no candidate. The candidates have no duplicates. A command that two sequence steps reach, where one step binds the name and the other step does not, gets a candidate from the path of the step with no binding.
- `POST /api/queue-templates` runs this after the value name check, for each entry (also a disabled entry, because it can be enabled later). The route checks each distinct image id with `IImageRepository.ExistsAsync`. Each id with no image gives an issue with the code `unknown_image_reference` and the message "Entry {i}: parameter '{name}' gives the image id '{id}' to field '{path}', but no image has that id." The route returns 400 with `ParameterDtoMapper.ToErrorBody` and does not save.
- An entry whose sequence does not exist has no candidates. The stale state already shows in the detail response.

**Alternatives rejected**:
- Check only an entry value and not a default. FR-010 asks for the default too.
- Follow the renames of `parameterBindings` exactly (for example a binding to `{{otherName}}`). The name-based rule with the one binding rule above is simpler, and a value that goes to an image field in one place and to a text field in another place gets the check (spec edge case).
- Ignore the bindings fully. Then the save can reject an entry value that the run never uses, because a binding replaces it.
- Apply a binding rule only at the sequence step. Then a nested command step that binds the name gives a false reject.
- Use the sequence default before the command default. The run uses the command default first, so the save then checks a value that the run does not use.
- Check at queue start. The spec clarification rejects it.

## R-009: Warnings on a sequence save

**Finding**: A command save returns `warnings` (`CommandResponse.Warnings`). A sequence save computes `parameterCheck.Warnings`, but `ToSequenceResponse` has no `warnings` member, so a sequence response does not show `static_check_skipped`.

**Decision**: `POST`, `PUT` and `PATCH /api/sequences` add a `warnings` member to the response when the parameter check gives one or more warnings (US2 scenario 1). The member uses `ParameterDtoMapper.ToResponseWarnings`. With no warnings, the response does not change. `GET` does not change.
- Only a per-step-shape body returns `warnings`. `parameterCheck` exists only in the per-step branch of each route. An old-shape body and a domain-shape body pass `null` for `warnings`, so their responses do not change.
- A dry run (`"dryRun": true` in the request body, feature 082) does not change. Its response stays `{ valid, dryRun, errors }` with no `warnings` member. Reason: the dry-run response has a different shape from the stored-save response, and feature 082 defines it. A parametrized `imageId` does not make a dry run fail, because the existence check skips it (R-006).

## R-010: The API documentation (FR-014)

**Finding**: The service does not give XML comments to Swagger. Field descriptions come from schema filters in `src/GameBot.Service/Swagger/` (for example `PrimitiveTapHoldSchemaFilter`), and OpenAPI tests read `/swagger/v1/swagger.json` (for example `tests/contract/Sequences/LastRunConditionOpenApiTests.cs`). `specs/openapi.json` is a snapshot that people update by hand. It has no `fieldTemplates` member on `CommandStepDto`.

**Decision**:
- A new schema filter `ParametrizedReferenceImageSchemaFilter` describes `fieldTemplates` on `CommandStepDto` (all keys, the whole-placeholder rule for the image keys, the codes `unknown_field_template_path` and `invalid_field_template_value`) and `imageId` on `ImageVisibleConditionContract` (the placeholder and the positions). It also describes `parameterValues` on the template entry save request with the code `unknown_image_reference`.
- `specs/openapi.json` gets the same descriptions and the `fieldTemplates` member on `CommandStepDto`.
- `docs/architecture.md` (parameters section and the feature 078 API list) tells the two new keys, the placeholder in `imageVisible.imageId` and the two new codes. The "Last reviewed" date changes.
- The comments on `CommandStep.FieldTemplates`, `CommandStepDto.FieldTemplates` and `src/web-ui/src/services/commands.ts` no longer say "numeric fields only".

## R-011: Performance

**Finding**: The condition resolver runs one time for each condition evaluation. The fast path returns the same instance when no leaf has a placeholder. The template save check reads each referenced sequence and each reachable command one time for each entry.

**Decision**: No performance budget changes. A template with N entries does N sequence reads and the command reads of the reach. This is the same cost as the queue-start check of feature 078. Image checks use a cache for each save.
