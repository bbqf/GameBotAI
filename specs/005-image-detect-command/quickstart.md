# Quickstart: Using Detection-Based Coordinates in Commands

## Define a Command

Add a command JSON including a DetectionTarget (confidence optional, defaults to 0.8). Base point is center-only; use offsets to adjust:

```json
{
  "id": "tap-home",
  "name": "Tap Home Center",
  "detectionTarget": {
    "referenceImageId": "home_button_ref",
    "confidence": 0.8,
    "offsetX": 0,
    "offsetY": 0
  },
  "actions": [
    { "type": "tap" }
  ]
}
```

For an offset tap, set `offsetX`/`offsetY` to non-zero values. Coordinates are clamped within screen bounds.

## Run the Service

Use the provided task to run the service:

```powershell
dotnet run -c Debug --project src/GameBot.Service
```

## Execute and Observe

- Exactly one on-screen match ≥ confidence: tap executes at center+offset, clamped to bounds.
- Multiple matches ≥ confidence: command skips actions; error log includes detection count and threshold.
- Zero matches: command skips actions; info log indicates no match.

## Programmatic Usage (Domain)

You can resolve coordinates programmatically using the domain helper:

```csharp
var matcher = new GameBot.Domain.Vision.TemplateMatcher();
var helper = new GameBot.Domain.Services.ActionCoordinateHelper(matcher);
var target = new GameBot.Domain.Commands.DetectionTarget("home_button_ref", 0.8, 0, 0);
// screenMat and templateMat are OpenCvSharp.Mat instances
var coord = helper.ResolveTapCoordinates(target, screenMat, templateMat, 0.8, out var error);
if (coord != null) {
  // Use coord.X, coord.Y for tap
}
```
