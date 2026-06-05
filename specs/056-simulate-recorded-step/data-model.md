# Data Model: Simulate Recorded Step

**Branch**: `056-simulate-recorded-step` | **Date**: 2026-06-05

## Frontend Changes (`src/web-ui/src/types/picker.ts`)

### New: `StepExecutionStatus`

```typescript
export type StepExecutionStatus = 'idle' | 'running' | 'success' | 'error';
```

### Updated: `RecordedPrimitiveTapStep`

Adds `executionStatus` and optional `errorMessage`.

```typescript
export type RecordedPrimitiveTapStep = {
  id: string;
  type: 'PrimitiveTap';
  imageId: string;
  offsetX: number;
  offsetY: number;
  label: string;
  executionStatus: StepExecutionStatus;   // new, default 'idle'
  errorMessage?: string;                  // new, present when status === 'error'
};
```

### Updated: `RecordedKeyInputStep`

```typescript
export type RecordedKeyInputStep = {
  id: string;
  type: 'KeyInput';
  key: string;
  label: string;
  executionStatus: StepExecutionStatus;   // new
  errorMessage?: string;                  // new
};
```

### Updated: `RecordedSwipeStep`

```typescript
export type RecordedSwipeStep = {
  id: string;
  type: 'Swipe';
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  durationMs: number;
  label: string;
  executionStatus: StepExecutionStatus;   // new
  errorMessage?: string;                  // new
};
```

### Updated: `PickerState`

Adds `isExecuting` to gate editing during any run.

```typescript
type PickerState = {
  status: PickerStatus;
  steps: RecordedStep[];
  screenshotUrl: string | null;
  matches: ImageMatchResult[];
  errorMessage: string | null;
  isExecuting: boolean;         // new: true while any step is running
};
```

### New reducer actions

```typescript
type PickerAction =
  | { type: 'LOAD_START' }
  | { type: 'LOAD_SUCCESS'; screenshotUrl: string; matches: ImageMatchResult[] }
  | { type: 'LOAD_ERROR'; message: string }
  | { type: 'ADD_STEP'; step: RecordedStep }
  | { type: 'REMOVE_STEP'; id: string }
  | { type: 'REORDER_STEPS'; steps: RecordedStep[] }
  | { type: 'RUN_STEP_START'; id: string }         // new
  | { type: 'RUN_STEP_COMPLETE'; id: string; result: StepRunResult }  // new
  | { type: 'RUN_ALL_DONE' };                      // new: clears isExecuting after runAll loop ends
```

### New: `StepRunResult`

Mirrors the backend `StepOutcomeDto`.

```typescript
export type StepRunResult = {
  status: 'executed' | 'timeout' | 'error' | string;
  reason?: string;
};
```

---

## Frontend: New Utility (`src/web-ui/src/components/commands/VisualStepPicker/stepUtils.ts`)

Converts a `RecordedStep` to the `CommandStepDto` shape expected by `POST /api/steps/execute`.

```typescript
import type { RecordedStep } from '../../../types/picker';
import type { CommandStepDto } from '../../../services/commands';

export function toCommandStepDto(step: RecordedStep): CommandStepDto {
  switch (step.type) {
    case 'PrimitiveTap':
      return {
        type: 'PrimitiveTap',
        order: 0,
        primitiveTap: {
          detectionTarget: {
            referenceImageId: step.imageId,
            offsetX: step.offsetX,
            offsetY: step.offsetY,
          },
        },
      };
    case 'KeyInput':
      return { type: 'KeyInput', order: 0, keyInput: { key: step.key } };
    case 'Swipe':
      return {
        type: 'Swipe',
        order: 0,
        swipe: {
          startX: step.startX,
          startY: step.startY,
          endX: step.endX,
          endY: step.endY,
          durationMs: step.durationMs,
        },
      };
  }
}
```

---

## Backend Changes

### New: `ExecuteStepRequest` DTO

Added in `src/GameBot.Service/Endpoints/StepsEndpoints.cs` (or collocated Models file).

```csharp
internal sealed class ExecuteStepRequest
{
    public required CommandStepDto Step { get; init; }
    public string? SessionId { get; init; }
}
```

### Existing reuse: `CommandForceExecutionResult` / `PrimitiveTapStepOutcome`

No new backend result types needed. The existing `CommandForceExecutionResult` and `PrimitiveTapStepOutcome` records (already used by `force-execute`) are reused for the single-step response. The response shape is identical to `force-execute` with a single-element `stepOutcomes` array.

### Updated: `ICommandExecutor`

New method added:

```csharp
Task<CommandForceExecutionResult> ForceExecuteStepAsync(
    string? sessionId,
    CommandStep step,
    CancellationToken ct = default);
```

### Updated: `CommandExecutor`

Implements the new interface method by extracting and calling the single-step dispatch logic that already exists in `ExecuteCommandRecursiveAsync`. No duplicated execution code — the recursive method is refactored to call the new per-step helper.

---

## State Transitions: `executionStatus` per step

```
idle ──[Run pressed]──► running ──[success response]──► success
                                ╰──[error/timeout]──────► error
success ──[Run pressed again]──► running   (re-run allowed)
error   ──[Run pressed again]──► running   (re-run allowed)
any ────[step field edited]────► idle      (reset on edit, clears errorMessage)
```
