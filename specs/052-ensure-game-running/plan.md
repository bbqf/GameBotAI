# Implementation Plan: Ensure Game Running Primitive Action

**Branch**: `052-ensure-game-running` | **Date**: 2026-06-03 | **Spec**: [spec.md](spec.md)

## Summary

Add a zero-config `ensure-game-running` primitive action that checks whether the linked game is the active foreground app on the emulator. Reports success if it is; starts the game and reports failure if it is not. Requires adding `PackageName` to `GameArtifact` and `LinkedGameId` to `ExecutionQueue`.

## Technical Context

**Language/Version**: C# / .NET 9 (backend); TypeScript / React 18 (frontend)
**Primary Dependencies**: Minimal API (ASP.NET Core), React, Vite, Jest
**Storage**: File-based JSON (`data/games/`, `data/queues/`)
**Testing**: Jest (frontend), xUnit (backend)
**Target Platform**: Windows (ADB operations are Windows-only; non-Windows returns platform-unavailable outcome)
**Project Type**: Desktop app — web UI + local service
**Performance Goals**: ADB foreground check completes in < 1 second; app launch is fire-and-forget with no added wait time
**Constraints**: Non-Windows execution must degrade gracefully (return platform-unavailable step outcome, not throw); optional injection of handler keeps `CommandExecutor` tests unaffected

## Constitution Check

- [x] Lint/format must pass — no new warnings; CamelCase method names throughout
- [x] Tests required — unit tests for `EnsureGameRunningActionHandler` covering all FR outcomes; ADB method parsing tests; frontend component tests for `QueueGameControls`
- [x] UX consistency — error messages actionable; game picker follows template picker pattern
- [x] Performance declared — see Technical Context above
- [x] Public-surface changes documented (API contracts, see Phase 1)
- [x] No pre-existing build/test failures blocking this work (clean baseline)

## Project Structure

### Documentation (this feature)

```text
specs/052-ensure-game-running/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code

```text
src/
├── GameBot.Domain/
│   ├── Actions/
│   │   ├── ActionTypes.cs               ← add EnsureGameRunning constant
│   │   ├── PrimitiveActionBase.cs       ← add to PrimitiveActionTypes.All
│   │   └── PrimitiveActionVariants.cs   ← add PrimitiveEnsureGameRunningAction
│   ├── Commands/
│   │   └── CommandStep.cs               ← add CommandStepType.EnsureGameRunning
│   ├── Games/
│   │   └── GameArtifact.cs              ← add PackageName property
│   └── Queues/
│       └── ExecutionQueue.cs            ← add LinkedGameId property
│
├── GameBot.Emulator/
│   └── Adb/
│       └── AdbClient.cs                 ← add GetForegroundPackageAsync, LaunchAppAsync
│
└── GameBot.Service/
    ├── Endpoints/
    │   ├── GamesEndpoints.cs            ← read/write packageName; guard delete with queue refs
    │   └── QueuesEndpoints.cs           ← add PUT /{id}/game endpoint
    ├── Contracts/
    │   └── Queues/
    │       ├── QueueResponse.cs         ← add LinkedGameId
    │       └── SetQueueGameLinkRequest.cs  ← new
    ├── Models/
    │   └── Games.cs                     ← add PackageName to GameResponse
    └── Services/
        ├── CommandExecutor.cs           ← add EnsureGameRunning step branch
        └── EnsureGameRunning/           ← new folder
            ├── IEnsureGameRunningActionHandler.cs
            ├── EnsureGameRunningActionHandler.cs
            └── EnsureGameRunningActionResult.cs

web-ui/src/
├── services/
│   ├── games.ts          ← add packageName to GameDto / GameCreate / GameUpdate
│   └── queues.ts         ← add linkedGameId, setQueueGameLink
├── components/
│   └── queues/
│       ├── QueueForm.tsx            ← add gameControls?: ReactNode slot
│       ├── QueueGameControls.tsx    ← new (mirrors QueueTemplateControls)
│       └── GamePickerDialog.tsx     ← new (mirrors TemplatePickerDialog)
└── pages/
    ├── GamesPage.tsx     ← add packageName field to create/edit forms
    └── QueuesPage.tsx    ← wire game controls
