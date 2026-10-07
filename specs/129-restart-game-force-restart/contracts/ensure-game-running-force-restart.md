# Contract: `forceRestart` on the ensure-game-running action

There is no new endpoint. The option is in three existing places.

## 1. Sequence action step

`POST /api/sequences`, `PUT /api/sequences/{id}`, `PATCH /api/sequences/{id}` (steps, loop bodies, if branches).

```json
{
  "stepId": "restart",
  "stepType": "Action",
  "action": { "type": "ensure-game-running", "parameters": { "forceRestart": true } }
}
```

| Input | Result |
|-------|--------|
| `forceRestart` absent or `false` | Accepted. Same behaviour as before. |
| `forceRestart: true` | Accepted. Read back unchanged. |
| `forceRestart` is `"true"`, `1`, `null`, `{}`, `[]`, or `"{{x}}"` | 400. Message: `Step '<label>' ensure-game-running action: forceRestart must be true or false.` |

The step ends in one of these results.

| Reason code | Step result |
|-------------|-------------|
| `restarted` | Success. Action outcome `restarted`. Condition state `restarted` (and `success`). |
| `restart_stop_failed` | Failed. The game was not started. |
| `restart_start_failed` | Failed. |
| `restart_foreground_timeout` | Failed. |
| `restart_no_device` | Failed. The session has no device serial. No stop is tried. |
| `no_queue_context`, `no_linked_game`, `no_package_name`, `platform_unsupported` | Failed, as today. No stop is tried. A package name with unsafe characters gives `no_package_name`. |

Each device call has its own time limit: stop 10 s, start 10 s, one foreground probe 5 s. The foreground wait is 30 s.
The longest run is 51 s. The stop command uses the device serial of the session only.

If a `forceRestart` value that is not a boolean reaches run time (for example through a binding or a hand-edited
file), the step fails with the message `ensure-game-running forceRestart must be true or false`. It does not throw and
it makes no device call.

The typed variant `PrimitiveEnsureGameRunningAction` has the same optional boolean, `forceRestart`. The value survives
the stored JSON round trip: save, read back, run.

## 2. Command step

`POST /api/commands`, `PUT /api/commands/{id}`, `POST /api/steps/test`.

```json
{ "type": "EnsureGameRunning", "ensureGameRunning": { "forceRestart": true }, "order": 1 }
```

| Input | Result |
|-------|--------|
| `forceRestart` absent or `false` | Accepted. Same behaviour as before. |
| `true` | Accepted. Read back as `true`. Kept even when there is no `readinessImage`. |
| Not a JSON boolean | 400 (body bind error). |

Step outcome on success: `status: executed`, `reason: restarted`. On failure: `status` and `reason` are the reason code.

## 3. Condition on a later step

```json
{ "type": "commandOutcome", "stepRef": "restart", "expectedState": "restarted" }
```

`expectedState` allowed values: `success`, `failed`, `skipped`, `break`, `no_break`, `restarted`. Another value gives 400.
A step that restarted also matches `success`. A step that found the game already running does not match `restarted`.

This reading is for a sequence action step. For a command step, a sequence condition sees `success`. The state
`restarted` shows only in the command outcome list of that step (`reason: restarted`).

## OpenAPI

- The text of `PrimitiveAction.payload` for `ensure-game-running` names `forceRestart` (boolean, optional, default
  false) and the reason codes.
- `EnsureGameRunningConfig` has `forceRestart` as an optional boolean.
- The `expectedState` text lists `restarted`.

## Step-through

A step with `forceRestart: true` is previewed and not run. History text: `would force-stop the game and start it again`.
A step without the option runs as before.
