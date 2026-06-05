# Research: Simulate Recorded Step

**Branch**: `056-simulate-recorded-step` | **Date**: 2026-06-05

## Decision 1: Endpoint strategy

**Decision**: New `POST /api/steps/execute` endpoint in a new `StepsEndpoints.cs`, accepting a step definition in the request body and delegating to `CommandExecutor` internal logic.

**Rationale**: The existing `POST /api/commands/{id}/force-execute` loads a persisted command by ID and runs all its steps. During recording, the steps only exist in-memory on the frontend — there is no command ID. A new endpoint is the minimal addition; it reuses the existing `CommandExecutor` infrastructure (as required by spec clarification A) without duplicating dispatch logic.

**Alternatives considered**:
- *Temporarily create/delete a command per run*: Too heavy — adds latency, pollutes storage, requires cleanup.
- *Extend force-execute with a step-filter query param*: Requires the command to be saved first; not applicable to in-recorder steps.
- *Add the endpoint to `CommandsEndpoints.cs`*: Would work but mixes two concerns. A separate `StepsEndpoints.cs` keeps the route registration clean.

## Decision 2: Timeout enforcement

**Decision**: Server-side `CancellationTokenSource(TimeSpan.FromSeconds(10))` linked to the request `CancellationToken`. On timeout the endpoint returns HTTP 200 with `status: "timeout"` in the step outcome (not HTTP 504), so the frontend can display the error inline without treating it as a network failure.

**Rationale**: The 10-second timeout must be enforced where the blocking call actually happens — the backend emulator interaction. A client-side `AbortController` would cancel the HTTP request but not the ongoing emulator action, leaving the emulator in an undefined state. The `status: "timeout"` outcome keeps the response shape identical to other failure outcomes, simplifying frontend handling.

**Alternatives considered**:
- *Client-side AbortController only*: Insufficient — does not stop the backend emulator action.
- *HTTP 504 on timeout*: Forces the frontend to special-case a non-2xx status; using a 200 with an outcome field is consistent with how `force-execute` handles partial failures today.

## Decision 3: Step-to-DTO mapping and PrimitiveTap defaults

**Decision**: The frontend converts each `RecordedStep` to a `CommandStepDto` using a new `toCommandStepDto()` utility in `src/web-ui/src/components/commands/VisualStepPicker/stepUtils.ts`. For `PrimitiveTap`, omit `confidence` and `selectionStrategy` from the request; the backend uses its existing defaults (0.8 confidence, `HighestConfidence` strategy) — the same defaults used when creating a tap step from the command editor.

**Rationale**: The recorder already uses `imageId + offsetX/Y` as the tap representation. The backend `DetectionTargetDto` has optional confidence/selectionStrategy fields already defaulted in the DTO-to-domain mapping. Keeping them absent in the execute-step request avoids inventing a new set of defaults and stays consistent with the command editor flow.

**Alternatives considered**:
- *Send explicit confidence 0.8 from frontend*: Couples the frontend to a magic number better owned by the backend.
- *New step-execute request shape*: Reusing `CommandStepDto` means zero new DTO types on the backend request path — just a thin wrapper.

## Decision 4: Execution status in frontend state

**Decision**: Add `executionStatus: StepExecutionStatus` and optional `errorMessage?: string` directly to each `RecordedStep` variant in `picker.ts`. A new `StepExecutionStatus = 'idle' | 'running' | 'success' | 'error'` type is also added. All steps initialize with `executionStatus: 'idle'`. Editing a step (any field change via a new `EDIT_STEP` reducer action) resets its status to `'idle'` and clears `errorMessage`.

**Rationale**: Co-locating execution state with the step record keeps the render logic simple — `RecordedStepList` gets everything it needs from a single step object. When the user confirms and calls `onConfirm(steps)`, `VisualStepPicker` strips the UI-only fields before passing the steps up.

**Alternatives considered**:
- *Separate `executionStatuses: Record<string, StepExecutionState>` map in PickerState*: More separation of concerns, but doubles the bookkeeping in every reducer action that touches step identity.

## Decision 5: "Run all" implementation

**Decision**: Implemented as a sequential async loop in `usePickerState.runAll()`. Each step is executed one at a time via the same `executeStep()` service function. The loop stops at the first failed or timed-out step. `isExecuting: boolean` is added to `PickerState` and set `true` for both single-step runs and "Run all" to gate the editing lock uniformly.

**Rationale**: Sequential execution matches how a real command executes (one step at a time, left to right). Parallel execution would fire all emulator actions simultaneously — incorrect behavior for gameplay automation.

**Alternatives considered**:
- *"Run all" as a backend endpoint that re-executes the step list*: Would require the frontend to synchronize state for the per-step progress indicators; the sequential loop keeps progress feedback fully in-client without a streaming protocol.

## NEEDS CLARIFICATION — Resolved

All clarifications from `/speckit-clarify` are already incorporated in the spec (see `spec.md` Clarifications section). No open items.
