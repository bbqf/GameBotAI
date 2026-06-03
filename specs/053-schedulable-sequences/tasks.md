# Tasks: Queue Sequence Scheduling

**Input**: Design documents from `specs/053-schedulable-sequences/`  
**Prerequisites**: plan.md ✅ · spec.md ✅ · research.md ✅ · data-model.md ✅ · contracts/ ✅

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other [P] tasks in the same phase (different files, no blocking deps)
- **[Story]**: User story label from spec.md (US1–US4)
- All paths are relative to the repository root

---

## Phase 1: Setup (Baseline Verification)

**Purpose**: Confirm the existing build and tests are green before any changes.

- [X] T001 Verify `dotnet build` succeeds and `dotnet test tests/unit` passes — record baseline (no file changes; gate check only)
- [X] T002 Verify `vite build` succeeds in `src/web-ui/` — record baseline

---

## Phase 2: Foundational — Domain Model & API Contracts

**Purpose**: Extend `QueueTemplateEntry` with schedule type and update the API layer. Every subsequent phase depends on these changes.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

### 2A: Domain types (blocks all execution and UI work)

- [X] T003 Create `ScheduleType` enum with `[JsonConverter(typeof(JsonStringEnumConverter))]` in `src/GameBot.Domain/QueueTemplates/ScheduleType.cs` — values: `OncePerRun = 0`, `EveryStep = 1`, `Timer = 2`; add XML `<summary>` to the enum declaration and each member describing its execution semantics
- [X] T004 Extend `QueueTemplateEntry` with `public ScheduleType ScheduleType { get; set; } = ScheduleType.OncePerRun;` and `public TimeOnly? TimerTimeOfDay { get; set; }` in `src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`; update XML summary for both new properties

### 2B: API contracts (depends on T003–T004; [P] tasks are independent of each other)

- [X] T005 [P] Create `TemplateEntrySaveRequest` with `string? SequenceId`, `string? ScheduleType`, `string? TimerTimeOfDay` in `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs`
- [X] T006 [P] Replace `string[]? SequenceIds` with `TemplateEntrySaveRequest[]? Entries` in `src/GameBot.Service/Contracts/QueueTemplates/SaveQueueTemplateRequest.cs`
- [X] T007 [P] Add `string ScheduleType { get; set; }` and `string? TimerTimeOfDay { get; set; }` to `QueueTemplateEntryResponse` in `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs`

### 2C: Endpoint handler (depends on T005–T007)

