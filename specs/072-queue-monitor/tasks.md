---

description: "Task list for Live Queue Monitor View (072-queue-monitor)"
---

# Tasks: Live Queue Monitor View

**Input**: Design documents from `specs/072-queue-monitor/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/queue-monitor.md](contracts/queue-monitor.md)

**Tests**: INCLUDED — the project constitution (Testing Standards) requires unit + integration coverage
for executable logic (≥80% line / ≥70% branch on touched areas). The projection is a pure function and
is the primary unit-test target.

**Organization**: Tasks are grouped by user story. Foundational phase builds the shared read-only data
source (endpoint returning a running/not-running envelope). US1 fills the "playlist" content, US2 routes
the UI, US3 adds the idle/empty/ended states.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish have no story label)

## Path Conventions

Web service backend under `src/GameBot.Service/`; React SPA under `src/web-ui/`; xUnit tests under
`tests/unit/` and `tests/integration/`; Jest tests colocated under `src/web-ui/src/**/__tests__/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a green baseline before adding the feature. No new dependencies, config, or env vars.

- [X] T001 Confirm baseline builds/tests are green: `dotnet build` + `dotnet test` (backend) and, in `src/web-ui`, `npm run build` + `npm test` (the real web-ui gate per project notes). Record any pre-existing failures so new work is not blamed for them. Also confirm the actual xUnit test-project layout for `tests/unit`/`tests/integration` and adjust the test file paths in T011/T016/T020 if the repo uses different project directories.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared, read-only data source every story depends on — the run-handle "now" tracking,
the projection types, the `IQueueMonitorService` skeleton (running/not-running envelope only), DI, the
`GET {id}/monitor` endpoint, and the web-ui client call. After this phase the endpoint returns
`running` + empty `current`/`upcoming` (no schedule content yet).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Add current-sequence tracking to the run handle in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`: `volatile string? CurrentSequenceId`, a lock/`Interlocked`-guarded `DateTimeOffset? CurrentSequenceStartedAt`, and a `SnapshotPendingTimerFirings()` accessor that returns a copy of `_pendingTimerFirings` under the existing `_timerLock`. Document each member.
- [X] T003 Set/clear the current-sequence fields around execution in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` `RunOneSequenceAsync`: set `CurrentSequenceId`/`CurrentSequenceStartedAt` before `ExecuteAsync`, clear them in `finally`. This is the ONLY queue-engine change — no scheduling behavior changes.
- [X] T004 [P] Create internal projection types in `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs`: `ScheduleKind` enum (`AtQueueStart`/`OncePerRun`/`EveryStep`/`TimerTimeOfDay`/`TimerRelative`/`LiveSchedule`/`SelfReschedule`), records `QueueMonitorSnapshot`, `QueueMonitorItem`, `RunOutcome` per [data-model.md](data-model.md) §2.
- [X] T005 [P] Create wire DTOs `src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs` and `src/GameBot.Service/Contracts/Queues/QueueMonitorItemResponse.cs` (camelCase; `scheduleKind` as string enum) per [contracts/queue-monitor.md](contracts/queue-monitor.md).
- [X] T006 Add `IQueueMonitorService` in `src/GameBot.Service/Services/QueueExecution/IQueueMonitorService.cs` with `Task<QueueMonitorSnapshot> BuildAsync(string queueId, CancellationToken ct = default)`.
- [X] T007 Implement the envelope-only `QueueMonitorService` in `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`: inject `IQueueRunRegistry`, `IQueueRepository`, `IQueueTemplateRepository`, `ISequenceRepository`, `IExecutionLogService`, `TimeProvider`. For now return `Running` from the registry, queue `Name`, `CycleExecution`/`RunStartedAt` from the handle when running, and empty `Current`/`Upcoming`, `LastOutcome = null`. Define `NothingScheduled` correctly from the start — `true` only when the run has **no** schedulable work of **any** kind: no template entries (AtQueueStart/OncePerRun/EveryStep/Timer) AND no pending live schedules (`handle.PendingLiveSchedules`) AND no pending self-reschedule timer firings (`handle.SnapshotPendingTimerFirings()`). This keeps FR-011 correct even for an MVP that ships without US3 (e.g. a live-scheduled-only run must not read as "nothing scheduled"). (US1 fills `Current`/`Upcoming`; US3 adds `LastOutcome` and the idle "waiting" label.)
- [X] T008 Register `IQueueMonitorService → QueueMonitorService` in `src/GameBot.Service/GameBotServiceSetup.cs` alongside the other QueueExecution services.
- [X] T009 Add `GET {id}/monitor` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: 404 when the queue is unknown; otherwise 200 with `BuildAsync(...)` projected to `QueueMonitorResponse` (including `running:false` when not running). Keep the endpoint map thin (delegate to the service + a small projector helper).
- [X] T010 [P] Add web-ui client types + call in `src/web-ui/src/services/queues.ts`: `QueueMonitorItemDto`, `QueueMonitorDto` (matching the contract), and `getQueueMonitor(id) => getJson<QueueMonitorDto>(\`${base}/${id}/monitor\`)`.

