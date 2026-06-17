---
description: "Task list for Relative-Time Sequence Scheduling (feature 059)"
---

# Tasks: Relative-Time Sequence Scheduling

**Input**: Design documents from `/specs/059-relative-schedule-time/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the project constitution (Principle II) mandates unit/contract/integration tests for executable logic, and feature 053 (which this extends) is covered the same way.

**Organization**: Tasks are grouped by user story so each can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 maps to the spec's user stories
- Exact file paths are included in each task

## Path Conventions

- Backend: `src/GameBot.Domain/`, `src/GameBot.Service/`, tests under `tests/{unit,contract,integration}/`
- Web UI: `src/web-ui/src/`, Jest specs colocated in `__tests__/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a known-green baseline before changes (constitution NON-NEGOTIABLE gate).

- [X] T001 Verify green baseline: run `dotnet test c:\src\GameBot\GameBot.sln` and `npm --prefix c:\src\GameBot\src\web-ui run build` + `npm --prefix c:\src\GameBot\src\web-ui test`; record results so regressions are attributable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared seams both P1 stories depend on — offset parsing/validation and a deterministic clock.

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

- [X] T002 [P] Add a relative-offset parse/validate helper (parse `"HH:mm:ss"`, require `>= TimeSpan.Zero` and `<= 24:00:00`, return parsed `TimeSpan` or a descriptive failure) in `src/GameBot.Service/Services/QueueExecution/RelativeOffsetParser.cs`, with a unit test in `tests/unit/Queues/RelativeOffsetValidationTests.cs`. CamelCase method names only.
- [X] T003 [P] Inject `System.TimeProvider` into `QueueExecutionService` (constructor param defaulting to `TimeProvider.System`); replace all direct `DateTime.Now` reads (existing time-of-day timer evaluation) with `_timeProvider.GetLocalNow()` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`; register `TimeProvider.System` in DI in `src/GameBot.Service/Program.cs`. Keep existing timer tests green.
- [X] T004 [P] Add a minimal in-repo `FakeTimeProvider` stub (subclass of `System.TimeProvider` exposing a settable/advanceable local now) under `tests/unit/Queues/FakeTimeProvider.cs` so elapsed-offset logic can be driven deterministically. Use the in-repo stub rather than an external test package to honor the plan's "no new external packages" constraint.

**Checkpoint**: Offset validation + controllable clock available — P1 stories can begin.

---

## Phase 3: User Story 1 - Template relative-offset timer (Priority: P1) 🎯 MVP

**Goal**: A `Timer` template entry can carry a relative offset anchored to run start; it fires once per run when the offset elapses, recomputes each run, and counts toward the run's completed-step total.

**Independent Test**: Save a template with a `Timer` entry using `timerRelativeOffset` of a small duration; start a queue (fake clock); confirm it does not fire before the offset, fires once at the first iteration boundary after it elapses, not again that run, and fires again on a fresh run.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [X] T005 [P] [US1] Contract test — template API accepts and returns `timerRelativeOffset`, rejects a `Timer` entry with both/neither timer fields, and rejects negative/out-of-range offsets — in `tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs`.
- [X] T006 [P] [US1] Integration test — a relative-offset entry fires once after the offset (fake clock), recomputes on a second run, and increments the run's executed total — in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`.
- [X] T007 [P] [US1] Unit test — `QueueExecutionService` relative-timer: fires once per run, counts toward `executed`, time-of-day timers/once-per-run/every-step behavior unchanged, and a **failed** relative-timer firing is non-fatal (recorded, run continues, counted in `failed`) per FR-016 — in `tests/unit/Queues/QueueExecutionServiceTests.cs`.

### Implementation for User Story 1

- [X] T008 [US1] Add `TimerRelativeOffset` (`TimeSpan?`) to `QueueTemplateEntry`, documenting relative-vs-time-of-day mode inference and the exactly-one invariant — `src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`.
- [X] T009 [P] [US1] Add `TimerRelativeOffset` (`string?`, `"HH:mm:ss"`) to `TemplateEntrySaveRequest` and `QueueTemplateEntryResponse` — `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs` and `.../QueueTemplateDetailResponse.cs`.
- [X] T010 [P] [US1] In `QueueTemplatesEndpoints.cs` validate a `Timer` entry has exactly one of `timerTimeOfDay`/`timerRelativeOffset`, parse+range-check the offset via the T002 helper, persist `TimerRelativeOffset`, and project it in `BuildDetailAsync` — `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`.
- [X] T011 [US1] In `QueueExecutionService.RunAsync` capture the run-start anchor (`runStartedAt = _timeProvider.GetLocalNow()`), partition relative `Timer` entries, evaluate them at each iteration boundary (`now - runStartedAt >= offset`), fire once per run via `RunOneSequenceAsync` tracked by a per-run `HashSet<int>`, and increment `executed` for each firing (FR-016a) — `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`. (Depends on T003, T008.)

