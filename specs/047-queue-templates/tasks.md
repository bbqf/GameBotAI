---

description: "Task list for Queue Templates (047)"
---

# Tasks: Queue Templates

**Input**: Design documents from `specs/047-queue-templates/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-templates-api.md

**Tests**: INCLUDED — the project constitution mandates unit/integration tests for executable logic (≥80% line / ≥70% branch on touched areas), and plan.md defines a Testing Strategy.

**Organization**: Tasks are grouped by user story. US1 (Save) and US2 (Load) are both P1 and together form the meaningful round-trip MVP; US3 (Delete) is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = Save, US2 = Load, US3 = Delete
- Exact file paths are included in every task.

## Path Conventions

Backend: `src/GameBot.Domain/`, `src/GameBot.Service/`, tests under `tests/` (xUnit).
Frontend: `src/web-ui/src/`, tests in colocated `__tests__/` (Jest + RTL).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new module locations; confirm a green baseline before changes.

- [ ] T001 Create backend folders `src/GameBot.Domain/QueueTemplates/`, `src/GameBot.Service/Contracts/QueueTemplates/`, and test folders `tests/unit/QueueTemplates/`, `tests/integration/QueueTemplates/`, `tests/contract/QueueTemplates/` (the test projects `tests/unit`, `tests/integration`, `tests/contract` already exist; just add subfolders)
- [ ] T002 Confirm baseline build + test pass (`dotnet build`, `dotnet test`, and `npm test` in `src/web-ui`) before starting (constitution: no red baseline)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared template module (domain, persistence, read endpoints, DI, frontend client) that every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Backend — domain & persistence

- [ ] T003 [P] Add route constant `QueueTemplates = Base + "/queue-templates"` in `src/GameBot.Service/ApiRoutes.cs`
- [ ] T004 [P] Create `QueueTemplateEntry` (`{ string SequenceId }`) in `src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`
- [ ] T005 [P] Create `QueueTemplate` (`Id`, `Name`, `List<QueueTemplateEntry> Entries`, `CreatedAt`, `UpdatedAt`) in `src/GameBot.Domain/QueueTemplates/QueueTemplate.cs`
- [ ] T006 [P] Create `IQueueTemplateRepository` (`GetAsync`, `ListAsync`, `FindByNameAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`) in `src/GameBot.Domain/QueueTemplates/IQueueTemplateRepository.cs`
- [ ] T007 Implement `FileQueueTemplateRepository` in `src/GameBot.Domain/QueueTemplates/FileQueueTemplateRepository.cs` — root `{storageRoot}/queue-templates`, GUID id, persist entries, indented JSON, safe-id/path-traversal guard and `FindByNameAsync` (OrdinalIgnoreCase); copy patterns from `FileQueueRepository.cs` (depends on T004–T006)

### Backend — DTOs & read endpoints

- [ ] T008 [P] Create `SaveQueueTemplateRequest` (`Name`, `string[] SequenceIds`, `bool Overwrite`) in `src/GameBot.Service/Contracts/QueueTemplates/SaveQueueTemplateRequest.cs`
- [ ] T009 [P] Create `QueueTemplateSummaryResponse` (`Id`, `Name`, `EntryCount`, `CreatedAt`, `UpdatedAt`) in `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateSummaryResponse.cs`
- [ ] T010 [P] Create `QueueTemplateDetailResponse` (summary + `Collection<QueueTemplateEntryResponse> Entries` with `SequenceId`, `SequenceName`, `Stale`) in `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs`
- [ ] T011 Create `QueueTemplatesEndpoints.MapQueueTemplateEndpoints()` scaffold + `GET ""` (list summaries) and `GET "{id}"` (detail with stale resolution via `ISequenceRepository`, mirroring `QueuesEndpoints.ProjectEntry`) in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`; reuse the shared `{ error:{code,message,hint} }` envelope (depends on T007–T010)
- [ ] T012 Register `IQueueTemplateRepository` singleton and call `app.MapQueueTemplateEndpoints()` in `src/GameBot.Service/Program.cs` (beside the existing queue registrations)

### Backend — foundational tests