**Checkpoint**: `GET /api/queues/{id}/monitor` returns a valid running/not-running envelope; the web-ui can call it. User stories can now begin.

---

## Phase 3: User Story 1 - Watch a running queue like a playlist (Priority: P1) 🎯 MVP

**Goal**: The monitor shows the sequence running **now** and the ordered **up-next** list, each with a
schedule reason and expected time, updating itself every ~2.5s.

**Independent Test**: Start a queue mixing at-start/once-per-run/every-step/timed entries, open it, and
confirm the ordered upcoming list with reasons + times, a highlighted "now" item, and self-updating
without manual refresh.

### Tests for User Story 1 ⚠️ (write first, ensure they fail)

- [X] T011 [P] [US1] Projection unit tests in `tests/unit/QueueMonitorServiceTests.cs`: with a fake `TimeProvider` and a hand-built `QueueRunHandle` + fake template repo, assert — OncePerRun spine in template order (`reason`, `relativeLabel` next/up-next); EveryStep surfaced once as "After Every Step"; TimerTimeOfDay → next-eligible `ExpectedAt` (today vs tomorrow); TimerRelative → `RunStartedAt + offset`; LiveSchedule → exact `fireAt`; SelfReschedule Timer → exact `FireAt`; timed items sorted ascending by `ExpectedAt`; `Current` reflects `CurrentSequenceId` and is excluded from `Upcoming`; and, for a **cycling** queue, OncePerRun/EveryStep items carry `Repeats:true` while a non-cycling queue leaves them `false` (FR-008).
- [X] T012 [P] [US1] Component test `src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx`: mock `getQueueMonitor`; assert the "Now" row and ordered "Up next" list render; advance Jest fake timers ~2.5s and assert a re-poll updates the list.

### Implementation for User Story 1