- [X] T008 Replace `foreach (var sid in req.SequenceIds)` loop with loop over `req.Entries`, parsing `ScheduleType` string to enum and `TimerTimeOfDay` string to `TimeOnly?`, in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` (`SaveQueueTemplate` handler)
- [X] T009 Add per-entry validation in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`: `SequenceId` non-blank; `ScheduleType` is a recognized value; when `Timer`, `TimerTimeOfDay` must be present and match `HH:mm`; return 400 with descriptive message on failure
- [X] T010 Update `BuildDetailAsync` in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` to populate `ScheduleType` (as string) and `TimerTimeOfDay` (`HH:mm` string or null) in each `QueueTemplateEntryResponse`

### 2D: Update existing integration tests for new request shape (depends on T008–T010)

- [X] T011 Update all existing calls to `POST /api/queue-templates` in `tests/integration/QueueTemplates/QueueTemplatesSaveEndpointTests.cs` to use `entries: [{ sequenceId: "..." }]` instead of `sequenceIds: [...]`

**Checkpoint**: `dotnet build` clean; `dotnet test` passes (existing tests updated and green)

---

## Phase 3: User Story 2 — Every-Step Execution (Priority: P1)

**Goal**: Sequences marked `EveryStep` execute after each once-per-run step (and after the final step) without counting toward run completion.

**Independent Test**: Template with once-per-run steps A, B and every-step sequence C → execution order A → C → B → C; run ends as "completed full run"; `executed` count = 2, not 4.

### Implementation for User Story 2

- [X] T012 [US2] Pre-partition `template.Entries` into three lists at snapshot time — `oncePerRunEntries`, `everyStepEntries`, `timerEntries` — replacing the existing `snapshot` list in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (line ~114)
- [X] T013 [US2] Refactor the `foreach (var sequenceId in snapshot)` inner loop in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` to iterate `oncePerRunEntries` and, after each, run all `everyStepEntries` in order; increment `executed` for once-per-run only (not every-step)
- [X] T014 [US2] Add the `else if (everyStepEntries.Count > 0)` branch in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` for the edge case where there are no once-per-run entries: run every-step sequences once and end (FR-009)
- [X] T015 [US2] Add unit tests covering every-step scenarios in `tests/unit/Queues/QueueExecutionServiceTests.cs`:
  - Every-step fires after each once-per-run step and after the last
  - `executed` count reflects only once-per-run sequences
  - No once-per-run entries: every-step runs exactly once
  - Multiple every-step sequences run in template order after each step
  - Every-step failure is non-fatal; run continues

**Checkpoint**: Every-step scheduling works independently; T015 tests pass; `dotnet build` clean

---

## Phase 4: User Story 3 — Timer Execution (Priority: P1)

**Goal**: Sequences marked `Timer` fire at the start of an iteration when their wall-clock time (server local) has passed, at most once per calendar day per run.

**Independent Test**: Timer time set to one minute ago → fires on first iteration before once-per-run steps; same timer → skipped on all subsequent iterations that same day; timer time set to tomorrow → never fires during a non-cyclic run.

### Implementation for User Story 3

- [X] T016 [US3] Declare and initialize `var timerFiredDate = new Dictionary<int, DateOnly>();` **outside and before the `do {` keyword** (inside the `if (sessionId is not null)` block, after the partition lists) in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` — it must persist across all cycles of the same run; placing it inside the `do { }` body would reset it every cycle and break the once-per-calendar-day invariant
- [X] T017 [US3] Add timer evaluation at the top of the `do` loop in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: for each `timerEntries[i]` where `TimerTimeOfDay` is set, check `TimeOnly.FromDateTime(DateTime.Now) >= entry.TimerTimeOfDay.Value` and `timerFiredDate` does not contain today's date for index `i`; if due, call `RunOneSequenceAsync`, record `timerFiredDate[i] = today`; timer executions are NOT counted toward `executed`
- [X] T018 [US3] Add unit tests covering timer scenarios in `tests/unit/Queues/QueueExecutionServiceTests.cs`:
  - Timer past due → fires once on first iteration
  - Timer not yet due → never fires in non-cyclic run
  - Timer fires once per calendar day in multi-iteration run (second iteration same day → skipped)
  - Timer fires again on next calendar day (simulate day change)
  - Multiple simultaneously-due timers → all fire before regular steps
  - Timer failure is non-fatal; run continues
  - Timer with every-step: timers fire first at iteration start, then regular + every-step pattern follows

**Checkpoint**: Timer scheduling works independently; T018 tests pass; `dotnet build` clean

---

## Phase 5: User Story 1 — Integration Verification & UI (Priority: P1)

**Goal**: All three schedule types are configurable in the template editor UI and observable end-to-end when a queue runs.

**Independent Test**: Create template via UI with all three schedule types, save, reload — types persist. Start queue; observe timer fires first, then once-per-run, then every-step in the execution log.

### 5A: Frontend type updates (depends on T007, parallelizable among themselves)

- [X] T019 [P] [US1] Add `scheduleType: string` and `timerTimeOfDay: string | null` to `QueueTemplateEntryDto`, and define `TemplateEntrySaveDto { sequenceId: string; scheduleType?: string; timerTimeOfDay?: string }` in `src/web-ui/src/services/queueTemplates.ts`; update `saveQueueTemplate` to send `entries` array instead of `sequenceIds`

### 5B: UI schedule type selector (depends on T019)

- [X] T020 [US1] Add per-entry schedule-type state (`Map<entryId, { scheduleType, timerTimeOfDay }>`) to the queue editor's local state in `src/web-ui/src/pages/QueuesPage.tsx`; initialize from loaded template entries when a template is loaded
- [X] T021 [US1] Add a `scheduleType` prop and `onScheduleTypeChange`/`onTimerTimeChange` callbacks to `QueueEntryList` component signature in `src/web-ui/src/components/queues/QueueEntryList.tsx`
- [X] T022 [US1] Add per-entry schedule type dropdown (Once Per Run / Every Step / Timer) to each entry row in `src/web-ui/src/components/queues/QueueEntryList.tsx`; show a conditional `<input type="time">` when Timer is selected
- [X] T023 [US1] Add schedule type badges (`EveryStep` and `Timer` entries show a visual chip/label; `OncePerRun` shows nothing extra) to the entry display in `src/web-ui/src/components/queues/QueueEntryList.tsx` (FR-024)
- [X] T024 [US1] Wire `QueuesPage` save-template flow to collect schedule types from per-entry state and pass them to `saveQueueTemplate` via `entries` array in `src/web-ui/src/pages/QueuesPage.tsx`; wire template-load to restore schedule types into per-entry state

### 5C: Integration test (depends on T008–T010, T013, T017)

- [X] T025 [US1] Create `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs` with tests:
  - Save template with `EveryStep` entry → GET returns `scheduleType: "EveryStep"`, `timerTimeOfDay: null`
  - Save template with `Timer` entry and `timerTimeOfDay: "15:30"` → GET returns both fields correctly
  - Omit `scheduleType` → GET returns `"OncePerRun"` (default)
  - Reload after restart → schedule types persisted in JSON

**Checkpoint**: All three schedule types configurable in UI and API; schedule type info persists and round-trips; T025 tests pass

---

## Phase 6: User Story 4 — API Validation Edge Cases (Priority: P2)

**Goal**: The API rejects invalid schedule-type configurations with clear error messages.

**Independent Test**: POST with `Timer` entry and no `timerTimeOfDay` → 400 with descriptive error. POST with unrecognized `scheduleType` → 400.

- [X] T026 [P] [US4] Add integration test: `Timer` entry missing `timerTimeOfDay` → 400 `invalid_request` in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`
- [X] T027 [P] [US4] Add integration test: unrecognized `scheduleType` string (e.g. `"Weekly"`) → 400 `invalid_request` in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`
- [X] T028 [P] [US4] Add integration test: entry with blank `sequenceId` → 400 `invalid_request` in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`
- [X] T029 [P] [US4] Add integration test: existing pre-feature template file (no `scheduleType` field) → GET returns `"OncePerRun"` for all entries (backward compatibility) in `tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs`

**Checkpoint**: All validation edge cases covered; `dotnet test tests/integration` passes

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T030 Update `BuildSummary` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` to note when every-step or timer sequences ran (e.g., append "N every-step execution(s)" to the completed-full-run summary line when applicable)
- [X] T031 [P] Add Jest tests for updated `QueueEntryList` in `src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx`: schedule type dropdown renders; Timer selected shows time input; EveryStep badge shows; OncePerRun shows no badge
- [X] T032 Run `vite build` and fix any TypeScript errors introduced by the new props and types in `src/web-ui/`
- [X] T033 Execute the quickstart.md end-to-end verification steps (save template with all three types via API → GET → load into queue → start → check execution log)
- [X] T034 Manually time the schedule-type configuration workflow in the UI using a 20-entry template: open queue editor → load/configure all three schedule types → save template → verify round-trip; confirm the complete workflow takes under 2 minutes (SC-007)
- [X] T035 Run full test suite — `dotnet test` (unit + integration + contract) + `npm test` in `src/web-ui/` — confirm all pass and no coverage regressions

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup/Baseline)
  └─▶ Phase 2 (Foundational: domain + API) — BLOCKS all story phases
        ├─▶ Phase 3 (US2: every-step execution)
        │     └─▶ Phase 4 (US3: timer execution) [can also start after Phase 2]
        │           └─▶ Phase 5 (US1: integration + UI) [needs Phase 3 + 4 for execution tests]
        ├─▶ Phase 6 (US4: API validation tests) [can start after Phase 2 independently]
        └─▶ Phase 7 (Polish) [after all story phases]
