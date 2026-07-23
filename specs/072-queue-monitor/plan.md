# Implementation Plan: Live Queue Monitor View

**Branch**: `072-queue-monitor` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/072-queue-monitor/spec.md`

## Summary

When a queue is **running**, opening it in the web UI shows a read-only **monitor** ("playlist")
instead of the entry editor: the sequence running **now**, the ordered **up-next** list, and — for
each item — why it is scheduled and when it is expected to run. The view auto-refreshes on a short
fixed interval (~2–3s). When a queue is **stopped**, opening it shows the existing editor unchanged.

The running queue already holds all the state needed (`QueueRunHandle`: partitioned entries via its
linked template, `PendingLiveSchedules`, self-reschedule timer firings, cycle flag, run-start anchor)
— but none of it is exposed. This feature adds (1) minimal **current-sequence tracking** to the run
handle, (2) a pure **projection** that folds the run handle + linked template + current wall-clock
into an ordered monitor snapshot (now + upcoming, with best-effort expected times and repeat markers),
(3) a read-only endpoint `GET /api/queues/{id}/monitor`, and (4) a **QueueMonitor** web-UI panel that
QueuesPage renders in place of the editor for running queues and polls every ~2.5s. Run controls
(start/stop/schedule) stay where they are today (the queues overview) and are out of scope for the
monitor panel.

## Technical Context

**Language/Version**: C# / .NET (net9.0) backend; TypeScript + React (web-ui)
**Primary Dependencies**: ASP.NET Minimal APIs; System.Text.Json; React 18 + Vite; xUnit; Jest
**Storage**: None new. The monitor reads the in-memory `QueueRunHandle` (via `IQueueRunRegistry`) plus
the already-persisted linked `QueueTemplate` (file-based). Nothing about the run is persisted by this
feature; the snapshot is computed on demand and discarded per request.
**Testing**: xUnit (`tests/unit`, `tests/integration`); web-ui Jest (component + page). Real green gate
is `vite build` + `jest` for web-ui (lint/tsc have pre-existing failures).
**Target Platform**: Windows host running the GameBot service (port 8080) + the web-ui SPA
**Project Type**: Web service (backend) + React SPA (web-ui) — both touched
**Performance Goals**: One monitor poll every ~2.5s per open panel; each request builds an in-memory
projection (no new I/O beyond the one linked-template read that `GET {id}` already performs). Snapshot
build is O(entries + pending firings) with no per-request allocation on the run's hot path.
**Constraints**: CamelCase method names; functions ≈<50 LOC; keep `Program.cs`/endpoint maps thin;
≥80% line / ≥70% branch coverage on touched areas; `docs/architecture.md` + `specs/STATUS.md` updated.
The run loop change is limited to setting/clearing two current-sequence fields — no change to scheduling
behavior (zero regression to the queue engine).
**Scale/Scope**: ~4–5 backend files (handle, projection service + interface, DTOs, endpoint, DI) + tests;
~3 web-ui files (service types, QueueMonitor component + css, QueuesPage routing) + tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), progression is blocked
until fixed or a documented waiver exists.

- **I. Code Quality Discipline**: PASS — additive, read-only projection over existing state; new members
  documented; functions kept small (partition → per-kind mappers → merge/sort); CamelCase throughout; no
  dead code. The only mutation to existing code is two field writes in `RunOneSequenceAsync`.
- **II. Testing Standards**: PASS — the projection is a **pure function** of (template, run-handle
  snapshot, `now`) and is unit-tested deterministically with a fake `TimeProvider` and hand-built handle
  across every schedule kind (AtQueueStart/OncePerRun/EveryStep/Timer-time-of-day/Timer-relative/live/
  self-reschedule), cycling vs non-cycling, current-sequence highlight, nothing-scheduled, and
  not-running→last-outcome. Endpoint tested for running (snapshot) vs stopped (running:false + outcome).
  web-ui Jest covers monitor render, polling update (fake timers), running→stopped transition, and
  QueuesPage monitor-vs-editor routing. Coverage ≥80/70 on touched areas.
- **III. UX Consistency**: PASS — reuses existing table/panel styling and the app's request/refresh
  idiom; schedule reasons use the operator-facing vocabulary already established ("After Every Step",
  time-of-day, etc.); read-only, so no destructive controls; empty/idle/ended states are explicit and
  actionable ("running & waiting until …", "run ended — see Execution Logs").
- **IV. Performance**: PASS — declared goal above; the monitor is off the run's hot path. Polling is a
  small periodic GET; the projection is in-memory. Perf note: no busy work added to the queue engine;
  current-sequence tracking is two field assignments per firing.
- **V. Living Documentation**: PASS (planned) — `docs/architecture.md` Queues section gains the monitor
  endpoint + live-plan projection; `spec.md` carries a `Status` line; `specs/STATUS.md` gets a 072 row.
  Complements 046/051 (queue runtime), 059 (live schedule), 065 (self-reschedule); supersedes nothing;
  no new env vars, so `ENVIRONMENT.md` unchanged.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/072-queue-monitor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-monitor.md # GET /api/queues/{id}/monitor contract
├── checklists/
│   └── requirements.md  # from /speckit-specify
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/
├── QueueRunHandle.cs                 # + CurrentSequenceId/CurrentSequenceStartedAt (volatile), set/clear
│                                     #   in RunOneSequenceAsync; + SnapshotPendingTimerFirings() accessor
├── QueueExecutionService.cs          # set/clear current-sequence fields around ExecuteAsync (only change)
├── IQueueMonitorService.cs           # NEW: BuildAsync(queueId, ct) → QueueMonitorSnapshot
└── QueueMonitorService.cs            # NEW: pure projection (registry + template repo + TimeProvider +
                                      #   best-effort last-outcome via IExecutionLogService.QueryAsync)

src/GameBot.Service/Contracts/Queues/
├── QueueMonitorResponse.cs           # NEW: running, cycleExecution, runStartedAt, current, upcoming[],
│                                     #   nothingScheduled, lastOutcome
└── QueueMonitorItemResponse.cs       # NEW: sequenceId, sequenceName, reason, scheduleKind,
                                      #   expectedAt?, relativeLabel, repeats, order

src/GameBot.Service/Endpoints/
└── QueuesEndpoints.cs                # + GET {id}/monitor → IQueueMonitorService

src/GameBot.Service/GameBotServiceSetup.cs  # register IQueueMonitorService (singleton/scoped)

src/web-ui/src/services/
└── queues.ts                         # + QueueMonitorDto types + getQueueMonitor(id)

src/web-ui/src/components/queues/
├── QueueMonitor.tsx                  # NEW: read-only now/next playlist; polls ~2.5s while mounted
└── QueueMonitor.css                  # NEW

src/web-ui/src/pages/
└── QueuesPage.tsx                    # open running queue → <QueueMonitor>; stopped → existing editor

tests/unit/…                         # QueueMonitorService projection matrix
tests/integration/…                  # GET {id}/monitor running vs stopped (optional but preferred)
src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx
src/web-ui/src/pages/__tests__/QueuesPage.monitor.spec.tsx

docs/architecture.md                 # Queues: monitor endpoint + live-plan projection
specs/STATUS.md                      # new 072 row
```

