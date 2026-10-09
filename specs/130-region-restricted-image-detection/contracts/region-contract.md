# Contract: `region` field

The `region` field is optional on two objects. It has the same shape on both.

## Shape

```json
"region": { "x": 120, "y": 640, "width": 300, "height": 90 }
```

| Field | Type | Rule |
|-------|------|------|
| `x` | integer | Required inside `region`. 0 or more. Capture pixels. |
| `y` | integer | Required inside `region`. 0 or more. Capture pixels. |
| `width` | integer | Required inside `region`. More than 0. |
| `height` | integer | Required inside `region`. More than 0. |

`region: null` or no `region` means the whole capture.

## Objects that accept `region`

### `imageVisible` condition

Used in sequence step conditions, `if` conditions, loop and break conditions, and children of `all`, `any`, and `none`.

```json
{
  "type": "imageVisible",
  "imageId": "price-icon",
  "minSimilarity": 0.9,
  "region": { "x": 0, "y": 400, "width": 540, "height": 120 }
}
```

### `detectionTarget`

Used in `primitiveTap.detectionTarget`, `waitForImage.detectionTarget`, `ensureGameRunning.readinessImage`, the command-level `detection`, and the `detectionTarget` of a sequence `waitForImage` payload.

```json
{
  "referenceImageId": "exchange-button",
  "confidence": 0.9,
  "region": { "x": 300, "y": 400, "width": 240, "height": 120 }
}
```

## Endpoints that carry the field

Request (save) and response (read-back):

- `POST` and `PUT` `/api/commands`, and `GET` of the same resources.
- The step endpoints that build command steps.
- `POST` and `PUT` `/api/sequences`, and `GET` of the same resources.
- The sequence step payloads `waitForImage` and `primitiveTap` (`detectionTarget.region`).
- The execution tree and step-through responses that show a condition. They show the region in the condition text, as `region=x,y,width,height`, only when a region is set (FR-012).

## Errors (400)

The body names the field. Nothing is stored.

| Input | Message (example) |
|-------|-------------------|
| `"region": { "x": 0, "y": 0, "width": 0, "height": 10 }` | `region.width must be greater than 0` |
| `"region": { "x": -1, "y": 0, "width": 10, "height": 10 }` | `region.x must be 0 or more` |
| `"region": { "x": 0, "y": 0, "width": 10 }` | `region.height is required` |

For a condition or a payload, the message starts with the step label and the path, as the existing messages do.

## Behavior

- The search runs only inside the region. The whole image must be inside the region.
- Returned coordinates and tap points are in full-capture pixels.
- If the region goes past the capture, the service uses the part inside the capture.
- If no usable area remains, the result is "not found". It is not an error.
- Names other than `region` (`searchRegion`, `roi`, `bounds`, flat `x`, `y`, `width`, `height`) are not supported.

## OpenAPI

Add a `region` property with this description to `ImageVisibleCondition` and to `DetectionTarget`. Add a `PixelRegion` schema with the four fields and the rules above.
