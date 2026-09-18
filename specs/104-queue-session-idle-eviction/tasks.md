# Tasks: Queue sessions survive idle gaps between scheduled runs

**Input**: Design documents from `specs/104-queue-session-idle-eviction/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested — the issue's acceptance item 5 and constitution II (a bug fix ships a test that
fails before the fix). Test tasks precede the implementation they cover.

**Format**: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Confirm a clean baseline: build `C:\src\GameBot\GameBot.sln` and run the unit tests under `tests/unit/Queues/` and `tests/unit/Sessions/`; record any pre-existing failures so they are not attributed to this feature

---

## Phase 2: Foundational (blocks US1 and US2)

- [ ] T002 Add `public string? OwnerQueueId { get; set; }` with an XML doc comment ("id of the queue that bound this session; null for an ad-hoc session; an owned session is never idle-retired") to `src/GameBot.Domain/Sessions/EmulatorSession.cs`

**Checkpoint**: solution builds; no behaviour change yet.

---

## Phase 3: User Story 1 - A scheduled queue wakes up after a long idle (Priority: P1) 🎯 MVP

**Goal**: A queue-owned session is never retired by the idle sweep, so the next firing after any idle
gap finds its session (FR-001, FR-002, FR-008).

**Independent Test**: A queue-owned session with `LastActivity` backdated past the timeout survives a
sweep while an ad-hoc one is retired; a queue marks the session it binds as owned.

### Tests for User Story 1 (write first, confirm they fail)

- [ ] T003 [P] [US1] Create `tests/unit/Sessions/SessionIdleEvictionTests.cs` (`[SupportedOSPlatform("windows")]`, `GAMEBOT_USE_ADB=false` set/reset in try/finally like `SessionCapacityMessageTests`): construct `SessionManager` with `IdleTimeoutSeconds = 60`; create two sessions, set `OwnerQueueId = "q1"` on one, backdate both `LastActivity` by 1 hour, call `ListSessions()`; assert the owned session remains and the ad-hoc one is gone. Add a second fact: after `StopSession` on the owned session it is gone (owned sessions still end on stop)
- [ ] T004 [P] [US1] In `tests/unit/Queues/QueueExecutionServiceTests.cs` add a fact `QueueMarksItsSessionAsOwnedByTheQueue`: start a queue whose sequence blocks, then assert `h.Sessions.ListSessions()` contains a session with `OwnerQueueId == "q1"`

### Implementation for User Story 1

- [ ] T005 [US1] In `src/GameBot.Emulator/Session/SessionManager.cs` `CleanupIdleSessions`, skip sessions whose `OwnerQueueId` is non-null (a one-line `continue` with a comment citing #217); leave every other path unchanged
- [ ] T006 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` extract the start-of-run bind (`CreateSession($"queue:{queue.Id}", queue.EmulatorSerial)` → set `OwnerQueueId = queue.Id` → `StartCapture` when a serial is bound) into a private `string BindQueueSession(ExecutionQueue queue)` that returns the new session id; call it from the existing start `try/catch (InvalidOperationException or KeyNotFoundException)` keeping the existing "emulator could not be reached" failure text and the `handle.SessionId` assignment
- [ ] T007 [US1] Run T003/T004 and the whole `tests/unit/Queues` and `tests/unit/Sessions` folders; all green

**Checkpoint**: the reported production failure can no longer happen.

---

## Phase 4: User Story 2 - Re-bind when the session is gone but the device is there (Priority: P2)

**Goal**: At every pre-firing check a missing session triggers one re-bind on the same serial; only a
failed re-bind fails the run (FR-003..FR-006).

**Independent Test**: Evict the queue's session between firings; the next firing and its
before-each-run entries run on a new session and the queue keeps running. With re-bind impossible, the
run fails with "connection lost".

### Tests for User Story 2 (write first, confirm they fail)

- [ ] T008 [US2] In `tests/unit/Queues/QueueExecutionServiceTests.cs` extend `FakeSessionManager` with `public void Evict(string id)` that removes the session from its dictionary WITHOUT touching `Connected` or `Stopped` (simulates an idle sweep), and a `public int Created` counter incremented in `CreateSession`
- [ ] T009 [US2] Re-express `ConnectionLostMidRunFailsTheRun` (T031) in `tests/unit/Queues/QueueExecutionServiceTests.cs`: in the handler for "A" also set `h.Sessions.CreateThrows = new KeyNotFoundException("ADB device 'emu-1' not found")` before `Connected = false`, with a comment that the device is genuinely gone; assertions unchanged
- [ ] T010 [US2] Re-express `ConnectionLostBeforeBeforeEachRunFailsTheRun` (T008(g)) in `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs` the same way; assertions unchanged
- [ ] T011 [US2] In `tests/unit/Queues/QueueExecutionServiceTests.cs` add `SessionEvictedBetweenFiringsIsReboundAndTheRunContinues`: queue with entries A, B (OncePerRun); in the handler for "A" capture the single listed session's id as `oldId` and evict it (`h.Sessions.Evict(oldId)`); assert executed `A, B`, final status success, `h.Sessions.Created == 2`, and `h.Sessions.Stopped` equals exactly one id that is not `oldId` (teardown stops the re-bound session)
- [ ] T011a [US2] In `tests/unit/Queues/QueueExecutionServiceTests.cs` add `ReboundFailsOnCapacityFailsTheRun`: as T009 but `CreateThrows = new InvalidOperationException(SessionManager.CapacityExceededMessage(8, 8))` and `h.Sessions.Evict(currentId)` instead of flipping `Connected`; assert executed `A`, final status failure, summary contains "connection lost"
- [ ] T011b [US2] In `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs` add `SeveralFiringsDueAtOneWakeUpRebindOnce`: `FakeTimeProvider` harness, entries `OncePerRun("A")`, `RelativeTimer("T1", 10 min)`, `RelativeTimer("T2", 10 min)`, `BeforeEachRun("B")`; after A, evict the session and advance 10 minutes; wait for T1 and T2; assert `h.Sessions.Created == 2`, then stop the queue
- [ ] T011c [US2] In `tests/unit/Queues/QueueExecutionServiceTests.cs` add `ReboundMovesBackgroundCaptureToTheNewSession`: build a `QueueExecutionService` with a real `BackgroundScreenCaptureService(_ => new NullProvider-style IScreenSource stub, 500, NullLogger)` passed as its capture-service argument (as `tests/unit/Sessions/SessionServiceCaptureLifecycleTests.cs` constructs one; dispose it at the end); run the T011 scenario with a blocking "B" so the run is still live, then assert `GetCaptureMetrics(newId)` is non-null and `GetCaptureMetrics(oldId)` is null; stop the queue
- [ ] T012 [US2] In `tests/unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs` add `SessionEvictedDuringIdleIsReboundBeforeBeforeEachRun`: `FakeTimeProvider` harness, entries `OncePerRun("A")`, `RelativeTimer("T", 10 min)`, `BeforeEachRun("B")`; after A runs, evict the session, advance the clock 10 minutes; wait for T; assert executed `A, B, T` and the queue status is still Running (not stopped), then stop it
- [ ] T013 [US2] In `tests/unit/Queues/QueueExecutionServiceTests.cs` add `ReboundSessionIsOwnedByTheQueue`: after the re-bind of T011's scenario, the new session has `OwnerQueueId == "q1"`

