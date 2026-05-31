---

description: "Task list for Emulator Execution Queue implementation"
---

# Tasks: Emulator Execution Queue

**Input**: Design documents from `specs/046-emulator-execution-queue/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queues-api.md

**Tests**: INCLUDED — the GameBot constitution (Testing Standards) requires unit/integration coverage for executable logic (≥80% line / ≥70% branch on touched areas). Test tasks are written before the implementation they cover within each story.

**Organization**: Tasks are grouped by user story (US1 = CRUD, US2 = sequence entries, US3 = start/stop status) so each can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (setup, foundational, and polish have no story label)

## Path Conventions

Web application: backend in `src/GameBot.Domain/` and `src/GameBot.Service/`; frontend in `src/web-ui/src/`; backend tests in `tests/unit/`, `tests/integration/`, `tests/contract/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the minimal shared scaffolding the rest of the feature builds on.

- [x] T001 [P] Add `Queues = Base + "/queues"` constant to `src/GameBot.Service/ApiRoutes.cs`
- [x] T002 [P] Create the domain folder and `QueueExecutionStatus` enum (`Stopped`, `Running`) in `src/GameBot.Domain/Queues/QueueExecutionStatus.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, persistence, in-memory runtime store, DI wiring, endpoint skeleton, and frontend service client that ALL user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 [P] Create `ExecutionQueue` persisted-config model (Id, Name, EmulatorSerial, CycleExecution, CreatedAt, UpdatedAt) in `src/GameBot.Domain/Queues/ExecutionQueue.cs`
- [x] T004 [P] Create `QueueEntry` (EntryId, SequenceId) and `QueueRuntimeState` (Entries list, Status) in `src/GameBot.Domain/Queues/QueueEntry.cs`
- [x] T005 [P] Define `IQueueRepository` (Get/List/Create/Update/Delete) in `src/GameBot.Domain/Queues/IQueueRepository.cs`
- [x] T006 Implement `FileQueueRepository` (JSON under `{dataRoot}/queues`, safe-id/path-traversal guard and JSON options copied from `FileSequenceRepository`; persists config only) in `src/GameBot.Domain/Queues/FileQueueRepository.cs` (depends on T003, T005)
- [x] T007 [P] Define `IQueueRuntimeStore` (GetEntries/AddEntry/RemoveEntry/GetStatus/SetStatus/Remove) in `src/GameBot.Domain/Queues/IQueueRuntimeStore.cs` (depends on T004)
- [x] T008 Implement `QueueRuntimeStore` (thread-safe `ConcurrentDictionary<queueId, QueueRuntimeState>`, append on AddEntry, default `Stopped`, non-persistent) in `src/GameBot.Domain/Queues/QueueRuntimeStore.cs` (depends on T007)
- [x] T009 [P] Create request/response DTOs (`CreateQueueRequest`, `UpdateQueueRequest`, `AddQueueEntryRequest`, `QueueResponse`, `QueueDetailResponse`) in `src/GameBot.Service/Contracts/Queues/` (one file per DTO)
- [x] T010 Create `QueuesEndpoints.MapQueueEndpoints(this IEndpointRouteBuilder)` skeleton (MapGroup on `ApiRoutes.Queues` with tag `Queues`, returns app; no routes yet) in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`
- [x] T011 Register `IQueueRepository` → `FileQueueRepository(storageRoot)` and `IQueueRuntimeStore` → `QueueRuntimeStore` singletons, and call `app.MapQueueEndpoints()` in `src/GameBot.Service/Program.cs` (depends on T006, T008, T010)
- [x] T012 [P] Create frontend service client with all DTO types and functions (`listQueues`, `getQueue`, `createQueue`, `updateQueue`, `deleteQueue`, `addQueueEntry`, `removeQueueEntry`, `startQueue`, `stopQueue`) in `src/web-ui/src/services/queues.ts`
- [x] T013 [P] Add a unit-test helper/fixture for a temp `storageRoot` (or reuse existing helper) referenced by repository tests in `tests/unit/Queues/`

**Checkpoint**: Foundation ready — models, persistence, runtime store, DI, endpoint group, and FE client all exist. User stories can now begin.

---

## Phase 3: User Story 1 - Manage queues (CRUD) (Priority: P1) 🎯 MVP

