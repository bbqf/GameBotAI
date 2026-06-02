---
description: "Task list for Queue Execution Runtime"
---

# Tasks: Queue Execution Runtime

**Input**: Design documents from `/specs/051-queue-execution-runtime/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-execution.md, quickstart.md

**Tests**: Included — the project Constitution (Principle II) makes unit tests mandatory for executable logic and requires green builds/tests before commit.

**Organization**: Tasks are grouped by user story (US1–US4) so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 (Setup/Foundational/Polish carry no story label)

## Path Conventions

Web application: backend service under `src/GameBot.*`, frontend under `src/web-ui/`, tests under `tests/unit`, `tests/integration`, `tests/contract`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a clean gate and create the new code locations before changes.

- [x] T001 Verify the baseline gate is green before changes: `dotnet build` + `dotnet test` and, in `src/web-ui`, `npm run build` + `npm test` (Constitution NON-NEGOTIABLE — no progression on a red gate).
- [x] T002 [P] Create backend folders `src/GameBot.Service/Services/QueueExecution/` and `src/GameBot.Service/Services/SequenceExecution/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared run platform every user story builds on — domain vocabulary, execution-log nesting, the reusable sequence-execution service, and the queue-run lifecycle scaffold.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [x] T003 [P] Add `QueueStopReason` enum (`CompletedFullRun`, `StoppedManually`, `Failure`) in `src/GameBot.Domain/Queues/QueueStopReason.cs` per data-model.md.
- [x] T004 Add queue-run logging to `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (and `IExecutionLogService`): `LogQueueStartAsync(queueId, queueName, ct)` creating a `running` root with `ExecutionType="queue"`, and `LogQueueFinalizeAsync(rootId, queueId, queueName, finalStatus, summary, stopReason, details, ct)` upserting the terminal queue root preserving root hierarchy.
- [x] T005 Add nestable sequence logging in the same `ExecutionLogService.cs` (and interface): a `LogSequenceStartAsync(sequenceId, sequenceName, ExecutionLogContext parentContext, ct)` overload that sets `ParentExecutionId`/`RootExecutionId`/`SequenceIndex` from the parent, and make `LogSequenceFinalizeAsync` preserve the entry's existing parent hierarchy on upsert (instead of forcing parent=null).
- [x] T006 [P] Create `ISequenceExecutionService` in `src/GameBot.Service/Services/SequenceExecution/ISequenceExecutionService.cs` with `ExecuteAsync(string sequenceId, string? sessionId, ExecutionLogContext? parentContext, CancellationToken ct)` returning the sequence execution result.
- [x] T007 Implement `SequenceExecutionService` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` by extracting the wiring currently inline in `src/GameBot.Service/Program.cs` (`sequences/{id}/execute`): root start (nested when `parentContext` provided), the command-executor delegate with child `ExecutionLogContext`, gate/condition/image evaluators, detail-item building, and finalize — preserving current standalone behavior exactly.
- [x] T008 Repoint the `sequences/{id}/execute` endpoint in `src/GameBot.Service/Program.cs` to call `ISequenceExecutionService.ExecuteAsync(..., parentContext: null, ...)` and register the service in DI; remove the now-duplicated inline wiring.
- [x] T009 [P] Create `IQueueExecutionService` (`StartAsync`, `StopAsync`, `IsRunning`) in `src/GameBot.Service/Services/QueueExecution/IQueueExecutionService.cs` and internal `QueueRunHandle` / `QueueRunResult` types in `src/GameBot.Service/Services/QueueExecution/` per data-model.md.
- [x] T010 Implement the lifecycle scaffold in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: a `ConcurrentDictionary<string, QueueRunHandle>` registry; `StartAsync` rejects when already running, otherwise creates the handle (with a `CancellationTokenSource` linked to host shutdown), sets `QueueRuntimeStore` status `Running`, and launches the run on a background `Task`; `StopAsync` cancels the handle's CTS; a `finally` removes the handle and sets status `Stopped`. Register as a singleton in `src/GameBot.Service/Program.cs`. (Run body is filled by US1–US4.)
- [x] T011 Wire start/stop in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` to delegate to `IQueueExecutionService` (start → 200/Running or 409 `already_running`; stop → 200, no-op when stopped) and add queue-run started/stopped/failed messages in `src/GameBot.Service/Endpoints/QueuesEndpoints.Logging.cs`.

**Checkpoint**: Build + existing tests green; sequence execution behaves exactly as before; queue start/stop launch/cancel a (currently no-op) background run and toggle status.

---

## Phase 3: User Story 1 - Run a queue's sequences end-to-end (Priority: P1) 🎯 MVP

**Goal**: Starting a queue loads its linked template, connects to the bound emulator, runs the sequences in order, and ends a full pass with a single "completed full run" queue-run log entry; the session is disconnected and status returns to Stopped.

**Independent Test**: Link a queue to a 2+ sequence template with a connected emulator, start it, and confirm the sequences run in order, one "completed full run" queue row appears (with the sequences nested), and the queue returns to Stopped.

### Tests for User Story 1

- [x] T012 [P] [US1] Unit test in `tests/unit/Queues/QueueExecutionServiceRunTests.cs`: with fake `ISessionManager`, `ISequenceExecutionService`, and `IExecutionLogService`, starting a queue resolves the linked template and invokes sequence execution in template order (FR-002 happy path, FR-006).
- [x] T013 [P] [US1] Unit test in `tests/unit/Queues/QueueExecutionServiceCompletionTests.cs`: a full pass (cycle off) finalizes exactly one queue-run entry with `CompletedFullRun` (status success) and disconnects the session (FR-009/010/023, SC-001/006).
- [x] T014 [P] [US1] web-ui test in `src/web-ui/src/pages/__tests__/executionLogGrid.test.ts`: `executionType === 'queue'` maps to type label "Queue" and projects as an expandable top-level row.

### Implementation for User Story 1

- [x] T015 [US1] Implement the happy-path run body in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: resolve the queue's linked template into an ordered sequence-id snapshot, open a session via `ISessionManager.CreateSession(gameIdOrPath: $"queue:{id}", preferredDeviceSerial: queue.EmulatorSerial)`, call `LogQueueStartAsync`, run each snapshot sequence via `ISequenceExecutionService.ExecuteAsync` with a `parentContext` (queue root id, incrementing `SequenceIndex`), then `LogQueueFinalizeAsync(CompletedFullRun)` and disconnect the session.
- [x] T016 [US1] In the same run body, pass the created session's `Id` explicitly to sequence execution so invoked commands resolve to the queue's session (the explicit-`sessionId` path in `CommandExecutor.ResolveSessionIdAsync`).
- [x] T017 [P] [US1] Add `queue: 'Queue'` to `NODE_TYPE_LABELS` and treat `executionType === 'queue'` as expandable in `src/web-ui/src/pages/executionLogGrid.ts`.
- [x] T018 [US1] Add a subtree assertion test in `tests/unit/.../ExecutionLogServiceQueueSubtreeTests.cs` confirming a queue root nests its sequence children (and their command/step descendants) and lists as a single root via `RootsOnly` (FR-007, SC re: single top-level row).

**Checkpoint**: US1 fully functional — a queue runs to completion and is visible as one nested queue entry.

---

## Phase 4: User Story 2 - Stop a running queue immediately (Priority: P1)

**Goal**: Stopping a running queue (UI or API) aborts promptly, disconnects the session, and records a "stopped manually" entry; stop on a not-running queue is a no-op and a second start is rejected.

**Independent Test**: Start a queue with several/long sequences, stop it mid-run, and confirm it aborts within ~3 s, the session is gone, status returns to Stopped, and a "stopped manually" entry is written.

### Tests for User Story 2

- [x] T019 [P] [US2] Unit test in `tests/unit/Queues/QueueExecutionServiceStopTests.cs`: cancelling a run aborts before remaining sequences run, disconnects the session, and finalizes `StoppedManually` (FR-018/019/020/021, SC-003/006). Also assert teardown resilience: when the fake `ISessionManager.StopSession` throws during disconnect, the run still finalizes its terminating entry and returns to Stopped (FR-023 edge case "failure to disconnect on stop").
- [x] T020 [P] [US2] Unit test in `tests/unit/Queues/QueueExecutionServiceGuardsTests.cs`: stop on a not-running queue is a no-op (FR-022); starting an already-running queue yields an `already_running` result (FR-013a); and two **different** queues bound to the **same** emulator serial can both reach Running concurrently with no guard rejecting the second (FR-013, SC-009).
- [x] T021 [P] [US2] Integration test in `tests/integration/Queues/QueueStartStopEndpointTests.cs`: `POST /start` then `POST /start` → 409 `already_running`; `POST /stop` on stopped → 200 no-op (contracts/queue-execution.md).

### Implementation for User Story 2

- [x] T022 [US2] In `QueueExecutionService.cs` run body, catch `OperationCanceledException` and finalize with `StoppedManually`; ensure session disconnect runs in the `finally` for every end path (completed/stopped/failure), and wrap the disconnect so a teardown exception is caught/logged and never prevents writing the terminating entry or returning the queue to Stopped (FR-023 edge case).
- [x] T023 [US2] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, map an in-progress start to 409 `already_running` and ensure `stop` returns promptly without awaiting run completion.
- [x] T024 [P] [US2] In `src/web-ui/src/pages/QueuesPage.tsx`, disable the Start control while `status === 'Running'` and keep Stop enabled; update `src/web-ui/src/pages/__tests__/QueuesPage.execution.spec.tsx` to assert real Running/Stopped transitions.

**Checkpoint**: US1 + US2 work — runs complete or stop cleanly with the correct terminating entry.

---

## Phase 5: User Story 3 - Cycle execution repeats automatically (Priority: P2)

**Goal**: With cycle execution on, the run repeats the template snapshot from the first sequence after the last (no reload) until stopped or a run-level failure; with it off, the run executes once. Empty templates never busy-loop.

**Independent Test**: Enable cycle on a 2-sequence queue, start it, observe at least two full passes (growing nested children), then stop → "stopped manually". With cycle off, one pass → "completed full run".

### Tests for User Story 3

- [x] T025 [P] [US3] Unit test in `tests/unit/Queues/QueueExecutionServiceCycleTests.cs`: cycle on repeats sequences from the start using the same snapshot (no template re-read), and a manual stop ends the run `StoppedManually` (FR-014/015).
- [x] T026 [P] [US3] Unit test in `tests/unit/Queues/QueueExecutionServiceEmptyTemplateTests.cs`: cycle on + empty template ends `CompletedFullRun` without spinning (FR-016/017).

### Implementation for User Story 3

- [x] T027 [US3] In `QueueExecutionService.cs`, wrap the sequence loop so it repeats from index 0 when `queue.CycleExecution` is true, checking the cancellation token (and run-level failure) between sequences and between cycles, reusing the snapshot resolved at start; add the empty-template guard (end instead of loop) for FR-017.

**Checkpoint**: US1–US3 work — single-pass and cycling runs both behave correctly.

---

## Phase 6: User Story 4 - Failure and connection outcomes are clearly logged (Priority: P2)

**Goal**: No-template and emulator-unreachable conditions end the run immediately with a clear failure entry (zero sequences run); a lost connection mid-run ends as failure; individual sequence failures (including stale references) are non-fatal and the run still completes.

**Independent Test**: Start a queue with no/deleted template → failure "no template to run", zero sequences. With a disconnected emulator → failure "emulator could not be reached". A failing sequence between two good ones (cycle off) → run still ends "completed full run" with the failure recorded.

### Tests for User Story 4

- [x] T028 [P] [US4] Unit test in `tests/unit/Queues/QueueExecutionServiceNoTemplateTests.cs`: no resolvable linked template → `Failure` entry "no template to run", zero sequence executions (FR-002, SC-005).
- [x] T029 [P] [US4] Unit test in `tests/unit/Queues/QueueExecutionServiceEmulatorFailureTests.cs`: fake `ISessionManager.CreateSession` throwing `no_adb_devices`/`KeyNotFoundException` → `Failure` entry "emulator could not be reached", zero sequences (FR-004, SC-002).
- [x] T030 [P] [US4] Unit test in `tests/unit/Queues/QueueExecutionServicePerSequenceFailureTests.cs`: a failed sequence result and a stale/unresolved sequence reference are both non-fatal — the run continues and ends `CompletedFullRun`, with the failed count recorded (FR-008/008b, SC-008).
- [x] T031 [P] [US4] Unit test in `tests/unit/Queues/QueueExecutionServiceConnectionLostTests.cs`: a connection loss mid-run ends the run with `Failure` (FR-008a).

### Implementation for User Story 4

- [x] T032 [US4] In `QueueExecutionService.cs`, implement run-level failure mapping: missing/unresolvable template (FR-002), session-create failure (FR-004), and mid-run connection loss (FR-008a) → `LogQueueFinalizeAsync(Failure, <user-facing reason>)` with no/partial sequence execution; ensure the session (if opened) is still disconnected.
- [x] T033 [US4] In `QueueExecutionService.cs`, implement per-sequence non-fatal handling: record a failed sequence result and continue; treat a stale sequence reference as a non-fatal per-sequence failure (log a failed sequence child entry) and continue (FR-008/008b); reflect failed counts in the completed-run summary (FR-012).
- [x] T034 [P] [US4] In `src/web-ui/src/pages/QueuesPage.tsx`, surface a run that ended in failure to the operator (toast/inline message) and update `src/web-ui/src/pages/__tests__/QueuesPage.execution.spec.tsx`.

**Checkpoint**: All user stories independently functional; every run ends with exactly one correctly-reasoned terminating entry.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T035 [P] Update `specs/openapi.json` for the start/stop behavior changes (409 `already_running`) and the `queue` execution type in execution-log responses, per contracts/queue-execution.md.
- [x] T036 [P] Execute the manual verification in `specs/051-queue-execution-runtime/quickstart.md` (happy path, stop, cycle, failure paths, concurrency) on a Windows host with a connected emulator. _(Verified manually by the operator.)_
- [x] T037 Final gate: `dotnet build` + `dotnet test` and `src/web-ui` `npm run build` + `npm test` all green; **`eslint` and `tsc --noEmit` clean across the whole web-ui** (fix everything — single-developer repo, no pre-existing failures are left behind); verify backend test coverage on touched areas meets the Constitution Principle II baseline (≥80% line / ≥70% branch) and add tests if short; confirm no dangling sessions remain after runs.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all user stories.
- **User Stories (Phase 3–6)**: all depend on Foundational. US1 is the MVP. US2/US3/US4 each extend the same `QueueExecutionService.cs` run body, so their implementation tasks are sequential with US1's (not parallel across stories); their **tests** and **web-ui** tasks are parallelizable.
- **Polish (Phase 7)**: depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Establishes the run body other stories extend.
- **US2 (P1)**: after US1 (shares the run body and finally/disconnect path).
- **US3 (P2)**: after US1 (wraps the sequence loop).
- **US4 (P2)**: after US1 (adds failure mapping around the run body). Independently testable via its own failure scenarios.

> Note: US1 and US2 are both **P1** in the spec (a runnable engine must also be stoppable). The phase ordering (US1 → US2 → US3 → US4) reflects implementation sequence on the shared `QueueExecutionService.cs`, not relative story priority.

### Within Each User Story

- Write the story's tests first and let them fail, then implement.
- Foundational order: T003/T006/T009 [P] → T004/T005 (exec-log) → T007 (extract) → T008 (repoint) → T010 (lifecycle) → T011 (endpoints).

### Parallel Opportunities

- T002 (Setup) is [P].
- Foundational [P]: T003, T006, T009 (distinct new files) before the shared-file edits.
- Within each story, all test tasks marked [P] and the web-ui task run in parallel with each other; backend run-body edits to `QueueExecutionService.cs` are serialized.
- Cross-story: US2/US3/US4 test authoring can proceed in parallel once US1's run body exists.

---

## Parallel Example: User Story 1

```bash
# Tests + web-ui (different files) together:
Task: "Unit test queue runs sequences in order in tests/unit/Queues/QueueExecutionServiceRunTests.cs"
Task: "Unit test completed-full-run finalize + disconnect in tests/unit/Queues/QueueExecutionServiceCompletionTests.cs"
Task: "executionLogGrid 'queue' label/expandable test in src/web-ui/src/pages/__tests__/executionLogGrid.test.ts"
# Then implement the run body (T015/T016) and the grid label (T017 [P] with backend).
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL) → 3. Phase 3 US1 → STOP and validate a queue running end-to-end and appearing as one nested "completed full run" entry. Demo-able.

### Incremental Delivery

Foundational → US1 (MVP: run to completion) → US2 (stop) → US3 (cycle) → US4 (failure clarity) → Polish. Each story is a shippable increment that does not break the previous.

---

## Notes

- [P] = different files, no incomplete dependencies. Backend run-body tasks (T015, T022, T027, T032, T033) all edit `QueueExecutionService.cs` → keep them sequential.
- The biggest risk is the T007/T008 extraction; existing sequence-execution tests must remain green (they are the regression guard for that refactor).
- Runtime state is in-memory only; no persistence/migration tasks are needed.
- Commit after each task or logical group; never commit on a red gate (Constitution).
