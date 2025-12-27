# Data Model: Commands Based on Detected Image

## Entities

- DetectionTarget
  - referenceImageId: string (required)
  - confidence: double (optional, default 0.8; range [0.0, 1.0])
  - offsetX: int (optional, default 0)
  - offsetY: int (optional, default 0)
  - basePoint: string (optional, default "center") // future-proof; initially only center supported
  - Validation: referenceImageId not empty; confidence within [0,1]; offsets numeric

- ResolvedCoordinate
  - x: int
  - y: int
  - confidence: double
  - bbox: { x: int, y: int, width: int, height: int }
  - sourceImageId: string

- Command (extension)
  - detectionTarget: DetectionTarget | null
  - Notes: When non-null, coordinate-requiring actions consume `ResolvedCoordinate` from resolver.

## Relationships

- Command 1..1 —(optional)→ DetectionTarget
- ResolvedCoordinate is produced at runtime from DetectionTarget + current screen via detection pipeline.

## Rules

- Unique Detection: Action proceeds only if exactly one detection ≥ configured threshold.
- Offsets: Apply (offsetX, offsetY) to base point (center) and clamp to [0..screenWidth-1], [0..screenHeight-1].
- Logging: Information on success with coordinates; error on multiple detections; info on zero detections; debug on clamping.
