# Tasks: Enable/Disable Template Sequences

**Feature**: 077-template-sequence-toggle | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This feature is a single prioritized user story (US1, P1): temporarily disable a sequence in a template without removing it, persisted to the template, ignored during the queue run, with an on/off switch in the UI. Tasks below deliver that story end to end. Tests are included per the project constitution (Testing Standards).

**Paths** are repository-absolute-relative to `C:\src\GameBot`.

## Phase 1: Setup

- [ ] T001 Confirm branch `077-template-sequence-toggle` is checked out and the solution builds clean before changes: run `dotnet build C:\src\GameBot\GameBot.sln` and note any pre-existing warnings (baseline).

## Phase 2: Foundational (blocking prerequisites)

Persisted domain field that everything else depends on.

- [ ] T002 Add `public bool Enabled { get; set; } = true;` to `QueueTemplateEntry` with an XML doc explaining default-on and legacy behavior, in src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs.
- [ ] T003 [P] Add a unit test asserting a freshly constructed `QueueTemplateEntry` has `Enabled == true`, and that deserializing template JSON lacking an `Enabled` key yields `Enabled == true` (legacy default), in tests/unit (new file e.g. tests/unit/QueueTemplates/QueueTemplateEntryEnabledTests.cs). Depends on T002.

**Checkpoint**: Domain carries the field and defaults on; persistence round-trips it.

## Phase 3: User Story 1 — Disable/enable a template sequence (P1)

**Goal**: Operator toggles a sequence off/on in the template editor; state persists to the template; disabled entries are skipped during the queue run; enabled entries run as before.

**Independent test**: Toggle an entry off, reload (persists), start the queue → disabled sequence never runs while enabled ones do; toggle on → it runs next run.

### Backend contracts + endpoint (US1)

- [ ] T004 [US1] Add `public bool? Enabled { get; set; }` (null → treated as true on save) with XML doc to `TemplateEntrySaveRequest`, in src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs.
- [ ] T005 [US1] Add `public bool Enabled { get; set; } = true;` with XML doc to `QueueTemplateEntryResponse`, in src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs.
- [ ] T006 [US1] In `QueueTemplatesEndpoints.SaveQueueTemplate`, set `Enabled = entry.Enabled ?? true` when building each `QueueTemplateEntry`; in `BuildDetailAsync`, populate `Enabled = entry.Enabled` on each `QueueTemplateEntryResponse`. File: src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs.
- [ ] T007 [P] [US1] Add endpoint round-trip tests in the existing QueueTemplates test project (tests/integration/QueueTemplates/QueueTemplatesSaveEndpointTests.cs / QueueTemplatesReadEndpointTests.cs, and/or tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs): (a) save an entry with `enabled:false` → GET returns `enabled:false`, same position/schedule; (b) save with `enabled` omitted → GET returns `enabled:true`; (c) **FR-008** — save a `Timer` entry with `enabled:false` and assert its `timerTimeOfDay`/`timerRelativeOffset`/`scheduleType` are unchanged on read (toggling does not alter schedule). Depends on T004–T006.

### Queue-run exclusion (US1)

- [ ] T008 [US1] In `QueueExecutionService.RunAsync`, change `var allEntries = template.Entries.ToList();` to exclude disabled entries: `var allEntries = template.Entries.Where(e => e.Enabled).ToList();` (single filter covering AtQueueStart/OncePerRun/EveryStep/Timer partitions and the monitor projection). Update the nearby comment. File: src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs (~line 182).
- [ ] T009 [US1] In `QueueExecutionService.StartAsync`, filter disabled entries out of the runtime materialization: `_runtime.SetEntries(queueId, template.Entries.Where(e => e.Enabled).Select(e => e.SequenceId))` so GET queue entries match what runs. File: src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs (~line 136).
- [ ] T010 [US1] In `QueuesEndpoints.MaybeAutoLoadAsync`, apply the same disabled filter to the auto-load `runtime.SetEntries(...)` call. File: src/GameBot.Service/Endpoints/QueuesEndpoints.cs (~line 205).
- [ ] T011 [P] [US1] Add integration test: a queue whose linked template has one disabled entry and one enabled entry runs → the disabled sequence never executes (no execution-log entry / not fired), the enabled one does; and an all-disabled template starts and completes idle without error. In tests/integration/Queues. Depends on T008–T010.

