# Data Model: Any-of image condition and WaitForImage for a list parameter

## ImageVisibleStepCondition (changed)

| Field | Type | Rule |
|-------|------|------|
| `imageId` | text | Existing. One id, or a whole `{{name}}` placeholder. A placeholder of a multi-value parameter expands to a list. |
| `imageIds` | list of text, optional | New. Written list. 1 to 50 items. Items are unique (first occurrence kept). An item may be a `{{name}}` placeholder of a single-value parameter. Not allowed together with `imageId`. |
| `match` | text, optional | New. Only value: `any`. Null means `any`. |
| `minSimilarity`, `region`, `negate` | existing | Apply to every item of the list. |

Stored form: `imageIds` and `match` are omitted when null.

## DetectionTarget for WaitForImage (changed)

| Field | Type | Rule |
|-------|------|------|
| `referenceImageId` | text | Existing. One id, or a whole `{{name}}` placeholder. A placeholder of a multi-value parameter expands to a list. |
| `referenceImageIds` | list of text, optional | New. Same rules as `imageIds`. Not allowed together with `referenceImageId`. Not accepted on `primitiveTap` or `ensureGameRunning`. |

## WaitForImage step result (changed)

| Field | Type | Rule |
|-------|------|------|
| `referenceImageId` | text | Existing. For a list, the first image of the list. |
| `matchedImageId` | text, optional | New. The first image of the list (in list order) that was visible when the step ended. Null when no image matched. |

## ParameterReference (changed, Domain, not stored)

| Field | Type | Rule |
|-------|------|------|
| `AllowsList` | bool | New. True for `imageVisible.imageId` and the WaitForImage image key (inline and overlay). False for all other fields. |

## Validation rules

- `imageId` and `imageIds` together: refused.
- `match` other than `any`: refused.
- Empty `imageIds`, an empty item, or more than 50 items: refused.
- An item that names a multi-value parameter: refused.
- A whole placeholder of a multi-value parameter where `AllowsList` is false: refused at save, with the parameter name and the field path.
- At run time, a list that is empty after expansion: the condition is false and the wait runs until the timeout.
