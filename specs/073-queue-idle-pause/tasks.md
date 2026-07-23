---

description: "Task list for Idle-Pause the Game During Queue Gaps; Retire the MCP Server"
---

# Tasks: Idle-Pause the Game During Queue Gaps; Retire the MCP Server

**Input**: Design documents from `specs/073-queue-idle-pause/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-idle-pause-config.md, quickstart.md

**Tests**: INCLUDED — the constitution mandates unit/contract coverage for executable logic, and the spec defines Independent Tests per story.

**Organization**: Grouped by user story. US1–US4 = idle-pause; **US5 = MCP-server removal (independent scope, ships on the same branch)**.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 / US5 (no label for Setup / Foundational / Polish)

## Path Conventions

Web application: .NET backend under `src/GameBot.Service/` + `src/GameBot.Domain/`; React front end under `src/web-ui/`; tests under `tests/`. The Node `src/mcp-server/` tool is being **deleted** by US5 — do NOT add new code to it, and the idle-pause config is exposed via the REST API + web-ui only (no MCP arm; FR-020).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a known-green baseline before changes.

- [ ] T001 Confirm baseline green gate before edits: run `dotnet build` + `dotnet test` for the solution, and `vite build` + `jest` in `src/web-ui/` (per memory, web-ui lint/tsc have pre-existing failures — the real gate is `vite build` + `jest`). Record any pre-existing reds so new failures are attributable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared building blocks the idle-pause stories depend on. **No US1–US4 work begins until this phase is complete.** (US5 does not depend on this phase.)

- [ ] T002 [P] Add persisted config fields to `ExecutionQueue` in `src/GameBot.Domain/Queues/ExecutionQueue.cs`: `public bool PauseWhenIdle { get; set; }` (default false) and `public int IdleThresholdSeconds { get; set; } = 30;` with XML docs. Initializer default gives JSON back-compat.
- [ ] T003 [P] Add `IdlePause` to the `ScheduleKind` enum in `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs` with a summary comment.
- [ ] T004 [P] Add the idle-pause register to `QueueRunHandle` in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`: `DateTimeOffset? IdlePausedUntil` (backed field + lock like `_currentLock`), derived `bool IsIdlePaused`, and `EnterIdlePause(DateTimeOffset resumeAt)` / `ClearIdlePause()` methods (CamelCase, monitor-readable across threads).
- [ ] T005 Inject `IEnsureGameRunningActionHandler` into `QueueExecutionService` constructor in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (field `_ensureGameRunning`); it is already DI-registered (used by `SequenceExecutionService`). Verify existing DI wiring in `src/GameBot.Service/GameBotServiceSetup.cs` resolves it; no test-constructor breakage (add as optional/last param if needed to keep existing test call sites compiling).

**Checkpoint**: Domain, handle, enum, and DI ready — idle-pause story work can begin.

---

## Phase 3: User Story 1 - Game is backed out while the queue waits (Priority: P1) 🎯 MVP

**Goal**: During an idle gap over the threshold on an enabled queue, background the game to the home screen and foreground it when the next firing is due — without watchdog cancellation or log entries.

**Independent Test**: Unit-test the run loop with a fake clock and fakes for `ISessionManager`/`IEnsureGameRunningActionHandler`: a distant next firing on an enabled queue triggers a HOME send, and a foreground call precedes the due firing; a sub-threshold gap or a disabled queue triggers neither.

### Tests for User Story 1 ⚠️ (write first, ensure they fail)

- [ ] T006 [P] [US1] Unit tests in `tests/unit/Queues/QueueExecutionServiceTests.cs`: (a) enabled + gap > threshold → HOME sent once and foreground called before the due sequence runs; (b) gap ≤ threshold → no background/foreground; (c) `PauseWhenIdle` false → no background/foreground (game untouched); (d) pause longer than the 4-min watchdog is NOT cancelled/failed (fake time advances past 4 min with no watchdog trip) [SC-005]; (e) background failure and foreground failure are non-fatal (the due sequence still runs) [FR-011]; (f) stop during pause aborts within one poll interval [SC-006]; (g) an idle-pause cycle (enter → hold → resume) writes **zero** execution-log child entries — assert the log service receives no pause-related writes [FR-007a/SC-007]; (h) when a firing becomes due, the resume decision is taken within one `RelativeTimerPollInterval` of the due instant (fake clock: advance to just past `nextDue`, assert foreground is invoked within one tick) [SC-002]. Use the injectable `TimeProvider`.

