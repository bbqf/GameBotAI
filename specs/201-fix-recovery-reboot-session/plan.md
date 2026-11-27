# Implementation Plan: Keep the queue Running and bind a new session after a recovery reboot (B-047)

**Branch**: `201-fix-recovery-reboot-session` | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/201-fix-recovery-reboot-session/spec.md`

## Summary

A recovery attempt (`QueueDeviceRecoveryRunner`) calls `QueueExecutionService.RebindSessionAsync`. That method stops the old session first and binds a new one after. During a reboot, ADB does not list the device, so the bind throws. The old session is then gone, and `handle.SessionId` keeps its ID. Before the next firing, the run loop (`EnsureSessionBound`) tries one bind, the bind throws again, and the loop ends the run with `QueueConnectionLostException` ("emulator connection lost mid-run"). The liveness watch does nothing while the run has no session, so no second attempt starts and no "recovery failed" alert is sent.

The fix has five parts:

1. **One shared bind method.** A new `QueueExecutionService.TryBindLostSession(queue, handle, out error)` is the only place that binds a session again for a lost session. It holds a new lock on the run handle (`QueueRunHandle.SessionBindLock`), so the run loop and the watch never bind two sessions (FR-011). It returns the bound session ID, or null when the bind fails or when a recovery attempt runs (the runner owns the rebind then). On a failure it sets `handle.SessionId` to null and logs (FR-010a).
2. **The run loop holds in place of a stop.** `EnsureSessionBound` becomes `TryEnsureSessionBound()` (returns `bool`). When the bind fails and the queue has recovery on (`handle.DeviceRecoveryReboots`), it returns false. When recovery is off, it throws `QueueConnectionLostException` as today (FR-008). The liveness gate (`HoldIfNotLiveAsync`) calls it first. When it gives false, the gate observes a hard `not_live` report with the reason `transport_not_ready` (`NoSessionReport`) and holds the firing with the feature 106 rules (FR-002, FR-002a, FR-004). The every-step pass and the before-each-run pass stop when no session is bound.
3. **The rebind of the runner does not leave a dead ID.** `RebindSessionAsync` sets `handle.SessionId` to null before it binds. So a failed bind leaves a clear "no session" state, and the attempt counts as a failed attempt (FR-005).
4. **The watch works with no session.** `QueueLivenessWatch.CheckOnceAsync` calls a new `tryBind` callback (it calls `TryBindLostSession`) when the session is null or gone. When the bind fails and recovery is on, the watch observes `NoSessionReport` and continues with alerts, the recovery claim and the fault cycle (FR-003, FR-004, FR-005). The watch also writes the current stored `deviceRecovery.Reboots` value to `handle.DeviceRecoveryReboots` at each refresh (feature 199 rule).
5. **The health read works with no session.** `DeviceLivenessReader.Project` returns the snapshot of the episode also when `handle.SessionId` is null or the session is gone, so `GET /api/queues/{id}` shows `health.deviceLiveness` (FR-006).

## Technical Context

**Language/Version**: C# (`LangVersion` preview) on .NET 9 (`net9.0`, service and domain)  
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, xUnit, FluentAssertions  
**Storage**: No change. The run handle is in memory. The queue store and its file format do not change.  
**Testing**: xUnit unit tests (`tests/unit/Queues`), integration tests with `WebApplicationFactory<Program>` and the device recovery test host (`tests/integration/DeviceRecoveryTestSupport.cs`)  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: Web service with a web UI. The web UI does not change.  
**Performance Goals**: No measurable change. A bind try runs at most one time for each liveness check (`QueueCheckIntervalMs`, default 1 s or more) and for each firing that the loop wants to start, only while no session is bound. The normal path adds one null check and one `GetSession` look-up, as today.  
**Constraints**: No change of the API schema, of the `deviceRecovery` schema, of the not-live rules, or of the manual start (FR-009). A queue with recovery off keeps its current end of the run (FR-008).  
**Scale/Scope**: 5 production files (`QueueExecutionService.cs`, `QueueRunHandle.cs`, `QueueLivenessWatch.cs`, `DeviceLivenessReader.cs`, `QueueDeviceRecoveryRunner.cs` comment only), tests, `docs/architecture.md`, `CHANGELOG.md`, spec status files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: When a `build` or a required `test` run fails (local or CI), do not continue the implementation. Fix the failures first, or get a maintainer waiver with a written rationale.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. The web UI shows the `status` and the `health` that the service gives. No phone support statement is necessary.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | PASS | New methods stay under 50 lines. Method names use CamelCase. New log messages use `LoggerMessage`. |
| II. Testing | PASS | Tests that fail before the fix come first (bug fix rule): a unit test of the watch with no session, a unit test of the run loop with a lost session and recovery on (`QueueLivenessGateTests`), and an integration test of the full reboot flow with a device that is not listed for some time. A test for recovery off keeps the current end. The current tests `ConnectionLostMidRunFailsTheRun`, `ReboundFailsOnCapacityFailsTheRun` and `QueueExecutionServiceRebindTests` stay as guards. Coverage of the changed files: ≥80% line and ≥70% branch. |
| III. UX consistency | PASS | No new API field. The current reason `transport_not_ready` and `recoveryState` tell the operator why no firing runs. Log messages tell the cause and the next action. |
| IV. Performance | PASS | No hot path change. Perf note: the bind try runs only while no session is bound, at most once for each check interval and firing. |
| V. Living documentation | PASS | Update `docs/architecture.md` (device recovery paragraph, "Last reviewed"), spec 121 status `Implemented (iterated by 199, 201)`, `specs/STATUS.md`. |
| VI. STE | PASS | All new text in STE. |
| Version bump | PASS | Phase 2 raises `patch` before the push (constitution "Version bump"). |

## Project Structure

### Documentation (this feature)

```text
specs/201-fix-recovery-reboot-session/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-lost-session.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs     # TryBindLostSession, TryEnsureSessionBound, gate change, RebindSessionAsync clears the ID, NoSessionReport, new LoggerMessages
src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs            # SessionBindLock, DeviceRecoveryReboots, LostSessionWarned flag
src/GameBot.Service/Services/QueueExecution/QueueLivenessWatch.cs        # tryBind callback, no-session path, writes DeviceRecoveryReboots
src/GameBot.Service/Services/QueueExecution/DeviceLivenessReader.cs      # Project works with no session
src/GameBot.Service/Services/QueueExecution/QueueDeviceRecoveryRunner.cs # class comment: a failed rebind leaves no session; the queue stays Running
tests/unit/Queues/QueueLivenessWatchTests.cs                              # no session: bind try, NoSessionReport, recovery claim, recovery failed alert
tests/unit/Queues/DeviceLivenessReaderRecoveryStateTests.cs               # Project with no session gives the snapshot
tests/unit/Queues/QueueLostSessionTests.cs (new)                          # TryBindLostSession: one session, skip while recovery runs, log once
tests/unit/Queues/QueueLivenessGateTests.cs                               # run loop: recovery on, the bind fails, the firing is held, the run continues after a bind
tests/unit/Queues/QueueExecutionServiceRebindTests.cs                     # RebindSessionAsync: a failed bind leaves handle.SessionId null
tests/integration/DeviceRecoveryLostSessionTests.cs (new)                 # full flow: reboot removes the session, device not listed, queue stays Running, binds, runs held firing; exhausted case; recovery off case
tests/integration/DeviceRecoveryTestSupport.cs                            # SerialSessionManager/SimulatedDevices: a serial that is not listed for some time
docs/architecture.md                                                      # device recovery paragraph, Last reviewed
specs/121-device-not-live-alert-recovery/spec.md, specs/STATUS.md         # status lines: 121 "Implemented (iterated by 199, 201)"; new row 201
CHANGELOG.md, installer/versioning/version.override.json                 # changelog entry, version bump
```

**Structure Decision**: The current single service project and test projects. No new project. One new unit test file and one new integration test file.

## Phase 0 and Phase 1 outputs

- [research.md](research.md): R-001 to R-007 (cause, scope of the change, shared bind, the reason value, the watch with no session, the health read, logs).
- [data-model.md](data-model.md): the new fields of the run handle and the "no session" state.
- [contracts/queue-lost-session.md](contracts/queue-lost-session.md): the observable behaviour of `GET /api/queues/{id}` and `GET /api/sessions/running` while the queue has no session.
- [quickstart.md](quickstart.md): how to check the fix.

## Constitution Check (after design)

All gates stay PASS. The design changes no API shape, no stored format, and no UI.

## Complexity Tracking

No violations.