- [ ] T013 [P] Repository tests in `tests/unit/QueueTemplates/FileQueueTemplateRepositoryTests.cs` (GameBot.UnitTests; mirrors `tests/unit/Queues/FileQueueRepositoryTests.cs`) — CRUD round-trip in a temp dir, **entries persisted & ordered**, `FindByNameAsync` case-insensitive, safe-id rejection
- [ ] T014 [P] Read-endpoint tests in `tests/integration/QueueTemplates/QueueTemplatesReadEndpointTests.cs` (GameBot.IntegrationTests; mirrors `tests/integration/Queues/QueuesCrudEndpointTests.cs`) — list summaries, detail resolves `sequenceName`/`stale`, 404 unknown id

### Frontend — shared client

- [ ] T015 [P] Create `src/web-ui/src/services/queueTemplates.ts` — types (`QueueTemplateSummary`, `QueueTemplateDetail`, `QueueTemplateEntryDto`, `SaveQueueTemplate`) + `listQueueTemplates`, `getQueueTemplate`, `saveQueueTemplate`, `deleteQueueTemplate` (mirror `services/queues.ts`)
- [ ] T016 [P] Service-client tests in `src/web-ui/src/services/__tests__/queueTemplates.spec.ts` — each call hits the right URL/verb/body (mock `lib/api`)

**Checkpoint**: Template module persists, lists, and reads; frontend can query it. Story work can begin.

---

## Phase 3: User Story 1 - Save the current queue's entries as a template (Priority: P1) 🎯 MVP

**Goal**: From the queue editor, save the current ordered entries as a named template, with name validation and case-insensitive overwrite confirmation; templates survive restart.