**Goal**: Operator can create, list, edit, and delete queues from a new **Queues** tab; config persists across restarts; emulator binding is fixed at creation.

**Independent Test**: Open the Queues tab, create a queue bound to an emulator, see it listed with name/emulator/cycle/status, edit name + cycle, reload to confirm persistence, then delete it — without touching sequences or start/stop.

### Tests for User Story 1 ⚠️ (write first, ensure they fail)

- [x] T014 [P] [US1] Unit tests for `FileQueueRepository` (create/get/list/update/delete round-trip in temp dir; safe-id rejection; config-only persistence — no entries/status serialized) in `tests/unit/Queues/FileQueueRepositoryTests.cs`
- [x] T015 [P] [US1] Integration tests for CRUD endpoints (create validation for missing name/emulator → 400 with field message; list/get shape incl. status `Stopped` + entryCount 0; PUT ignores emulator; update/delete on a non-running queue succeed; 404 for unknown id; **two queues may bind to the same `emulatorSerial` — no uniqueness constraint per FR-003a**) in `tests/integration/Queues/QueuesCrudEndpointTests.cs`
- [x] T016 [P] [US1] Contract test asserting `/api/queues` CRUD request/response shapes match `contracts/queues-api.md` in `tests/contract/Queues/QueuesApiContractTests.cs`
- [x] T017 [P] [US1] Frontend tests for `QueueForm` (required-field validation blocks submit; emulator picker disabled/hidden on edit) in `src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx`

### Implementation for User Story 1

- [x] T018 [US1] Implement CRUD routes in `MapQueueEndpoints` — `POST` (validate name+emulatorSerial), `GET` (list, join runtime status + entryCount), `GET {id}` (detail), `PUT {id}` (name/cycle only; reject emulator change), `DELETE {id}` — in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T006, T008, T009). NOTE: the running-state guard on `PUT`/`DELETE` (409 `queue_running`) is intentionally added later in T036 (US3), since status only becomes meaningful in US3.
- [x] T019 [US1] Add a private helper in `QueuesEndpoints.cs` to build `QueueResponse`/`QueueDetailResponse` from `ExecutionQueue` + runtime store (resolve entryCount; entries resolution stubbed until US2)
- [x] T020 [P] [US1] Add `'Queues'` to the `AuthoringTab` union and `tabs[]` array in `src/web-ui/src/components/Nav.tsx`
- [x] T021 [US1] Render `<QueuesPage/>` when `tab === 'Queues'` in `src/web-ui/src/App.tsx`; extend `normalizeTab` to accept `'Queues'` in `src/web-ui/src/lib/navigation.ts` if it whitelists names
- [x] T022 [P] [US1] Implement `QueueForm` (name `FormField`, emulator picker fed by `useAdbDevices` — create-only, cycle-execution checkbox; validation per FR-008) in `src/web-ui/src/components/queues/QueueForm.tsx`
- [x] T023 [US1] Implement `QueuesPage` list view using the `List` component (name, emulator serial, cycle on/off, status chip labeled `Running`/`Stopped` per FR-014, entry count) with New/Edit/Delete (delete via `ConfirmDeleteModal`) wired to `services/queues.ts` in `src/web-ui/src/pages/QueuesPage.tsx` (depends on T012, T022)
- [x] T024 [P] [US1] Frontend tests for `QueuesPage` CRUD (lists queues; create/edit/delete flows; delete confirmation) in `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`

**Checkpoint**: Queues tab supports full CRUD with persisted config — MVP is independently functional and testable.

---

## Phase 4: User Story 2 - Add and view sequences in a queue (Priority: P2)

**Goal**: Operator can add sequences (appended at the end), view the ordered list, and remove entries; deleted-sequence references are kept and flagged stale; contents are in-memory (empty after restart).

**Independent Test**: Open a queue, add two sequences and confirm order (newest last), remove one, and confirm a reference to a deleted sequence shows as stale.

### Tests for User Story 2 ⚠️ (write first, ensure they fail)

