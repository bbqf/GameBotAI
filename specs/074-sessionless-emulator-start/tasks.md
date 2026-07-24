# Tasks: Start the Emulator From a Backend-Only, Session-Less State

**Feature**: 074-sessionless-emulator-start
**Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md) | **Data model**: [data-model.md](data-model.md) | **Contract**: [contracts/queue-emulator-instance-config.md](contracts/queue-emulator-instance-config.md)

Tests are included: the constitution mandates bug-repro-first and coverage on touched code, and the
plan commits to them.

**Tech**: C# / .NET (GameBot.Domain, GameBot.Service), xUnit; TypeScript React web-ui, Jest/RTL.
Reuses feature-070 `IEnsureEmulatorRunningActionHandler` + `EnsureEmulatorRunningArgs` unchanged.

## Phase 1: Setup

- [ ] T001 Confirm baseline green gate before changes: run `dotnet build GameBot.sln` and, in `src/web-ui`, `npm run build` — record they pass so later regressions are attributable (no file changes).

## Phase 2: Foundational (blocking prerequisites — shared config plumbing)

- [ ] T002 Add optional persisted fields `EmulatorInstanceName` (`string?`, null default) and `EmulatorInstanceIndex` (`int?`, null default) with XML docs to `src/GameBot.Domain/Queues/ExecutionQueue.cs` (mirror the `PauseWhenIdle` field style; null default gives JSON back-compat via whole-object serialization).
- [ ] T003 [P] Add `EmulatorInstanceName` (`string?`) and `EmulatorInstanceIndex` (`int?`) to `src/GameBot.Service/Contracts/Queues/CreateQueueRequest.cs`.
- [ ] T004 [P] Add `EmulatorInstanceName` (`string?`) and `EmulatorInstanceIndex` (`int?`) to `src/GameBot.Service/Contracts/Queues/UpdateQueueRequest.cs`.
- [ ] T005 [P] Add `EmulatorInstanceName` (`string?`) and `EmulatorInstanceIndex` (`int?`) to `src/GameBot.Service/Contracts/Queues/QueueResponse.cs`.

**Checkpoint**: domain + contract fields exist; solution still builds.

## Phase 3: User Story 1 — Bring the emulator up from a cold, session-less machine (P1)

**Goal**: A queue configured with an emulator instance starts that emulator before creating its device
session, so a backend-only cold state self-recovers.

**Independent test**: With only the backend running (emulator closed, no session), start a queue that
has `emulatorInstanceName` set and confirm the emulator comes up and the session is then created —
proven by the unit behavior matrix (T007) using a fake handler.

### Tests (write first — bug-repro-first)

- [ ] T006 [US1] Add failing unit test to `tests/unit/Queues/QueueExecutionServiceTests.cs` reproducing the cold path: with an instance identifier set and a fake `IEnsureEmulatorRunningActionHandler` returning `already_healthy`/`started`, assert the handler is invoked **before** `CreateSession` (ordering) and the session is created. Use the existing test construction pattern + a spy session manager.
- [ ] T007 [US1] Extend `tests/unit/Queues/QueueExecutionServiceTests.cs` with the full outcome matrix: `already_healthy`/`started`/`restarted` → session created + run proceeds; `platform_unsupported`/`control_unavailable` → session created (neutral); `recovery_timed_out`/`instance_not_found` → run ends `Failure` with an actionable reason and **no** `CreateSession` call; fields unset → handler never invoked (no-op).

### Implementation