- [X] T013 [US1] Implement the projection body in `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`: load the linked `QueueTemplate`, partition by `ScheduleType`, resolve sequence names (stale when unresolved), build `Current` from the handle's `CurrentSequenceId`/`StartedAt`, and build `Upcoming` per [data-model.md](data-model.md) §3 (OncePerRun spine → EveryStep annotation → merged timed/live/self-reschedule sorted by `ExpectedAt`). Keep helpers small (per-kind mappers) under ~50 LOC each.
- [X] T014 [US1] Compute best-effort `ExpectedAt`/`Reason` for template timers in a small pure helper (time-of-day next-eligible; relative = anchor + offset) in `QueueMonitorService.cs`; pull live schedules from `handle.PendingLiveSchedules` and self-reschedule firings from `handle.SnapshotPendingTimerFirings()`.
- [X] T015 [P] [US1] Build the `QueueMonitor` component in `src/web-ui/src/components/queues/QueueMonitor.tsx` + `QueueMonitor.css`: read-only "Now" row + "Up next" list (name, reason, expected time / relative label, repeats badge); poll `getQueueMonitor` on a ~2.5s `setInterval` (within the spec's 2–3s target, FR-007) while mounted, clearing on unmount. No run controls. Render a neutral "Nothing scheduled" empty state when `nothingScheduled` is true (so the component is correct even if the release stops at US1; US3 enriches the idle/ended states).
- [X] T016 [US1] Integration test `tests/integration/QueueMonitorEndpointTests.cs`: `GET {id}/monitor` returns a populated snapshot (current + ordered upcoming) for a running queue and 404 for an unknown id; assert `expectedAt` values serialize as ISO-8601 **with a numeric offset** (FR-014), not a bare/UTC-`Z` local time.

**Checkpoint**: Opening a running queue via the component shows a live, correct playlist. MVP complete.

---

## Phase 4: User Story 2 - Monitor page for running, edit page for stopped (Priority: P1)

**Goal**: Opening a queue shows the monitor when Running and the existing editor when Stopped; a
running→stopped transition flips the open monitor to an ended state with a path back to the editor.

**Independent Test**: Open a stopped queue → editor; open/started a running queue → monitor; stop it →
monitor shows "ended" and the editor is reachable again.

### Tests for User Story 2 ⚠️ (write first, ensure they fail)

- [X] T017 [P] [US2] Page test `src/web-ui/src/pages/__tests__/QueuesPage.monitor.spec.tsx`: opening a `Running` queue renders `QueueMonitor` (not the editor form); opening a `Stopped` queue renders the editor; a monitor poll returning `running:false` surfaces the ended state and a way back to the editor. Mock/stub the `QueueMonitor` component so routing is asserted independently of US1's rendering (keeps US2 testable even if US1 is incomplete).

### Implementation for User Story 2

- [X] T018 [US2] Route views by status in `src/web-ui/src/pages/QueuesPage.tsx`: when the opened queue is `Running`, render `<QueueMonitor queueId=…>` instead of the `QueueForm` editor; keep the editor for `Stopped`. Add a name-click / "Monitor" affordance to open the panel for a running queue (Edit stays disabled while running). Leave Start/Stop/Schedule controls in the overview row unchanged (FR-012).
- [X] T019 [US2] Handle the running→stopped transition in `QueuesPage.tsx`/`QueueMonitor.tsx`: when a poll returns `running:false`, stop polling and show the "run ended / not running" state with the option to return to the editor (and a link to Execution Logs).

**Checkpoint**: View routing is correct in both directions and survives a mid-view stop.

---

## Phase 5: User Story 3 - Understand a queue with nothing imminent (Priority: P2)

**Goal**: A running-but-idle queue reads as alive and waiting (what it waits for and when); a
running queue with no schedule reads as "nothing scheduled"; a stopped queue surfaces its last outcome.

**Independent Test**: A non-cycling queue whose only work is a far-future timer shows "waiting until
<time>"; an empty running queue shows "nothing scheduled"; a stopped queue shows its last run outcome.

### Tests for User Story 3 ⚠️ (write first, ensure they fail)

- [X] T020 [P] [US3] Extend `tests/unit/QueueMonitorServiceTests.cs`: running with no work of any kind → `NothingScheduled:true` with empty lists; running with **only** a pending live schedule (no template entries) → `NothingScheduled:false` (regression guard for F1); a lone pending future timer → item present with its `ExpectedAt` and a "waiting" `RelativeLabel`; not-running with a prior finalized run → `LastOutcome` populated from `IExecutionLogService.QueryAsync`; not-running with no history → `LastOutcome:null`.
- [X] T021 [P] [US3] Extend `src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx`: renders the "running & waiting until …" state, the "nothing scheduled" empty state, and the "ended — <outcome>" state.

### Implementation for User Story 3

- [X] T022 [US3] Populate `LastOutcome` in `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs` when not running: query `IExecutionLogService.QueryAsync` for the most recent finalized queue-run entry for the queue and map its status + summary to `RunOutcome` (null when none).
- [X] T023 [US3] Add idle "waiting" semantics in `QueueMonitorService.cs`: when running with no imminent OncePerRun step but a pending future firing exists, set the earliest pending timed item's `RelativeLabel` to "waiting" so an idle-but-alive run is legible. (`NothingScheduled` is already computed correctly in T007 — do not redefine it here.)
- [X] T024 [US3] Render the idle/empty/ended states in `src/web-ui/src/components/queues/QueueMonitor.tsx`: "Running — waiting until <time>", "Nothing scheduled", and the ended state showing `lastOutcome.status`/`summary`. Ensure an idle-but-alive queue never looks empty or broken (FR-009/SC-005).

**Checkpoint**: Idle, empty, and ended queues are all legible and never confused with a stuck queue.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Living documentation and final verification (constitution Principle V + Definition of Done).

- [X] T025 [P] Update `docs/architecture.md` Queues section: document `GET /api/queues/{id}/monitor`, the read-only live-plan projection, and the sequence-level "now" tracking; refresh the "Last reviewed" date.
- [X] T026 [P] Add the `Status` line to `specs/072-queue-monitor/spec.md` and a 072 row to `specs/STATUS.md` consistent with the other entries.
- [X] T027 Verify coverage ≥80% line / ≥70% branch on touched backend areas and run the full gates: `dotnet build` + `dotnet test`, and in `src/web-ui` `npm run build` + `npm test`. Fix any regressions (hard stop per constitution).
- [ ] T028 Run the [quickstart.md](quickstart.md) manual walkthrough end-to-end against a live queue (mixed schedule types + cycling) to confirm the playlist, live-schedule appearance, and stop transition. _(Not run in the implementation environment — requires a live emulator/game. The equivalent scenarios are covered by automated tests: the projection matrix in [QueueMonitorServiceTests.cs](../../tests/unit/Queues/QueueMonitorServiceTests.cs), the endpoint integration in [QueueMonitorEndpointTests.cs](../../tests/integration/Queues/QueueMonitorEndpointTests.cs), and the component/routing specs QueueMonitor.test.tsx + QueuesPage.monitor.spec.tsx.)_

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: after Setup — **BLOCKS all user stories**. Within it: T002→T003 (same area); T004/T005/T010 are [P]; T006→T007→T008/T009 (service before DI/endpoint); T007 depends on T004.
- **US1 (Phase 3)**: after Foundational. MVP.
- **US2 (Phase 4)**: after Foundational; uses the `QueueMonitor` component from US1 (T015) — do US1 first, or stub the component to test routing independently.
- **US3 (Phase 5)**: after Foundational; extends the projection (T013/T014) and component (T015), so best after US1.
- **Polish (Phase 6)**: after all desired stories.

### User Story Dependencies

- **US1 (P1)**: independent given Foundational — the core deliverable.
- **US2 (P1)**: depends on Foundational's `running` flag + endpoint; consumes US1's component.
- **US3 (P2)**: depends on Foundational; builds on US1's projection/component.

### Within Each User Story

- Tests written first and failing before implementation.
- Backend projection before endpoint/integration assertions; service before UI consumption.

### Parallel Opportunities

- Foundational: T004, T005, T010 in parallel; T002 (backend handle) and T010 (web-ui service) are independent.
- US1: T011 (backend test) ‖ T012 (web-ui test); T013/T014 (backend) ‖ T015 (web-ui component).
- US3: T020 (backend test) ‖ T021 (web-ui test).
- Polish: T025 ‖ T026.

---

## Parallel Example: User Story 1

```bash
# Tests first (parallel — different files):
Task: "Projection unit tests in tests/unit/QueueMonitorServiceTests.cs"          # T011
Task: "Component test in src/web-ui/src/components/queues/__tests__/QueueMonitor.test.tsx"  # T012

# Then implementation (backend ‖ web-ui):
Task: "Projection body in QueueMonitorService.cs"                                 # T013/T014
Task: "QueueMonitor.tsx + QueueMonitor.css with ~2.5s polling"                    # T015
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational (blocks everything) → 3. Phase 3 US1 →
   **STOP & VALIDATE**: a running queue shows a correct, self-updating playlist.

### Incremental Delivery

1. Foundational → endpoint envelope + client call working.
2. US1 → live playlist content (MVP, demoable).
3. US2 → monitor-vs-editor routing + stop transition.
4. US3 → idle/empty/ended legibility.
5. Polish → docs + gates.

---

## Notes

- [P] = different files, no dependencies. [Story] labels map tasks to US1/US2/US3 for traceability.
- The only queue-engine change is T002/T003 (current-sequence tracking) — keep scheduling behavior identical.
- Times cross the wire as ISO-8601 with offset (service local clock) for one unambiguous local time (FR-014).
- Commit after each task or logical group; never mark a phase complete on a red build/test (constitution hard stop).