### Implementation for User Story 1

- [ ] T007 [US1] Add a `ComputeNextDue(now)` helper inside `QueueExecutionService.RunAsync` (or a private method with the needed state) in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` that returns the earliest upcoming instant across: time-of-day timers (next-eligible, unfired-today), unfired relative offsets (`runStartedAt + offset`), pending self-reschedule timer firings (`handle.SnapshotPendingTimerFirings()` min `FireAt`), live schedules (`handle.PendingLiveSchedules` min), and queued once-per-run/next-cycle self-reschedules (due-now). Return null when nothing pending.
- [ ] T008 [US1] Add a private `IdlePauseHoldAsync(DateTimeOffset resumeAt, string sessionId, QueueRunHandle handle, CancellationToken ct)` in the same file: send Android `KEYCODE_HOME` (3) once via `_sessions.SendInputsAsync` (mirror `DispatchGoToHomeScreenAsync`, best-effort/non-fatal), call `handle.EnterIdlePause(resumeAt)`, poll `Task.Delay(RelativeTimerPollInterval, ct)` re-computing next-due each tick until due/earlier/cancelled, then call `_ensureGameRunning.ExecuteAsync(sessionId, ct)` (best-effort/non-fatal) and `handle.ClearIdlePause()`. Runs inline (NOT via `RunOneSequenceAsync`) so it is watchdog-exempt **and emits no execution-log entries — this inline design is what satisfies FR-015** (the pause is never an ordinary watchdog-subject sequence).
- [ ] T009 [US1] Wire the branch into the non-cyclic wait tail of `RunAsync` (near line 374–376): replace the unconditional `await Task.Delay(RelativeTimerPollInterval, ct)` with — compute `nextDue`; if `queue.PauseWhenIdle` and `nextDue` is set and `nextDue - now > TimeSpan.FromSeconds(Max(1, queue.IdleThresholdSeconds))`, `await IdlePauseHoldAsync(...)`; else keep the existing one-tick delay. Do not alter any firing/eval logic (FR-013).
- [ ] T010 [US1] Ensure `IdlePauseHoldAsync` clears idle-pause state in a `finally` so a cancellation/stop never leaves `IdlePausedUntil` set; confirm teardown in `RunAsync`'s `finally` still stops the session cleanly when stopped mid-pause.

**Checkpoint**: Idle-pause backgrounds/foregrounds correctly on an enabled queue; watchdog-exempt; disabled queues unchanged. MVP behavior complete.

---

## Phase 4: User Story 2 - The pause is visible in the live monitor (Priority: P1)

**Goal**: While paused, the monitor's `current` shows an explicit "Idle Pause" item with a resume time; no execution-log entries are written.

**Independent Test**: Monitor projection unit test with an idle-paused handle returns a synthetic current item (`scheduleKind IdlePause`, `expectedAt` = resume, `relativeLabel "paused"`); web-ui renders it distinctly.

### Tests for User Story 2 ⚠️

- [ ] T011 [P] [US2] Unit test in `tests/unit/Queues/QueueMonitorServiceTests.cs`: given a running handle with `IdlePausedUntil` set and `CurrentSequenceId` null, `BuildAsync` returns `Current` = synthetic idle-pause item (`SequenceId ""`, `SequenceName "Idle Pause"`, `ScheduleKind.IdlePause`, `ExpectedAt` = resume, `RelativeLabel "paused"`, `Stale false`); when a real sequence is executing, the real item wins (no idle item).
- [ ] T012 [P] [US2] web-ui test in `src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx`: a monitor payload with `current.scheduleKind === "IdlePause"` renders a paused state showing the resume time, distinct from a running sequence and a stopped queue.

### Implementation for User Story 2

- [ ] T013 [US2] In `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs` `BuildCurrent`: when `handle.CurrentSequenceId` is null and `handle.IsIdlePaused`, return the synthetic idle-pause `QueueMonitorItem` (per data-model.md §4) with `ExpectedAt = handle.IdlePausedUntil` and reason `"Game paused — resumes at HH:mm"`. Prefer a real `CurrentSequenceId` when present.
- [ ] T014 [US2] Ensure `ScheduleKind.IdlePause` serializes to the string `"IdlePause"` in the monitor response mapping (`src/GameBot.Service/Endpoints/QueuesEndpoints.cs` `ProjectMonitorItem` / `QueueMonitorResponse`): if the response uses an explicit enum→string switch/map, add the `IdlePause` case; if it serializes the enum by name automatically, add a test assertion that the emitted string is exactly `"IdlePause"`. Either way, the wire value MUST be `"IdlePause"`.
- [ ] T015 [US2] Render the idle-pause kind in the web-ui monitor component (`src/web-ui/src/pages/QueuesPage.tsx` / the `QueueMonitor` component under `src/web-ui/src/components/queues/`): show "Idle Pause" + resume time as a distinct state; update the `scheduleKind` type union in `src/web-ui/src/services/queues.ts`.

**Checkpoint**: Operator sees a clear, continuous idle-pause state with resume time; no log volume added.

---

## Phase 5: User Story 3 - An earlier-arriving schedule still wins (Priority: P2)

**Goal**: A live/self-reschedule that becomes the soonest firing during a pause ends the pause promptly.

**Independent Test**: Enter a pause sized to a distant firing, inject a live schedule due in a few seconds, and assert the game foregrounds and the injected sequence runs near its new due time.

### Tests for User Story 3 ⚠️

- [ ] T016 [P] [US3] Unit test in `tests/unit/Queues/QueueExecutionServiceTests.cs`: while idle-paused toward a distant firing, add a `PendingLiveSchedules` entry (or a self-reschedule timer firing) due within one/two ticks; assert the hold recomputes next-due, ends early, foregrounds, and the earlier sequence fires before the original distant time.

### Implementation for User Story 3

- [ ] T017 [US3] Verification-only (no re-implementation): confirm `IdlePauseHoldAsync` (T008) already recomputes `ComputeNextDue(now)` every tick and exits when the soonest due instant is reached — including newly-arrived live schedules and self-reschedule firings. Specifically verify the loop does NOT use a stale captured `resumeAt` as its sole exit condition; if T008 already does this correctly, this task is satisfied by the T016 test and a code review note rather than new production code.

**Checkpoint**: On-demand and self-rescheduled work is never swallowed by a pause.

---

## Phase 6: User Story 4 - Opt-in per queue (Priority: P2)

**Goal**: Expose the two config fields through the config surface — **REST API + web-ui only** (no MCP arm; the MCP server is being deleted in US5, FR-020); default disabled = unchanged behavior.

**Independent Test**: Create/update a queue with `pauseWhenIdle`/`idleThresholdSeconds`; response and re-read echo them; omitted → `false`/`30`; a pre-existing queue JSON reads as `false`/`30`.

### Tests for User Story 4 ⚠️

- [ ] T018 [P] [US4] Contract/integration test (e.g. `tests/contract/` alongside existing queue API tests): create with `pauseWhenIdle:true, idleThresholdSeconds:45` → response echoes both; update round-trips; omitted defaults to `false`/`30`; `idleThresholdSeconds:0` coerces to `30`.
- [ ] T019 [P] [US4] Back-compat test in `tests/unit/Queues/FileQueueRepositoryTests.cs`: a serialized `ExecutionQueue` JSON without the new fields deserializes to `PauseWhenIdle=false`, `IdleThresholdSeconds=30`.
- [ ] T020 [P] [US4] web-ui test for `src/web-ui/src/pages/__tests__/QueuesPage.*`: the queue editor shows the "Pause game when idle" toggle + threshold input and submits both fields.

### Implementation for User Story 4

- [ ] T021 [P] [US4] Add `PauseWhenIdle` + `IdleThresholdSeconds` to `src/GameBot.Service/Contracts/Queues/CreateQueueRequest.cs`, `UpdateQueueRequest.cs`, and `QueueResponse.cs`.
- [ ] T022 [US4] Map the new fields on create/update/response in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (mirror `CycleExecution` at the create block, update block, and both `BuildResponse` sites); coerce `IdleThresholdSeconds < 1` to `30`.
- [ ] T023 [P] [US4] Add the fields to the web-ui queue type/service in `src/web-ui/src/services/queues.ts` and the queue editor UI in `src/web-ui/src/pages/QueuesPage.tsx` (toggle + threshold input). **No mcp-server change** — that arm is intentionally omitted (US5).

**Checkpoint**: Idle-pause is configurable via the REST API and web-ui; disabled queues are byte-for-byte unchanged in behavior.

---

## Phase 7: User Story 5 - Retire the MCP server (Priority: P3, independent)

**Goal**: Remove the project's own MCP server completely (component + registration + the single incidental doc reference), leaving the build/test gate green and REST/web-ui behavior unchanged.

**Independent Test**: After removal, `src/mcp-server` and `.mcp.json` no longer exist; a repo-wide search for the project MCP server finds only immutable spec-history hits and the unrelated external GitHub-MCP agent file; `dotnet build` + `dotnet test` and web-ui `vite build` + `jest` all pass.

**Note**: This phase does not depend on Phases 2–6 and may be executed independently (before, after, or in parallel with the idle-pause work), except that T026 and the Polish architecture task both edit `docs/architecture.md` and must be sequenced (same file).

### Implementation for User Story 5

- [ ] T024 [P] [US5] Delete the entire MCP server component. First `git rm -r src/mcp-server` (removes the ~19 **tracked** files: TypeScript source under `src/` + `src/tools/`, `package.json`, `package-lock.json`, `tsconfig*.json`, `README.md`). Because `node_modules/` and `dist/` are **gitignored** (untracked), `git rm` will NOT touch them — so also delete the working-tree directory so nothing lingers on disk: `Remove-Item -Recurse -Force C:\src\GameBot\src\mcp-server`. Verify BOTH that `git ls-files src/mcp-server` returns nothing AND that the directory no longer exists on disk [SC-008].
- [ ] T025 [P] [US5] Delete the root MCP registration: `git rm .mcp.json` (its only entry is the `gamebot` server → `src/mcp-server/dist/index.js`).
- [ ] T026 [US5] Edit `docs/architecture.md` (~line 72): remove the incidental phrase "/ MCP `start_session`" so the sentence references only the REST `/api/sessions/start` endpoint. Do NOT touch anything else in this task (the idle-pause architecture additions are a separate Polish task on the same file).
- [ ] T027 [US5] Verify the removal: (1) repo-wide search for `mcp-server` / `gamebot-mcp` returns only `specs/**` history hits and `.github/agents/speckit.taskstoissues.agent.md` (external `github/github-mcp-server` — MUST remain untouched, FR-022); (2) confirm `.github/workflows/*.yml`, `GameBot.sln`, and root `README.md` have no project-MCP references; (3) `dotnet build` + `dotnet test` and web-ui `vite build` + `jest` are green [SC-008/SC-009]. Leave `.claude/settings.local.json` local allow-rules as-is (untracked developer settings, out of scope).

**Checkpoint**: The project MCP server is gone; the build/test gate is green; the external GitHub MCP and prior spec history are untouched.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T028 Update `docs/architecture.md` for the idle-pause change: queue config gains `pauseWhenIdle`/`idleThresholdSeconds`; document the runtime idle-pause behavior, watchdog exemption, and the monitor `IdlePause` state; refresh the "Last reviewed" date. **Same file as T026 (US5) — run after T026, not in parallel.**
- [ ] T029 [P] Set `spec.md` Status to Implemented and update `specs/STATUS.md` to include 073 (both scopes); ensure features 069/072 are referenced (not superseded).
- [ ] T030 Run `quickstart.md` Part A validation on the live queue: enable idle-pause on `PNS Daily 5558`, restart, confirm background→monitor "Idle Pause"→foreground at due time, and no new execution-log entries. Then remove the "PNS Queue Pause 15m" entry from the template (keep the sequence definition) and restart. **Requires service redeploy first.**
- [ ] T031 Final green gate: `dotnet build` + `dotnet test` and `vite build` + `jest` all pass (constitution NON-NEGOTIABLE); resolve any new failures before marking complete.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: none.
- **Foundational (T002–T005)**: after Setup. **Blocks US1–US4.**
- **US1 (T006–T010)**: after Foundational. MVP.
- **US2 (T011–T015)**: after Foundational; independent of US1 (reads the handle state US1 sets, but testable via a hand-set handle).
- **US3 (T016–T017)**: builds on US1's hold loop (T008); do after US1.
- **US4 (T018–T023)**: after Foundational (needs the domain fields T002); independent of US1–US3.
- **US5 (T024–T027)**: fully independent — needs nothing from Setup/Foundational/US1–US4. Only intra-file coupling: T026 and Polish T028 both edit `docs/architecture.md`.
- **Polish (T028–T031)**: after all targeted stories; T028 after T026.

### Within Each User Story

- Tests first (write, see fail) → implementation → checkpoint.
- US1: `ComputeNextDue` (T007) → `IdlePauseHoldAsync` (T008) → wiring (T009) → finally-guard (T010).
- US5: deletions (T024, T025) + doc edit (T026) → verify (T027).

### Parallel Opportunities

- Foundational: T002, T003, T004 are `[P]` (different files); T005 touches the service + DI (after or alongside).
- US4 implementation: T021 and T023 are `[P]` (different files/languages); T022 depends on T021.
- US5: T024 and T025 are `[P]` (independent deletions); T026 (docs) then T027 (verify).
- US5 can run in parallel with the entire idle-pause track (different files, except the `docs/architecture.md` sequencing between T026 and T028).
- Tests marked `[P]` across stories can run in parallel once their targets exist.

---

## Parallel Example: Foundational

```bash
Task: "T002 Add PauseWhenIdle/IdleThresholdSeconds to ExecutionQueue.cs"
Task: "T003 Add ScheduleKind.IdlePause to QueueMonitorSnapshot.cs"
Task: "T004 Add idle-pause register to QueueRunHandle.cs"
```

## Parallel Example: User Story 5 (MCP removal)

```bash
Task: "T024 git rm -r src/mcp-server (delete the component)"
Task: "T025 git rm .mcp.json (delete the root registration)"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001) → Foundational (T002–T005) → US1 (T006–T010).
2. **STOP and validate** US1 via unit tests (watchdog-exempt background/foreground on an enabled queue).
3. US1 is the core value; it can be exercised in tests without the API surface.

### Incremental Delivery

1. Foundational → US1 (backing out works) → US2 (visible in monitor) → US4 (configurable via UI/API) → US3 (earlier-firing correctness) → Polish.
2. **US5 (MCP removal) is independent** and can be done at any point on this branch — it shares no files with the idle-pause work except the `docs/architecture.md` sequencing (T026 before T028).
3. Each story is independently testable; disabled queues stay unchanged throughout.

### Notes

- `[P]` = different files, no incomplete-task dependency.
- Verify tests fail before implementing.
- CamelCase method names only (constitution); no underscores.
- The idle-pause config surface stops at REST + web-ui — **do not** re-add an MCP tool arm (it is being deleted).
- Deployment reminder: the live queue only changes behavior after a service rebuild/redeploy + queue restart (T030).