**Checkpoint**: Template relative-offset timers fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Live relative schedule via API (Priority: P1)

**Goal**: A `POST /api/queues/{id}/live-schedule` call schedules any library sequence to fire once after an offset from now, ephemeral to the running queue, most-recent-wins, counted toward the run total.

**Independent Test**: With a queue running (fake clock), POST a live schedule for a known sequence with a small offset; advance the clock; confirm it fires once at an iteration boundary, not again, the template on disk is unchanged, and a re-POST schedules it again.

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL)

- [X] T012 [P] [US2] Contract test — live-schedule endpoint returns 200 with `expectedFireAt`; 400 for malformed/negative offset; 404 for unknown queue or unknown sequence; 409 when no run is active; and a successful live-schedule call leaves the linked template unchanged on disk (SC-004) — in `tests/contract/Queues/QueueLiveScheduleApiContractTests.cs`.
- [X] T013 [P] [US2] Unit test — `ScheduleRelative` upsert/most-recent-wins, `NotRunning` when no run, live firing happens once and is removed, counts toward `executed`, and a **failed** live firing is non-fatal (recorded, run continues, counted in `failed`) per FR-016 — in `tests/unit/Queues/QueueExecutionServiceTests.cs`.

### Implementation for User Story 2

- [X] T014 [P] [US2] Add `RunStartedAt` (`DateTimeOffset`) and `PendingLiveSchedules` (`ConcurrentDictionary<string,DateTimeOffset>`) to `QueueRunHandle`, documenting ephemerality and most-recent-wins keying by sequence id — `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`.
- [X] T015 [US2] Add `LiveScheduleOutcome` enum and `ScheduleRelative(string queueId, string sequenceId, TimeSpan offset)` to `IQueueExecutionService` and implement it in `QueueExecutionService` (look up handle in `_runs`; `NotRunning` if absent; else upsert `sequenceId -> now + offset`) — `src/GameBot.Service/Services/QueueExecution/IQueueExecutionService.cs` and `QueueExecutionService.cs`. (Depends on T014.)
- [X] T016 [US2] In `QueueExecutionService.RunAsync` iteration boundary, snapshot due `PendingLiveSchedules` (`fireAt <= now`), fire each once via `RunOneSequenceAsync`, `TryRemove` it, and increment `executed` (FR-016a) — `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`. (Depends on T011, T015 — same file as T011, sequence after it.)
- [X] T017 [P] [US2] Add `LiveScheduleRequest`/`LiveScheduleResponse` in `src/GameBot.Service/Contracts/Queues/` and map `POST {id}/live-schedule` in `QueuesEndpoints.cs`: validate offset (T002 helper), 404 on unknown queue/sequence (`ISequenceRepository`), call `ScheduleRelative`, 409 on `NotRunning`, else 200 with `expectedFireAt` — `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`. (Depends on T015.)

**Checkpoint**: Live relative scheduling works via API; US1 + US2 both independently functional.

---

## Phase 5: User Story 4 - Configure relative offsets in the template editor UI (Priority: P2)

**Goal**: The queue-template editor lets the operator pick, per `Timer` entry, time-of-day vs relative offset, enter the offset, persist it, and see a distinguishing badge.

**Independent Test**: In the editor, set an entry to `Timer` → Relative, enter 10 min 0 sec, save, reopen, and confirm the mode and value are preserved and the entry shows a relative badge.

### Tests for User Story 4 ⚠️ (write first, ensure they FAIL)

- [X] T018 [P] [US4] Jest test — `QueueEntryList` renders the time-of-day/relative mode toggle for `Timer`, shows offset inputs in relative mode, validates non-negative before emitting, and renders the relative badge — in `src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx`.

### Implementation for User Story 4

- [X] T019 [P] [US4] Add `timerRelativeOffset?: string | null` to `QueueTemplateEntryDto` and `TemplateEntrySaveDto` (and include it when building save payloads) — `src/web-ui/src/services/queueTemplates.ts`.
- [X] T020 [US4] In `QueueEntryList.tsx` add a time-of-day/relative mode toggle shown when schedule type is `Timer`, hours/minutes/seconds inputs composing `"HH:mm:ss"`, client-side non-negative validation, and a relative-timer badge — `src/web-ui/src/components/queues/QueueEntryList.tsx` (and wire the new `EntrySchedule` field through `QueuesPage.tsx` template-edit state). (Depends on T019; backend T010 for end-to-end.)