```

### User Story Dependencies

- **US2 (Phase 3)**: Depends on Phase 2. No dependency on US3, US4.
- **US3 (Phase 4)**: Depends on Phase 2. No dependency on US2 (but shares `RunAsync` changes — complete Phase 3 first to avoid merge conflicts).
- **US1 (Phase 5)**: Depends on Phase 2 (API), Phase 3 (every-step), Phase 4 (timer) for full integration test. UI tasks depend only on Phase 2 (TypeScript types).
- **US4 (Phase 6)**: Depends on Phase 2 (endpoint handler). Fully independent of Phase 3–5.

### Within Each Phase

- Domain types before contracts (T003 → T004 → T005/T006/T007)
- Contracts before endpoint handler (T005–T007 → T008–T010)
- Endpoint handler before integration tests (T008–T010 → T011, T025)
- Pre-partitioning before every-step loop (T012 → T013 → T014 → T015)
- Pre-partitioning before timer evaluation (T012 must precede T017; T016 can start after T003)
- TypeScript types before UI state (T019 → T020 → T021–T024)

### Parallel Opportunities Within Phases

**Phase 2**: After T003–T004, tasks T005, T006, T007 can run in parallel (different files).  
**Phase 5**: T019 runs first; then T020–T024 can proceed with T020 first, T021–T023 in parallel after T020, T024 after T021.  
**Phase 6**: All four test tasks T026–T029 can run in parallel (same file, separate test methods — commit sequentially).  
**Phase 7**: T031 and T032 can run in parallel with T030.

---

## Parallel Example: Phase 2 (Foundational)

```text
# Sequential: domain types first
T003 → T004 (ScheduleType enum → QueueTemplateEntry extension)

