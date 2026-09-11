# Tasks: Duplicate Queues

**Input**: Design documents from `/specs/083-duplicate-queues/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/duplicate-queue.md, quickstart.md

**Tests**: Included — the plan's Constitution Check commits to backend + web-ui test coverage for this feature (Testing Standards principle).

**Organization**: Single user story (P1 — the whole feature is one independently testable slice), organized backend-first then web-ui, per plan.md's project structure.

## Phase 1: Setup

- [ ] T001 Confirm no new packages are needed: this feature reuses `IQueueRepository`, `IQueueRuntimeStore`, and existing `QueuesEndpoints.cs`/`queues.ts` — no `dotnet add package` / `npm install` required. (No file changes; sanity check only.)

## Phase 2: Foundational

- [ ] T002 [P] Create `DuplicateQueueRequest` contract in `src/GameBot.Service/Contracts/Queues/DuplicateQueueRequest.cs` with a single nullable `Name` string property, matching the style of `src/GameBot.Service/Contracts/Queues/UpdateQueueRequest.cs`.

**Checkpoint**: Contract compiles; ready for endpoint wiring.

## Phase 3: User Story 1 - Duplicate an existing queue (Priority: P1)

**Goal**: A user can duplicate an existing queue via the API and the Queues web UI, getting a new queue that copies every configuration field and current entries from the source, with a different required name, always created stopped.

**Independent Test**: `POST /api/queues/{id}/duplicate` with a valid new name against a queue that has a linked template, linked game, and non-empty entries returns `201` with a queue matching the source field-for-field (except id/name/status history); repeating with the source's unchanged name returns `400`.

### Tests for User Story 1

- [ ] T003 [P] [US1] Add integration tests in `tests/integration/Queues/QueuesCrudEndpointTests.cs` (or a new sibling file `tests/integration/Queues/QueuesDuplicateEndpointTests.cs` if preferred for isolation) covering: successful duplicate copies all config fields + entries and returns `201` with `Location` header; duplicate of a queue with no linked template/game copies the absence; duplicating while the source is `Running` still succeeds and the new queue is `Stopped`; missing/blank `name` returns `400 invalid_request`; `name` equal to the source's current name returns `400 invalid_request`; unknown source `id` returns `404 not_found`; the source queue is unchanged (re-fetch and compare) after duplication.
- [ ] T004 [P] [US1] Add a contract test case in `tests/contract/Queues/QueuesApiContractTests.cs` asserting the `POST /api/queues/{id}/duplicate` response shape matches the existing `QueueResponse` contract (same fields as `POST /api/queues`'s response).

### Implementation for User Story 1

- [ ] T005 [US1] Implement the `POST {id}/duplicate` handler in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (`MapQueueEndpoints`, alongside the other `group.MapPost("{id}/...")` handlers): load the source queue (404 if missing), validate `Name` (400 if blank, 400 if it equals the trimmed source `Name` — ordinal comparison — with message "name must differ from the original queue's name"), create a new `ExecutionQueue` via `repo.CreateAsync` copying `EmulatorSerial`, `CycleExecution`, `PauseWhenIdle`, `IdleThresholdSeconds` (through `CoerceThreshold`), `EmulatorInstanceName` (through `NormalizeInstanceName`), `EmulatorInstanceIndex`, `LinkedTemplateId`, `LinkedGameId` from the source, then copy runtime entries via `runtime.SetEntries(created.Id, runtime.GetEntries(id).Select(e => e.SequenceId))`, and return `Results.Created($"{ApiRoutes.Queues}/{created.Id}", BuildResponse(created, runtime))`. Name the endpoint `.WithName("DuplicateQueue")`.
- [ ] T006 [US1] Add `duplicateQueue(id: string, name: string)` to `src/web-ui/src/services/queues.ts`, calling `postJson<QueueDto>(`${base}/${id}/duplicate`, { name })`, placed near `createQueue`/`updateQueue`.
- [ ] T007 [P] [US1] Create `src/web-ui/src/components/queues/DuplicateQueueModal.tsx`: a small modal (mirroring `ConfirmDeleteModal.tsx`'s structure — `modal-backdrop`/`modal`/`modal-actions`, `role="dialog"` `aria-modal="true"`) with a single controlled text input pre-filled by the caller with `"<source name> (copy)"`, a "Duplicate" confirm button (disabled when the trimmed value is empty or equals the source name) and a "Cancel" button, plus an optional inline error message slot for server-side rejection (e.g. name-unchanged, blank name).
- [ ] T008 [P] [US1] Add a Jest test `src/web-ui/src/components/queues/__tests__/DuplicateQueueModal.test.tsx` (mirroring `ConfirmDeleteModal.test.tsx`) covering: renders with the pre-filled suggested name; Cancel invokes `onCancel`; submitting with the pre-filled name invokes `onConfirm` with that name; the confirm button is disabled when the edited name is emptied or set back to the source name; an externally supplied error message renders.
- [ ] T009 [US1] Wire a "Duplicate" row action into `src/web-ui/src/pages/QueuesPage.tsx`: add a `duplicateOpen`/`duplicateSource` state pair, a "Duplicate" button next to "Edit"/"Delete" in the actions cell (available regardless of running state, matching FR-008 — do not gate it on `running`), opening `DuplicateQueueModal` pre-filled with `"${q.name} (copy)"`; on confirm call `duplicateQueue(source.id, name)`, close the modal, `refresh()` the list, and show a `tableMessage` success confirmation (`'Queue "<name>" duplicated successfully.'`); on a `400`/`409`-style `ApiError` keep the modal open and show the server message inline via the modal's error slot instead of the page-level `tableError` (so the user can correct the name without losing the dialog).

**Checkpoint**: Duplicate is usable end-to-end via API and UI; source queues are never mutated.

## Phase 4: Polish & Cross-Cutting Concerns

- [ ] T010 [P] Update `docs/architecture.md`: add a bullet to the Domain model section (near the existing "Queue" / "Queue Template" bullets) describing queue duplication — copies all configuration fields and current runtime entries, links the same template (no template copy), always created stopped, name must differ from the source — and refresh the `_Last reviewed: <date>_` line at the top of the file (Living Documentation principle).
- [ ] T011 [P] Update `specs/083-duplicate-queues/spec.md`'s `**Status**` line to `Implemented` once T005-T009 are merged, and add a corresponding row to `specs/STATUS.md`'s table (`| 083 | Duplicate Queues | Implemented |`), keeping the table's existing column alignment.
- [ ] T012 [P] Add a `CHANGELOG.md` entry under `## [Unreleased]` → `### Added`, following the existing bullet style (feature number + short description), e.g. "Duplicate queues (083-duplicate-queues)" summarizing the new `POST /api/queues/{id}/duplicate` endpoint and the Queues page "Duplicate" action.
- [ ] T013 Run the full verification pass: `dotnet build` + `dotnet test` from the repo root, and `npm run build` + `npx jest` (or the project's standard test script) in `src/web-ui`, per the web-ui quality gate (`vite build` + `jest` is the real green gate, not `tsc --noEmit`/lint). Fix any failures before considering the feature done (constitution's Release Blocker gate).

