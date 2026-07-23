# Implementation Plan: Idle-Pause the Game During Queue Gaps

**Branch**: `073-queue-idle-pause` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/073-queue-idle-pause/spec.md`

## Summary

When a running queue has no sequence due and the wait until the next scheduled firing exceeds a
per-queue threshold (default 30s), the queue runtime backs the game out to the device home screen and
surfaces a transient "Idle Pause" state in the live monitor (with a resume time). When the next firing
becomes due — or an earlier live/self-reschedule arrives — the runtime brings the game back to the
foreground and runs the due sequence normally. The pause is driven by the scheduler itself (not a
scheduled sequence), so it is exempt from the 4-minute per-sequence watchdog and writes nothing to the
execution log. Opt-in per queue via two new persisted fields on the queue entity.

Technical approach: reuse the primitive device operations already available to the service
(`ISessionManager.SendInputsAsync` for HOME; `IEnsureGameRunningActionHandler.ExecuteAsync` for
foreground) directly from `QueueExecutionService`'s non-cyclic wait branch — no sequence, no watchdog,
no logging. Add an idle-pause state register to `QueueRunHandle`; project it as the monitor's current
item. Thread two new config fields (`PauseWhenIdle`, `IdleThresholdSeconds`) through the standard queue
config path (domain → repository JSON → API contracts → mcp-server → web-ui), mirroring `CycleExecution`.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service, GameBot.Domain); TypeScript (web-ui React, mcp-server)
**Primary Dependencies**: ASP.NET minimal APIs, System.Text.Json; React + Jest/RTL; Node mcp-server
**Storage**: JSON file persistence for queue config (`FileQueueRepository`, serializes `ExecutionQueue`); runtime run state is in-memory (`QueueRunHandle`), never persisted
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`); Jest + React Testing Library (web-ui); `dotnet test`; web-ui green gate = `vite build` + `jest`
**Target Platform**: Windows host driving an Android emulator via ADB
**Project Type**: Web application — .NET backend service + React web-ui + TypeScript mcp-server
**Performance Goals**: Idle-pause reuses the existing 250 ms non-cyclic poll cadence (`RelativeTimerPollInterval`); no new hot path. The runtime must *decide to resume* within ~one poll interval of a firing's due time (SC-002); the subsequent game foreground/relaunch time is additional and expected. The pause loop performs only cheap wall-clock comparisons per tick.
**Constraints**: Single serial run loop (one sequence at a time); 4-min `SequenceWatchdogTimeout` applies ONLY to firings routed through `RunOneSequenceAsync` — the idle-pause branch must sit outside it (FR-006); a stop request (`ct`) must abort a pause within one tick (FR-012/SC-006); backgrounding/foregrounding are best-effort/non-fatal (FR-011)
**Scale/Scope**: One production queue opts in today; the setting is general. ~17 template entries; gaps range seconds→tens of minutes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS (planned). Changes are cohesive and localized (one runtime branch + one handle register + a monitor projection arm + standard config plumbing). Methods stay under ~50 LOC (idle-pause helper is a small loop). No dead code; the superseded template entry is removed (FR-016). Public members get XML docs. **CamelCase method names only — no underscores.**
- **II. Testing Standards**: PASS (planned). Unit tests for every idle-pause branch (enter/skip/resume/earlier-firing/stop/failure-nonfatal) using the injectable `TimeProvider` and fakes for `ISessionManager`/`IEnsureGameRunningActionHandler`; monitor projection tests; contract tests for the new config round-trip + backward-compat default. Targets the ≥80% line / ≥70% branch baseline on touched code. Bug-repro-first for the watchdog-exemption case.
- **III. UX Consistency**: PASS (planned). New config surfaced consistently across API (create/update/response), mcp-server tool schema, and web-ui, mirroring `cycleExecution`. Monitor shows an explicit, human-readable idle-pause item. Error/edge behavior is non-fatal and documented.
- **IV. Performance**: PASS. No new polling cadence; pause loop is O(pending firings) cheap comparisons per existing tick. Perf note: idle-pausing REDUCES device/game activity during gaps. No hot-path regression.
- **V. Living Documentation**: PASS (planned). `docs/architecture.md` updated (queue config gains idle-pause fields; runtime idle-pause behavior; monitor idle-pause state; "Last reviewed" refreshed). `spec.md` Status set to Implemented on completion; `specs/STATUS.md` updated; feature 069 (go-to-home-screen) and 072 (monitor) referenced, not superseded.

**Initial gate**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/073-queue-idle-pause/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── queue-idle-pause-config.md
├── checklists/
│   └── requirements.md  # from /speckit-specify
└── tasks.md             # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
└── Queues/
    ├── ExecutionQueue.cs            # + PauseWhenIdle, IdleThresholdSeconds (persisted config)
    └── FileQueueRepository.cs       # JSON round-trip (property initializers give back-compat defaults)

