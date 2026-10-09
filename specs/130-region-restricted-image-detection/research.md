# Research: Region-Restricted Image Detection

No item in the Technical Context needed clarification. This file records the decisions and the facts found in the code.

## R1. Why the region is dropped today

**Finding**: Each layer copies fields by name. Examples: `ToDomainDetection`, `ToDomainStep`, `MapWaitForImageDetectionTarget`, `MapPerStepCondition`, `TryResolveImage`, and `TryDetection`. A field that is not in the list is lost at the first copy. The DTO classes have no `region` property, so System.Text.Json also ignores it on read.

**Decision**: Add the field to the domain type, the DTO, and every copy site. Use the list in `plan.md` as the check list. Add one read-back test per site.

**Alternatives considered**: A generic copy through reflection. Rejected because the code base uses explicit copies, and reflection would hide the next missing field.

## R2. Existing region types

**Finding**: Two types exist. `Triggers.Region` and `Blocks.Rect` hold doubles. `ImageMatchEvaluator` treats them as fractions of the capture (0 to 1). `OcrRegion` in the OCR feature uses pixels.

**Decision**: Add a new integer pixel type, `PixelRegion`. Do not change the fraction types.

**Alternatives considered**:
- Convert pixels to fractions in the adapter. Rejected because the adapter does not know the capture size, and rounding would move the edge.
- Reuse the OCR region type. Rejected because it belongs to another feature and Domain code should not depend on it.

## R3. Where to crop for a tap target

**Finding**: All tap and `waitForImage` detection goes through `DetectionCoordinateResolver.ResolveCenter` (through `CommandRunner`, `ActionExecutionAdapter`, and `ImageDetectionHelper`). The game-readiness probe uses the same path.

**Decision**: Crop in `DetectionCoordinateResolver`. Then `primitiveTap`, `waitForImage`, and the readiness image all get the feature with one change.

**Alternatives considered**: Filter matches after a full-screen match. Rejected. A stronger match outside the region would take a result slot (`maxResults`), and the best match inside the region could be lost. Cropping first is exact (FR-005).

## R4. Crop and coordinates

**Decision**: Use `Mat.SubMat` on the clipped rectangle. Add the rectangle origin to each match box. The existing clamp to the capture stays the same.

**Edge cases**: The clipped area can be empty, or smaller than the template. In both cases return "no detection above threshold" and no error. This follows FR-007.

**Alternatives considered**: Mask the screen outside the region. Rejected because it costs a full-size match and can create false edges.

## R5. Where to crop for a condition

**Finding**: `ImageMatchEvaluator.ComputeSimilarity` already crops by a fraction region with `SubMat`. It returns 0 when the template is larger than the region.

**Decision**: Add an optional `PixelRegion` to `ImageMatchParams`. When it is set, compute the rectangle from it (with the clip rule) and use the existing code after that point. The "whole image inside the region" rule (spec clarification) is then true by construction.

## R6. Validation rule and message

**Decision**: `x >= 0`, `y >= 0`, `width > 0`, `height > 0`, and all four fields present. No upper bound, because the capture size is unknown at save time (spec clarification). Messages name the field, for example `region.width must be greater than 0`. The save paths return 400.

**Alternatives considered**: Reject a region larger than a known screen size. Rejected by the spec.

## R7. Parameter placeholders

**Finding**: Some numeric fields of a detection target accept `{{name}}` placeholders (field templates).

**Decision**: Not in scope. The region fields are plain integers. The resolver copies the region unchanged.

## R8. Old domain class `ImageVisibleCondition`

**Finding**: `src\GameBot.Domain\Commands\ImageVisibleCondition.cs` is used only by `SequenceStep.ConditionExpression`. The active condition model is `ImageVisibleStepCondition`.

**Decision**: The task list MUST confirm by search that the old class has no read or write path in the API. If it has one, add the region there. If it has none, leave it unchanged and record this in the PR.

## R10. Sequence step payloads (FR-011)

**Finding**: `SequencesEndpoints.MapWaitForImageDetectionTarget` reads `detectionTarget` from a raw payload dictionary field by field (`referenceImageId`, `confidence`, `offsetX`, `offsetY`, `selectionStrategy`). A `region` object is not read, so it is lost. A `primitiveTap` payload in a sequence step is also a raw dictionary.

**Decision**: Read and validate `region` in `MapWaitForImageDetectionTarget`. For `primitiveTap`, the task list starts with a check for a typed map. If one exists, add the region there. If not, record in the PR that the raw payload passes the value unchanged.

**Alternatives considered**: Deserialize the whole payload to `DetectionTargetDto`. Rejected. It is a larger change than the issue needs.

## R11. Execution description (FR-012)

**Finding**: `SequenceStepConditionEvaluator.Describe` and `SequenceRunner.DescribeBreakCondition` build the condition text for execution logs, trees, and step-through. Both print `imageId` and `minSimilarity` only.

**Decision**: Add `, region=x,y,width,height` after `minSimilarity` only when a region is set. The text for a condition with no region does not change. One test covers each of the two methods and the step-through output.

**Alternatives considered**: Always print `region=none`. Rejected. It changes existing log text and breaks FR-008.

## R12. Evaluator input path

**Finding**: A condition reaches the evaluator through `SequenceStepConditionEvaluator`, which builds a `Blocks.Condition`. `ImageVisibleConditionAdapter` and `ImageDetectionConditionAdapter` turn it into `ImageMatchParams`. The region is lost at the first step.

