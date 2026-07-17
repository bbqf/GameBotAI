# Phase 1 Data Model: Connect-to-Game Emulator Pre-heal

Additive only: two optional fields on the connect-to-game action. No new entities, no schema
migration (they ride the existing `SequenceActionPayload.Parameters` dictionary and JSON serializers).

## Connect-to-Game action parameters (extended)

| Field           | Type    | Required | Rule                                                        |
|-----------------|---------|----------|-------------------------------------------------------------|
| `gameId`        | string  | yes      | Unchanged.                                                  |
| `adbSerial`     | string  | yes      | Unchanged; also reused as the emulator responsiveness probe serial when pre-heal runs. |
| `instanceName`  | string  | **no**   | LDPlayer instance name for the optional pre-heal.           |
| `instanceIndex` | integer | **no**   | LDPlayer instance index for the optional pre-heal; ≥ 0 when supplied. |

Carriers:
- `PrimitiveConnectToGameAction` — add `InstanceName?`, `InstanceIndex?`.
- `ConnectToGameArgs` — add `InstanceName?`, `InstanceIndex?`; populated by both `TryFrom` overloads.
- `SequenceActionPayload.Parameters` — keys `instanceName` / `instanceIndex` (already free-form dict).

**Validation** (`PrimitiveActionValidationService`, connect-to-game case):
- `gameId` required (unchanged), `adbSerial` required (unchanged).
- `instanceName` / `instanceIndex` optional.
- If `instanceIndex` is supplied it MUST be ≥ 0.

## Pre-heal decision (in `DispatchConnectToGameAsync`)

```
parse gameId + adbSerial (unchanged; missing → failed as today)
if EnsureEmulatorRunningArgs.TryFrom(parameters) succeeds:      // instance id present
    emu = ensureEmulatorRunning.ExecuteAsync(args)
    if not (emu.IsSuccess or emu.IsUnsupported):                // RecoveryTimedOut / InstanceNotFound
        return failed("emulator pre-heal failed: <reason>")     // do NOT StartSession
    preheatNote = emu.ReasonCode
else:
    preheatNote = none                                          // unchanged behavior
StartSession(gameId, adbSerial)                                 // as today
launch = ensureGameRunning.ExecuteAsync(session)               // as today (best-effort)
return executed("connected …; [emulator: <preheatNote>; ] game launch: <launch.ReasonCode>")
```

## Reused feature-070 outcomes (no change)

`EnsureEmulatorRunningOutcome` and its `IsSuccess` / `IsUnsupported` / `ReasonCode` are consumed
as-is: AlreadyHealthy / Started / Restarted / (PlatformUnsupported / ControlUnavailable) ⇒ proceed;
RecoveryTimedOut / InstanceNotFound ⇒ fail-fast.
