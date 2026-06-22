# Tasks: Ensure Game Running Primitive Action

**Input**: Design documents from `specs/052-ensure-game-running/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓

**Organization**: Tasks grouped by phase in implementation-dependency order. US3 (game package name) and US2 (queue-game link) are infrastructure prerequisites that must exist before US1 (the action) can be end-to-end tested. They are scheduled before US1 despite US2 being marked P1 in the spec — this is a dependency-driven ordering, not a priority override.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other [P] tasks in the same phase (different files, no conflicts)
- **[Story]**: User story from spec.md (US1/US2/US3)
- All paths relative to repo root

---

## Phase 1: Setup

**Purpose**: Baseline verification before any changes.

- [x] T001 Confirm build is green: run `vite build` in `src/web-ui` and verify no pre-existing errors

---

## Phase 2: Foundational (Domain Model — Blocks All Phases)

**Purpose**: Domain-layer additions shared by all three user stories. No story phase can proceed until this is complete.

**⚠️ CRITICAL**: Complete before moving to any user story phase.

- [x] T002 Add `PackageName` property (`public string? PackageName { get; set; }`) to `GameArtifact` in `src/GameBot.Domain/Games/GameArtifact.cs`
- [x] T003 Add `LinkedGameId` property (`public string? LinkedGameId { get; set; }`) to `ExecutionQueue` in `src/GameBot.Domain/Queues/ExecutionQueue.cs`
- [x] T004 [P] Add `public const string EnsureGameRunning = "ensure-game-running";` to `ActionTypes` in `src/GameBot.Domain/Actions/ActionTypes.cs`
- [x] T005 [P] Add `EnsureGameRunning = "ensure-game-running"` constant to `PrimitiveActionTypes` and include it in the `All` collection in `src/GameBot.Domain/Actions/PrimitiveActionBase.cs`; add `PrimitiveEnsureGameRunningAction` class (no parameters, calls `base(PrimitiveActionTypes.EnsureGameRunning)`) to `src/GameBot.Domain/Actions/PrimitiveActionVariants.cs`
- [x] T006 Add `EnsureGameRunning` to the `CommandStepType` enum in `src/GameBot.Domain/Commands/CommandStep.cs`

**Checkpoint**: Domain model changes complete — all user story phases can begin.

---

## Phase 3: User Story 3 — Configure Game Package Name (Priority: P2)

**Note**: Scheduled before US2 (P1) because the game picker in US2 is more useful once games have package names, and because US1 requires both to be in place. This is a dependency-driven ordering — US2 and US3 are independently testable after Phase 2.

**Goal**: Games have a configurable `packageName` field that operators can set through the UI, providing the Android package identifier used to detect and launch the game.

**Independent Test**: Create a new game, enter `com.example.game` in the Package Name field, save — reload the edit form and confirm the package name is displayed. Edit an existing game and clear the package name — save succeeds without error.

### Implementation

- [x] T007 [P] [US3] Add `public string? PackageName { get; init; }` to `GameResponse` in `src/GameBot.Service/Models/Games.cs`
- [x] T008 [P] [US3] Update `GetGame` and `ListGames` handlers in `src/GameBot.Service/Endpoints/GamesEndpoints.cs` to include `packageName` in all response shapes (both the `GameResponse` path and the anonymous `{ id, name, metadata }` path)
- [x] T009 [US3] Update `CreateGame` and `UpdateGame` handlers in `src/GameBot.Service/Endpoints/GamesEndpoints.cs` to read `packageName` from the request body and persist it to `GameArtifact.PackageName` (depends on T002, T007)
- [x] T010 [P] [US3] Add `packageName?: string` to `GameDto`, `GameCreate`, and `GameUpdate` types in `src/web-ui/src/services/games.ts`
- [x] T011 [US3] Add an optional `packageName` text input (label "Package Name", placeholder `e.g. com.example.game`) to both the create form and the edit form in `src/web-ui/src/pages/GamesPage.tsx`; update the `GameFormValue` type to include `packageName: string`; pass the field value to `createGame` / `updateGame` (depends on T010)

**Checkpoint**: Games can store and display a package name. US3 independently verifiable.

---

## Phase 4: User Story 2 — Link Queue to Game (Priority: P1)

**Goal**: A queue can be associated with a specific game. The operator can link and unlink the game through the queue edit UI, and the association persists across service restarts.

**Independent Test**: Open the queue edit form — a "Game" row is visible below the template controls. Click "Link Game", select a game from the picker, confirm the game name now shows. Click "Unlink" and confirm the row reverts to "(no game)". Restart the service and reload — the link is preserved.

### Implementation

- [x] T012 [P] [US2] Add `public string? LinkedGameId { get; set; }` and `public string? LinkedGameName { get; set; }` to `QueueResponse` in `src/GameBot.Service/Contracts/Queues/QueueResponse.cs`, and add `LinkedGameName` property to `QueueDetailResponse` in `src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs` (`LinkedGameId` is inherited from `QueueResponse`)
- [x] T013 [P] [US2] Create `SetQueueGameLinkRequest` class with `public string? GameId { get; set; }` in `src/GameBot.Service/Contracts/Queues/SetQueueGameLinkRequest.cs`
- [x] T014 [US2] Add `PUT /api/queues/{id}/game` endpoint to `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: validate game exists when `gameId` non-null (inject `IGameRepository`), set `queue.LinkedGameId`, persist, return `QueueDetailResponse` with resolved `LinkedGameName`. Also update `BuildResponse` and `BuildDetailAsync` helpers to populate `LinkedGameId` and `LinkedGameName` from the game store. (depends on T003, T012, T013)
- [x] T015 [P] [US2] Add `linkedGameId: string | null` and `linkedGameName: string | null` to `QueueDto` and `QueueDetailDto` types, and add `export const setQueueGameLink = (id: string, gameId: string | null) => putJson<QueueDetailDto>(...)` to `src/web-ui/src/services/queues.ts`
- [x] T016 [P] [US2] Create `GamePickerDialog` component in `src/web-ui/src/components/queues/GamePickerDialog.tsx`: props `open: boolean`, `onSelect: (gameId: string) => void`, `onClose: () => void`; fetches games via `listGames()`, renders each as a row with a "Select" button; mirrors the `TemplatePickerDialog` structure
- [x] T017 [P] [US2] Create `QueueGameControls` component in `src/web-ui/src/components/queues/QueueGameControls.tsx`: props `linkedGameId: string | null`, `linkedGameName: string | null`, `status: QueueStatus`, `onLink: (gameId: string) => void`, `onUnlink: () => void`; renders game name (or "(no game)"), "Link Game" toggle button that opens `GamePickerDialog`, "Unlink" button disabled when running or unlinked; mirrors `QueueTemplateControls` (depends on T016)
- [x] T018 [US2] Add `gameControls?: React.ReactNode` prop to `QueueFormProps` and `QueueForm` in `src/web-ui/src/components/queues/QueueForm.tsx`; render it in edit mode between `templateControls` and the cycle-execution checkbox (depends on T017)
- [x] T019 [US2] Wire `QueueGameControls` into `QueuesPage` in `src/web-ui/src/pages/QueuesPage.tsx`: read `linkedGameId` and `linkedGameName` from queue detail, pass as the `gameControls` prop on `QueueForm`, call `setQueueGameLink` on link/unlink, refresh local state after success (depends on T014, T015, T017, T018)

