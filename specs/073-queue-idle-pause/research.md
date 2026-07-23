# Phase 0 Research: Idle-Pause the Game During Queue Gaps; Retire the MCP Server

All Technical Context unknowns are resolved below. No open `NEEDS CLARIFICATION` remain.

Sections R1–R7 cover the idle-pause change; R8 covers the independent MCP-server removal.

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

**Config surface boundary (revised 2026-07-23)**: The fields are exposed through the REST API
(create/update/response) and the web-ui only. They are **not** added to the MCP tool schema — the MCP
server is being deleted in this same branch (see R8, FR-020). This is the one deviation from a literal
`CycleExecution` mirror, which historically also had an mcp-server arm.

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

## R8. Retire the project's MCP server (independent scope)

**Decision**: Delete the project's own MCP server entirely. Concretely:
- Delete the directory `src/mcp-server/` (TypeScript source under `src/` and `src/tools/`, `dist/`,
  `node_modules/`, `package.json`, `package-lock.json`, `tsconfig*.json`, `README.md`).
- Delete the root MCP registration `.mcp.json` (its only server entry is `gamebot` →
  `src/mcp-server/dist/index.js`).
- Edit `docs/architecture.md` to drop the single incidental phrase "/ MCP `start_session`" (~line 72),
  leaving the REST `/api/sessions/start` reference intact.

**Footprint verification (grep, node_modules excluded)** — the ONLY non-spec references to the project
MCP server are `.mcp.json` and files inside `src/mcp-server/` itself:
- `GameBot.sln` — does not include the Node project (nothing to remove).
- `.github/workflows/dotnet.yml`, `release-installer.yml` — no MCP build/publish steps.
- Root `README.md`, `docs/` — no `mcp-server` references; `docs/architecture.md` has the one incidental
  `MCP start_session` phrase only.
- `.claude/settings.local.json` — contains local, developer-only allow-rules mentioning `mcp-server`;
  these are un-tracked local settings and are out of scope (harmless once the server is gone).

**Explicitly out of scope (FR-022)**: `.github/agents/speckit.taskstoissues.agent.md` references
`github/github-mcp-server` — an unrelated **external** GitHub MCP used by tooling agents. It MUST NOT be
touched. Prior feature specs (069/070/071) that mention `src/mcp-server/src/tools/*` are immutable
point-in-time history and are NOT retro-edited (constitution: specs are history, not living docs).

**Rationale**: The MCP server is a thin client over the REST API — every capability it exposed is
reachable via `/api/*` and the web-ui (FR-021). No .NET build target, solution, or CI job depends on it,
so its deletion leaves the build and test gate green (SC-008). Removing it eliminates an unused access
channel, dependency surface, and maintenance burden.

**Alternatives considered**:
- *Keep a disabled `.mcp.json` stub for easy reinstatement* — rejected: the operator chose a full delete;
  a stub is dead configuration and contradicts "remove completely."
- *Leave `src/mcp-server` but unregister it* — rejected: same reason; leaves a large unused subtree and
  `node_modules` in the tree.

**Verification**: after deletion, (1) `git status` shows `src/mcp-server/` and `.mcp.json` removed;
(2) a repo-wide search for `mcp-server`/`gamebot-mcp` returns only spec-history hits and the external
GitHub-MCP agent file (SC-009); (3) `dotnet build` + `dotnet test` and the web-ui `vite build` + `jest`
gate pass (SC-008).
