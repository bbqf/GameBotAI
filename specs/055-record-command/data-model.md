# Data Model: Visual Command Recorder

## Frontend Types (TypeScript)

### RecordedStep (union)

Transient during a picker session. Never persisted directly — converted to `StepEntry` on confirm.

```typescript
type RecordedPrimitiveTapStep = {
  id: string;              // UUID, assigned on capture
  type: 'PrimitiveTap';
  imageId: string;         // ReferenceImage ID
  offsetX: number;         // signed px from bounding box center (natural image coords)
  offsetY: number;         // signed px from bounding box center (natural image coords)
  label: string;           // human-readable: "<imageName> (±x, ±y)"
};

type RecordedKeyInputStep = {
  id: string;
  type: 'KeyInput';
  key: string;             // ADB key identifier (e.g. "ENTER", "DPAD_UP")
  label: string;           // human-readable: "Key: <key>"
};

type RecordedSwipeStep = {
  id: string;
  type: 'Swipe';
  startX: number;          // natural image coords
  startY: number;
  endX: number;
  endY: number;
  durationMs: number;      // gesture elapsed time in ms
  label: string;           // human-readable: "Swipe (x1,y1)→(x2,y2) <ms>ms"
};

type RecordedStep = RecordedPrimitiveTapStep | RecordedKeyInputStep | RecordedSwipeStep;
```

### ImageMatchResult

Returned from the bulk detection API; used to render bounding box overlays.

```typescript
type ImageMatchResult = {
  imageId: string;       // ReferenceImage ID
  imageName: string;     // display label for overlay
  x: number;            // bounding box top-left (natural image coords)
  y: number;
  width: number;
  height: number;
  confidence: number;   // 0–1
};
```

### PickerState

Component-level state for the `VisualStepPicker` modal.

```typescript
type PickerStatus = 'loading' | 'ready' | 'error';

type PickerState = {
  status: PickerStatus;
  captureId: string | null;
  screenshotUrl: string | null;     // blob URL of captured PNG
  naturalWidth: number;
  naturalHeight: number;
  matches: ImageMatchResult[];      // current overlay set
  steps: RecordedStep[];            // accumulated recorded steps
  errorMessage: string | null;
};
```

### Conversion: RecordedStep → StepEntry

On confirm, each `RecordedStep` is mapped to an existing `StepEntry` shape (defined in `CommandForm.tsx`):

| RecordedStep type | StepEntry type | Field mapping |
|---|---|---|
| `PrimitiveTap` | `'PrimitiveTap'` | `primitiveTap.detectionTarget = { referenceImageId: imageId, offsetX, offsetY }` |
| `KeyInput` | `'KeyInput'` | `keyInput.key = key` |
| `Swipe` | `'Swipe'` | `swipe = { startX, startY, endX, endY, durationMs }` (all as strings) |

---

## Backend DTOs (C#)

### DetectAllRequest

```csharp
record DetectAllRequest(string CaptureId);
```

### DetectAllMatch

```csharp
record DetectAllMatch(
    string ImageId,
    string ImageName,
    int X, int Y, int Width, int Height,
    double Confidence
);
```

### DetectAllResponse

```csharp
record DetectAllResponse(IReadOnlyList<DetectAllMatch> Matches);
```

---

## Key Relationships

- `PickerState.matches[i].imageId` → `ReferenceImageStore` key (existing)
- `RecordedPrimitiveTapStep.imageId` → `DetectionTarget.ReferenceImageId` (existing domain model)
- `RecordedPrimitiveTapStep.offsetX/Y` → `DetectionTarget.OffsetX/OffsetY` (existing domain model)
- `RecordedStep` (transient) → `StepEntry` (existing `CommandFormValue.steps` element) on confirm

No new persistent entities. All new state is transient within the picker session.