```

## Phase 0: Research

Research is complete. See [research.md](research.md) for decisions and rationale.

**All NEEDS CLARIFICATION items resolved:**
- "Game running" = active foreground app, checked via `adb shell dumpsys activity activities | grep mResumedActivity`
- Single failure reason (`game_not_running`) regardless of launch outcome
- Zero-config action; direct execution without queue context → `no_queue_context` failure

## Phase 1: Design & Contracts

### Data Model

Full details in [data-model.md](data-model.md).

**`GameArtifact` (GameBot.Domain)**
```csharp
public sealed class GameArtifact {
  public required string Id { get; set; }
  public required string Name { get; set; }
  public string? Description { get; set; }
  public string? PackageName { get; set; }          // NEW — Android package identifier
}
```

**`ExecutionQueue` (GameBot.Domain)**
```csharp
public class ExecutionQueue {
  // ...existing fields...
  public string? LinkedGameId { get; set; }         // NEW — optional game reference (0..1)
}
```

**`ActionTypes` / `PrimitiveActionTypes` (GameBot.Domain)**
```csharp
public const string EnsureGameRunning = "ensure-game-running";
// Added to PrimitiveActionTypes.All collection
```

**`PrimitiveEnsureGameRunningAction` (GameBot.Domain)**
```csharp
public sealed class PrimitiveEnsureGameRunningAction : PrimitiveActionBase {
  public PrimitiveEnsureGameRunningAction() : base(PrimitiveActionTypes.EnsureGameRunning) { }
}
```

**`CommandStepType` (GameBot.Domain)**
```csharp
public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning    // NEW
}
```

**`EnsureGameRunningActionResult` (GameBot.Service)**
```csharp
public enum EnsureGameRunningOutcome {
  GameRunning,         // Success: game was already in foreground
  GameNotRunning,      // Failure: game was not running (launch attempted)
  NoQueueContext,      // Failure: action not running in a queue
  NoLinkedGame,        // Failure: queue has no linked game
  NoPackageName,       // Failure: linked game has no package name
  PlatformUnsupported  // Failure: ADB not available (non-Windows)
}

public sealed record EnsureGameRunningActionResult(EnsureGameRunningOutcome Outcome) {
  public bool IsSuccess => Outcome == EnsureGameRunningOutcome.GameRunning;
  public string ReasonCode => Outcome switch {
    EnsureGameRunningOutcome.GameRunning        => "game_running",
    EnsureGameRunningOutcome.GameNotRunning     => "game_not_running",
    EnsureGameRunningOutcome.NoQueueContext     => "no_queue_context",
    EnsureGameRunningOutcome.NoLinkedGame       => "no_linked_game",
    EnsureGameRunningOutcome.NoPackageName      => "no_package_name",
    _                                           => "platform_unsupported"
  };
}
```

**`IEnsureGameRunningActionHandler` (GameBot.Service)**
```csharp
internal interface IEnsureGameRunningActionHandler {
  Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default);
}
```

**`EnsureGameRunningActionHandler` logic (GameBot.Service)**
1. Get session from `ISessionManager.GetSession(sessionId)` → if null: `NoQueueContext`
2. Parse `queue:{queueId}` from `session.GameId` → if no prefix: `NoQueueContext`
3. `await _queues.GetAsync(queueId)` → if null or `LinkedGameId` empty: `NoLinkedGame`
4. `await _games.GetAsync(queue.LinkedGameId)` → if null or `PackageName` empty: `NoPackageName`
5. If `!OperatingSystem.IsWindows()` → `PlatformUnsupported`
6. `adb.GetForegroundPackageAsync(session.DeviceSerial)` → compare to `game.PackageName`
7. Match → `GameRunning`; no match → `adb.LaunchAppAsync(packageName)` then `GameNotRunning`

**`CommandExecutor` changes**
```csharp
// New case in ExecuteCommandRecursiveAsync foreach loop:
if (step.Type == CommandStepType.EnsureGameRunning) {
  var result = _ensureGameRunning is not null
    ? await _ensureGameRunning.ExecuteAsync(sessionId, ct).ConfigureAwait(false)
    : new EnsureGameRunningActionResult(EnsureGameRunningOutcome.PlatformUnsupported);

  if (result.IsSuccess) {
    totalAccepted++;
    stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "executed", result.ReasonCode, null, null, StepType: "ensure-game-running"));
  } else {
    stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, result.ReasonCode, result.ReasonCode, null, null, StepType: "ensure-game-running"));
  }
  continue;
}
```

**`AdbClient` new methods (GameBot.Emulator)**
```
GetForegroundPackageAsync(CancellationToken ct):
  runs: shell dumpsys activity activities
  parses: line containing "mResumedActivity" → extract package before "/"
  returns: package name string or null