src/GameBot.Service/
├── Services/QueueExecution/
│   ├── QueueExecutionService.cs     # idle-pause branch in the non-cyclic wait; inject IEnsureGameRunningActionHandler
│   ├── QueueRunHandle.cs            # + idle-pause state register (IdlePausedUntil + set/clear, monitor-readable)
│   ├── QueueMonitorSnapshot.cs      # + ScheduleKind.IdlePause
│   └── QueueMonitorService.cs       # BuildCurrent → synthetic Idle Pause item when paused
├── Contracts/Queues/
│   ├── CreateQueueRequest.cs        # + pauseWhenIdle, idleThresholdSeconds
│   ├── UpdateQueueRequest.cs        # + pauseWhenIdle, idleThresholdSeconds
│   └── QueueResponse.cs             # + pauseWhenIdle, idleThresholdSeconds
└── Endpoints/QueuesEndpoints.cs     # map new fields on create/update/response

src/mcp-server/src/tools/queues.ts   # expose new fields in create/update/get
src/web-ui/src/services/queues.ts    # types for new fields
src/web-ui/src/pages/QueuesPage.tsx  # toggle + threshold input; monitor idle-pause rendering (QueueMonitor component)

tests/
├── unit/Queues/QueueExecutionServiceTests.cs      # idle-pause behavior matrix
├── unit/Queues/QueueMonitorServiceTests.cs        # idle-pause current-item projection
├── unit/Queues/FileQueueRepositoryTests.cs        # back-compat defaults for new fields
├── contract/                                      # queue config round-trip (new fields)
└── (web-ui) QueueMonitor.test.tsx / QueuesPage.*  # render idle-pause + config control

docs/architecture.md                 # living-docs update
specs/STATUS.md                       # add 073
```

**Structure Decision**: Existing web-application layout (backend service + web-ui + mcp-server). The
feature threads two config fields through the established queue-config path (identical to
`CycleExecution`) and adds a self-contained idle-pause branch to the run loop plus a monitor projection
arm. No new projects, services, or persistence stores.

## Phase 0 — Research

See [research.md](research.md). Key resolved decisions:

1. **How to background/foreground from the runtime without a sequence** → call the same device
   operations the sequence dispatcher uses: `ISessionManager.SendInputsAsync` with Android
   `KEYCODE_HOME` (3) for background; `IEnsureGameRunningActionHandler.ExecuteAsync(sessionId)` for
   foreground. `QueueExecutionService` already holds `ISessionManager`; inject
   `IEnsureGameRunningActionHandler`. This bypasses `RunOneSequenceAsync` → no watchdog, no log entries.
2. **Where the idle-pause branch lives** → in the non-cyclic wait tail of `RunAsync`, replacing the
   unconditional `await Task.Delay(RelativeTimerPollInterval, ct)` with: compute next-due; if enabled
   and gap > threshold, run the idle-pause hold; else delay one tick as today.
3. **Next-due computation** → a runtime-local helper folding the same sources the loop already fires
   from (time-of-day timers via next-eligible, unfired relative offsets, pending self-reschedule timer
   firings, live schedules, and any queued once-per-run/next-cycle work = due-now). Documented as a
   candidate for future consolidation with the monitor's upcoming projection; kept separate now to
   avoid coupling scheduling to the projection.
4. **Config storage & back-compat** → new properties on `ExecutionQueue` with initializers
   (`PauseWhenIdle = false`, `IdleThresholdSeconds = 30`); System.Text.Json leaves initializer values
   intact for fields absent from existing JSON, so stored queues upgrade cleanly.
5. **Monitor surfacing** → new `ScheduleKind.IdlePause`; `BuildCurrent` returns a synthetic item
   (no real sequence) when the handle is idle-paused, carrying the resume time in `ExpectedAt`.

## Phase 1 — Design & Contracts

- **Data model**: [data-model.md](data-model.md) — the two persisted config fields, the transient
  run-scoped idle-pause register, and the next-due value object.
- **Contracts**: [contracts/queue-idle-pause-config.md](contracts/queue-idle-pause-config.md) — the
  queue create/update/response wire additions and the monitor `scheduleKind: "IdlePause"` item shape.
- **Quickstart**: [quickstart.md](quickstart.md) — enabling idle-pause on the live queue and verifying
  it, plus removing the superseded "PNS Queue Pause 15m" template entry.
- **Agent context**: update the `<!-- SPECKIT ... -->` plan reference in `CLAUDE.md` to this plan.

**Post-design Constitution re-check**: PASS. Design stays within existing patterns; no new violations;
Complexity Tracking not required.
