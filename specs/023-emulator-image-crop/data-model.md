# Data Model: Emulator Screenshot Cropping

## Entities

### ScreenshotCaptureSession
- **Id**: string (unique per capture)
- **CapturedAtUtc**: datetime
- **Source**: emulator/session identifier
- **ImagePath**: file path to the full captured screenshot (temporary)
- **Width/Height**: integers (pixels)
- **ExpiresAtUtc**: optional datetime for cleanup

### StoredImageAsset
- **Name**: user-provided display name (must be unique within authoring scope)
- **FileName**: stored PNG filename
- **SavedAtUtc**: datetime
- **SourceCaptureId**: reference to ScreenshotCaptureSession
- **CropBounds**: { x, y, width, height } in pixels relative to captured image
- **StoragePath**: file path under `data/images`
- **Checksum**: optional hash for integrity (if computed)

## Relationships
- `StoredImageAsset.SourceCaptureId` references `ScreenshotCaptureSession.Id`.

## Validation Rules
- `CropBounds.width` and `CropBounds.height` >= 16 pixels.
- `CropBounds` must fit within the associated `ScreenshotCaptureSession` dimensions.
- `Name` must not collide without explicit overwrite confirmation.
- PNG format only for `FileName`.
- Capture sessions may be ephemeral; assets persist after save.
