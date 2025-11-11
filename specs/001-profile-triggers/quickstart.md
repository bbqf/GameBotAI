# Quickstart: Triggered Profile Execution

## Overview
This guide shows how to add and test triggers that start a profile automatically based on time, image appearance, or OCR text conditions.

## 1. Add a Delay Trigger
```
POST /profiles/{profileId}/triggers
{
  "type": "delay",
  "params": { "seconds": 30 },
  "cooldownSeconds": 120
}
```
Expect 201 with trigger object. Profile will start ~30s after trigger is enabled.

## 2. Add an Image Match Trigger
1. Upload an image asset to register a `referenceImageId`:
```
POST /images
{
  "id": "victory-banner",
  "data": "data:image/png;base64,<Base64PNG>"
}
```
2. Create trigger:
```
POST /profiles/{profileId}/triggers
{
  "type": "image-match",
  "params": {
    "referenceImageId": "victory-banner",
    "region": { "x": 0.4, "y": 0.3, "width": 0.2, "height": 0.1 },
    "similarityThreshold": 0.88
  }
}
```

## 3. Add a Text Found Trigger
```
POST /profiles/{profileId}/triggers
{
  "type": "text-match",
  "params": {
    "target": "Victory",
    "region": { "x": 0.0, "y": 0.0, "width": 1.0, "height": 0.2 },
    "confidenceThreshold": 0.82,
    "mode": "found"
  }
}
```

## 4. Test a Trigger Immediately
```
POST /profiles/{profileId}/triggers/{triggerId}/test
```
Returns evaluation result with `status`, and similarity or confidence metrics.

## 5. Check Trigger List
```
GET /profiles/{profileId}/triggers
```
Review states: `pending`, `satisfied`, `cooldown`, `disabled`.

## 6. Force Evaluation (Optional)
```
POST /profiles/{profileId}/triggers/evaluate
```
Runs evaluation cycle on-demand.

## 7. Cooldown Behavior
After firing, `status` becomes `Cooldown` until `cooldownSeconds` elapse; subsequent potential matches are ignored.

Tip: Re-testing a satisfied trigger immediately should return `Cooldown`; after waiting beyond the cooldown window, it can become `Satisfied` again.

## 8. Updating a Trigger
```
PATCH /profiles/{profileId}/triggers/{triggerId}
{
  "enabled": false
}
```
Disables trigger safely.

## 9. Deleting a Trigger
```
DELETE /profiles/{profileId}/triggers/{triggerId}
```
Removes the trigger from the profile definition.

## Notes
- Regions are normalized (0..1) for resolution independence.
- Use conservative thresholds first; lower only if matches are missed.
- Text `not-found` triggers should expect a slight delay due to confirmation cycle.
 - For local testing, you can set `GAMEBOT_DATA_DIR` to isolate state and `GAMEBOT_AUTH_TOKEN` to enable bearer auth.
 - Integration tests can inject a faux screen using `GAMEBOT_TEST_SCREEN_IMAGE_B64` (base64 PNG): the evaluator will compare against this image.
