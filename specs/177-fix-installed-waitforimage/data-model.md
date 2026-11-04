# Data Model: Fix inline WaitForImage in installed gamepack sequences

This fix adds no new stored entity and changes no stored format. It sets a member that already exists.

## SequenceStep (existing, `GameBot.Domain.Commands`)

| Member | Type | Role in this fix |
|--------|------|------------------|
| `Action` | `SequenceActionPayload?` | Source. `Action.Type` is `WaitForImage`. `Action.Parameters` (`Dictionary<string, object?>`) holds the payload. Null means the step is skipped. |
| `WaitForImage` | `WaitForImageConfig?` | Target. The walk sets it. The runner reads it. |
| `Body` | `IReadOnlyList<SequenceStep>` | Child steps of a loop or the then branch of an if. A member of the step itself. |
| `ElseBody` | `IReadOnlyList<SequenceStep>?` | Else branch of an if. Null means no else branch. A member of the step itself. |

## WaitForImageConfig (existing)

| Field | Source in payload | Default |
|-------|-------------------|---------|
| `TimeoutMs` | `timeoutMs` | 1000 |
| `Until` | `until` (string) | null |
| `AbsentCaptures` | `absentCaptures` (whole number) | null |
| `DetectionTarget` | `detectionTarget` | null |

## DetectionTarget (existing)

| Field | Source in `detectionTarget` | Default |
|-------|-----------------------------|---------|
| `ReferenceImageId` | `referenceImageId` (non-blank string) | required; if missing, the whole target is null |
| `Confidence` | `confidence` (number) | 0.8 |
| `OffsetX`, `OffsetY` | `offsetX`, `offsetY` (whole numbers) | 0 |
| `SelectionStrategy` | `selectionStrategy` | `HighestConfidence`; `FirstMatch` when named |
| `Region` | `region` (`x`, `y`, `width`, `height`) | null; set only when all four are whole numbers and `PixelRegion.Validate` gives no error |

## Payload value types (to confirm by test)

| Payload value | Expected runtime type after a pack read | Source of the check |
|---------------|------------------------------------------|---------------------|
| `detectionTarget` | `JsonElement` (object) | Unit test that reads a real pack file |
| `timeoutMs`, `absentCaptures` | `JsonElement` (number) | same |
| `until` | `JsonElement` (string) | same |

The mapper accepts the type that the test shows. The save path gives `JsonElement` also.

## New code types (no stored data)

| Type | Place | Role |
|------|-------|------|
| `WaitForImageConfigMapper` (static) | `GameBot.Domain.Commands` | `Map(actionType, payload)` gives a `WaitForImageConfig` or null. `TryReadInt32` and `TryReadRegion` are the only readers of these payload fields. |
| `RegionFields` (sealed record) | `GameBot.Domain.Commands` | Four `int?` members `X`, `Y`, `Width`, `Height`. Result of `TryReadRegion`. Service wraps it in `PixelRegionDto`. |
| `GamepackWaitForImageMapping` (static) | `GameBot.Domain.Gamepacks` | `Apply(CommandSequence)` walks all steps and sets `WaitForImage` |

## State and order in install

1. Pack file is read. Steps have `Action.Parameters`. `WaitForImage` is null.
2. Image-reference and command-reference checks run on the read steps (no change).
3. Plan builder rewrites command ids (`RewriteCommandIds`).
4. `GamepackWaitForImageMapping.Apply` sets `WaitForImage` for each inline WaitForImage step.
5. The `PlannedObject` is added. `PostHash` is computed from the final content (take-over).
6. The applier stores the sequence (new install, take-over, or update). The stored JSON now holds `waitForImage`.

Sequences that were installed before this fix keep the old data until the operator reinstalls or updates the pack. An update writes the corrected steps.