**Independent Test**: Arrange entries in a queue, save as "Daily Farm", restart the service, and confirm the template still exists with the same ordered entries (queue's own entries are gone).

### Tests for User Story 1

- [ ] T017 [P] [US1] Save-endpoint tests in `tests/integration/QueueTemplates/QueueTemplatesSaveEndpointTests.cs` (GameBot.IntegrationTests) — new name → 201; same name (incl. different casing) + `overwrite:false` → 409 `template_exists`; `overwrite:true` → 200 replaces entries + bumps `UpdatedAt`; blank/illegal-char/>100-char name → 400 naming the rule; empty `sequenceIds` allowed; duplicates preserved
- [ ] T018 [P] [US1] `SaveTemplateDialog` tests in `src/web-ui/src/components/queues/__tests__/SaveTemplateDialog.test.tsx` — pre-fills `originName`; name-rule validation; on `template_exists` shows overwrite confirm then re-saves with `overwrite:true`; cancel saves nothing

### Implementation for User Story 1

- [ ] T019 [US1] Implement `POST ""` save handler in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` — validate name (trim → non-blank → `[A-Za-z0-9 _-]` → ≤100), `FindByNameAsync` → create (201) / overwrite when `Overwrite` (200) / 409 `template_exists` with hint; build entries from `SequenceIds` preserving order (depends on T011)
- [ ] T020 [P] [US1] Create `SaveTemplateDialog` in `src/web-ui/src/components/queues/SaveTemplateDialog.tsx` — name input (pre-filled via `originName`), client-side validation hints, Save/Cancel, overwrite-confirm flow calling `onSave(name, overwrite)`
- [ ] T021 [US1] Wire **Save as template** into `src/web-ui/src/pages/QueuesPage.tsx` — button in the Edit-Queue section; build `sequenceIds` from `detail.entries`; call `saveQueueTemplate`; map 409 → dialog overwrite confirm; pass `loadedTemplateName` as `originName`
- [ ] T022 [US1] Add `QueuesPage` save-wiring tests in `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` — save builds sequenceIds from entries; success message; 409 path triggers overwrite confirm

**Checkpoint**: Saving (incl. overwrite confirmation + validation) works end-to-end and persists across restart. US1 is independently demoable.

---

## Phase 4: User Story 2 - Load a template into a queue (Priority: P1)

**Goal**: From the queue editor, pick a saved template and load its ordered entries into the queue (full replacement, copy/no-link); replace-confirm when non-empty; blocked while running. Same template loadable into multiple queues.

**Independent Test**: Save a template from one queue, open a different queue, load it, and confirm the second queue now holds the template's ordered entries; editing one queue later does not change the template.

### Tests for User Story 2

- [ ] T023 [P] [US2] `QueueRuntimeStore.SetEntries` tests in `tests/unit/Queues/QueueRuntimeStoreSetEntriesTests.cs` (GameBot.UnitTests; alongside `QueueRuntimeStoreEntriesTests.cs`) — replaces existing entries, preserves order, assigns new `EntryId`s, empty input clears
- [ ] T024 [P] [US2] Replace-endpoint tests in `tests/integration/Queues/QueueEntriesReplaceEndpointTests.cs` (GameBot.IntegrationTests; alongside `QueueEntriesEndpointTests.cs`) — 404 unknown queue; 409 `queue_running` when Running; replaces and returns detail with resolved names/stale; empty array clears
- [ ] T025 [P] [US2] `TemplatePickerDialog` tests in `src/web-ui/src/components/queues/__tests__/TemplatePickerDialog.test.tsx` — lists templates; empty state when none; **Load** fires `onLoad(id)`

### Implementation for User Story 2

- [ ] T026 [P] [US2] Add `SetEntries(string queueId, IEnumerable<string> sequenceIds)` to `src/GameBot.Domain/Queues/IQueueRuntimeStore.cs`
- [ ] T027 [US2] Implement `SetEntries` in `src/GameBot.Domain/Queues/QueueRuntimeStore.cs` — under the per-state lock, clear and append fresh `QueueEntry`s in order; return the new list (depends on T026)
- [ ] T028 [P] [US2] Create `ReplaceQueueEntriesRequest` (`string[] SequenceIds`) in `src/GameBot.Service/Contracts/Queues/ReplaceQueueEntriesRequest.cs`
- [ ] T029 [US2] Add `PUT "{id}/entries"` to `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` — 404 if missing; 409 `queue_running` if Running; else `SetEntries` and return `BuildDetailAsync` (depends on T027, T028)
- [ ] T030 [P] [US2] Add `replaceQueueEntries(id, sequenceIds)` to `src/web-ui/src/services/queues.ts` (`putJson<QueueDetailDto>(\`${base}/${id}/entries\`, { sequenceIds })`)
- [ ] T031 [P] [US2] Create `TemplatePickerDialog` (the load picker) in `src/web-ui/src/components/queues/TemplatePickerDialog.tsx` — fetch `listQueueTemplates`, render names with **Load**, empty state; `onLoad(id)` callback
- [ ] T032 [US2] Wire **Load template** into `src/web-ui/src/pages/QueuesPage.tsx` — open picker; on load: `getQueueTemplate(id)` → replace-confirm if `detail.entries.length > 0` → `replaceQueueEntries(queueId, ids)` → `reloadDetail` + `refresh`; set `loadedTemplateName`; disable Load while `status === 'Running'` and surface 409 message (depends on T030, T031)
- [ ] T033 [US2] Add `QueuesPage` load-wiring tests to `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` — load replaces entries + reloads; replace-confirm appears for non-empty queue and is cancelable; loaded name pre-fills the save dialog; Load disabled while running; **FR-016** — loading the same template into two different queues drives an independent `replaceQueueEntries` call per queue, each carrying the template's `sequenceIds`

**Checkpoint**: Round trip works — save in one queue, load into another; replacement + running guard + copy-independence verified. US1+US2 = MVP.

---

## Phase 5: User Story 3 - Delete a template (Priority: P2)

**Goal**: Delete a saved template from the load picker (confirm first); deletion never alters any queue's entries.

**Independent Test**: Save a template, open the picker, delete it with confirmation, verify it's gone (and stays gone after restart) and that a queue which had loaded it is unaffected.

### Tests for User Story 3

- [ ] T034 [P] [US3] Delete-endpoint tests in `tests/integration/QueueTemplates/QueueTemplatesDeleteEndpointTests.cs` (GameBot.IntegrationTests) — 204 on delete, 404 on unknown id
- [ ] T035 [P] [US3] `TemplatePickerDialog` delete tests in `src/web-ui/src/components/queues/__tests__/TemplatePickerDialog.test.tsx` — **Delete** opens confirm; confirming fires `onDelete(id)` and removes the row; cancel retains

### Implementation for User Story 3

- [ ] T036 [US3] Implement `DELETE "{id}"` in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` — `DeleteAsync` → 204 / 404 (depends on T011)
- [ ] T037 [US3] Add per-template **Delete** action to `src/web-ui/src/components/queues/TemplatePickerDialog.tsx` using `ConfirmDeleteModal`; `onDelete(id)` callback (depends on T031)
- [ ] T038 [US3] Wire delete handler in `src/web-ui/src/pages/QueuesPage.tsx` — `deleteQueueTemplate(id)` then refresh the picker list; queue entries untouched
- [ ] T039 [US3] Add delete-wiring tests to `src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx` — delete removes from picker; current queue entries unchanged

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T040 [P] Add minimal styling for the new dialogs (Save / picker) consistent with existing queue UI in `src/web-ui/src/` styles (e.g., `App.css` or the relevant stylesheet)
- [ ] T041 [P] Contract test for the template API in `tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs` (GameBot.ContractTests; mirrors `tests/contract/Queues/QueuesApiContractTests.cs`) — validate response/request shapes and status codes for list/detail/save (201/200/400/409)/delete (204/404) per `contracts/queue-templates-api.md` (FR-021..FR-024)
- [ ] T042 [P] Scale test in `tests/integration/QueueTemplates/QueueTemplatesScaleTests.cs` (GameBot.IntegrationTests; mirrors `tests/integration/Queues/QueueScaleTests.cs`) — list 50 templates and load one with 100 entries reflect within target responsiveness (SC-007)
- [ ] T043 Verify coverage on touched areas meets ≥80% line / ≥70% branch (backend `dotnet test` + frontend `npm test -- --coverage`)
- [ ] T044 Run `dotnet build`, full `dotnet test`, and `npm test` — all green (constitution release-blocker gate)
- [ ] T045 Execute `specs/047-queue-templates/quickstart.md` flows A–F manually against a running service

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **US1 (Phase 3)** and **US2 (Phase 4)**: Depend only on Foundational; independently testable. Both P1.
- **US3 (Phase 5)**: Depends on Foundational; its UI reuses `TemplatePickerDialog` created in US2 (T031), so schedule US3's frontend (T037) after T031. US3's backend (T036) depends only on Foundational.
- **Polish (Phase 6)**: After the desired stories are complete.

### Within Each User Story

- Tests are written to fail first, then implementation makes them pass.
- Backend: models → repository → DTOs → endpoints; Frontend: service → component → page wiring.

### Parallel Opportunities

- Foundational: T003, T004, T005, T006 in parallel; T008, T009, T010 in parallel; T013, T014, T015, T016 in parallel (after their deps).
- US1: T017, T018 (tests) parallel; T020 parallel with backend T019.
- US2: T023, T024, T025 (tests) parallel; T026/T028/T030/T031 parallel where files differ.
- US3: T034, T035 parallel; T036 (backend) parallel with frontend once T031 exists.

---

## Parallel Example: Foundational

```bash
# Domain + route constant together (different files):
Task: "ApiRoutes.cs QueueTemplates constant (T003)"
Task: "QueueTemplateEntry.cs (T004)"
Task: "QueueTemplate.cs (T005)"
Task: "IQueueTemplateRepository.cs (T006)"

# DTOs together:
Task: "SaveQueueTemplateRequest.cs (T008)"
Task: "QueueTemplateSummaryResponse.cs (T009)"
Task: "QueueTemplateDetailResponse.cs (T010)"
```

---

## Implementation Strategy

### MVP

- Minimal MVP: **US1** alone (persist + survive restart) is independently valuable.
- Meaningful MVP: **US1 + US2** — the save↔load round trip that makes templates shareable across queues.

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. US1 → **validate** → 4. US2 → **validate** (MVP).

### Incremental Delivery

Foundation → US1 (save/persist) → US2 (load/share) → US3 (delete) → Polish. Each story adds value without breaking the previous.

---

## Notes

- Foundational tasks intentionally carry no story label — they are shared prerequisites for all stories.
- [P] = different files, no incomplete-task dependencies.
- Commit after each task or logical group. Auto-commit is disabled in `git-config.yml`; commit via `/speckit-git-commit` or manually.
- Verify each test fails before implementing it.
- Stop at any checkpoint to validate a story independently.