**Checkpoint**: Template relative offsets are fully authorable in the UI.

---

## Phase 6: User Story 3 - Live-schedule a sequence from the UI (Priority: P2)

**Goal**: From the running-queue view, the operator schedules a selected sequence after a relative offset and sees a pending indicator until it fires.

**Independent Test**: With a queue running in the UI, use the "Schedule in mm:ss" control on a sequence, observe the pending indication and expected fire time, and confirm it fires once.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL)

- [X] T021 [P] [US3] Jest test — the running-queue live-schedule control submits a valid offset (calls the client), blocks negative/blank input with a message, and shows a pending indicator with expected fire time — in `src/web-ui/src/pages/__tests__/QueuesPage.liveSchedule.spec.tsx`.

### Implementation for User Story 3

- [X] T022 [P] [US3] Add `liveScheduleSequence(queueId, sequenceId, offset)` returning `{ sequenceId, offset, expectedFireAt }` — `src/web-ui/src/services/queues.ts`.
- [X] T023 [US3] In `QueuesPage.tsx` add a per-sequence "Schedule in mm:ss" control on the running-queue view that validates input, calls `liveScheduleSequence`, surfaces success/error, and shows a pending indicator labeling the time as the **expected (earliest)** fire time — actual firing is the first iteration boundary at/after it (FR-020, avoid over-promising an exact instant) — `src/web-ui/src/pages/QueuesPage.tsx`. (Depends on T022; backend T017 for end-to-end.)

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T024 [P] Update API docs/notes for the new template field and live-schedule endpoint if a central API reference exists; otherwise confirm `specs/059-relative-schedule-time/quickstart.md` matches the shipped routes/fields.
- [X] T025 [P] Add edge-case unit coverage: offset `00:00:00` fires at first boundary; offset that never elapses never fires; multiple relative/live firings all precede regular steps in an iteration — extend `tests/unit/Queues/QueueExecutionServiceTests.cs`.
- [X] T026 Run quickstart.md validation end-to-end and the full green gate: `dotnet test c:\src\GameBot\GameBot.sln`, `npm --prefix c:\src\GameBot\src\web-ui run build`, `npm --prefix c:\src\GameBot\src\web-ui test`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: After Setup — BLOCKS all user stories (offset helper + `TimeProvider` seam).
- **US1 (Phase 3)** and **US2 (Phase 4)**: After Foundational. Independent in their API/UI/contract files, but **both edit `QueueExecutionService.cs`** (T011 then T016) so the runtime edits are sequenced US1 → US2.
- **US4 (Phase 5)**: After Foundational; end-to-end depends on US1 backend (T008–T010).
- **US3 (Phase 6)**: After Foundational; end-to-end depends on US2 backend (T014–T017).
- **Polish (Phase 7)**: After all targeted stories.

### Within Each User Story

- Tests written first and failing before implementation.
- Domain model → DTOs/contracts → endpoints/runtime.
- Web-ui: service client → component.

### Parallel Opportunities

- Foundational: T002, T003, T004 in parallel (different files).
- US1 tests T005–T007 in parallel; impl T009/T010 in parallel after T008 (T011 separate file, also after T008).
- US2 tests T012–T013 in parallel; impl T014 then T017 in parallel with T015 once T014 lands; T016 after T011 (shared file).
- US4 and US3 are independent of each other and can be built in parallel once their backends exist.

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Contract test timerRelativeOffset in tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs"
Task: "Integration test relative timer in tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs"
Task: "Unit test relative timer in tests/unit/Queues/QueueExecutionServiceTests.cs"

# After T008 (model), parallel impl on different files:
Task: "Extend DTOs in src/GameBot.Service/Contracts/QueueTemplates/*.cs"
Task: "Validate/persist/project in src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP and VALIDATE**: relative-offset template timers fire once per run and count toward the total.
3. Demo the MVP.

### Incremental Delivery

1. Setup + Foundational → ready.
2. US1 (template relative timer) → MVP.
3. US2 (live API scheduling) → both P1 stories shipped.
4. US4 (template editor UI) → relative offsets authorable without the API.
5. US3 (live UI) → operators schedule live without the API.

---

## Notes

- [P] = different files, no incomplete dependencies.
- US1 and US2 share `QueueExecutionService.cs` for their runtime edits — sequence T011 before T016.
- Relative/live firings count toward `executed`; time-of-day timers and every-step entries do not — preserve this distinction.
- Verify each test fails before implementing; keep the existing 053 timer behavior green throughout (SC-008).
- Commit after each task or logical group.
