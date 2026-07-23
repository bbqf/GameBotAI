# Phase 0 Research: Live Queue Monitor View

All Technical Context items are known from the existing codebase; there were no open
`NEEDS CLARIFICATION` markers (the four spec clarifications were resolved in `/speckit-clarify`).
This file records the decisions that shape the design.

## R1 — Where does the running queue's live plan already live?

- **Decision**: Read the existing in-memory `QueueRunHandle` (owned by `IQueueRunRegistry`) plus the
  queue's linked `QueueTemplate`; do not add persistence.
- **Findings**:
  - `QueueRunHandle` (`Services/QueueExecution/QueueRunHandle.cs`) already carries: `CycleExecution`,
    `RunStartedAt` (local-clock anchor), `RootExecutionId`, `SessionId`, `PendingLiveSchedules`
    (`sequenceId → fireAt`), self-reschedule registers (`PendingOncePerRun`, `PendingNextCycleStart`,
    `EveryStepInjections`), and private timer firings with `FireAt`.
  - The **schedule types and order** live on `QueueTemplate.Entries` (`ScheduleType`,
    `TimerTimeOfDay`, `TimerRelativeOffset`). The runtime store only holds sequence ids, so the
    projection must load the template (same read `GET /api/queues/{id}` already performs).
  - `IQueueRunRegistry.TryGet(queueId, out handle)` gives O(1) access to the active handle; all types
    are `internal` to `GameBot.Service`, so a projection service in the same assembly can read them.
- **Gap**: the handle does **not** track which sequence is executing right now, and the private timer
  firings have no read accessor. Both are added (minimally) — see R2.
- **Alternatives considered**: (a) Persist a plan snapshot each iteration — rejected: adds I/O to the
  hot path and a storage surface for ephemeral data. (b) Derive everything from Execution Logs —
  rejected: logs are historical/after-the-fact and cannot show *upcoming* work or future times.

## R2 — How to expose the "now" sequence and pending timer firings with minimal engine change?

- **Decision**: Add two `volatile` fields (`CurrentSequenceId`, `CurrentSequenceStartedAt`) set/cleared
  in `RunOneSequenceAsync`, and a `SnapshotPendingTimerFirings()` read accessor on the handle.
- **Rationale**: `RunOneSequenceAsync` is the single method every firing passes through, so two field
  writes there capture the current sequence for **all** schedule kinds without touching scheduling
  logic. `volatile` (reference/`DateTimeOffset?` via a small lock or `Interlocked` on a struct box) makes
  a concurrent monitor read safe without introducing locks on the run path. The timer-firings list is
  already lock-guarded internally; the accessor returns a copy under the same lock.
- **Alternatives considered**: threading a callback/event out of the loop (heavier, more surface);
  exposing `relativeTimerFired`/`timerFiredDate` (unnecessary given best-effort times — see R3).

## R3 — How to present expected times without a guaranteed timeline?

- **Decision**: Best-effort per the clarified assumption. Exact where the handle has an instant
  (`PendingLiveSchedules` fireAt; self-reschedule `FireAt`); next-eligible wall-clock projection for
  template timers (time-of-day → today/tomorrow at `HH:mm`; relative → `RunStartedAt + offset`).
- **Rationale**: The engine evaluates timers lazily at iteration boundaries; there is no precomputed
  schedule to read. The spec explicitly permits "next eligible time" for far-future timers. This keeps
  the engine untouched and the projection a pure function of (template, handle snapshot, `now`).
- **Ordering vs timing**: schedule **reason/kind** and **order** are exact; only far-future absolute
  timer instants are approximate. The projection sorts timed/live/self-reschedule items by expected
  time and keeps the OncePerRun spine in template order.

## R4 — Live-update mechanism (clarified: ~2–3s auto-refresh)

- **Decision**: Client-side polling of `GET /api/queues/{id}/monitor` on a fixed ~2.5s interval while
  the monitor panel is mounted (`setInterval`, cleared on unmount / when the queue is no longer
  running). No server push / SSE / WebSocket.
- **Rationale**: Matches the clarification and the app's existing request/refresh idiom (QueuesPage,
  Execution Logs); trivial load at this scale (single-operator tool); far simpler to build and test than
  streaming. Jest fake timers make the polling deterministically testable.
- **Alternatives considered**: SSE/WebSocket push (rejected: disproportionate complexity for one local
  operator); manual refresh only (rejected: contradicts "dynamic").

## R5 — View routing (monitor vs editor)

- **Decision**: QueuesPage selects the panel from the queue's `status`: `Running` → `QueueMonitor`
  (read-only); `Stopped` → existing editor. Run controls stay in the overview row.
- **Rationale**: Reuses the status the overview already renders and the existing rule that editing is
  disabled while running; no new "mode" concept. Satisfies FR-001..FR-003 and keeps controls out of the
  monitor panel (FR-012).
- **Transition**: a poll returning `running:false` flips the panel to an "ended" state (shows
  `lastOutcome`, offers return to editor / Execution Logs), covering the stop-while-open edge case.

## R6 — Last-run outcome when not running (FR-010)

- **Decision**: Best-effort via `IExecutionLogService.QueryAsync` for the most recent finalized
  queue-run entry for the queue; return its `status` + `summary` as `lastOutcome`, else `null`.
- **Rationale**: Reuses the existing execution-log query surface (queue runs are already finalized with
  a summary by `QueueExecutionService`), so no new persistence and the outcome text matches the logs.
- **Alternatives considered**: caching the last `QueueRunResult` on a service field (rejected: extra
  mutable state, lost across restarts, duplicates what the log already stores).
