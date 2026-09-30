# Implementation Plan: Device Not-Live Alert, Optional Recovery, and Capture Pile-Up Guard

**Branch**: `121-device-not-live-alert-recovery` | **Date**: 2026-09-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/121-device-not-live-alert-recovery/spec.md`. Closes GitHub issue #261.

## Summary

The queue already reports `health.deviceLiveness` (spec 106, issue #220) and holds firings while the device is `not_live`. It never tells the operator and never repairs the device. This feature adds three parts.

1. **Alert (P1)**: After `AlertAfterMs` in one not-live episode, the service sends one "device not live" message to every enabled notification target. It sends one "device live again" message at the end. The alert needs no failure policy and ignores `notificationLevel`.
2. **Recovery (P2, P3)**: An optional queue field `deviceRecovery` turns on an instance reboot with `ldconsole.exe reboot --name`. One recovery slot for the service starts one reboot at a time, at least `RecoveryStaggerMs` apart. After the reboot, the runner waits for the device, rebinds the session, and checks for `live`. After `maxAttempts` failures, it sends one "recovery failed" alert.
3. **Capture guard (P3)**: A per-device capture gate keeps at most one unfinished capture. A timed-out capture puts the device in state `Suspect`. The service starts no new capture until the first one ends or the device is repaired.

The detection rules of `DeviceLivenessEvaluator` do not change (FR-015). `health.deviceLiveness` gains the read-only members `alertSent`, `recoveryAttempts`, and `recoveryState` (`idle`, `running`, `exhausted`) (FR-017). The work also updates `docs\architecture.md`, `specs\STATUS.md`, and the Status line of spec 106 (FR-018).

## Technical Context

**Language/Version**: C# 12 on .NET 8 (existing solution `GameBot.sln`)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (schema filters), existing `LdConsoleClient`, `AdbClient`, and the feature 120 notification worker. No new package.
**Storage**: Queue JSON file store. One nullable member `deviceRecovery` on the queue. The episode, the recovery slot, and the capture gate are memory only.
**Testing**: xUnit unit tests (`tests\unit`), contract tests (`tests\contract`), integration tests (`tests\integration`). Fake `TimeProvider`, fake `IEmulatorControl`, and a fake capture provider.
**Target Platform**: Windows service host with LDPlayer 9 and `adb`.
**Project Type**: Web service (REST API and background services).
**Performance Goals**: Alert within `AlertAfterMs` plus 1 minute (SC-001). Blind device: 2 or fewer `screencap` processes over 10 minutes (SC-004). Healthy device: no change in capture rate (SC-005). The added work in the liveness watch is one lock and one time compare per check.
**Constraints**: The liveness watch checks at most once a minute (it uses `QueueCheckIntervalMs`, 30 s). The alert time minimum is 1 second. `Program.cs` stays thin. All new service registrations go in `GameBotServiceSetup.cs` in one task.
**Scale/Scope**: About 4 queues and 4 emulator instances on one host. One recovery at a time for the whole service.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | New types are small and cohesive: episode claims, coordinator, runner, capture gate, validator. The runner and the settings record have different names (R-017). Public members get comments with inputs, outputs, and errors. |
| II. Testing | Pass | Each story has test tasks that fail first. Tests cover the episode claims, the coordinator, the watch, the worker, the validator, the gate, and the contract and integration paths. Fakes keep tests fast and deterministic. |
| III. UX consistency | Pass | Validation errors are HTTP 400 with actionable text (FR-010). OpenAPI descriptions change in the same work (FR-016). Alert text follows the feature 120 format. |
| IV. Performance | Pass | Goals are in Technical Context. The gate adds one dictionary lookup for each capture. |
| V. Living documentation | Pass | FR-018 and the docs task: refresh `docs\architecture.md` and its "Last reviewed" date, set spec 121 to Implemented in `specs\STATUS.md`, and point the Status line of spec 106 to spec 121. |
| VI. STE | Pass | All artifacts are written in STE. The analyze step checks it. |

Post-design re-check: no violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/121-device-not-live-alert-recovery/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (manual checks for SC-002, SC-003, SC-004)
├── contracts/
│   └── device-recovery-api.md   # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks - not made by this command)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Queues/
│   │   ├── QueueDeviceRecovery.cs            # new: settings record
│   │   ├── QueueDeviceRecoveryValidator.cs   # new: range and cross-field rules
│   │   └── ExecutionQueue.cs                 # add nullable DeviceRecovery
│   └── Sessions/DeviceLivenessOptions.cs     # add AlertAfterMs, RecoveryStaggerMs, RebootReadyTimeoutMs
├── GameBot.Emulator/
│   ├── DeviceCaptureGate.cs                  # new: per-serial capture state
│   └── Adb/AdbClient.cs                      # add HasRunningScreencapAsync
└── GameBot.Service/
    ├── GameBotServiceSetup.cs                # ONE task registers all new services
    ├── Contracts/Queues/                     # deviceRecovery and three health members
    ├── Endpoints/                            # queue create, update, duplicate: validate and map
    ├── Swagger/                              # schema filters (DeviceLiveness, QueueHealth)
    └── Services/
        ├── EnsureEmulatorRunning/            # IEmulatorControl.RebootAsync returns bool
        ├── Notifications/                    # QueueAlert, SendAlert, formatter, worker
        └── QueueExecution/
            ├── QueueLivenessEpisode.cs       # alert, recovery flags, claims
            ├── QueueLivenessWatch.cs         # alert and recovery trigger
            ├── IDeviceRecoveryCoordinator.cs # new: before the runner
            ├── DeviceRecoveryCoordinator.cs  # new: recovery slot and stagger
            ├── QueueDeviceRecoveryRunner.cs  # new: per-queue recovery steps
            └── QueueExecutionService.cs      # rebind session, gate uses handle session
tests/
├── unit/        # options, episode, coordinator, validator, worker, watch, gate
├── contract/    # queue field, 400 cases, health members
└── integration/ # alert flow, recovery flow
docs/architecture.md
specs/STATUS.md
specs/106-wedged-device-liveness/spec.md      # Status line only
```

