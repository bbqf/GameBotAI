# Implementation Plan: Simulate Recorded Step

**Branch**: `056-simulate-recorded-step` | **Date**: 2026-06-05 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/056-simulate-recorded-step/spec.md`

## Summary

Add **Run** and **Run all** actions to the VisualStepPicker command recorder so users can execute individual recorded steps (or all steps in sequence) against the emulator without leaving the recorder, enabling a tight record-verify-adjust loop.

The implementation adds one new backend endpoint (`POST /api/steps/execute`) that reuses the existing `CommandExecutor` single-step dispatch logic, and extends the frontend recorder (`usePickerState`, `RecordedStepList`, `VisualStepPicker`) with execution state management and run buttons.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript 5 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core minimal APIs; existing `ICommandExecutor` / `CommandExecutor`; `@dnd-kit/sortable` (already in use)  
**Storage**: N/A — no new persistent storage  
**Testing**: xUnit (backend unit + integration); Jest + @testing-library/react (frontend)  
**Target Platform**: Windows desktop (locally served web UI)  
**Project Type**: Web application — ASP.NET Core backend + React/Vite frontend  
**Performance Goals**: Step execution result (success or error) visible within 3 seconds (SC-002); 10-second hard execution timeout enforced server-side (FR-011)  
**Constraints**: No new persistent storage; execution reuses existing `CommandExecutor` step dispatch logic; no new emulator protocol  
**Scale/Scope**: Single user; ≤50 steps per recorder session

## Constitution Check

*GATE: Must pass before proceeding to implementation.*

| Principle | Status | Notes |
|---|---|---|
| I. Code Quality | ✅ Pass | CamelCase methods; new `StepsEndpoints.cs` keeps command/step concerns separated; `toCommandStepDto` utility isolated in `stepUtils.ts`; no dead code |
| II. Testing | ✅ Pass | Unit tests for reducer (RUN_STEP_START/COMPLETE, reset-on-remove); integration test for `POST /api/steps/execute`; React component tests for Run button states |
| III. UX Consistency | ✅ Pass | Run button styled consistently with existing step row actions (matches remove button pattern); error messages include reason string from backend |
| IV. Performance | ✅ Pass | SC-002 (3s result visible) declared; FR-011 (10s timeout) enforced server-side via `CancellationTokenSource`; no hot-path regressions expected |

No violations. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/056-simulate-recorded-step/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   └── api-execute-step.md  ← Phase 1 output
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code

```text
src/GameBot.Service/
├── Endpoints/
│   └── StepsEndpoints.cs         ← new: POST /api/steps/execute
└── Execution/
    ├── ICommandExecutor.cs       ← add ForceExecuteStepAsync method signature
    └── CommandExecutor.cs        ← implement ForceExecuteStepAsync; extract per-step dispatch helper

src/web-ui/src/
├── types/
│   └── picker.ts                 ← add StepExecutionStatus; extend RecordedStep variants; extend PickerState
├── services/
│   └── commands.ts               ← add executeStep() function
└── components/commands/VisualStepPicker/
    ├── stepUtils.ts              ← new: toCommandStepDto() conversion utility
    ├── usePickerState.ts         ← add RUN_STEP_START/COMPLETE/reset-on-edit actions; runStep(); runAll(); isExecuting
    ├── RecordedStepList.tsx      ← add Run button per step; status badge; disable editing during execution
    └── VisualStepPicker.tsx      ← add Run all button to toolbar; pass isExecuting to child components
```

**Structure Decision**: Web application (backend + frontend split). Backend adds one new file (`StepsEndpoints.cs`) alongside existing endpoint files. Frontend extends the existing VisualStepPicker sub-directory with one new utility file and modifications to four existing files.

## Phase 0: Research

See [research.md](research.md) — all decisions resolved.

Key decisions:
1. **Endpoint**: New `POST /api/steps/execute` in `StepsEndpoints.cs`; reuses existing `CommandExecutor` step dispatch logic via new `ForceExecuteStepAsync` interface method
2. **Timeout**: Server-side `CancellationTokenSource(10s)` linked to request CT; returns `status: "timeout"` in outcome (HTTP 200), not HTTP 504
3. **Step-DTO mapping**: Frontend `toCommandStepDto()` utility converts `RecordedStep → CommandStepDto`; PrimitiveTap omits confidence (backend defaults to 0.8 / HighestConfidence)
4. **Execution state**: `executionStatus` + `errorMessage` added directly to each `RecordedStep` variant; stripped when confirming steps
5. **Run all**: Sequential async loop in `usePickerState.runAll()`; stops on first failure; `isExecuting` gates all editing

## Phase 1: Design & Contracts

### Backend: POST /api/steps/execute

See [contracts/api-execute-step.md](contracts/api-execute-step.md).

**Implementation location**: New `StepsEndpoints.cs` in `src/GameBot.Service/Endpoints/`.

**Logic**:
1. Deserialize `ExecuteStepRequest` (contains `CommandStepDto Step` + optional `string? SessionId`)
2. Validate step: reject `Command`-type steps (not applicable here); run existing `ValidateStep()` checks
3. Convert `CommandStepDto → CommandStep` domain object via existing `ToDomainStep()` mapper
4. Create `CancellationTokenSource(TimeSpan.FromSeconds(10))` linked to `ct`
5. Call `ICommandExecutor.ForceExecuteStepAsync(sessionId, step, linkedCt.Token)`
6. If `OperationCanceledException` and source was timeout: return `{ accepted: 0, stepOutcomes: [{ status: "timeout", reason: "Step execution timed out after 10 seconds" }] }` as HTTP 200
7. Otherwise return same `{ accepted, stepOutcomes }` shape as `force-execute`

**`ICommandExecutor` addition**:
```csharp
Task<CommandForceExecutionResult> ForceExecuteStepAsync(
    string? sessionId,
    CommandStep step,
    CancellationToken ct = default);
