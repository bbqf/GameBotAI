# Quickstart: Region-Restricted Image Detection

Use this guide to check the feature after the build. The service runs on port 8080.

## 1. Save a condition with a region

Send a sequence with an `imageVisible` condition that has a `region`.

```json
{
  "type": "imageVisible",
  "imageId": "price-icon",
  "minSimilarity": 0.9,
  "region": { "x": 0, "y": 400, "width": 540, "height": 120 }
}
```

Read the sequence back with `GET /api/sequences/{id}`. The condition MUST contain the same `region`.

## 2. Save a tap target with a region

Send a command with a `primitiveTap` step.

```json
{
  "referenceImageId": "exchange-button",
  "confidence": 0.9,
  "region": { "x": 300, "y": 400, "width": 240, "height": 120 }
}
```

Read it back. The `detectionTarget` MUST contain the same `region`.

## 3. Check the 400 errors

Send a region with `width` of 0. The response MUST be 400 and MUST name `width`. Repeat with a negative `x` and with a missing `height`. Read the object again. It MUST not contain the bad region.

## 4. Check the search area

1. Open a screen where the same image shows in two places.
2. Set a region around the lower place. Run the condition. It MUST be true.
3. Move the region to an empty area. Run the condition. It MUST be false.
4. Run the tap with the region around the lower place. The tap MUST land on the lower image, in full-capture pixels.

Run a sequence step with a `waitForImage` payload that has a `detectionTarget.region`. Read the sequence back. The payload MUST contain the same `region`. Open the execution tree or the step-through output. The condition text MUST show `region=x,y,width,height`.

## 5. Check old data

Load a sequence and a command saved before this feature. They MUST load with no `region` and MUST run as before.

## 6. Run the tests

Run `dotnet test` for the solution. All existing tests MUST pass without a change to their expected results (SC-003).
