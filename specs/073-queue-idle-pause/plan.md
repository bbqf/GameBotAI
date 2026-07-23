# Implementation Plan: Idle-Pause the Game During Queue Gaps; Retire the MCP Server

**Branch**: `073-queue-idle-pause` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/073-queue-idle-pause/spec.md`

## Summary

This branch delivers two independent changes:

1. **Idle-pause (User Stories 1–4)** — When a running queue has no sequence due and the wait until the
   next scheduled firing exceeds a per-queue idle-detection threshold (default 30s, configurable), the
   queue runtime backs the game out to the device home screen and surfaces a transient "Idle Pause"
   state in the live monitor (with a resume time). The game stays backed out for the whole remaining
   gap; when the next firing becomes due — or an earlier live/self-reschedule arrives — the runtime
   foregrounds the game and runs the due sequence normally. The pause is driven by the scheduler itself
   (not a scheduled sequence), so it is exempt from the 4-minute per-sequence watchdog and writes
   nothing to the execution log. Opt-in per queue via two new persisted fields on the queue entity,
   exposed through the **REST API and web-ui only**.

2. **MCP server retirement (User Story 5, independent)** — Delete the project's own MCP server
   (`src/mcp-server`) in its entirety, remove the root `.mcp.json` registration, and clean the single
   incidental documentation reference, so the build and tests stay green with no project MCP server.
   The MCP was a thin client over the REST API; removing it removes an access channel, not capability.

Technical approach (idle-pause): reuse the primitive device operations already available to the service
(`ISessionManager.SendInputsAsync` for HOME; `IEnsureGameRunningActionHandler.ExecuteAsync` for
foreground) directly from `QueueExecutionService`'s non-cyclic wait branch — no sequence, no watchdog,
no logging. Add an idle-pause state register to `QueueRunHandle`; project it as the monitor's current
item. Thread two new config fields (`PauseWhenIdle`, `IdleThresholdSeconds`) through the queue config
path (domain → repository JSON → API contracts → web-ui), mirroring `CycleExecution` **but stopping at
the REST/web-ui boundary — the MCP layer is being deleted in the same branch**.

Technical approach (MCP removal): a pure deletion — remove `src/mcp-server/`, remove `.mcp.json`, and
update the one incidental mention in `docs/architecture.md`. No .NET/solution/CI wiring references the
Node MCP server, so build and CI are unaffected.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service, GameBot.Domain); TypeScript (web-ui React). The Node
`src/mcp-server` TypeScript tool is being removed by this plan.
**Primary Dependencies**: ASP.NET minimal APIs, System.Text.Json; React + Jest/RTL
**Storage**: JSON file persistence for queue config (`FileQueueRepository`, serializes `ExecutionQueue`); runtime run state is in-memory (`QueueRunHandle`), never persisted
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`); Jest + React Testing Library (web-ui); `dotnet test`; web-ui green gate = `vite build` + `jest`
**Target Platform**: Windows host driving an Android emulator via ADB
**Project Type**: Web application — .NET backend service + React web-ui (after this plan; the TypeScript mcp-server is removed)
**Performance Goals**: Idle-pause reuses the existing 250 ms non-cyclic poll cadence (`RelativeTimerPollInterval`); no new hot path. The runtime must *decide to resume* within ~one poll interval of a firing's due time (SC-002); the subsequent game foreground/relaunch time is additional and expected. The pause loop performs only cheap wall-clock comparisons per tick. MCP removal has no runtime-performance dimension.
**Constraints**: Single serial run loop (one sequence at a time); 4-min `SequenceWatchdogTimeout` applies ONLY to firings routed through `RunOneSequenceAsync` — the idle-pause branch must sit outside it (FR-006); a stop request (`ct`) must abort a pause within one tick (FR-012/SC-006); backgrounding/foregrounding are best-effort/non-fatal (FR-011). MCP removal MUST keep the build/test gate green (SC-008) and must not touch external MCP references (FR-022).
**Scale/Scope**: One production queue opts in to idle-pause today; the setting is general. ~17 template entries; gaps range seconds→tens of minutes. MCP removal touches one directory + one root file + one doc line.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS (planned). Idle-pause changes are cohesive and localized (one runtime branch + one handle register + a monitor projection arm + standard config plumbing). Methods stay under ~50 LOC. No dead code; the superseded template entry is removed (FR-016). MCP removal *reduces* surface area and removes an unused-by-the-app component. Public members get XML docs. **CamelCase method names only — no underscores.**
- **II. Testing Standards**: PASS (planned). Unit tests for every idle-pause branch (enter/skip/resume/earlier-firing/stop/failure-nonfatal) using the injectable `TimeProvider` and fakes for `ISessionManager`/`IEnsureGameRunningActionHandler`; monitor projection tests; contract tests for the new config round-trip + backward-compat default. Targets the ≥80% line / ≥70% branch baseline on touched code. Bug-repro-first for the watchdog-exemption case. MCP removal is verified by a green full build + test run (SC-008) and a repo-wide reference scan (SC-009); it deletes code and its own tests, so no coverage regression on retained code.
- **III. UX Consistency**: PASS (planned). New config surfaced consistently across the API (create/update/response) and web-ui, mirroring `cycleExecution`. Monitor shows an explicit, human-readable idle-pause item. Removing the MCP tool surface is a deliberate, documented interface retirement (no partial/broken tool schemas left behind).
- **IV. Performance**: PASS. No new polling cadence; pause loop is O(pending firings) cheap comparisons per existing tick. Idle-pausing REDUCES device/game activity during gaps. MCP removal is performance-neutral. No hot-path regression.
- **V. Living Documentation**: PASS (planned). `docs/architecture.md` updated for BOTH changes (queue config gains idle-pause fields; runtime idle-pause behavior; monitor idle-pause state; the incidental `MCP start_session` mention removed; "Last reviewed" refreshed). `spec.md` Status set to Implemented on completion; `specs/STATUS.md` updated; features 069 (go-to-home-screen) and 072 (monitor) referenced, not superseded. Prior specs that referenced `src/mcp-server` tool files (069/070/071) are point-in-time history and are NOT retro-edited (per constitution: specs are immutable history), but their Status lines remain accurate.