```

**`CommandExecutor` implementation**: Extract the per-step type dispatch from `ExecuteCommandRecursiveAsync` into a private `ExecuteOneStepAsync(sessionId, step, context, ct)` helper. The new `ForceExecuteStepAsync` calls this helper after resolving/validating the session. `ExecuteCommandRecursiveAsync` is refactored to also call this helper (no logic duplication).

**Error paths**: 400 if step type is `Command` or required fields missing; 503 if emulator unavailable; 200+timeout outcome if 10s exceeded.

### Frontend

**`types/picker.ts`** — See [data-model.md](data-model.md) for full type changes.
- Add `StepExecutionStatus` union type
- Add `executionStatus: StepExecutionStatus` and `errorMessage?: string` to each RecordedStep variant
- Add `isExecuting: boolean` to `PickerState`
- Add `StepRunResult` type mirroring backend `StepOutcomeDto`

**`services/commands.ts`** — Add:
```typescript
export const executeStep = (step: CommandStepDto, sessionId?: string) =>
  postJson<CommandExecuteResponse>('/api/steps/execute', { step, sessionId });
```

**`stepUtils.ts`** (new) — `toCommandStepDto(step: RecordedStep): CommandStepDto` — see [data-model.md](data-model.md).

**`usePickerState.ts`** — Add:
- Reducer actions: `RUN_STEP_START`, `RUN_STEP_COMPLETE`, `RUN_ALL_DONE`
  - `RUN_STEP_START`: sets `isExecuting: true`; sets target step `executionStatus: 'running'`
  - `RUN_STEP_COMPLETE`: sets `isExecuting: false` (for single-step); updates step to `success` or `error` + `errorMessage`
  - `RUN_ALL_DONE`: sets `isExecuting: false`
  - `ADD_STEP` already inserts with `executionStatus: 'idle'`
  - Future `EDIT_STEP` action (if needed) resets step status; for this feature reset is triggered by `REMOVE_STEP` + re-add (reorder/edit flow already replaces step objects)
- Exported async functions:
  ```typescript
  runStep: (id: string) => Promise<void>
  runAll: () => Promise<void>
  ```
  Both dispatch `RUN_STEP_START`, call `executeStep()`, dispatch `RUN_STEP_COMPLETE`.
  `runAll` loops sequentially; breaks on `status !== 'executed'`; dispatches `RUN_ALL_DONE` when done.

**`RecordedStepList.tsx`** — Add per-step **▶ Run** button:
- Disabled when `step.executionStatus === 'running'` or `isExecuting`
- Shows spinner icon when `step.executionStatus === 'running'`
- Shows ✓ or ✗ badge when status is `success` or `error`; `errorMessage` displayed as tooltip or inline small text
- Drag handle, remove button, and run button all disabled while `isExecuting`

**`VisualStepPicker.tsx`** — Add to toolbar:
- **Run all** button; disabled when `steps.length < 1` or `isExecuting`
- Pass `isExecuting` down to `RecordedStepList`
- On `onConfirm`, strip `executionStatus`/`errorMessage` from steps before calling parent `onConfirm`:
  ```typescript
  const handleConfirm = () => {
    onConfirm(steps.map(({ executionStatus, errorMessage, ...rest }) => rest));
  };
  ```

### Data Model

See [data-model.md](data-model.md).

### Quickstart

See [quickstart.md](quickstart.md).

## Performance Budget

| Operation | Target | Approach |
|---|---|---|
| Single step execution (KeyInput / Swipe) | ≤ 500 ms | Direct emulator action; no detection needed |
| Single step execution (PrimitiveTap) | ≤ 3000 ms | Template match on fresh screen capture; existing parallel matcher |
| Hard timeout | 10 000 ms | Server-side `CancellationTokenSource` |
| Run button disable latency | < 16 ms | Synchronous reducer dispatch on click |
