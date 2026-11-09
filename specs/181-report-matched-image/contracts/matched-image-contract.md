# Contract: matched image

## Condition

```json
{ "type": "matchedImage", "stepRef": "wait-quest", "imageId": "quest-a", "negate": false }
```

- `stepRef`: required. Id of a prior step in the same sequence.
- `imageId`: required. Image id to compare.
- Save error when `stepRef` is empty, unknown, or not a prior step. Save error when `imageId` is empty.
- Run: true when the latest match of that step is `imageId`. False when the step has no match.

## Step result (WaitForImage)

```json
{ "exitCondition": "image_detected", "referenceImageId": "quest-a", "matchedImageId": "quest-b", "confidence": 0.93 }
```

- `matchedImageId` is present only when an image matched. Old clients ignore it.

## Placeholder

`{{step.wait-quest.matchedImageId}}` in a text field of a later step. It becomes the matched image id. If the step has no match, the run reports the unresolved-placeholder error.

## Per-step run log

The entry of an image check step that matched holds `matchedImageId` and `confidence`.