**Initial gate**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/073-queue-idle-pause/
├── plan.md              # This file
├── research.md          # Phase 0 output (idle-pause decisions + MCP-removal decision)
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (enable idle-pause; retire old pause; remove MCP)
├── contracts/           # Phase 1 output
│   └── queue-idle-pause-config.md
├── checklists/
│   └── requirements.md  # from /speckit-specify
└── tasks.md             # /speckit-tasks output (regenerated after this plan)
```

### Source Code (repository root)

```text
# ── Idle-pause ──────────────────────────────────────────────────────────────
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

src/web-ui/src/services/queues.ts    # types for new fields
src/web-ui/src/pages/QueuesPage.tsx   # toggle + threshold input; monitor idle-pause rendering (QueueMonitor component)

# ── MCP removal (deletions) ─────────────────────────────────────────────────
src/mcp-server/                       # DELETE entire directory (tracked files via git rm; ignored node_modules/dist via Remove-Item)
.mcp.json                             # DELETE (root MCP registration)

tests/
├── unit/Queues/QueueExecutionServiceTests.cs      # idle-pause behavior matrix
├── unit/Queues/QueueMonitorServiceTests.cs        # idle-pause current-item projection
├── unit/Queues/FileQueueRepositoryTests.cs        # back-compat defaults for new fields
├── contract/                                      # queue config round-trip (new fields)
└── (web-ui) QueueMonitor.test.tsx / QueuesPage.*  # render idle-pause + config control

docs/architecture.md                 # living-docs update: remove incidental "MCP start_session" mention (~L72) + add idle-pause fields/behavior/monitor state
specs/STATUS.md                       # add 073
```

**Structure Decision**: Existing web-application layout (backend service + web-ui). The idle-pause
feature threads two config fields through the established queue-config path (identical to
`CycleExecution`) up to the REST/web-ui boundary, and adds a self-contained idle-pause branch to the
run loop plus a monitor projection arm. The MCP removal deletes the `src/mcp-server` project and its
root registration; no .NET/solution/CI target builds it, so the build graph is unaffected. No new
projects, services, or persistence stores.

## Complexity Tracking

Not required — Constitution Check passed with no violations.