- [ ] T008 [US1] Inject `IEnsureEmulatorRunningActionHandler? ensureEmulatorRunning = null` as the last constructor parameter of `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (mirror the existing optional `IEnsureGameRunningActionHandler?`), store in a readonly field with an XML-doc comment.
- [ ] T009 [US1] Add a private `async Task<string?> EnsureEmulatorBeforeSessionAsync(ExecutionQueue queue, CancellationToken ct)` helper to `QueueExecutionService.cs`: returns null when no instance identifier is set or the handler is null (no-op); otherwise builds `EnsureEmulatorRunningArgs` from `EmulatorInstanceName`/`EmulatorInstanceIndex` + `queue.EmulatorSerial`, calls the handler, returns null on `IsSuccess`/`IsUnsupported`, and an actionable failure reason string (naming the instance + reason code) on `recovery_timed_out`/`instance_not_found`. CamelCase name, ≤50 LOC.
- [ ] T010 [US1] Wire the helper into `RunAsync` in `QueueExecutionService.cs` immediately before step 2 (`_sessions.CreateSession`): if the helper returns a failure reason, set `reason = QueueStopReason.Failure` and `failureReason` and skip session creation (leave `sessionId` null so the existing `if (sessionId is not null)` guard skips the run); otherwise proceed to `CreateSession` unchanged.
- [ ] T011 [US1] Add a `LoggerMessage` for the cold-start outcome (started/restarted/already-healthy/failed/not-applied) and log it from the helper so the outcome is observable (FR-010); keep it to the existing queue logger.

**Checkpoint**: US1 unit tests pass; cold queue starts the emulator before the session; genuine failure creates no session.

## Phase 4: User Story 2 — Integrated into the existing queue with no separate artifact (P1)

**Goal**: Operators enable the cold-start purely through the queue's existing config surfaces (REST +
web-ui); unset queues are byte-for-byte unchanged.

**Independent test**: Create/update a queue via REST and the web-ui with `emulatorInstanceName` and
confirm it round-trips; a queue with the fields unset performs no emulator work.

### Tests

- [ ] T012 [P] [US2] Add/extend queue contract tests under `tests/contract` (queues): create + update + response round-trip the new `emulatorInstanceName`/`emulatorInstanceIndex`; a negative `emulatorInstanceIndex` is rejected (400); omitting the fields yields null (back-compat).
- [ ] T013 [P] [US2] Add a back-compat test to `tests/unit/Queues/FileQueueRepositoryTests.cs`: a queue JSON without the new fields deserializes with both properties null and re-serializes without data loss.
- [ ] T014 [P] [US2] Add/extend web-ui tests (e.g. `src/web-ui/src/pages/__tests__/QueuesPage.*`) asserting the emulator-instance inputs render and their values are included in the create/update payload.

### Implementation

- [ ] T015 [US2] Map the new fields in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: set them from `CreateQueueRequest`/`UpdateQueueRequest` onto the queue, echo them in the response builder(s) (mirror the `PauseWhenIdle` lines at create/update/response), and reject a negative `EmulatorInstanceIndex` with a 400 (validation helper alongside the existing `CoerceThreshold`).
- [ ] T016 [P] [US2] Add `emulatorInstanceName?: string | null` and `emulatorInstanceIndex?: number | null` to the queue types and create/update payloads in `src/web-ui/src/services/queues.ts` (mirror `pauseWhenIdle`).
- [ ] T017 [US2] Add **Emulator instance name** and **Emulator instance index** inputs to the queue configuration form in `src/web-ui/src/pages/QueuesPage.tsx`, wired to the create/update payload (mirror the idle-pause control placement); optional/blank = unset.

**Checkpoint**: fields round-trip through REST + web-ui + persistence; unset queues unchanged.

## Phase 5: Polish & Cross-Cutting

- [ ] T018 [P] Update `docs/architecture.md`: queue config gains `EmulatorInstanceName`/`EmulatorInstanceIndex`; describe the queue-start pre-session emulator ensure (reuses feature 070); refresh the "Last reviewed" date.
- [ ] T019 [P] Add `074-sessionless-emulator-start` to `specs/STATUS.md` and set `spec.md` **Status** to Implemented once green.
- [ ] T020 Run the full green gate: `dotnet test GameBot.sln`, then in `src/web-ui` `npm run build` and `npm test` — all green (fix any regressions before marking complete).
- [ ] T021 [P] Manual quickstart smoke per [quickstart.md](quickstart.md): close the emulator, start a queue with `emulatorInstanceName` set, confirm it cold-starts the emulator then creates the session (or note if hardware unavailable in this environment).

## Dependencies & Execution Order

- **Phase 1** (T001) → **Phase 2** (T002–T005) → **Phase 3** (US1) → **Phase 4** (US2) → **Phase 5**.
- US1 depends on Foundational (needs the domain field T002). US2 depends on Foundational (contracts) and
  is cleanest after US1's runtime exists, but its plumbing (T015–T017) is independent of US1 internals.
- T002 blocks T009/T010 (uses the new domain fields) and T013.
- T003/T004/T005 block T015 and T012.

## Parallel Opportunities

- T003, T004, T005 (three separate contract files) can run in parallel after T002.
- T012, T013, T014 (contract / repo / web-ui tests) are independent files — parallel.
- T016 (web-ui types) and T018/T019 (docs) are independent — parallel.

## Implementation Strategy

MVP = Phase 2 + Phase 3 (US1): the runtime cold-start with the domain field is the whole value — a
queue can be configured via REST (Phase 4 REST mapping T015) and self-start the emulator. US2's web-ui
form (T017) and the remaining tests/docs complete the increment. Ship US1 first, then US2.