- [x] T025 [P] [US2] Unit tests for `QueueRuntimeStore` entries (append preserves insertion order; remove keeps relative order; duplicate sequenceId allowed; unknown queue returns empty) in `tests/unit/Queues/QueueRuntimeStoreEntriesTests.cs`
- [x] T026 [P] [US2] Integration tests for entry endpoints (`POST /entries` appends and returns entry; `DELETE /entries/{entryId}`; missing sequenceId → 400; `GET {id}` resolves `sequenceName` and sets `stale:true` for deleted sequences; entries empty on a fresh store) in `tests/integration/Queues/QueueEntriesEndpointTests.cs`
- [x] T027 [P] [US2] Frontend tests for `QueueEntryList` (add appends to end; remove; stale badge rendered for stale entry) in `src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx`

### Implementation for User Story 2

- [x] T028 [US2] Add entry routes to `MapQueueEndpoints` — `POST /api/queues/{id}/entries` (append via runtime store; validate sequenceId) and `DELETE /api/queues/{id}/entries/{entryId}` — in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T008, T018)
- [x] T029 [US2] Implement stale-aware entries resolution in the response helper (resolve each `SequenceId` via `ISequenceRepository`; set `sequenceName`/`stale`) and include `entries[]` in `QueueDetailResponse` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T019, T028)
- [x] T030 [P] [US2] Implement `QueueEntryList` (ordered entries; "Add sequence" via `SearchableDropdown` populated from the sequences service; per-row remove; stale badge) in `src/web-ui/src/components/queues/QueueEntryList.tsx` (depends on T012)
- [x] T031 [US2] Wire `QueueEntryList` into `QueuesPage` (open a selected queue to manage its entries; refresh entryCount/detail after add/remove) in `src/web-ui/src/pages/QueuesPage.tsx` (depends on T023, T030)

**Checkpoint**: Queues hold ordered, in-memory sequence entries with stale-reference handling; US1 + US2 both work independently.

---

## Phase 5: User Story 3 - Start and stop queue execution (Priority: P3)

**Goal**: Operator sees each queue's status and can start/stop it (placeholder — status flip only, logged); start allowed regardless of emulator connectivity; rename/cycle-toggle/delete blocked while running; status resets to Stopped on restart.

**Independent Test**: From the list, Start a queue → status becomes Running and Edit/Delete disable; Stop → returns to Stopped and re-enable.

### Tests for User Story 3 ⚠️ (write first, ensure they fail)

- [x] T032 [P] [US3] Unit tests for `QueueRuntimeStore` status (default `Stopped`; SetStatus; start/stop idempotency) in `tests/unit/Queues/QueueRuntimeStoreStatusTests.cs`
- [x] T033 [P] [US3] Integration tests for start/stop endpoints (`POST /start` → Running, idempotent, allowed when emulator serial not connected, logs transition; `POST /stop` → Stopped, idempotent; PUT and DELETE return 409 `queue_running` while Running; add/remove entry still allowed while Running) in `tests/integration/Queues/QueueExecutionEndpointTests.cs`
- [x] T034 [P] [US3] Frontend tests for `QueuesPage` start/stop (Start flips to Running and disables Edit/Delete; Stop re-enables; status chip reflects state) in `src/web-ui/src/pages/__tests__/QueuesPage.execution.spec.tsx`

### Implementation for User Story 3

- [x] T035 [US3] Add `POST /api/queues/{id}/start` and `POST /api/queues/{id}/stop` routes to `MapQueueEndpoints` (set status via runtime store, idempotent, `ILogger` info on each transition per FR-019b, start allowed when offline per FR-019a) in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T008, T018)
- [x] T036 [US3] Enforce running-state guards on `PUT {id}` and `DELETE {id}` (return 409 `queue_running` when status is Running per FR-005a) in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T018, T035)
- [x] T037 [P] [US3] Add a status chip (labeled `Running`/`Stopped` per FR-014) + Start/Stop buttons to the `QueuesPage` list rows (Start/Stop call the service; Edit/Delete disabled while Running) in `src/web-ui/src/pages/QueuesPage.tsx` (depends on T023, T012)

**Checkpoint**: All three user stories are independently functional; full create → add → start → stop → delete workflow works from the Queues tab.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, consistency, and end-to-end validation across stories.