**Checkpoint**: Queue-game link is functional end-to-end. US2 independently verifiable.

---

## Phase 5: User Story 1 — Check Game Running Status (Priority: P1) 🎯 Core Value

**Goal**: The `ensure-game-running` primitive action is available in the command step editor, executes within a queue-run sequence, checks whether the queue's linked game is the active foreground app on the emulator, reports success if it is, and starts the game then reports failure if it is not.

**Independent Test**: Create a Command with an `EnsureGameRunning` step; add that Command to a Sequence; run the Sequence from a Queue linked to a game with a configured package name. Observe execution log: when the game is in the foreground the log shows success; when it is not, the log shows failure with reason `game_not_running` and the game is launched.

### Implementation

- [x] T020 [P] [US1] Add `GetForegroundPackageAsync(CancellationToken ct)` and `LaunchAppAsync(string packageName, CancellationToken ct)` methods to `AdbClient` in `src/GameBot.Emulator/Adb/AdbClient.cs`. `GetForegroundPackageAsync` runs `shell dumpsys activity activities` then delegates to an `internal static string? ParseForegroundPackage(string adbOutput)` helper that parses the line containing `mResumedActivity` to extract the package name (the token immediately after `u0 ` and before `/`), returning `null` if the line is absent — keeping the parsing logic in a pure static method enables unit testing without a real ADB process (see T026). `LaunchAppAsync` runs `shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1` (fire-and-forget, result not propagated). Add `LoggerMessage` entries to the existing `Log` static class. Add an XML doc comment to each method documenting inputs, return value, and expected execution time (< 1 second for `GetForegroundPackageAsync` under normal conditions). *(Constitution §IV: hot-path perf note required)*
- [x] T021 [P] [US1] Create `EnsureGameRunningOutcome` enum (values: `GameRunning`, `GameNotRunning`, `NoQueueContext`, `NoLinkedGame`, `NoPackageName`, `PlatformUnsupported`) and `EnsureGameRunningActionResult` sealed record with `Outcome` property, computed `bool IsSuccess` (`Outcome == GameRunning`), and computed `string ReasonCode` (switch expression mapping each outcome to its kebab-case string) in `src/GameBot.Service/Services/EnsureGameRunning/EnsureGameRunningActionResult.cs`
- [x] T022 [P] [US1] Create `IAdbGameOperations` interface in `src/GameBot.Service/Services/EnsureGameRunning/IAdbGameOperations.cs` with two methods: `Task<string?> GetForegroundPackageAsync(string deviceSerial, CancellationToken ct)` and `Task LaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct)`. Create `AdbGameOperations` implementation in the same folder: guards `OperatingSystem.IsWindows()` on each call, delegates to `new AdbClient().WithSerial(deviceSerial)`. Create `IEnsureGameRunningActionHandler` interface with `Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default)` in `src/GameBot.Service/Services/EnsureGameRunning/IEnsureGameRunningActionHandler.cs`
- [x] T023 [US1] Implement `EnsureGameRunningActionHandler` in `src/GameBot.Service/Services/EnsureGameRunning/EnsureGameRunningActionHandler.cs`: inject `ISessionManager`, `IQueueRepository`, `IGameRepository`, and `IAdbGameOperations`; execution order: (1) get session by `sessionId` — if null return `NoQueueContext`; (2) parse `queue:{queueId}` from `session.GameId` — if no `queue:` prefix return `NoQueueContext`; (3) **`if (!OperatingSystem.IsWindows()) return PlatformUnsupported`** — handler owns this outcome explicitly; `AdbGameOperations` also guards it internally for defense in depth, but `null` from `GetForegroundPackageAsync` must not be misread as `GameNotRunning`; (4) load queue — if null or `LinkedGameId` empty return `NoLinkedGame`; (5) load game — if null or `PackageName` empty return `NoPackageName`; (6) call `_adb.GetForegroundPackageAsync(session.DeviceSerial, ct)` — if result matches `game.PackageName` (OrdinalIgnoreCase) return `GameRunning` — **no launch call in this path** (FR-005: running game must not be disturbed); (7) call `_adb.LaunchAppAsync(session.DeviceSerial, game.PackageName, ct)` then return `GameNotRunning`. Injecting `IAdbGameOperations` allows T027 to mock all ADB paths including `GameRunning` and `GameNotRunning` without platform constraints. (depends on T002, T003, T020, T021, T022)
- [x] T024 [US1] Add `EnsureGameRunning` case to the `foreach` loop in `ExecuteCommandRecursiveAsync` in `src/GameBot.Service/Services/CommandExecutor.cs`: if `step.Type == CommandStepType.EnsureGameRunning`, call `_ensureGameRunning is not null ? await _ensureGameRunning.ExecuteAsync(sessionId, ct) : new EnsureGameRunningActionResult(EnsureGameRunningOutcome.PlatformUnsupported)`; if `result.IsSuccess` increment `totalAccepted` by 1 and add `PrimitiveTapStepOutcome(step.Order, "executed", result.ReasonCode, null, null, StepType: "ensure-game-running")`; otherwise add `PrimitiveTapStepOutcome(step.Order, result.ReasonCode, result.ReasonCode, null, null, StepType: "ensure-game-running")`; **end with `continue`** (not `return`) so the loop proceeds to the next step regardless of outcome (FR-012: failure must not abort sequence). Add `private readonly IEnsureGameRunningActionHandler? _ensureGameRunning;` field and update both constructors to accept it as an optional last parameter. (depends on T005, T006, T021, T022, T023)
- [x] T025 [US1] Register `IAdbGameOperations` → `AdbGameOperations` and `IEnsureGameRunningActionHandler` → `EnsureGameRunningActionHandler` in the DI container in `src/GameBot.Service/Program.cs`; update the `CommandExecutor` registration to supply the handler (depends on T022, T023, T024)