**Structure Decision**: Keep the existing three-project layout (Domain, Emulator, Service). Domain holds the persisted settings and the validator. Emulator holds the capture gate, next to the capture loop. Service holds the coordinator, the runner, the watch, and the notification changes. The coordinator interface and the coordinator are built before the runner, so no recovery runs without the slot.

## Design Decisions (summary)

The full decisions and alternatives are in [research.md](research.md).

- Alert runs in `QueueLivenessWatch.CheckOnceAsync` after `Observe`. Episode claims make each message one time only (R-001, R-002).
- `SendAlert` on the notification dispatcher uses the same channel and never drops an item (R-003).
- Alert time, stagger time, and reboot wait are service settings (R-004, R-005).
- Recovery slot: one semaphore and `lastStartedAt` in `DeviceRecoveryCoordinator` (R-006). SC-003 is met when the gap between two reboot start times is `RecoveryStaggerMs` or more (R-021).
- `recoveryState` is derived from the episode: `running` when `recoveryRunning` is true, else `exhausted` when `attempts` reached `maxAttempts` and the device is not live, else `idle` (R-022).
- Capture gate with `Suspect` state, released by a device-side `pidof screencap` check or by a session rebind (R-012, R-013, R-014).
- Living documents are updated in one docs task (R-016).

## Delivery Order

1. Settings, episode claims, `RebootAsync` result, settings record and validator, coordinator interface and coordinator.
2. All new service registrations in `GameBotServiceSetup.cs`, in ONE task.
3. User story 1: alert path and health members.
4. User story 2 and 3: runner, rebind, validation routes, one-at-a-time tests (verification only).
5. User story 4: capture gate.
6. Polish: OpenAPI text, `docs\architecture.md` (refresh "Last reviewed"), `specs\STATUS.md`, Status line of `specs\106-wedged-device-liveness\spec.md`, full test run.

## Complexity Tracking

No violation. This table is empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | - | - |
