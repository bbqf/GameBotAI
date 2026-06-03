# Research: Ensure Game Running Primitive Action

## Execution Architecture

**Decision**: Implement as a new `CommandStepType.EnsureGameRunning` handled in `CommandExecutor`.

**Rationale**: The user will create a Command containing this step, then reference that command from a Sequence. The `CommandExecutor.ExecuteCommandRecursiveAsync` dispatches based on `step.Type`, with existing cases for `PrimitiveTap` and `WaitForImage`. Adding a third case is the established pattern. Inline sequence actions (`SequenceActionPayload`) are an alternative path but would bypass the command logging that the user expects.

**Alternatives considered**: (a) Inline `SequenceActionPayload` step — skipped because the user explicitly said they will create a command from this action; (b) dedicated service class per action — skipped as over-engineering for a single action type.

---

## Queue Context Resolution

**Decision**: Extract queue ID from the session label `queue:{queueId}` stored in `EmulatorSession.GameId`.

**Rationale**: `QueueExecutionService.RunAsync` creates sessions with label `$"queue:{queue.Id}"` (line 118 of `QueueExecutionService.cs`). `EmulatorSession.GameId` holds this label. Since `CommandExecutor` already has `ISessionManager`, it can get the session and parse the label without any new interface changes. For direct (non-queue) execution, the label won't have the `queue:` prefix → action fails with `no_queue_context` (FR-008a).

**Alternatives considered**: Thread a `QueueId` through `ExecutionLogContext` — valid but requires touching multiple call sites and the context model; deferred since session-label parsing achieves the same result with zero interface changes.

---

## ADB Foreground Detection

**Decision**: Use `adb shell dumpsys activity activities | grep mResumedActivity`.

**Rationale**: Outputs a line like `mResumedActivity: ActivityRecord{... u0 com.example.game/...}`. Parse the package name from the segment after `u0 ` and before `/`. Works on Android 5+ without root. More reliable than `mFocusedApp` from window dumps which can return the launcher overlay.

**Launch command**: `adb shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1`. Fire-and-forget (we don't wait for the app to appear — the action has already reported failure by this point).

**Alternatives considered**: `dumpsys window | grep mFocusedApp` — can return system overlays; `am start` — requires knowing the activity name. `monkey` is the most compatible launcher across API levels.

---

## Service Design for the Handler

**Decision**: Create `IEnsureGameRunningActionHandler` interface + `EnsureGameRunningActionHandler` implementation in `GameBot.Service/Services/EnsureGameRunning/`. Inject optionally into `CommandExecutor` (like `IExecutionLogService`).

**Rationale**: Keeps `CommandExecutor` focused on orchestration, not on ADB details or game context resolution. The handler is the only place that needs `IQueueRepository`, `IGameRepository`, and ADB calls. Optional injection means `CommandExecutor` constructor signature change is backward-compatible with existing tests.

---

## Data Model Changes

**`GameArtifact`**: Add `public string? PackageName { get; set; }`. `FileGameRepository` serializes directly to JSON — new field round-trips transparently; existing files without the field deserialize with `null`.

**`ExecutionQueue`**: Add `public string? LinkedGameId { get; set; }` — mirrors existing `LinkedTemplateId`. Same optional 0..1 relationship. Same file-based storage round-trip behavior.

**Queue delete guard**: When a queue references a game via `LinkedGameId`, the game delete endpoint must reject with 409 (the `GamesPage` frontend already handles `ApiError(409)` for delete, checking `err.references`).

---

## Frontend Pattern

**Decision**: Follow the `QueueTemplateControls` + `TemplatePickerDialog` pattern exactly.

**Rationale**: `QueueForm.tsx` uses a `templateControls?: React.ReactNode` slot prop. Adding a parallel `gameControls?: React.ReactNode` slot keeps the form extensible and the change minimal. The game picker is simpler than the template picker (no save/reload operations — only link and unlink).

**New components**: `QueueGameControls.tsx` (link/unlink button + picker toggle) and `GamePickerDialog.tsx` (list of games with a select action).

---

## API Contracts

**Games endpoints**: `PackageName` is added to `GameArtifact` and flows through `GamesEndpoints.cs`. The endpoint uses manual `JsonDocument` parsing — add `packageName` property read/write to both `POST` and `PUT` handlers. `GameResponse` in `Models/Games.cs` gets a `PackageName` property; frontend `GameDto` gets `packageName?: string`.

**Queue game link**: New `PUT /api/queues/{id}/game` endpoint (mirrors `PUT /api/queues/{id}/template`). `SetQueueGameLinkRequest` with `GameId: string?`. `QueueResponse` and `QueueDetailResponse` get `LinkedGameId: string?`. Frontend `queues.ts` gets `linkedGameId: string | null` on `QueueDto` / `QueueDetailDto` and a `setQueueGameLink(id, gameId)` function.

---

## Performance Goals (Constitution IV)

- ADB foreground check: target < 1 second under normal emulator conditions (single `dumpsys` call).
- ADB launch: fire-and-forget, no wait time added to the action's execution.
- No regression on existing sequence/command execution paths — the `EnsureGameRunning` case is an additive branch in `ExecuteCommandRecursiveAsync`.
