# Phase 0 Research: Queue Execution Runtime

All Technical Context items are known from the existing codebase; the items below resolve the design decisions that drive Phase 1.

## D1 — Where the orchestration lives

- **Decision**: Add a singleton `IQueueExecutionService` in `GameBot.Service/Services/QueueExecution`. Start/stop endpoints in `QueuesEndpoints.cs` delegate to it.
- **Rationale**: Orchestration needs `ISessionManager`, `ICommandExecutor`, condition/image evaluators, and `IExecutionLogService` — all Service-layer concerns already composed by the existing `sequences/{id}/execute` endpoint in `Program.cs`. The domain (`SequenceRunner`) stays pure.
- **Alternatives considered**: A hosted `BackgroundService` per queue (rejected — runs are operator-initiated and short-to-indefinite, not scheduled; a registry of tasks is simpler). Putting orchestration in the domain (rejected — would pull Service dependencies into `GameBot.Domain`).

## D2 — Reuse of the sequence-execution wiring

- **Decision**: Extract the inline body of `sequences/{id}/execute` (root-start, command executor delegate with child `ExecutionLogContext`, gate/condition evaluators, detail-item building, finalize) into `ISequenceExecutionService.ExecuteAsync(sequenceId, sessionId, parentContext, ct)`. The existing endpoint and the queue runner both call it.
- **Rationale**: Avoids duplicating ~240 lines of nested-logging logic; guarantees queue-run sequences produce identical nested detail to standalone runs (SC re: "same depth of information"). Satisfies Constitution I (no duplication).
- **Parent nesting**: `parentContext` carries `ParentExecutionId`/`RootExecutionId` = the queue-run root and a `SequenceIndex` = position in the queue. When `parentContext` is null (standalone endpoint), behavior is unchanged (the sequence is its own root).
- **Alternatives considered**: Copy the wiring into the queue runner (rejected — duplication, drift risk). Have the queue runner call the HTTP endpoint (rejected — in-process HTTP round-trips, loses cancellation/session control).

## D3 — Execution-log shape for a queue run

- **Decision**: A queue run is a new top-level entry with `ExecutionType = "queue"`. Add `LogQueueStartAsync(queueId, queueName)` (creates a `running` root) and `LogQueueFinalizeAsync(rootId, queueId, queueName, finalStatus, summary, …)` (upserts the terminal entry preserving root hierarchy). Each sequence executed in the run is a child entry (`ParentExecutionId` = queue-run root, `SequenceIndex` = order), and the commands a sequence invokes remain its children (depth 2) exactly as today.
- **Rationale**: Reuses the 049 root/child/`RootsOnly` model and the 050 grid: `BuildTreeNode` already nests arbitrary `directChildren` when a parent has no `StepOutcomes` (the queue root case), so sequences nest under the queue with no tree-builder changes. The grid lists roots only, so a queue run shows as **one** row whose subtree is the sequences and their steps.
- **Stop-reason / status mapping**: `FinalStatus` stays in the existing vocabulary `{running, success, failure}`. Mapping: **completed full run → success**, **failure → failure**, **stopped manually → success** but with a summary that explicitly says "stopped manually" (FR-012 "distinguishable from a clean completion" is met via the summary text; no change to `NormalizeStatus`/grid status enum). The structured stop reason is also recorded as a leading detail item so it is machine-readable.
- **Alternatives considered**: Add a new `stopped`/`cancelled` `FinalStatus` value (rejected this iteration — ripples into `NormalizeStatus`, the grid status column, filters, and existing tests for marginal benefit; summary text is sufficient and consistent with how sequences already encode outcome).

## D4 — Establishing the emulator session for a queue

- **Decision**: The runner calls `ISessionManager.CreateSession(gameIdOrPath, preferredDeviceSerial: queue.EmulatorSerial)`, using a synthetic game identifier (the queue id, e.g. `queue:{id}`) as `gameIdOrPath`. It passes the created session's `Id` **explicitly** to `ICommandExecutor.ForceExecuteAsync(sessionId, …)` for every command, so command/session resolution never depends on the game.
- **Rationale**: `CreateSession` already binds and validates the ADB serial: it throws `InvalidOperationException("no_adb_devices")` when no device is connected and `KeyNotFoundException` when the requested serial is absent — exactly the "emulator not available" signal for FR-004. Commands resolve via the explicit `sessionId` path in `ResolveSessionIdAsync`, so a placeholder game is harmless.
- **Emulator availability**: On Windows real-ADB mode, an unavailable serial surfaces as the exceptions above → mapped to a run-level failure with a clear reason. In stub mode (non-Windows/CI/`GAMEBOT_USE_ADB=false`), `CreateSession` succeeds with a stub session; availability cannot be exercised there, so failure paths are covered by a fake `ISessionManager` in tests.
- **Disconnect**: On every end path the runner calls `ISessionManager.StopSession(sessionId)` (FR-020/023). Teardown errors are caught and do not prevent finalize (edge case "failure to disconnect on stop").
- **Alternatives considered**: Reuse a cached session via `ISessionContextCache` (rejected — the queue owns its session lifetime start-to-stop; sharing risks cross-run interference and complicates disconnect).

## D5 — Run lifecycle, cancellation, and concurrency

- **Decision**: `QueueExecutionService` holds a `ConcurrentDictionary<string queueId, QueueRunHandle>` where each handle owns a `CancellationTokenSource` and the run `Task`. `StartAsync` adds the handle, sets `QueueRuntimeStore` status `Running`, and launches the run on `Task.Run`; the endpoint returns immediately. `StopAsync` cancels the CTS and returns (the run finalizes itself). The run's `finally` removes the handle, disconnects the session, sets status `Stopped`, and writes `LogQueueFinalizeAsync`.
- **Prompt abort (FR-019)**: The cancellation token threads into `ISequenceExecutionService.ExecuteAsync` → `SequenceRunner.ExecuteAsync`, which already calls `ct.ThrowIfCancellationRequested()` between steps and `Task.Delay(…, ct)`, and `SessionManager.SendInputsAsync` checks `ct`. The inter-sequence loop checks the token each iteration. So a stop is observed at the next await — well within the 3 s budget (SC-003).
- **Already-running (FR-013a)**: `StartAsync` returns a result the endpoint maps to **409 `already_running`** when a handle exists for the queue.
- **Concurrent same emulator (FR-013)**: No guard — two queues bound to the same serial each create their own session and run. (Note: `SessionManager.MaxConcurrentSessions` is the only ceiling; documented as an operational limit, not a feature guard.)
- **Cancellation vs failure**: A run-level failure (no template / connect failure / lost connection) sets stop reason `Failure`; an `OperationCanceledException` from a stop sets `StoppedManually`; reaching the end of the sequences (cycle off) or being cancelled while cycling sets `CompletedFullRun`/`StoppedManually` respectively.

## D6 — Cycle execution semantics

- **Decision**: The template's sequences are resolved **once** into an in-memory snapshot at run start. The runner loops over the snapshot; if `CycleExecution` is true it repeats from index 0 after the last sequence, checking the cancellation token (and run-level failure) between sequences and between cycles. It never re-reads the template or the queue's runtime entries mid-run (FR-015).
- **Empty-template guard (FR-017)**: If the snapshot has zero sequences, the run ends immediately with `CompletedFullRun` (cycle on or off) instead of looping — no tight loop.
- **Per-sequence failure (FR-008)**: A sequence that returns a failed result is recorded (it already logs its own child entry); the runner increments a failed-count and continues. Only run-level failures break the loop. A stale/unresolved sequence reference (no such sequence) is treated as a non-fatal per-sequence failure (FR-008b) — the runner logs a failed sequence child entry and continues.

## D7 — Frontend impact

- **Decision**: Add `queue: 'Queue'` to `NODE_TYPE_LABELS` and treat `executionType === 'queue'` as expandable in `projectEntryRow` (`pages/executionLogGrid.ts`). `QueuesPage.tsx` already renders start/stop and polls status; add a small failure surface (toast/inline message) when a run ends in failure and keep the start control disabled while `status === 'Running'`.
- **Rationale**: The grid (050) and Queues page (046) infrastructure already exist; this is additive. Live status already polls, so a run appearing/clearing needs no new transport.
- **Alternatives considered**: A dedicated "current run" panel (out of scope — progress UI beyond existing live logs is excluded by the spec).

## Open risks / notes

- The `sequences/{id}/execute` extraction (D2) is the largest single change; it must preserve current behavior exactly (covered by existing sequence-execution tests, which must stay green).
- Stub-mode CI cannot exercise real ADB failure; those paths rely on fake `ISessionManager` unit tests.
