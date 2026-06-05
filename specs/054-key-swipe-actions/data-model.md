# Data Model: Key Input and Swipe Steps

**Feature**: 054-key-swipe-actions
**Date**: 2026-06-04

## Domain Layer

### KeyInputConfig (new)

```csharp
public sealed class KeyInputConfig {
  public required string Key { get; init; }
}
```

- `Key`: the key identifier string (e.g., `"Enter"`, `"Escape"`, `"F5"`, `"a"`). Required, non-empty. Runtime validates that it names a supported key.

### SwipeConfig (new)

```csharp
public sealed class SwipeConfig {
  public required int StartX { get; init; }
  public required int StartY { get; init; }
  public required int EndX { get; init; }
  public required int EndY { get; init; }
  public int? DurationMs { get; init; }
}
```

- `StartX`, `StartY`: absolute screen pixel coordinates of the swipe origin. Required integers.
- `EndX`, `EndY`: absolute screen pixel coordinates of the swipe destination. Required integers.
- `DurationMs`: swipe duration in milliseconds. Optional; null means use the runtime default. When present must be ≥ 0.

### CommandStepType enum (updated)

```csharp
public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,    // NEW
  Swipe        // NEW
}
```

### CommandStep class (updated)

```csharp
public sealed class CommandStep {
  public required CommandStepType Type { get; init; }
  public string TargetId { get; init; } = string.Empty;
  public PrimitiveTapConfig? PrimitiveTap { get; init; }
  public WaitForImageConfig? WaitForImage { get; init; }
  public KeyInputConfig? KeyInput { get; init; }   // NEW
  public SwipeConfig? Swipe { get; init; }         // NEW
  public int Order { get; init; }
}
```

## Service (DTO) Layer

### KeyInputConfigDto (new)

```csharp
internal sealed class KeyInputConfigDto {
  public required string Key { get; init; }
}
```

### SwipeConfigDto (new)

```csharp
internal sealed class SwipeConfigDto {
  public required int StartX { get; init; }
  public required int StartY { get; init; }
  public required int EndX { get; init; }
  public required int EndY { get; init; }
  public int? DurationMs { get; init; }
}
```

### CommandStepTypeDto enum (updated)

```csharp
internal enum CommandStepTypeDto {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,    // NEW
  Swipe        // NEW
}
```

### CommandStepDto class (updated)

```csharp
internal sealed class CommandStepDto {
  public required CommandStepTypeDto Type { get; init; }
  public string? TargetId { get; init; }
  public PrimitiveTapConfigDto? PrimitiveTap { get; init; }
  public WaitForImageConfigDto? WaitForImage { get; init; }
  public KeyInputConfigDto? KeyInput { get; init; }   // NEW
  public SwipeConfigDto? Swipe { get; init; }         // NEW
  public int Order { get; init; }
}
```

## Frontend Layer

### StepEntry (updated in CommandForm.tsx)

```typescript
export type StepEntry = {
  id: string;
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | 'KeyInput' | 'Swipe';
  targetId?: string;
  primitiveTap?: { detectionTarget: DetectionTargetForm };
  waitForImage?: { detectionTarget?: DetectionTargetForm; timeoutMs?: string };
  keyInput?: { key: string };
  swipe?: { startX: string; startY: string; endX: string; endY: string; durationMs?: string };
};
```

### CommandStepDto (updated in commands.ts)

```typescript
export type CommandStepDto = {
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | 'KeyInput' | 'Swipe';
  targetId?: string;
  order: number;
  primitiveTap?: PrimitiveTapConfigDto;
  waitForImage?: WaitForImageConfigDto;
  keyInput?: KeyInputConfigDto;    // NEW
  swipe?: SwipeConfigDto;          // NEW
};

export type KeyInputConfigDto = {
  key: string;
};

export type SwipeConfigDto = {
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  durationMs?: number;
};
```

### KeyInputPanelValue

```typescript
type KeyInputPanelValue = {
  key: string;
};
```

### SwipePanelValue

```typescript
type SwipePanelValue = {
  startX: string;
  startY: string;
  endX: string;
  endY: string;
  durationMs?: string;
};
```

## Persistence (JSON)

Key Input step in a saved command:
```json
{
  "type": "KeyInput",
  "order": 1,
  "keyInput": { "key": "Enter" }
}
```

Swipe step in a saved command:
```json
{
  "type": "Swipe",
  "order": 2,
  "swipe": {
    "startX": 100,
    "startY": 800,
    "endX": 100,
    "endY": 200,
    "durationMs": 300
  }
}
```

## Step List Display

| Step type | Label | Description |
|-----------|-------|-------------|
| KeyInput | `Key: <key>` (truncated with ellipsis) | (empty) |
| Swipe | `Swipe` | `(startX,startY) → (endX,endY)` optionally appended with `, Nms` when duration is set |