**Structure Decision**: Web service + React SPA — both are in scope. The backend adds a read-only
projection endpoint over already-in-memory run state; the web-ui adds a monitor panel and routes to it
based on the queue's running status. No persistence, no schema, no new config/env.

## Design Decisions

1. **Current-sequence tracking is the only run-engine change.** `RunOneSequenceAsync` is the single
   choke point through which every firing (at-start, once-per-run, every-step, timer, relative, live,
   self-reschedule) flows. Set `handle.CurrentSequenceId` + `handle.CurrentSequenceStartedAt` at the top
   and clear them in `finally`. Fields are `volatile`/interlocked-simple so a concurrent monitor read is
   safe without locking. This yields the sequence-level "now" indicator (clarified granularity) with no
   change to scheduling behavior — zero regression.

2. **The monitor snapshot is a pure projection, not stored state.** A new `QueueMonitorService` reads the
   `QueueRunHandle` (via `IQueueRunRegistry.TryGet`) and the linked `QueueTemplate`, and folds them with
   `TimeProvider.GetLocalNow()` into an ordered snapshot. Keeping it pure (inputs → output) makes it
   deterministically unit-testable and keeps all timing logic out of the endpoint. It mirrors — but does
   not duplicate — the run loop's schedule semantics.

3. **Expected times are best-effort by design (per clarification).** The run loop evaluates timers
   lazily at iteration boundaries, so the monitor does not attempt a guaranteed timeline:
   - **Live schedules** → exact `fireAt` from `PendingLiveSchedules`.
   - **Self-reschedule Timer firings** → exact `FireAt` via a new `SnapshotPendingTimerFirings()`.
   - **Timer (time-of-day)** → next eligible instant: today at `HH:mm` if `now < HH:mm`, else tomorrow.
   - **Timer (relative)** → `RunStartedAt + offset`; if already elapsed, shown as "due/soon" (fires once
     per run). No run-loop `relativeTimerFired`/`timerFiredDate` state is exposed — the wall-clock
     approximation satisfies the "next eligible time" allowance and keeps the engine untouched.
   Ordering and schedule **reasons** are exact; only far-future timer instants are approximate.

4. **OncePerRun is the spine; EveryStep is an annotation, not N interleaved copies.** The upcoming list
   shows OncePerRun entries in template order (the "playlist" spine). EveryStep sequences are surfaced
   once, labeled "After Every Step" (existing operator vocabulary), rather than exploding the list with a
   copy after every step — this keeps a long/cycling run readable (spec FR-008, edge cases). Timed/live/
   self-reschedule firings are merged into the list ordered by expected time.

5. **Cycling is a marker, not an unrolled timeline (per clarification).** For a cycling queue the
   OncePerRun spine is shown **once** with a `repeats: true` marker; the endpoint never projects an
   infinite future. Pending timed/live firings are shown alongside with their concrete times.

6. **View routing keys off existing running/stopped status; no new "mode" concept.** QueuesPage opens the
   monitor when `status === 'Running'` and the editor when `'Stopped'`, reusing the status the overview
   already shows. The overview keeps Start/Stop/Schedule. The Edit button (already disabled while running)
   is complemented by a name-click / "Monitor" affordance that opens the panel for a running queue. When a
   poll returns `running: false`, the panel switches to an "ended" state showing `lastOutcome` and a path
   back to the editor / Execution Logs (FR-010).

7. **Last outcome is best-effort via the execution log.** When the queue is not running, the projection
   queries `IExecutionLogService.QueryAsync` for the most recent finalized queue-run entry for that queue
   and returns its status + summary as `lastOutcome`. Absent/none → `null` (UI shows a neutral "not
   running"). This satisfies "surface the final outcome where available" without new persistence.

8. **Times cross the wire as ISO-8601 with offset.** The service uses local-clock instants
   (`GetLocalNow()`, service-local offset — consistent with the queue engine and the known +02:00 memory
   note), so `DateTimeOffset` carries the offset and the UI renders one unambiguous local time (FR-014).

## Complexity Tracking

No constitution violations — no entries required.