### Tests for User Story 1 *(Constitution §II: required for executable logic)*

- [x] T026 [P] [US1] Write unit tests for `AdbClient.GetForegroundPackageAsync` output parsing in `tests/unit/Emulator/AdbClientForegroundPackageTests.cs`: test cases — line containing `mResumedActivity` with `u0 com.example.game/` returns `"com.example.game"`; output with no `mResumedActivity` line returns `null`; multiple lines with exactly one `mResumedActivity` line returns correct package. No ADB process required — test the parsing logic in isolation.
- [x] T027 [P] [US1] Write unit tests for `EnsureGameRunningActionHandler` in `tests/unit/Services/EnsureGameRunning/EnsureGameRunningActionHandlerTests.cs` covering all six outcome paths: (a) `NoQueueContext` when `GetSession` returns null; (b) `NoQueueContext` when `session.GameId` has no `queue:` prefix; (c) `NoLinkedGame` when queue has no `LinkedGameId`; (d) `NoPackageName` when game has no `PackageName`; (e) `GameRunning` when foreground package matches; (f) `GameNotRunning` when foreground package does not match (also verify `LaunchAppAsync` is called). Use mocked `ISessionManager`, `IQueueRepository`, `IGameRepository`, and a mock/stub `AdbClient`-like abstraction as needed.
- [x] T028 [US1] Write an integration test for `CommandExecutor` `EnsureGameRunning` step in `tests/integration/CommandExecutor/EnsureGameRunningStepTests.cs`: (a) step with mock handler returning `GameRunning` → `accepted == 1`, step outcome `Status="executed"`, overall command status `"success"`; (b) step with mock handler returning `GameNotRunning` → `accepted == 0`, step outcome `Status="game_not_running"`, overall command status `"failure"`; (c) step executed after a failure → sequence continues (next step executes), confirming FR-012. (depends on T024)