LaunchAppAsync(string packageName, CancellationToken ct):
  runs: shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1
  fire-and-forget (result not inspected by caller)
```

### API Contracts

**Games API**

`GET /api/games` and `GET /api/games/{id}` — add `packageName` to response shape.

`POST /api/games` and `PUT /api/games/{id}` — accept optional `packageName` field; persist to `GameArtifact.PackageName`.

`DELETE /api/games/{id}` — check if any `ExecutionQueue.LinkedGameId` references this game; return 409 with references if so.

Frontend: `GameDto.packageName?: string`, `GameCreate.packageName?: string`, `GameUpdate.packageName?: string`.

**Queues API (new endpoint)**

`PUT /api/queues/{id}/game`
- Body: `{ "gameId": "string | null" }`
- Validates: if `gameId` non-null, game must exist
- Sets `queue.LinkedGameId`; persists; returns `QueueDetailResponse`
- Pattern: identical to `PUT /api/queues/{id}/template`

`QueueResponse` / `QueueDetailResponse` — add `LinkedGameId: string?` (surfaced as `linkedGameId` in frontend).

Frontend: `QueueDto.linkedGameId: string | null`, `QueueDetailDto.linkedGameId: string | null`, new `setQueueGameLink(queueId, gameId | null)` function.

### Frontend Components

**`QueueGameControls` props**
```typescript
type QueueGameControlsProps = {
  linkedGameId: string | null;
  linkedGameName: string | null;   // resolved name, null if unlinked
  status: QueueStatus;
  onLink: (gameId: string) => void;
  onUnlink: () => void;
};
```
Renders: linked game name (or "(no game)"), "Link Game" toggle button, "Unlink" button (only when linked and queue stopped).

**`GamePickerDialog` props**
```typescript
type GamePickerDialogProps = {
  open: boolean;
  onSelect: (gameId: string) => void;
  onClose: () => void;
};
```
Renders: list of games fetched from API; "Select" button per row.

**`QueueForm` changes**
Add `gameControls?: React.ReactNode` slot — rendered in edit mode between `templateControls` and the cycle-execution checkbox.

**`QueuesPage` wiring**
- Fetch `linkedGameId` and resolve game name from `listGames()` (or `getGame()`)
- Render `QueueGameControls` as the `gameControls` slot
- Call `setQueueGameLink(queueId, gameId)` on link/unlink actions

**`GamesPage` changes**
Add `packageName` text field to both create and edit forms (optional, placeholder: "e.g. com.example.game").

## Complexity Tracking

No constitution violations. This is a straightforward additive feature: new domain fields, new endpoint, new service, new frontend components.

## Agent Context

Plan complete. Update `CLAUDE.md` to reference this plan.