## Dependencies & Execution Order

- Phase 1 (T001) has no dependencies.
- Phase 2 (T002) has no dependencies; blocks T005 (handler needs the request contract).
- User Story 1 tests (T003, T004) can be written in parallel with each other and before/alongside T005-T009 (write first if following TDD; they exercise the same endpoint so should land before or with T005).
- T005 depends on T002. T006 depends on T005 (needs the real endpoint shape) but can be stubbed against the contract doc first if desired. T007/T008 are independent of the backend (pure UI component) and can proceed in parallel with T002-T006. T009 depends on T006 and T007.
- Phase 4 (T010-T013) runs after Phase 3 is complete; T013 is last.

## Parallel Example

```text
# After T002 lands, these can run together:
T003 [P] [US1] Backend integration tests
T004 [P] [US1] Backend contract test
T007 [P] [US1] DuplicateQueueModal component (no backend dependency)
T008 [P] [US1] DuplicateQueueModal test (depends only on T007)
```

## Implementation Strategy

MVP = this entire feature (single P1 story): T002 → T005 (backend) unlocks a working API; T006-T009
(web-ui) make it usable from the Queues page. T010-T012 are documentation upkeep required by the
constitution's Living Documentation principle, not functional scope. T013 is the final quality
gate before calling the feature done.
