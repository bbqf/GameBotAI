# Data Model: Report which image of a list matched

## StepImageMatch (in memory, one run)

| Field | Type | Rule |
|-------|------|------|
| stepKey | text | Key of the sequence step. Not case-sensitive. |
| matchedImageId | text | Id of the image that matched. Not empty. |
| confidence | number or null | Confidence of the match pass. |

- One entry for each step key. A new match replaces the older entry. A run of the step with no match removes the entry.
- The store lives for one run. It is not saved to a file.

## WaitForImageStepResultDetails (existing, changed)

| Field | Change |
|-------|--------|
| referenceImageId | Unchanged. |
| confidence | Unchanged. Holds the confidence of the match on `image_detected`. |
| matchedImageId | NEW. Text, omitted when null. Set on `image_detected` only. |

## MatchedImageStepCondition (new condition type `matchedImage`)

| Field | Type | Rule |
|-------|------|------|
| type | text | `matchedImage` |
| stepRef | text | Required. Names a prior step in the same sequence. |
| imageId | text | Required. Not empty. May hold a single-value placeholder. |
| negate | bool | Existing base field. |

Evaluation: true when the store entry of `stepRef` has `matchedImageId` equal to `imageId`. No entry gives false before `negate`.

## Placeholder `step.<stepRef>.matchedImageId`

- Reserved namespace `step.`. The value is the `matchedImageId` of the store entry of `stepRef`.
- No entry gives the standard unresolved-placeholder error.
