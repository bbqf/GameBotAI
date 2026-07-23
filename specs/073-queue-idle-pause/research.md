# Phase 0 Research: Idle-Pause the Game During Queue Gaps

All Technical Context unknowns are resolved below. No open `NEEDS CLARIFICATION` remain.

## R1. Backgrounding/foregrounding the game from the runtime (no sequence)

**Decision**: Drive the two device operations directly from `QueueExecutionService`, reusing the exact
mechanisms the sequence dispatcher uses:
- **Background**: `ISessionManager.SendInputsAsync(sessionId, [key KEYCODE_HOME=3])` — identical to
  `SequenceExecutionService.DispatchGoToHomeScreenAsync`. HOME backgrounds the app without stopping it.
- **Foreground**: `IEnsureGameRunningActionHandler.ExecuteAsync(sessionId, ct)` — identical to
  `DispatchEnsureGameRunningAsync`. Brings the game to the foreground (launches/resumes as needed).

`QueueExecutionService` already depends on `ISessionManager` (`_sessions`) and has the run's `sessionId`.
Add `IEnsureGameRunningActionHandler` to its constructor (registered in DI already, used by
`SequenceExecutionService`).

**Rationale**: Calling these directly (not via `ISequenceExecutionService.ExecuteAsync`) means the pause
does NOT pass through `RunOneSequenceAsync`, so it is not wrapped by the 4-minute watchdog (FR-006) and
writes no execution-log entries (FR-007a). It reuses proven, tested device code paths (features 069/052).

**Note (redundancy is intentional)**: Many sequences already begin with a `recover`/`connect` step that
foregrounds the game. The runtime foreground at resume (FR-004) is a **safety net** for sequences that
lack such a step; when a sequence has one, the second foreground is an idempotent no-op. This redundancy
is harmless and deliberate — it guarantees the game is up regardless of the due sequence's shape.

**Alternatives considered**:
- *Run a tiny built-in HOME/foreground sequence via `ISequenceExecutionService`* — rejected: re-imposes
  the watchdog and emits log entries, the two things the spec forbids.
- *New device abstraction* — rejected: unnecessary; the operations already exist.

## R2. Placement of the idle-pause branch in the run loop

**Decision**: In `RunAsync`, the non-cyclic tail currently reads:
```
if (queue.CycleExecution) continue;
if (!HasPendingRelativeOrLive()) break;
await Task.Delay(RelativeTimerPollInterval, ct);
```
Insert idle-pause handling at the delay point: compute `nextDue`; if `queue.PauseWhenIdle` and
`nextDue - now > threshold`, run an `IdlePauseHoldAsync(nextDue, sessionId, handle, ct)` that backgrounds
once, marks the handle idle-paused, polls (re-checking next-due) until due/earlier/stop, then
foregrounds and clears the state. Otherwise fall back to the existing one-tick delay.

**Rationale**: This is exactly the moment the loop is idle-but-alive. The hold replaces the idle wait
without altering any firing logic — after it returns, the next loop iteration evaluates and fires the
now-due entry through the normal path (FR-013). Cycling runs (`continue` above) do not wait and are
unaffected (spec Assumptions).

**Alternatives considered**:
- *Separate hosted background service watching runs* — rejected: duplicates scheduling knowledge and
  races the run loop.
- *Bracket every sequence (foreground-first/HOME-last) across all sequences* — rejected earlier: 17
  edits, ordering hazards with existing recover steps, and can't express "only when gap > threshold."

## R3. Computing the next-due instant

**Decision**: A runtime-local helper `ComputeNextDue(now)` returns the earliest upcoming firing across:
- **Time-of-day timers** not yet fired today: next-eligible instant (today at `HH:mm` if `now < tod`,
  else it is already due → treat as due-now if not yet fired today).
- **Relative-offset timers** not yet fired: `runStartedAt + offset` when still in the future.
- **Pending self-reschedule timer firings**: min `FireAt` from `handle.SnapshotPendingTimerFirings()`.
- **Live schedules**: min value from `handle.PendingLiveSchedules`.
- **Queued once-per-run / next-cycle-start self-reschedules**: due-now (gap 0) if non-empty.

If any source is due-now (gap ≤ 0), there is no pause. The helper closes over the loop's local timer
state (`timerFiredDate`, `relativeTimerFired`, `runStartedAt`, `timerEntries`).

**Rationale**: Mirrors the sources the loop actually fires from, so the pause never masks a due firing.
Re-evaluated every tick during the hold so an earlier live/self firing shortens the pause (FR-003, US3).

**Alternatives considered**:
- *Reuse `QueueMonitorService` upcoming projection* — rejected for now: it depends on the template and a
  name map and returns display items; coupling the scheduler to a projection risks drift in the wrong
  direction. Noted as a future consolidation opportunity (both compute "soonest firing").

## R4. Watchdog exemption & prompt stop

**Decision**: Because the hold runs inline in `RunAsync` (not in `RunOneSequenceAsync`), the
`SequenceWatchdogTimeout` never applies (FR-006/SC-005). The hold's poll loop awaits
`Task.Delay(RelativeTimerPollInterval, ct)` with the run's `ct`, so `StopAsync`'s cancellation aborts it
within one tick (FR-012/SC-006). On cancellation the hold stops immediately; teardown in the `finally`
of `RunAsync` handles session cleanup as today. The paused game is left backgrounded on stop, which is
acceptable (a normal stop also leaves the device wherever the last step left it).

## R5. Config persistence & backward compatibility

**Decision**: Add to `ExecutionQueue`:
- `public bool PauseWhenIdle { get; set; }` (defaults `false`)
- `public int IdleThresholdSeconds { get; set; } = 30;`

`FileQueueRepository` serializes/deserializes `ExecutionQueue` directly with System.Text.Json, which
does not overwrite property initializers for members absent from the JSON. Existing stored queues (no
such fields) therefore deserialize to `PauseWhenIdle=false`, `IdleThresholdSeconds=30` — disabled by
default (FR-009), no migration needed. Validation: clamp/validate `IdleThresholdSeconds` to a sane
minimum (≥1s) at the endpoint; treat non-positive as the 30s default.

**Rationale**: Matches the established `CycleExecution` pattern end-to-end; zero-risk upgrade.

## R6. Monitor surfacing of the pause

**Decision**: Add `ScheduleKind.IdlePause`. In `QueueMonitorService.BuildCurrent`, when
`handle.CurrentSequenceId` is null AND the handle is idle-paused, return a synthetic
`QueueMonitorItem` with no real sequence (`SequenceId = ""`, `SequenceName = "Idle Pause"`,
`Stale = false`), `ScheduleKind.IdlePause`, `Reason = "Game paused — resumes at HH:mm"`,
`ExpectedAt = resumeAt`, `RelativeLabel = "paused"`. The response already carries `scheduleKind`
(string) and `expectedAt`; web-ui renders the new kind. No execution-log involvement (FR-007a).

**Rationale**: Reuses the existing current-item channel the monitor already reads from the handle
(feature 072), giving a single continuous visible pause for the whole gap without a real sequence.

## R7. Rollout of the superseded pause entry

**Decision**: As part of rollout (quickstart), remove the "PNS Queue Pause 15m" entry from the
production queue template (`PNS Daily 5558`); keep the sequence definition in the library (FR-016).
This is an operational data change (template edit + queue restart), not a code change.