- [ ] T038 [P] Run `specs/046-emulator-execution-queue/quickstart.md` end-to-end (incl. the restart check: config persists, entries empty, status Stopped) — *manual UI walkthrough still pending; restart/persistence behavior is covered by automated tests (QueueRuntimeStore unit tests + endpoint integration tests).*
- [x] T039 [P] Verify CamelCase-only method names and ≤50 LOC per function across new C#/TS files; run ESLint + .NET analyzers clean
- [x] T040 Confirm coverage thresholds (≥80% line / ≥70% branch) on new backend and frontend modules; add edge-case tests if short
- [ ] T041 [P] Regenerate/verify OpenAPI (`specs/openapi.json`) reflects the new `/api/queues` routes if the project tracks it — *deferred: requires running the service to dump the OpenAPI doc; not regenerated in this pass.*
- [x] T042 [P] Scale smoke test for SC-007: seed ~50 queues each with ~100 entries, assert `GET /api/queues` (list) and `GET /api/queues/{id}` (detail) complete within the <1s interaction budget, in `tests/integration/Queues/QueueScaleTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational. US1 is the MVP; US2 and US3 build on the same endpoints file/page but are independently testable. Recommended order P1 → P2 → P3.
- **Polish (Phase 6)**: After the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational.
- **US2 (P2)**: Depends on Foundational; reuses US1's endpoint group + page shell (T018/T023) but adds independent behavior.
- **US3 (P3)**: Depends on Foundational; T036 (running guards) builds on US1's PUT/DELETE (T018) and US3's status (T035).

### Within Each User Story

- Tests written first and failing before implementation.
- Models → persistence/store → endpoints → frontend.
- Same-file tasks (e.g., all `QueuesEndpoints.cs` route additions, all `QueuesPage.tsx` edits) are sequential, not parallel.

### Parallel Opportunities

- Setup: T001, T002 in parallel.
- Foundational: T003/T004/T005/T007/T009/T012/T013 in parallel; then T006 (after T003/T005), T008 (after T007), T010, then T011 (after T006/T008/T010).
- US1 tests T014–T017 in parallel; T020 and T022 in parallel with endpoint work (different files); T018→T019 sequential (same file); T023 after T022/T012.
- US2 tests T025–T027 in parallel; T028→T029 sequential (same file); T030 parallel; T031 after T030.
- US3 tests T032–T034 in parallel; T035→T036 sequential (same file); T037 parallel.

---

## Parallel Example: Foundational Phase

```bash
# Launch independent foundational tasks together:
Task: "Create ExecutionQueue model in src/GameBot.Domain/Queues/ExecutionQueue.cs"        # T003
Task: "Create QueueEntry + QueueRuntimeState in src/GameBot.Domain/Queues/QueueEntry.cs"  # T004
Task: "Define IQueueRepository in src/GameBot.Domain/Queues/IQueueRepository.cs"           # T005
Task: "Define IQueueRuntimeStore in src/GameBot.Domain/Queues/IQueueRuntimeStore.cs"       # T007
Task: "Create DTOs in src/GameBot.Service/Contracts/Queues/"                               # T009
Task: "Create services/queues.ts client in src/web-ui/src/services/queues.ts"             # T012
```

## Parallel Example: User Story 1 Tests

```bash
Task: "FileQueueRepository unit tests in tests/unit/Queues/FileQueueRepositoryTests.cs"          # T014
Task: "CRUD endpoint integration tests in tests/integration/Queues/QueuesCrudEndpointTests.cs"   # T015
Task: "API contract test in tests/contract/Queues/QueuesApiContractTests.cs"                     # T016
Task: "QueueForm tests in src/web-ui/src/components/queues/__tests__/QueueForm.test.tsx"         # T017
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1 — CRUD).
3. **STOP and VALIDATE**: create/list/edit/delete a queue; confirm config persists across restart.
4. Demo if ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → CRUD MVP → validate/demo.
3. US2 → sequence entries + stale handling → validate/demo.
4. US3 → start/stop status + running guards → validate/demo.

### Parallel Team Strategy

After Foundational, one developer can take the backend endpoint additions while another builds the frontend page/components, since the FE client (T012) and DTOs (T009) define the shared contract up front.

---

## Notes

- [P] = different files, no incomplete dependencies.
- Endpoint routes and `QueuesPage.tsx` edits accumulate across stories in the same files → keep those sequential.
- The non-persistent runtime store gives restart-reset behavior for free; the repository must persist config only.
- Verify tests fail before implementing; commit after each task or logical group.
