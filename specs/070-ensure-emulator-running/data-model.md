# Phase 1 Data Model: Ensure Emulator Running Action

This feature is additive: one new action type, its parameters, one command-step config, and one
execution-outcome enum. No persisted schema migration is required (new enum members + new optional
parameters round-trip through the existing JSON serializers).

## Action type key

- `ActionTypes.EnsureEmulatorRunning = "ensure-emulator-running"`
- `PrimitiveActionTypes.EnsureEmulatorRunning = "ensure-emulator-running"` (added to
  `PrimitiveActionTypes.All`, which auto-covers the three `All`-derived validators)

## Authoring parameters (author-supplied)

| Field           | Type    | Required | Rule                                                                 |
|-----------------|---------|----------|----------------------------------------------------------------------|
| `instanceName`  | string  | one-of   | LDPlayer instance name for `ldconsole --name`.                       |
| `instanceIndex` | integer | one-of   | LDPlayer instance index for `ldconsole --index`; MUST be ≥ 0.        |
| `adbSerial`     | string  | yes      | Device serial (e.g. `emulator-5558`) used for the responsiveness probe. |

**Validation** (`PrimitiveActionValidationService` new case, mirrored in `CommandsEndpoints.ValidateStep`):
- `adbSerial` MUST be non-empty.
- At least one of `instanceName` / `instanceIndex` MUST be supplied; supplying neither is an error.
- If both are supplied, `instanceName` takes precedence (documented; not an error).
- `instanceIndex`, when present, MUST be ≥ 0.

These live as:
- `PrimitiveEnsureEmulatorRunningAction` (variant) with `InstanceName?`, `InstanceIndex?`, `AdbSerial`.
- `SequenceActionPayload.Parameters` keys `instanceName` / `instanceIndex` / `adbSerial` (sequence path).
- `EnsureEmulatorRunningConfig` on `CommandStep` (command-step path): `InstanceName?`, `InstanceIndex?`, `AdbSerial`.
- `EnsureEmulatorRunningArgs` (strongly-typed, mirrors `ConnectToGameArgs`) with `TryFrom` overloads
  for both the variant and an `InputAction`/Parameters dictionary.

## Emulator Instance (external entity, not persisted)

Represents the LDPlayer instance being watched. Observable states:

| State                | Detected by                                                        | Remediation |
|----------------------|-------------------------------------------------------------------|-------------|
| Not running          | `ldconsole isrunning` → not running                               | `launch`    |
| Running & responsive | isrunning=running AND serial state `device` AND boot_completed=1  | none (no-op)|
| Running but hung     | isrunning=running AND (serial offline/absent OR boot_completed≠1 within probe timeout) | `reboot` |
| Nonexistent instance | `ldconsole` reports the name/index does not exist                | fail step   |

## Execution outcome (`EnsureEmulatorRunningOutcome`)

Mirrors `EnsureGameRunningOutcome`. Enum members and their step mapping:

| Outcome              | Meaning                                                         | Step result |
|----------------------|----------------------------------------------------------------|-------------|
| `AlreadyHealthy`     | Running and responsive on first check; nothing done            | success     |
| `Started`            | Was stopped; launched and reached boot-complete within wait    | success     |
| `Restarted`          | Was hung; rebooted and reached boot-complete within wait       | success     |
| `RecoveryTimedOut`   | (Re)started but never became healthy within `EmulatorBootWaitMs` | failed    |
| `InstanceNotFound`   | Well-formed identifier matches no instance (FR-014)            | failed      |
| `PlatformUnsupported`| Non-Windows host                                               | neutral/no-op success (unsupported) |
| `ControlUnavailable` | `ldconsole` not found or ADB unavailable                       | neutral/no-op success (unsupported) |

> Neutral outcomes (`PlatformUnsupported`, `ControlUnavailable`) mirror `ensure-game-running`: the
> step does not crash the run and reports a neutral "not-applied" result rather than a hard failure.
> The step-level mapping (which outcomes surface as `executed` vs `failed`) matches how
> `DispatchEnsureGameRunningAsync` maps its result.

## Configuration (`AppConfig`, new fields)

| Field                    | Default | Env override                        | Rule            |
|--------------------------|---------|-------------------------------------|-----------------|
| `EmulatorProbeTimeoutMs` | 10000   | `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS` | clamped ≥ 0/min |
| `EmulatorBootWaitMs`     | 120000  | `GAMEBOT_EMULATOR_BOOT_WAIT_MS`     | clamped ≥ probe |
| `EmulatorPollIntervalMs` | 3000    | `GAMEBOT_EMULATOR_POLL_INTERVAL_MS` | clamped ≥ 100   |

Invalid / non-numeric env values fall back to the default (same convention as `GAMEBOT_ADB_*`).

## State transitions (handler)

```
check → AlreadyHealthy ────────────────────────────► success
      → NotRunning → launch → poll ─┬─ healthy ────► Started (success)
                                    └─ timeout ────► RecoveryTimedOut (failed)
      → Hung       → reboot → poll ─┬─ healthy ────► Restarted (success)
                                    └─ timeout ────► RecoveryTimedOut (failed)
      → InstanceNotFound ──────────────────────────► failed
non-Windows / no ldconsole / no adb ───────────────► neutral no-op (unsupported)
```
