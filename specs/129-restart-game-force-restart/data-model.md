# Data Model: Restart the Game From a Sequence

## Ensure-game-running action (sequence action step)

Stored in `SequenceActionPayload` (type `ensure-game-running`).

| Field | Type | Required | Default | Rule |
|-------|------|----------|---------|------|
| `parameters.forceRestart` | boolean | No | false | JSON `true` or `false` only. Text, number, null, object, array, and `{{name}}` text give 400. |

A stored step without the field reads as false (FR-013).

After a file read, the value is a `JsonElement`. The reader accepts a `JsonElement` of kind `True` or `False` and a CLR
`bool`. At run time, the dispatcher uses the same reader. A value that is not a boolean fails the step with the message
`ensure-game-running forceRestart must be true or false`. The step does not throw and makes no device call.

## Typed variant (`PrimitiveEnsureGameRunningAction`)

File: `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs`.

| Field | Type | Required | Default | Rule |
|-------|------|----------|---------|------|
| `ForceRestart` | `bool?` | No | null (read as false) | A text value is rejected by `System.Text.Json`. The value survives serialize and deserialize (FR-016). |

`ToActionPayload()` writes `parameters.forceRestart` only when the value is true or false. `TryFromActionPayload(...)`
reads it back with the shared reader.

## Ensure-game-running config (command step)

`EnsureGameRunningConfig` (domain) and `EnsureGameRunningConfigDto` (API).

| Field | Type | Required | Default | Rule |
|-------|------|----------|---------|------|
| `readinessImage` | detection target | No | none | Unchanged. |
| `readinessTimeoutMs` | integer | No | 90000 | Unchanged. |
| `forceRestart` | boolean | No | false | Not written to storage when false. A non-boolean JSON value gives 400. No `FieldTemplates` key. |

## Handler result (`EnsureGameRunningActionResult`)

| Outcome | Reason code | Success | When |
|---------|-------------|---------|------|
| `GameRunning` | `game_running` | Yes | Unchanged. |
| `GameNotRunning` | `game_not_running` | No | Unchanged. |
| `NoQueueContext` | `no_queue_context` | No | Unchanged. |
| `NoLinkedGame` | `no_linked_game` | No | Unchanged. |
| `NoPackageName` | `no_package_name` | No | Unchanged. Also for a package name with bad characters (restart only). |
| `PlatformUnsupported` | `platform_unsupported` | No | Unchanged. |
| `Restarted` (new) | `restarted` | Yes | Stop and start worked and the game is in the foreground. |
| `RestartNoDevice` (new) | `restart_no_device` | No | The session has no device serial. No ADB call is made (FR-017). |
| `RestartStopFailed` (new) | `restart_stop_failed` | No | The force-stop gave a non-zero exit code, a fault, or no answer in 10 s. The game was not started. |
| `RestartStartFailed` (new) | `restart_start_failed` | No | The start gave a non-zero exit code, a fault, or no answer in 10 s. |
| `RestartForegroundTimeout` (new) | `restart_foreground_timeout` | No | The game was not in the foreground after 30 s. |

## Restart state transitions

```text
resolve ──fault──> no_queue_context | platform_unsupported | no_linked_game | no_package_name   (no ADB call)
   │ ok
check serial ──blank──> restart_no_device       (no ADB call)
   │ ok
force-stop (10 s limit) ──fail or timeout──> restart_stop_failed        (no start)
   │ ok
settle 1 s
   │
start (10 s limit) ──fail or timeout──> restart_start_failed
   │ ok
poll foreground (1 s step, 5 s limit per probe, 30 s max) ──timeout──> restart_foreground_timeout
   │ match
restarted
```

The longest run is 10 + 1 + 10 + 30 = 51 s. A cancel from the caller ends the run at once and is not a reason code.

## Step outcome states (`commandOutcome.expectedState`)

Allowed values: `success`, `failed`, `skipped`, `break`, `no_break`, `restarted` (new).

| Written by the runner | Matches `expectedState` |
|-----------------------|-------------------------|
| `success` | `success` |
| `restarted` (action step that restarted) | `restarted` and `success` |

The execution log status for the outcome `restarted` is `success`.

For a command step, the runner of the sequence sees one unit. It writes `success`. The reason `restarted` shows only in
the command outcome list.

## Dispatch result of the action step

| Case | `ActionDispatchResult.Outcome` | Message |
|------|-------------------------------|---------|
| Already in the foreground, no option | `executed` | `game is running in the foreground (game_running)` (unchanged) |
| Restart done | `restarted` | `game restarted and is in the foreground (restarted)` |
| Any failure | `failed` | `ensure-game-running restart failed: <reason code>` |
| `forceRestart` is not a boolean at run time | `failed` | `ensure-game-running forceRestart must be true or false` (no throw, no device call) |
