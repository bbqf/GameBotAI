# Implementation Plan: Queue Execution Runtime

**Branch**: `051-queue-execution-runtime` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/051-queue-execution-runtime/spec.md`

## Summary

Replace the placeholder queue start/stop (which only flips a status flag) with a real execution engine. Starting a queue launches a background run that: (1) loads the queue's **linked template** server-side, (2) opens an **emulator session** to the queue's bound ADB serial, (3) runs the template's sequences **in order**, reusing the existing `SequenceRunner` + `CommandExecutor` wiring, nesting each sequence under a new **queue-run** root in the execution log. Per-sequence failures are non-fatal (recorded, run continues); a run-level failure (no template, emulator unreachable, connection lost) ends the run as a failure. When **cycle execution** is on, the run repeats from the first sequence using the template snapshot loaded at start until stopped or a run-level failure. **Stop** cancels the run promptly, disconnects the session, and writes a terminating log entry. Every run ends with exactly one queue-run execution log entry recording the stop reason (completed full run / stopped manually / failure).

Approach: introduce a singleton `IQueueExecutionService` in `GameBot.Service` that owns a registry of in-flight runs (one `CancellationTokenSource` per queue) and orchestrates load→connect→run→finalize on a background task. The inline sequence-execution wiring currently in `Program.cs` (`sequences/{id}/execute`) is extracted into a reusable `ISequenceExecutionService` so both the existing endpoint and the queue runner share one code path (no duplication). The execution-log service gains queue-run start/finalize methods and a nestable sequence variant (sequences carry a parent execution id). The web-ui Queues page already has start/stop controls and live status; only minor additions are needed (a `queue` type label in the execution-log grid and reflecting Running/Stopped from real runs).

## Technical Context

**Language/Version**: C# / .NET (net9.0) backend; TypeScript 5.x + React 18.3 web-ui
**Primary Dependencies**: ASP.NET Core Minimal APIs; existing `SequenceRunner`, `CommandExecutor`/`ICommandExecutor`, `IExecutionLogService`, `ISessionManager`, `IQueueRepository`/`IQueueRuntimeStore`, `IQueueTemplateRepository`; React + existing `services/queues.ts`, `services/executionLogsApi.ts`, `pages/executionLogGrid.ts`
**Storage**: Queue config + template + queue→template link are persisted (existing file repos). Run state (in-flight runs, statuses) is **in-memory only** (`QueueRuntimeStore` + a new run registry) and resets on restart, by design. Execution-log entries persist via the existing `IExecutionLogRepository`.
**Testing**: xUnit (`GameBot.Tests`) for backend; Jest + `@testing-library/react` for web-ui. `GAMEBOT_USE_ADB=false`/non-Windows runs `SessionManager` in stub mode, so the runner is tested against a fake `ISessionManager`.
**Target Platform**: Windows desktop service (ADB real mode) + cross-platform CI (stub mode); web browser SPA
**Project Type**: Web application (backend service `src/GameBot.*` + frontend `src/web-ui`)
**Performance Goals**: Orchestration overhead negligible vs. ADB I/O. Stop aborts within 3 s (SC-003) — cancellation observed at the next `await`/`ThrowIfCancellationRequested` in `SequenceRunner` and the inter-sequence loop. Status changes reflected in the list within ~2 s via the existing poll cadence.
**Constraints**: No blocking the HTTP start call for the run's duration — start launches a background task and returns immediately (Running). Cycle mode must not busy-loop on an empty template (FR-017). No dangling sessions — session disconnected on every end path (FR-023). CamelCase method names only (no underscores). Build + tests green before commit.
**Scale/Scope**: Operator scale — up to ~50 queues, ~100 sequences each; typically one or a few concurrent runs. Concurrent runs on the same emulator are allowed without a guard (FR-013).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality Discipline | PASS — new `IQueueExecutionService` is cohesive and small; the sequence-run wiring is **extracted** from `Program.cs` into `ISequenceExecutionService` (removes duplication rather than adding it). No dead code; CamelCase method names; public service methods documented with inputs/outcomes/cancellation behavior. No new third-party dependencies. |
| II. Testing Standards | PASS — unit tests for the run engine cover: no-template failure (FR-002), emulator-unavailable failure (FR-004), in-order execution (FR-006), per-sequence failure non-fatal/continue (FR-008), cycle loop + empty-template guard (FR-014/017), prompt stop + session disconnect (FR-019/020/023), terminating log entry + stop reason (FR-009/010), already-running rejection (FR-013a), concurrent same-emulator allowed (FR-013). Deterministic via fakes (`ISessionManager`, sequence execution, execution-log). Tests pass before commit. |
| III. UX Consistency | PASS — start/stop reuse existing controls and error envelope (`{ error: { code, message, hint } }`); 409 `already_running` mirrors existing 409 `queue_running`; the queue run appears as one top-level execution-log entry consistent with the 049/050 hierarchy/grid; messages are operator-facing with stop reasons. |
| IV. Performance Requirements | PASS — perf note above; cancellation latency bounded; no N+1 (template + sequences loaded once per run; cycle reuses the in-memory snapshot). Background run uses a single long-lived task per queue, not per-sequence threads. |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/051-queue-execution-runtime/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/           # Phase 1 output
│   └── queue-execution.md
└── checklists/
    └── requirements.md   # Created by /speckit-specify
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Queues/
│       ├── QueueExecutionStatus.cs        # UNCHANGED (Stopped/Running)
│       └── QueueStopReason.cs             # NEW: enum CompletedFullRun | StoppedManually | Failure
├── GameBot.Service/
│   ├── Services/
│   │   ├── QueueExecution/
│   │   │   ├── IQueueExecutionService.cs   # NEW: StartAsync / StopAsync / IsRunning
│   │   │   ├── QueueExecutionService.cs     # NEW: run registry (CTS per queue), orchestration loop
│   │   │   └── QueueRunResult.cs            # NEW: stop reason + counters (internal)
│   │   ├── SequenceExecution/
│   │   │   ├── ISequenceExecutionService.cs # NEW: ExecuteAsync(sequenceId, sessionId, parentContext, ct)
│   │   │   └── SequenceExecutionService.cs  # NEW: extracted from Program.cs sequence-execute wiring
│   │   └── ExecutionLog/
│   │       └── ExecutionLogService.cs       # EDIT: LogQueueStartAsync/LogQueueFinalizeAsync + nestable sequence start/finalize
│   ├── Endpoints/
│   │   ├── QueuesEndpoints.cs               # EDIT: start/stop delegate to IQueueExecutionService
│   │   └── QueuesEndpoints.Logging.cs       # EDIT: queue run started/stopped/failed log messages
│   └── Program.cs                           # EDIT: register services; sequence-execute endpoint calls ISequenceExecutionService
└── web-ui/src/
    ├── pages/executionLogGrid.ts            # EDIT: add 'queue' → 'Queue' type label; queue rows expandable
    └── pages/QueuesPage.tsx                 # EDIT (minor): surface run failures (toast) / disable start while running
```

**Structure Decision**: Web application. Backend changes are concentrated in `GameBot.Service` (orchestration is a Service concern — it needs `ISessionManager`, `ICommandExecutor`, evaluators, and `IExecutionLogService`, exactly like the existing sequence-execute endpoint). A tiny domain addition (`QueueStopReason`) keeps the stop-reason vocabulary in the domain. The frontend touch is minimal because start/stop controls and live status already exist (feature 046); the execution-log grid (050) gains a `queue` row type.

## Complexity Tracking

> Not applicable — Constitution Check passed with no violations.
