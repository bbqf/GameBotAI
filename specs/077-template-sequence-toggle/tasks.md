# Tasks: Enable/Disable Template Sequences

**Feature**: 077-template-sequence-toggle | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This feature is a single prioritized user story (US1, P1): temporarily disable a sequence in a template without removing it, persisted to the template, ignored during the queue run, with an on/off switch in the UI. Tasks below deliver that story end to end. Tests are included per the project constitution (Testing Standards).

**Paths** are repository-absolute-relative to `C:\src\GameBot`.

## Phase 1: Setup

- [x] T001 Confirm branch `077-template-sequence-toggle` is checked out and the solution builds clean before changes: run `dotnet build C:\src\GameBot\GameBot.sln` and note any pre-existing warnings (baseline).

## Phase 2: Foundational (blocking prerequisites)

Persisted domain field that everything else depends on.

- [x] T002 Add `public bool Enabled { get; set; } = true;` to `QueueTemplateEntry` with an XML doc explaining default-on and legacy behavior, in src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs.
- [x] T003 [P] Add a unit test asserting a freshly constructed `QueueTemplateEntry` has `Enabled == true`, and that deserializing template JSON lacking an `Enabled` key yields `Enabled == true` (legacy default), in tests/unit/QueueTemplates/QueueTemplateEntryEnabledTests.cs. Depends on T002.

**Checkpoint**: Domain carries the field and defaults on; persistence round-trips it.

## Phase 3: User Story 1 - Disable/enable a template sequence (P1)

**Goal**: Operator toggles a sequence off/on in the template editor; state persists to the template; disabled entries are skipped during the queue run; enabled entries run as before.

**Independent test**: Toggle an entry off, reload (persists), start the queue -> disabled sequence never runs while enabled ones do; toggle on -> it runs next run.

### Backend contracts + endpoint (US1)

- [x] T004 [US1] Add `public bool? Enabled { get; set; }` (null -> treated as true on save) with XML doc to `TemplateEntrySaveRequest`, in src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs.
- [x] T005 [US1] Add `public bool Enabled { get; set; } = true;` with XML doc to `QueueTemplateEntryResponse`, in src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs.
- [x] T006 [US1] In `QueueTemplatesEndpoints.SaveQueueTemplate`, set `Enabled = entry.Enabled ?? true` when building each `QueueTemplateEntry`; in `BuildDetailAsync`, populate `Enabled = entry.Enabled` on each `QueueTemplateEntryResponse`. File: src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs.
- [x] T007 [P] [US1] Add endpoint round-trip tests in the QueueTemplates test project (tests/integration/QueueTemplates/QueueTemplatesEnabledEndpointTests.cs): (a) save an entry with `enabled:false` -> GET returns `enabled:false`, same position/schedule; (b) save with `enabled` omitted -> GET returns `enabled:true`; (c) **FR-008** - save a `Timer` entry with `enabled:false` and assert its `timerRelativeOffset`/`scheduleType` are unchanged on read (toggling does not alter schedule). Depends on T004-T006.

### Queue-run exclusion (US1)

- [x] T008 [US1] In `QueueExecutionService.RunAsync`, change `var allEntries = template.Entries.ToList();` to exclude disabled entries: `var allEntries = template.Entries.Where(e => e.Enabled).ToList();` (single filter covering AtQueueStart/OncePerRun/EveryStep/Timer partitions and the monitor projection). Update the nearby comment. File: src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs (~line 182).
- [x] T009 [US1] ~~Filter disabled entries out of `StartAsync` runtime materialization.~~ **Reverted during implementation** - the template editor renders from the runtime store and merges schedule/enabled BY POSITION, so the runtime store MUST keep all entries in order (disabled ones stay visible/re-enableable). Only `RunAsync` (T008) filters. Left an explanatory comment at src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs (~line 136).
- [x] T010 [US1] ~~Filter `MaybeAutoLoadAsync`.~~ **Not needed** for the same reason as T009 - the loaded-entries display keeps all entries; the monitor already excludes disabled via the run schedule. File left unchanged: src/GameBot.Service/Endpoints/QueuesEndpoints.cs.
- [x] T011 [P] [US1] Add unit test in tests/unit/Queues/QueueExecutionServiceTests.cs: a queue whose linked template has one disabled entry and one enabled entry runs -> the disabled sequence never executes, the enabled one does (order preserved); and an all-disabled template starts and completes idle without error. Depends on T008.