**Checkpoint**: `ensure-game-running` executes end-to-end in a queue run and is covered by unit + integration tests. US1 fully verifiable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Delete-guard, execution log correctness, and regression check.

- [x] T029 [P] Add queue-reference guard to `DeleteGame` in `src/GameBot.Service/Endpoints/GamesEndpoints.cs`: before deleting, call `IQueueRepository.ListAsync()`, check if any queue has `LinkedGameId == id`; if so return 409 with a references list (FR edge case: linked game must not be deletable while queues reference it; `GamesPage` frontend already handles `ApiError(409)` with `references` property)
- [x] T030 [P] Verify `SequenceExecutionService` in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` does not misclassify `ensure-game-running` step type: confirm the `isWaitForImageStep` check does not accidentally match the new type; the step should appear in the execution log detail as `stepType: "command"` (wrapping command) with the `actionOutcome` propagated from the `PrimitiveTapStepOutcome` outcome field
- [x] T031 Run full build and test suite: `vite build` in `src/web-ui`; `jest` for frontend; `dotnet test` from repo root (covers `tests/unit/`, `tests/integration/`, `tests/contract/`) — all must pass before marking the feature complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (T001)**: No dependencies
- **Phase 2 (T002–T006)**: Must complete before any story phase; T004/T005/T006 parallelisable after T002–T003
- **Phase 3 / US3 (T007–T011)**: Depends on Phase 2; independently testable
- **Phase 4 / US2 (T012–T019)**: Depends on Phase 2; independently testable
- **Phase 5 / US1 (T020–T028)**: Depends on Phase 2; requires US2 + US3 for full integration testing
- **Phase 6 (T029–T031)**: Depends on all story phases

### User Story Dependencies

- **US3**: Foundational only
- **US2**: Foundational only
- **US1**: Foundational required; US2 + US3 required for end-to-end testing

### Parallel Opportunities

**Phase 2** (after T002–T003):
```
T004  ActionTypes.cs
T005  PrimitiveActionBase.cs + Variants
T006  CommandStep.cs
```

**Phase 3** (start in parallel):
```
T007  GameResponse model
T008  GET endpoint responses
T010  games.ts types
```

**Phase 4** (start in parallel):
```
T012  QueueResponse/QueueDetailResponse
T013  SetQueueGameLinkRequest
T015  queues.ts types + setQueueGameLink
T016  GamePickerDialog component
T017  QueueGameControls component (needs T016)
```

**Phase 5** (start in parallel):
```
T020  AdbClient new methods
T021  EnsureGameRunningActionResult types
T022  IEnsureGameRunningActionHandler interface
```

**Phase 5 tests** (start in parallel after T020–T023):
```
T026  AdbClient parsing unit tests
T027  EnsureGameRunningActionHandler unit tests
```

---

## Implementation Strategy

### MVP Path (all phases required for testable end-to-end)

1. Phase 2: Foundation
2. Phase 3: US3 — game has a package name
3. Phase 4: US2 — queue can be linked to a game
4. Phase 5: US1 — action checks and optionally starts the game + tests
5. **VALIDATE**: run a sequence with `ensure-game-running` in a queue with a linked game; run `dotnet test`
6. Phase 6: Polish

### Incremental Delivery

1. Foundation → US3 → *verify: package name saves and loads*
2. → US2 → *verify: queue-game link persists across reload*
3. → US1 implementation → *verify: execution log shows correct outcome*
4. → US1 tests → *verify: `dotnet test` green*
5. → Polish → *verify: delete guard, no regressions*

---

## Notes

- `[P]` tasks operate on different files — no edit conflicts when run concurrently
- `[Story]` labels map tasks to spec.md user stories for traceability
- The Windows platform guard is encapsulated in `AdbGameOperations` (T022) — `EnsureGameRunningActionHandler` no longer constructs `AdbClient` directly, so platform concerns do not leak into the handler
- `CommandExecutor` has two constructors — both must accept the new optional `IEnsureGameRunningActionHandler?` as the last parameter (T024)
- The session label convention `queue:{queueId}` (set in `QueueExecutionService.cs:118`) is the only coupling between the action and queue context — no interface changes needed in the execution chain
- `QueueDetailResponse.LinkedGameName` requires injecting `IGameRepository` into `BuildDetailAsync` in `QueuesEndpoints.cs` (T014)
- Constitution §II requires tests for all executable logic — T026, T027, T028 are mandatory, not optional

