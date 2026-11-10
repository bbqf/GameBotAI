# Data Model: Resolve parameter placeholders in inline WaitForImage

No stored data changes. Two log shapes get one optional field.

## WaitForImageConfig (input, unchanged)

| Field | Type | Note |
|-------|------|------|
| detectionTarget.referenceImageId | string | May hold a whole placeholder such as `{{s}}` or `{{q}}` |
| detectionTarget.referenceImageIds | string[] | Optional written list |
| timeoutMs | int | Unchanged |

Resolution result (in memory only):

| Input | Resolved `referenceImageId` | Resolved `referenceImageIds` |
|-------|----------------------------|------------------------------|
| `{{s}}`, one value | the value | none |
| `{{q}}`, multi-value parameter | first value | all values, normalized |
| literal id | the id | as written |

## WaitForImageDetailAttributes (domain record) and ExecutionLogWaitForImageDetailAttributesDto

| Field | Type | Change |
|-------|------|--------|
| timeoutMs, effectiveTimeoutMs, referenceImageId, confidence, exitCondition, imageLoadStatus | as before | none |
| matchedImageId | string, optional | NEW. Set only when the exit condition is `image_detected`. Omitted (null) in all other cases. |

Validation rules: `matchedImageId` is one of the resolved ids. For a single image, it equals `referenceImageId`.
