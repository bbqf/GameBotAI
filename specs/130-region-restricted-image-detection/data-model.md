# Data Model: Region-Restricted Image Detection

## PixelRegion (new, domain)

A rectangle in capture pixels. Immutable value.

| Field | Type | Rule |
|-------|------|------|
| `X` | int | 0 or more. Left edge. |
| `Y` | int | 0 or more. Top edge. |
| `Width` | int | More than 0. |
| `Height` | int | More than 0. |

The region covers pixels `X` to `X + Width - 1` and `Y` to `Y + Height - 1`.

Operations:

- `Validate(prefix)`: returns a list of messages that name the field. It returns an empty list when the region is valid.
- `ClipTo(captureWidth, captureHeight)`: returns the part of the region inside the capture, or "none" when no pixel remains.

JSON name: `region`. JSON field names: `x`, `y`, `width`, `height`.

## PixelRegionDto (new, service)

Same four fields, each a nullable integer. A missing field stays `null` so validation can name it. The service maps the DTO to `PixelRegion` only after validation.

## DetectionTarget (changed)

| Field | Type | Change |
|-------|------|--------|
| `ReferenceImageId` | string | none |
| `Confidence` | double | none |
| `OffsetX`, `OffsetY` | int | none |
| `SelectionStrategy` | enum | none |
| `Region` | `PixelRegion?` | NEW. Optional. `null` means the whole capture. |

The constructor gets a new last optional parameter, `region`. Existing calls compile and behave as before. The persisted JSON uses the property name `region`.

## ImageVisibleStepCondition (changed)

| Field | Type | Change |
|-------|------|--------|
| `ImageId` | string | none |
| `MinSimilarity` | double? | none |
| `Negate` | bool | none |
| `Region` | `PixelRegion?` | NEW. Optional. Omitted from JSON when `null`. |

Composite conditions (`all`, `any`, `none`) hold children of this type. Each child keeps its own region.

## Evaluator inputs (changed)

| Type | Field | Change |
|------|-------|--------|
| `Blocks.Condition` | `PixelRegion` (`PixelRegion?`) | NEW. The existing `Region` (fractions) stays for text conditions. |
| `Triggers.ImageMatchParams` | `PixelRegion` (`PixelRegion?`) | NEW. When set, it replaces the fraction `Region` in `ImageMatchEvaluator`. |

## Execution description (FR-012)

`Describe` and `DescribeBreakCondition` print `imageVisible(imageId=..., minSimilarity=..., region=x,y,width,height)` when a region is set. They print the old text when no region is set.

## Validation rules (FR-004)

| Case | Result |
|------|--------|
| `region` absent or `null` | Valid. No region. |
| Any of `x`, `y`, `width`, `height` missing | 400. Message names the missing field. |
| `x < 0` or `y < 0` | 400. Message names the field. |
| `width <= 0` or `height <= 0` | 400. Message names the field. |
| Region larger than the capture | Valid. The run step clips it. |

## Run-time behavior (FR-005 to FR-007)

1. No region: search the whole capture. Same code path as today.
2. Region set: clip it to the capture.
3. Clipped area empty, or smaller than the image: result is "not found". No error.
4. Otherwise: match inside the clipped area only.
5. Add the region origin to each match box. The result is in full-capture pixels.

## State transitions

None. The region is plain data. It has no life cycle.
