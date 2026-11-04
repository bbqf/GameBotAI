# Contract: WaitForImageConfigMapper and GamepackWaitForImageMapping

No HTTP endpoint is added or changed (FR-007). This file gives the internal contracts of the new Domain types.

## WaitForImageConfigMapper

Place: `GameBot.Domain.Commands`. This type is the ONLY reader of the WaitForImage payload fields (FR-005d).

### Map

Signature: `public static WaitForImageConfig? Map(string? actionType, IReadOnlyDictionary<string, object?>? payload)`

- Return null when `actionType` is null or is not `WaitForImage` (case-insensitive). Return null when `payload` is null.
- `timeoutMs`: whole number read by `TryReadInt32`. Default 1000.
- `until`: string or JSON string. Other values give null.
- `absentCaptures`: read by `TryReadInt32`. If not readable, the value is null.
- `detectionTarget`: JSON object with a non-blank string `referenceImageId`. Else null. Defaults: confidence 0.8, offsets 0, strategy `HighestConfidence` (`FirstMatch` when named).
- `region` in `detectionTarget`: read by `TryReadRegion`. Kept when the read succeeds, all four numbers are present, and `PixelRegion.Validate` gives no error. Otherwise null. `Map` does not throw and does not validate for the caller.
- Accepted runtime types are the types that the pack read path gives (see data-model.md). The runtime type test fixes the list.
- `Map` does not read the image store and does not check that the image exists.

### TryReadInt32

Signature: `public static bool TryReadInt32(object? value, out int result)`

- Moved from `SequencesEndpoints` with no change. Accepts `int`, `long` in the `int` range, `JsonElement` number that fits `int`, and numeric `string`. Else false.

### TryReadRegion

Signature: `public static bool TryReadRegion(JsonElement detectionTarget, out RegionFields? region, out IReadOnlyList<string> errors, string prefix = "region")`

- Moved from `SequencesEndpoints`. Same rules and same error texts: no `region` member or a null region gives true and a null result; a region that is not an object gives `"{prefix} must be an object"`; a field that is not a whole number gives `"{prefix}.{name} must be a whole number"`; a missing field stays null so the caller can name it with `PixelRegion.Validate`.
- `RegionFields` is a sealed record with `int? X`, `int? Y`, `int? Width`, `int? Height`.

### Callers

- `SequencesEndpoints` save mapping (top-level and nested steps): calls `Map`.
- `SequencesEndpoints.CollectRegionErrors` (save-time region check, feature 130): calls `TryReadRegion`, wraps the numbers in `PixelRegionDto`, and calls `PixelRegionDto.Validate`. Its messages do not change.
- `GamepackWaitForImageMapping` (install path): calls `Map`.

No other code reads these payload fields.

## GamepackWaitForImageMapping

Place: `GameBot.Domain.Gamepacks`.

Signature: `public static void Apply(CommandSequence sequence)`

- Throw `ArgumentNullException` when `sequence` is null.
- Walk `sequence.Steps` with one recursive method. For each step, visit the step, then `Body`, then `ElseBody` (skip when null). Handle every depth.
- For a step with `Action` not null and type `WaitForImage`, set `step.WaitForImage = WaitForImageConfigMapper.Map(step.Action.Type, step.Action.Parameters)`. This replaces any value from the pack file.
- Skip a step with `Action` null. Do not fail. Do not change other step members or other step types.
- Add no validation.

Call site: `GamepackValidator`, after `GamepackReferences.RewriteCommandIds(sequence, Map)` and before the sequence `PlannedObject` is added. This runs before `PostHash` is computed. `GamepackApplier` does not change.

## Equality rule

For equal payloads, the stored step of a pack install and the stored step of `POST /api/sequences` are equal (SC-003). A contract test compares both stored steps. Compared fields: `action` and `waitForImage` (`TimeoutMs`, `Until`, `AbsentCaptures`, and all `DetectionTarget` fields including `Region`).

## Unchanged behavior

- Image-reference collection and validation of a pack (a missing reference image on a WaitForImage step gives the same install result as before).
- Steps of other types, commands, and WaitForStill steps.
- All HTTP contracts.
