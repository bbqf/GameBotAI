# Contract: image list in conditions and WaitForImage

## imageVisible condition

```json
{ "type": "imageVisible", "imageIds": ["quest_a", "quest_b"], "match": "any" }
```

```json
{ "type": "imageVisible", "imageId": "{{quests}}" }
```

- `imageIds`: array of text, 1 to 50 items. Optional. Not allowed with `imageId`.
- `match`: text. Optional. Only value `any`. The default for a list is `any`.
- `imageId`: a whole `{{name}}` placeholder of a multi-value parameter expands to the list, with `match: "any"`.
- Result: true when at least one image is visible. False when none is visible, and when the list is empty.

## waitForImage step

```json
{ "waitForImage": { "timeoutMs": 5000, "detectionTarget": { "referenceImageId": "{{quests}}" } } }
```

```json
{ "waitForImage": { "timeoutMs": 5000, "detectionTarget": { "referenceImageIds": ["quest_a", "quest_b"] } } }
```

- Overlay key `waitForImage.detectionTarget.referenceImageId` accepts a list placeholder.
- The step ends with `executed` / `image_detected` when any image is visible before the timeout.
- Timeout outcome and `image_unavailable` outcome are unchanged.
- Result adds `matchedImageId`. `referenceImageId` holds the first image of the list.

## Save errors

| Case | Result |
|------|--------|
| Multi-value parameter as a whole placeholder in a one-value position | Save refused. The message names the parameter and the field path. |
| `imageId` and `imageIds` together | Save refused. |
| `match` not `any` | Save refused. |
| List placeholder in an allowed image field | Save accepted with the `static_check_skipped` warning. |

## Compatibility

A condition or step with one image id has the same stored form, request, response, and behavior as before.