**Decision**: Add `PixelRegion` to `Blocks.Condition` and `ImageMatchParams`. Copy it at both adapters.

## R13. One validation rule (D1)

**Decision**: `PixelRegion.Validate` holds the rule of FR-004. Endpoint maps, domain validators, and the repository guard call it and do not copy it. The 400 names every invalid field, each with its own message.

**Alternatives considered**: A rule in each endpoint. Rejected. The copies would drift, and the same bad region would give different messages in different places.

## R14. Command-level detection and readiness image (C2)

**Finding**: `CommandsEndpoints` and `StepsEndpoints` convert `DetectionTargetDto` in several places (command-level `detection`, `detectionTarget`, `ensureGameRunning.readinessImage`). `CommandStepResolver.TryDetection` copies the target at run time.

**Decision**: Add the region to `DetectionTargetDto` once. `ToDomainDetection` and `ToResponseDetection` carry it, so all these places get it. The tasks have one task for the command-level `detection` and one for the readiness image.

## R15. Detection helper and adapters (C3)

**Finding**: `ImageDetectionHelper.cs` takes a whole `DetectionTarget`. `ImageDetectionConditionAdapter.cs` and `ImageVisibleConditionAdapter.cs` build `ImageMatchParams` field by field.

**Decision**: The adapters copy `PixelRegion`. The helper is a check task: if it reads the target field by field, it carries the region. Otherwise record no change.

## R9. OpenAPI

**Finding**: The project builds OpenAPI descriptions with schema filters, not XML comments (see `ParametrizedReferenceImageSchemaFilter`, `PrimitiveActionSchemaFilter`, `RegionSchema`).

**Decision**: Add a `PixelRegionDto` schema description and a `region` property description on `ImageVisibleConditionContract` and `DetectionTargetDto`. Update the `waitForImage` and `primitiveTap` payload text in `PrimitiveActionSchemaFilter`.

## Setup check results

Results of the setup checks (tasks T001 to T004), found in the code during the implementation. Each result ends in a named change or in "no change" with the reason.

| Check | Result | Action |
|-------|--------|--------|
| Legacy class `ImageVisibleCondition` (`src\GameBot.Domain\Commands\ImageVisibleCondition.cs`): API read or write path | The only use is the property `SequenceStep.ConditionExpression` (`SequenceStep.cs`). No file in `src\GameBot.Service` reads or writes it. The only other name hit is the OpenAPI alias `ImageVisibleCondition` for `ImageVisibleConditionContract` (`ConditionalFlowSchemaDocumentFilter.cs`), which is the active model. | No change (T025 skipped). The active model `ImageVisibleStepCondition` has the region. |
| `primitiveTap` sequence payload: typed map or raw dictionary | Raw dictionary. A sequence step `primitiveAction.payload` is `Dictionary<string, object>` and the value is stored unchanged in `SequenceActionPayload.Parameters`. No code reads `detectionTarget` of a `tap` payload at run time. `MapWaitForImageDetectionTarget` reads the `waitForImage` payload field by field. | `region` of the `waitForImage` payload is read in `MapWaitForImageDetectionTarget` (new `TryReadRegion`). Both payload kinds are checked at save time in `ValidateRegionsInRequest` (any payload that has `detectionTarget`), so a bad region gives 400. A `primitiveTap` payload keeps the region by the raw pass-through. |
| `ImageDetectionHelper.cs`: reads a condition or a target field by field | It passes the whole `DetectionTarget` to `ActionExecutionAdapter.TryApplyDetectionCoordinates`, then `CommandRunner`, then `DetectionCoordinateResolver`. It reads `Confidence` only. | No change. The region travels inside the `DetectionTarget` and the resolver crops. |
| `ImageDetectionConditionAdapter.cs`: reads a condition or a target field by field | It takes a `ConditionOperand` (old operand model: `TargetRef`, `ExpectedState`, `Threshold`). `ConditionOperand` has no region and no API path sets one for an `imageVisible` condition. | No change (T019 recorded no change). `ImageVisibleConditionAdapter` copies `PixelRegion` instead. |
| Web UI types: drop `region` on save | `DetectionTargetDto` (`services\commands.ts`) and `ImageVisibleStepCondition` (`types\sequenceFlow.ts`) do not declare `region`. The editors change a condition with an object spread (`{ ...condition, imageId }`), so an existing `region` is kept on save. The UI has no field to set a region. | No change. Authors set `region` through the API. A UI field is outside this feature. |

Other findings during the implementation:

- `StepsEndpoints.ValidateStep` does not read `ensureGameRunning.readinessImage` (the endpoint takes only `forceRestart` from that step). The readiness image is covered by `CommandsEndpoints` (save and read-back). T034 maps and validates the region for the `primitiveTap` and `waitForImage` targets of the steps endpoint only.
- A sequence file is written with the default JSON options, so a stored region has the names `Region`, `X`, `Y`, `Width`, `Height`. A command file uses the web options (`region`, `x`, `y`, `width`, `height`). The API always uses `region` with lower-case fields.
- `SequenceRunner` builds a `Blocks.Condition` for the `waitForImage` step of a sequence (`DetectionTarget`). It now carries `PixelRegion` too, so a `waitForImage` payload region limits the search at run time.
