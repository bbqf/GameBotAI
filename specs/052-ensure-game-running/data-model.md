# Data Model: Ensure Game Running

## Modified Entities

### GameArtifact (GameBot.Domain/Games/GameArtifact.cs)
Adds `PackageName` — the Android package identifier used to detect and launch the game.

| Field | Type | Required | Notes |
|---|---|---|---|
| Id | string | yes | stable GUID |
| Name | string | yes | display name |
| Description | string? | no | existing |
| PackageName | string? | no | NEW — e.g. `com.example.game` |

Storage: `data/games/{id}.json` — `FileGameRepository` serializes all public properties. Existing files without `packageName` deserialize with `null`.

### ExecutionQueue (GameBot.Domain/Queues/ExecutionQueue.cs)
Adds `LinkedGameId` — optional 0..1 reference to a `GameArtifact`.

| Field | Type | Required | Notes |
|---|---|---|---|
| Id | string | yes | |
| Name | string | yes | |
| EmulatorSerial | string | yes | |
| CycleExecution | bool | yes | |
| LinkedTemplateId | string? | no | existing |
| LinkedGameId | string? | no | NEW |

Storage: `data/queues/{id}.json` — same round-trip behavior.

## New Entities

### EnsureGameRunningActionResult (GameBot.Service/Services/EnsureGameRunning/)
In-memory result record; not persisted.

| Field | Type | Notes |
|---|---|---|
| Outcome | EnsureGameRunningOutcome | enum |
| IsSuccess | bool | computed: Outcome == GameRunning |
| ReasonCode | string | computed: see plan.md |

### EnsureGameRunningOutcome (enum)
- `GameRunning` — success, game was already the active foreground app
- `GameNotRunning` — failure, game was not running (launch attempted)
- `NoQueueContext` — failure, not running in a queue
- `NoLinkedGame` — failure, queue has no linked game
- `NoPackageName` — failure, linked game has no package name
- `PlatformUnsupported` — failure, ADB not available (non-Windows)

## Relationships

```
ExecutionQueue ──LinkedGameId──> GameArtifact (0..1)
ExecutionQueue ──LinkedTemplateId──> QueueTemplate (0..1, existing)
```

## Validation Rules

- `GameArtifact.PackageName` is optional; no format validation at persistence layer (per spec assumption).
- `ExecutionQueue.LinkedGameId` set/cleared only via `PUT /api/queues/{id}/game`; the endpoint validates that the referenced game exists.
- `GameArtifact` delete is blocked (409) if any `ExecutionQueue.LinkedGameId` references the game.