### Web UI (US1)

- [x] T012 [US1] Add `enabled: boolean` to `QueueTemplateEntryDto` and `enabled?: boolean` to `TemplateEntrySaveDto`, in src/web-ui/src/services/queueTemplates.ts.
- [x] T013 [US1] Add `enabled?: boolean` (undefined = on) to the `EntrySchedule` type, in src/web-ui/src/components/queues/QueueEntryList.tsx.
- [x] T014 [US1] Add `enabled: boolean` to `SchedulingCard` and set it in `groupEntriesIntoAreas` from the entry's schedule (`sched.enabled ?? true`); `applyDragMove` already spreads current schedule so `enabled` is preserved when changing area. File: src/web-ui/src/components/queues/schedulingAreas.ts.
- [x] T015 [US1] Render an on/off switch (checkbox with role=switch, accessible label `Enable/Disable {label}`) on each card, wire an `onToggleEnabled(entryId, enabled)` prop, add an "Off" badge, and apply a visibly "off" style (dim/dashed) when `enabled === false`. Files: src/web-ui/src/components/queues/SchedulingSequenceCard.tsx + CSS in QueueSchedulingAreas.css.
- [x] T016 [US1] Thread `onToggleEnabled` through `SchedulingArea` and `QueueSchedulingAreas` (all four area instances) to the card. Files: src/web-ui/src/components/queues/SchedulingArea.tsx, src/web-ui/src/components/queues/QueueSchedulingAreas.tsx.
- [x] T017 [US1] In `QueuesPage`: add the toggle handler (`setEntrySchedule` updating `enabled` for the entryId), include `enabled` in the `handleSaveTemplate` payload **only when false** (omission means enabled), and read `enabled` in `buildScheduleFromTemplateEntries` on load. File: src/web-ui/src/pages/QueuesPage.tsx.
- [x] T018 [P] [US1] Add jest tests: a card with `enabled:false` shows the off state/badge/disabled style; toggling the switch calls `onToggleEnabled`; the save payload includes `enabled:false` for a toggled-off entry and omits it otherwise; a template loaded with a disabled entry renders it off. Files: src/web-ui/src/components/queues/__tests__/QueueSchedulingAreas.test.tsx, src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx. Depends on T012-T017.

**Checkpoint**: US1 fully functional and independently testable end to end.

## Phase 4: Polish & Cross-Cutting Concerns

- [x] T019 [P] Update `docs/architecture.md`: document the `Enabled` template-entry field, the "disabled entries excluded at run build" behavior, and refresh the "Last reviewed" date. File: docs/architecture.md.
- [x] T020 [P] Set the spec `Status` line to Implemented and add a row for 077 in specs/STATUS.md.
- [x] T021 Full green gate: ran `dotnet test` on tests/unit (734), tests/integration (294), tests/contract (94), and in src/web-ui `npm run build` + `npm test` (577) - all green.

## Dependencies & Execution Order

- Phase 1 (T001) -> Phase 2 (T002 -> T003) -> Phase 3 -> Phase 4.
- Within Phase 3: backend contracts/endpoint (T004-T006) and run-exclusion (T008) proceeded in parallel with the UI (T012-T017) since they touch different files; their tests (T007, T011, T018) depend on their respective implementation tasks.
- T021 (full gate) is last; T019/T020 (docs) run any time after the design is stable.

## Parallel Opportunities

- [P] T003 alongside other Phase 2/early Phase 3 work.
- [P] Backend group (T004-T006) parallel with run-exclusion (T008) parallel with UI group (T012-T017) - different files.
- [P] Tests T007, T011, T018 in parallel once their impl tasks are done.
- [P] Docs T019, T020 in parallel.

## Implementation Strategy

MVP = User Story 1 (the whole feature). Landed the persisted field (Phase 2), then the backend save/read + run-exclusion so the behavior is real and API-testable, then the UI switch, then docs and the full green gate.

## Format validation

All tasks use `- [x] Tnnn [P?] [US1?] description with file path`. Setup/Foundational/Polish tasks carry no story label; Phase 3 tasks carry `[US1]`.