### Implementation for User Story 2

- [ ] T014 [US2] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` add a `SessionRebound` Warning log message to the `QueueExecutionLog` class (queue id, serial, old session id, new session id) using the file's existing `LoggerMessage` pattern and the next free event id
- [ ] T015 [US2] In `RunAsync` add a local function `void EnsureSessionBound()` next to `RunEveryStepPassAsync`: return when `_sessions.GetSession(sessionId)` is non-null; otherwise best-effort `_captureService?.StopCapture(oldId)`, then one `BindQueueSession(queue)` inside `try/catch (InvalidOperationException or KeyNotFoundException)` — on success assign `sessionId` and `handle.SessionId` and log `SessionRebound`; on failure `throw new QueueConnectionLostException()`. XML/inline comment cites #217 and FR-003/FR-004
- [ ] T016 [US2] Replace every `if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();` in `RunAsync` (every-step, before-each-run, at-start, next-cycle, time-of-day, daily retry, relative, live, self-reschedule timer, once-per-run, once-per-run reschedule) with `EnsureSessionBound();`; grep afterwards to confirm no occurrence remains in the file
- [ ] T017 [US2] Run T009..T013 (including T011a–T011c) plus all of `tests/unit/Queues`; all green

**Checkpoint**: any other way of losing the binding is recovered at the next firing.

---

## Phase 5: User Story 3 - Screenshot 404 wording (Priority: P3)

**Goal**: FR-007.

**Independent Test**: `GET /api/emulator/screenshot?serial=<unbound>` returns 404 `session_not_found` whose message names how to bind a session.

- [ ] T018 [P] [US3] In `tests/contract/EmulatorScreenshotSelectorContractTests.cs`, extend the existing `session_not_found` test to assert the `message` contains `No running session is bound to device` and `POST /api/sessions/start`
- [ ] T019 [US3] In `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs` change the `?serial=` 404 message to `No running session is bound to device '<serial>'. Start a session (POST /api/sessions/start) or a queue on that device to bind one.`; code and status unchanged
- [ ] T020 [US3] Run the contract test from T018; green

---

## Phase 6: Polish & Cross-Cutting

- [ ] T021 [P] Update `docs/architecture.md`: in the section describing sessions/queue runs, state that queue-bound sessions carry an owner and are exempt from the `Service:Sessions:IdleTimeoutSeconds` sweep, that a queue re-binds once at a firing when its session is missing and fails with "emulator connection lost" only when the re-bind fails, and refresh the "Last reviewed" line (feature 104, #217)
- [ ] T022 [P] Add a `### Fixed` entry under `## [Unreleased]` in `CHANGELOG.md` (create the subsection if absent, after `### Added`) describing the #217 fix, the re-bind, the screenshot message, and compatibility (ad-hoc sessions unchanged; failure message unchanged)
- [ ] T023 [P] Set `**Status**: Implemented` in `specs/104-queue-session-idle-eviction/spec.md` and add row `| 104 | Queue sessions survive idle gaps between scheduled runs | Implemented |` to `specs/STATUS.md` after row 103
- [ ] T024 Full gate: `dotnet build C:\src\GameBot\GameBot.sln -c Release` (warnings as errors) and `dotnet test` for unit, integration (including `ResourceLimitsTests.IdleSessionIsEvictedAfterTimeout`, which must still pass unchanged — FR-002) and contract projects; fix anything red before committing

---

## Dependencies & Execution Order

- T001 → T002 → US1 (T003–T007) → US2 (T008–T017) → US3 (T018–T020) → Polish (T021–T024)
- US2 depends on US1's `BindQueueSession` (T006). US3 is independent of US1/US2 and may run any time after T001.
- Within a story, tests are written first and must fail before the implementation task.

## Parallel Opportunities

- T003 and T004 (different files).
- T018 can run alongside any US1/US2 task.
- T021, T022, T023 are independent documentation files.

## Implementation Strategy

MVP = Phase 3 (US1): it alone removes the production failure. US2 adds recovery for any other loss of
the binding; US3 is message wording. Deliver all three in one PR since each is small.