### Web UI (US1)

- [ ] T012 [US1] Add `enabled: boolean` to `QueueTemplateEntryDto` and `enabled?: boolean` to `TemplateEntrySaveDto`, in src/web-ui/src/services/queueTemplates.ts.
- [ ] T013 [US1] Add `enabled?: boolean` (undefined = on) to the `EntrySchedule` type, in src/web-ui/src/components/queues/QueueEntryList.tsx.
- [ ] T014 [US1] Add `enabled: boolean` to `SchedulingCard` and set it in `groupEntriesIntoAreas` from the entry's schedule (`sched.enabled ?? true`); ensure `applyDragMove` preserves `enabled` when changing area (spread current schedule). File: src/web-ui/src/components/queues/schedulingAreas.ts.
- [ ] T015 [US1] Render an on/off switch (checkbox/toggle with accessible label `Enable/Disable {label}`) on each card, wire an `onToggleEnabled(entryId, enabled)` prop, and apply a visibly "off" style (dim/opacity) when `enabled === false`. File: src/web-ui/src/components/queues/SchedulingSequenceCard.tsx (+ CSS in QueueSchedulingAreas.css if needed).
- [ ] T016 [US1] Thread `onToggleEnabled` through `SchedulingArea` and `QueueSchedulingAreas` to the card. Files: src/web-ui/src/components/queues/SchedulingArea.tsx, src/web-ui/src/components/queues/QueueSchedulingAreas.tsx.
- [ ] T017 [US1] In `QueuesPage`: add the toggle handler (`setEntrySchedule` updating `enabled` for the entryId), include `enabled` in the `handleSaveTemplate` payload (`enabled: sched.enabled ?? true`), and read `enabled` in `buildScheduleFromTemplateEntries` on load. File: src/web-ui/src/pages/QueuesPage.tsx.
- [ ] T018 [P] [US1] Add a jest test: rendering a card with `enabled:false` shows the off state; toggling the switch calls `onToggleEnabled`; and `handleSaveTemplate` (or the save payload builder) includes `enabled` per entry. In src/web-ui/src/components/queues/__tests__ and/or src/web-ui/src/pages/__tests__. Depends on T012–T017.

**Checkpoint**: US1 fully functional and independently testable end to end.

## Phase 4: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Update `docs/architecture.md`: document the `Enabled` template-entry field, the "disabled entries excluded at run build" behavior, and refresh the "Last reviewed" date. File: docs/architecture.md.
- [ ] T020 [P] Set the spec `Status` line to Implemented and add a row for 077 in specs/STATUS.md.
- [ ] T021 Full green gate: run `dotnet test C:\src\GameBot\tests\unit`, `C:\src\GameBot\tests\integration`, and `C:\src\GameBot\tests\contract`, and in src/web-ui run `npm run build` + `npm test`; fix any failures introduced by this feature.

## Dependencies & Execution Order

- Phase 1 (T001) → Phase 2 (T002 → T003) → Phase 3 → Phase 4.
- Within Phase 3: backend contracts/endpoint (T004–T006) and run-exclusion (T008–T010) can proceed in parallel with the UI (T012–T017) since they touch different files; their tests (T007, T011, T018) depend on their respective implementation tasks.
- T021 (full gate) is last; T019/T020 (docs) can run any time after the design is stable.

## Parallel Opportunities

- [P] T003 alongside other Phase 2/early Phase 3 work.
- [P] Backend group (T004–T006) ∥ run-exclusion group (T008–T010) ∥ UI group (T012–T017) — different files.
- [P] Tests T007, T011, T018 in parallel once their impl tasks are done.
- [P] Docs T019, T020 in parallel.

## Implementation Strategy

MVP = User Story 1 (the whole feature). Recommended path: land the persisted field (Phase 2), then the backend save/read + run-exclusion so the behavior is real and API-testable, then the UI switch, then docs and the full green gate. Each group is independently verifiable.

## Format validation

All tasks use `- [ ] Tnnn [P?] [US1?] description with file path`. Setup/Foundational/Polish tasks carry no story label; Phase 3 tasks carry `[US1]`.