# Then parallel: all three contract files at once
T005  T006  T007
 ↓     ↓     ↓
TemplateEntrySaveRequest  SaveQueueTemplateRequest  QueueTemplateEntryResponse

# Then sequential: endpoint handler needs all contracts
T008 → T009 → T010 → T011
```

## Parallel Example: Phase 5 (UI)

```text
# T019 first (TypeScript types)
T019 (queueTemplates.ts types)
  ↓
T020 (QueuesPage per-entry state)
  ↓
T021  T022  T023       ← parallel (different parts of QueueEntryList)
  ↓    ↓    ↓
T024 (QueuesPage save-template wiring — after T020–T023)
```

---

## Implementation Strategy

### MVP (Phase 2 + Phase 3 only)

1. Complete Phase 1: baseline verification
2. Complete Phase 2: domain model + API → templates now accept and return `scheduleType`
3. Complete Phase 3: every-step execution → `EveryStep` sequences fire as designed
4. **STOP and VALIDATE**: Verify every-step templates save, load, and execute correctly via API + quickstart  
5. Minimum viable: every-step scheduling works end-to-end without UI changes

### Full Incremental Delivery

1. MVP (Phases 1–3) → every-step works
2. Add Phase 4 (US3) → timer scheduling works
3. Add Phase 5 (US1) → UI schedule configuration + full integration test
4. Add Phase 6 (US4) → API validation hardened
5. Phase 7 → polish and final gate

---

## Notes

- [P] tasks operate on different files; commit each before starting the next to avoid conflicts in shared files (`QueueExecutionService.cs`, `QueueTemplatesEndpoints.cs`)
- `FileQueueTemplateRepository` requires no changes — `System.Text.Json` auto-serializes new `QueueTemplateEntry` fields
- Constitution requirement: methods use CamelCase only (no underscores) in all new C# code
- Timer evaluation uses `DateTime.Now` (server local time per clarification); no UTC conversion needed
- Backward compatibility is automatic: existing JSON template files without `ScheduleType` deserialize to `OncePerRun` (enum default = 0)
