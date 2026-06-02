---
description: "Task list for Queue–Template Link with Auto-Load"
---

# Tasks: Queue–Template Link with Auto-Load

**Input**: Design documents from `specs/049-queue-template-link/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queue-template-link-api.md, quickstart.md

**Tests**: INCLUDED — the project constitution mandates tests for executable logic
(≥80% line / ≥70% branch on touched areas) and the plan's Testing Strategy enumerates them.

**Organization**: Tasks are grouped by user story. Both stories are P1; US1 (auto-load) is
the demonstrable MVP and is independently testable by seeding a queue with a link directly,
while US2 (set/clear the link) provides the user-facing way to create that link.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 or US2 (Setup/Foundational/Polish carry no story label)

## Path Conventions

Web application: C# backend under `src/GameBot.Domain/`, `src/GameBot.Service/`, tests under
`tests/`; React frontend under `src/web-ui/src/`. Paths below are exact.

⚠️ **Shared-file note** (do NOT parallelize these across tasks):
`src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (T008 → T013 → T021),
`src/web-ui/src/pages/QueuesPage.tsx` (T014 → T023 → T024),
`src/web-ui/src/services/queues.ts` (T009 → T022),
`tests/contract/Queues/QueuesApiContractTests.cs` (T011 → T017),
`specs/openapi.json` (T015 → T025) are each touched by multiple tasks and must be done in
the listed order.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a clean baseline before changing shared queue code.

- [x] T001 Confirm baseline is green: run `dotnet test` and (in `src/web-ui`) `npm test`, plus lint/format, and record that the tree builds clean before changes (constitution release-blocker gate). No new dependencies are added by this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The persisted link field, the runtime "first display" probe, and the response
projections that BOTH user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T002 [P] Add nullable `LinkedTemplateId` (persisted, by stable template ID) to the queue config in `src/GameBot.Domain/Queues/ExecutionQueue.cs`, with an XML doc comment noting it is 0..1 and rename-safe.
- [x] T003 [P] Add `bool HasRuntimeState(string queueId)` to the runtime store contract in `src/GameBot.Domain/Queues/IQueueRuntimeStore.cs` with a doc comment ("true iff a runtime state record exists; distinguishes never-materialized from exists-but-empty").
- [x] T004 Implement `HasRuntimeState` via `_states.ContainsKey(queueId)` in `src/GameBot.Domain/Queues/QueueRuntimeStore.cs` (depends on T003).
- [x] T005 [P] Unit-test the `HasRuntimeState` lifecycle (false before any op; true after AddEntry/SetEntries/SetStatus; false again after Remove) in `tests/unit/Queues/QueueRuntimeStoreHasStateTests.cs` (depends on T004).
- [x] T006 [P] Add `LinkedTemplateId` (string?) to `src/GameBot.Service/Contracts/Queues/QueueResponse.cs`.
- [x] T007 [P] Add `LinkedTemplateName` (string?) to `src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs`.
- [x] T008 Project the link fields in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: set `LinkedTemplateId` in `BuildResponse`, and resolve `LinkedTemplateName` from the template repository in `BuildDetailAsync` (null when unlinked/unresolved). Keep helpers ≤50 LOC (depends on T002, T006, T007).
- [x] T009 [P] Add `linkedTemplateId: string | null` to `QueueDto` and `linkedTemplateName: string | null` to `QueueDetailDto` in `src/web-ui/src/services/queues.ts`.

**Checkpoint**: Queue config persists a link field and every queue response carries it; the runtime store can report whether a queue has been materialized. User stories can now begin.

---

## Phase 3: User Story 1 - Linked template loads automatically when opening a queue (Priority: P1) 🎯 MVP

**Goal**: When a queue with a linked template is opened on its edit/detail page, its
template's ordered entries are auto-loaded into the queue's server-side runtime (once per
service lifetime, not while Running, not re-filling a deliberately emptied queue); a broken
link is cleared on open without error.

**Independent Test**: Seed a queue file with `LinkedTemplateId` pointing at a template
(A,B,C), with no runtime state (fresh start); `GET /api/queues/{id}` returns entries A,B,C
and `linkedTemplateName`. Re-open while Running, or with state already present, or with the
template deleted, to verify the guards and the clear-on-missing behavior.

### Tests for User Story 1

> Write these tests FIRST and ensure they FAIL before implementation.

- [x] T010 [P] [US1] Integration auto-load matrix in `tests/integration/Queues/QueueAutoLoadEndpointTests.cs`: linked + fresh → entries materialized from template; linked + Running → unchanged; linked + runtime state already exists → not re-filled (incl. a deliberately emptied queue stays empty); linked + template missing → empty AND `linkedTemplateId` cleared & persisted; **linked + template deleted while the queue already has materialized entries → those existing entries are left unchanged (FR-014, guard skips on existing runtime state)**; unlinked → empty, no error; materialized entries are usable by a subsequent start; **auto-load of a ~100-entry linked template completes within the SC-005 budget (<1s), reusing the existing scale-test pattern (`tests/integration/Queues/QueueScaleTests.cs`)**.
- [x] T011 [US1] Extend `tests/contract/Queues/QueuesApiContractTests.cs` to assert `GET /api/queues/{id}` exposes `linkedTemplateId` and `linkedTemplateName` (and list responses expose `linkedTemplateId`). (Shared file — before T017.)
- [x] T012 [P] [US1] Frontend test `src/web-ui/src/pages/__tests__/QueuesPage.autoload.spec.tsx`: opening a queue whose mocked `getQueue` returns `linkedTemplateName` + auto-loaded entries shows those entries and the template name on the controls; opening an unlinked queue shows "(no template)".

### Implementation for User Story 1

- [x] T013 [US1] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, inject `IQueueTemplateRepository` into the `GET {id}` handler and add a `MaybeAutoLoadAsync` helper applied before building the detail: skip when unlinked, Running, or `HasRuntimeState`; otherwise resolve the template — if missing, clear `LinkedTemplateId` and persist via `IQueueRepository.UpdateAsync`; else `SetEntries` from the template's sequence ids. Keep the helper ≤50 LOC (depends on T008).
- [x] T014 [P] [US1] In `src/web-ui/src/pages/QueuesPage.tsx`, change `openEdit` to derive `associatedTemplateName` from `q.linkedTemplateName ?? undefined` (replacing the reset-to-undefined); the auto-loaded entries arrive in `q.entries` with no extra call (depends on T009).
- [x] T015 [US1] Document the new `linkedTemplateId`/`linkedTemplateName` response fields. NOTE: `specs/openapi.json` does not contain the queues/queue-templates API (046/047/048 never added it; the queue family is contract-documented under `specs/<feature>/contracts/`). Documented in [contracts/queue-template-link-api.md](contracts/queue-template-link-api.md) instead of injecting an inconsistent isolated path into openapi.json.

**Checkpoint**: A queue with a pre-existing link auto-loads on open and survives restart; broken links clear themselves. US1 is independently demonstrable.

---

## Phase 4: User Story 2 - Associate a queue with a template (replace / auto-clear) (Priority: P1)

**Goal**: Loading a template into a queue, or saving the queue's entries to a template,
persists that template as the queue's link (by ID), replacing any prior link; the link is
settable even while the queue is Running (save-while-running); no new visual control is
added.

**Independent Test**: `PUT /api/queues/{id}/template { templateId }` persists the link
(re-read via a fresh repository instance); a different id replaces it; `null` clears it; an
unknown template → 400; unknown queue → 404; succeeds while Running. In the UI, loading and
saving each call the link endpoint with the right id.

### Tests for User Story 2

> Write these tests FIRST and ensure they FAIL before implementation.

- [x] T016 [P] [US2] Integration tests in `tests/integration/Queues/QueueTemplateLinkEndpointTests.cs`: set link persists across a new repository instance; set `null` clears; non-existent template → 400 `invalid_request`; non-existent queue → 404; link settable while Running; **per-queue independence (FR-004): setting Q2's link leaves Q1's `linkedTemplateId` and the referenced template's entries unchanged**.
- [x] T017 [US2] Extend `tests/contract/Queues/QueuesApiContractTests.cs` to assert `PUT /api/queues/{id}/template` exists and returns the queue detail. (Shared file — after T011.)
- [x] T018 [P] [US2] Frontend service test `src/web-ui/src/services/__tests__/queues.spec.ts`: `setQueueTemplateLink(id, templateId)` issues `PUT /api/queues/{id}/template` with `{ templateId }` (and `null` to clear).
- [x] T019 [P] [US2] Frontend test `src/web-ui/src/pages/__tests__/QueuesPage.link.spec.tsx`: loading a template calls `setQueueTemplateLink` with the loaded template's id; saving a template calls it with the saved template's id (from the save response).

### Implementation for User Story 2

- [x] T020 [P] [US2] Create `src/GameBot.Service/Contracts/Queues/SetQueueTemplateLinkRequest.cs` with `{ string? TemplateId }` and a doc comment (null clears the link).
- [x] T021 [US2] Add `PUT /api/queues/{id}/template` to `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: 404 if queue missing; 400 (`invalid_request`, "template not found") if `TemplateId` non-null and unresolved; otherwise set `LinkedTemplateId` (null clears), `UpdateAsync`, return the detail. NOT gated on Running. Reuse the existing error envelope (depends on T013, T020).
- [x] T022 [P] [US2] Add `setQueueTemplateLink(id, templateId: string | null)` → `putJson('/api/queues/{id}/template', { templateId })` in `src/web-ui/src/services/queues.ts` (depends on T009).
- [x] T023 [US2] In `src/web-ui/src/pages/QueuesPage.tsx`, thread the template id through the load path: `applyLoad(name, sequenceIds, templateId)` calls `setQueueTemplateLink(detail.id, templateId)` after `replaceQueueEntries`; pass `tpl.id` from `handleLoadTemplate` and `match.id` from `handleReload` (depends on T014, T022).
- [x] T024 [US2] In `src/web-ui/src/pages/QueuesPage.tsx`, after `saveQueueTemplate` resolves, call `setQueueTemplateLink(detail.id, saved.id)` so saving associates the queue with the saved template (depends on T023).
- [x] T025 [US2] Document the `PUT /api/queues/{id}/template` path (request body + 200/400/404). NOTE: same as T015 — the queues API is absent from `specs/openapi.json`; documented in [contracts/queue-template-link-api.md](contracts/queue-template-link-api.md).

**Checkpoint**: Operators can establish/replace a queue's link via existing load/save with no new controls; the link drives US1's auto-load end-to-end.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Verification and backward-compatibility safety net.

- [x] T026 [P] Add/confirm a backward-compatibility test in `tests/unit/Queues/FileQueueRepositoryTests.cs`: a queue JSON written without `LinkedTemplateId` deserializes with the field `null` (unlinked) — no migration needed.
- [x] T027 Run the full suites (`dotnet test`; `npm test` in `src/web-ui`) and lint/format/static analysis; confirm ≥80% line / ≥70% branch on touched areas and a clean build (constitution gate). RESULT: backend 642 passed (63 contract + 333 unit + 246 integration), web-ui 317 passed; backend build 0 warnings/0 errors; ESLint clean on all touched files. NOTE: repo has pre-existing ESLint/`tsc --noEmit` failures in unrelated files (BreakStepRow.tsx, CommandsPage.tsx, sessionsApi/validation specs, setupTests.ts) that predate this feature and are out of scope.
- [x] T028 Execute the `specs/049-queue-template-link/quickstart.md` behavioral flows. Verified via automated equivalents: restart→`AutoLoadSurvivesRestartAndIsRunnable`; replace→`SetLinkToDifferentTemplateReplacesPrior`; independence→`SettingOneQueuesLinkLeavesOtherQueuesAndTemplateUnchanged`; no-clobber→`AlreadyMaterializedQueueIsNotRefilledAfterDeliberateClear`; broken-link→`MissingTemplateOnFreshDisplayClearsLinkAndOpensEmpty`; running→`RunningQueueIsNotAutoLoaded`; link via load/save→`QueuesPage.link.spec.tsx` + `QueueTemplateLinkEndpointTests`. The list-start limitation is by-design (no test). Manual UI walkthrough optional for the operator.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: after Setup — BLOCKS both user stories.
- **US1 (Phase 3)** and **US2 (Phase 4)**: both after Foundational. They share
  `QueuesEndpoints.cs`, `QueuesPage.tsx`, the contract test, and `openapi.json`, so when
  worked together they must respect the shared-file order (T008→T013→T021;
  T014→T023→T024; T011→T017; T015→T025). US1 is independently testable without US2 (seed a
  link directly).
- **Polish (Phase 5)**: after the desired stories are complete.

### Within Each User Story

- Tests (T010–T012; T016–T019) are written first and must FAIL before implementation.
- Backend contract/request types before endpoint logic; endpoint before frontend wiring
  that calls it.
- US2's `applyLoad`/save wiring (T023/T024) builds on US1's `openEdit` change (T014).

### Parallel Opportunities

- Foundational: T002, T003, T006, T007, T009 in parallel (distinct files); then T004→T005, then T008.
- US1 tests: T010 and T012 in parallel (T011 edits the shared contract file).
- US2 tests: T016, T018, T019 in parallel (T017 edits the shared contract file after T011).
- US2 impl: T020 and T022 in parallel; T021 then T023→T024; T025 after T015.

---

## Parallel Example: Foundational Phase

```bash
# Distinct files, no inter-dependencies — run together:
Task: "Add LinkedTemplateId to src/GameBot.Domain/Queues/ExecutionQueue.cs"
Task: "Add HasRuntimeState to src/GameBot.Domain/Queues/IQueueRuntimeStore.cs"
Task: "Add LinkedTemplateId to src/GameBot.Service/Contracts/Queues/QueueResponse.cs"
Task: "Add LinkedTemplateName to src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs"
Task: "Add linkedTemplateId/linkedTemplateName to src/web-ui/src/services/queues.ts"
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Auto-load matrix integration test in tests/integration/Queues/QueueAutoLoadEndpointTests.cs"
Task: "Auto-load UI test in src/web-ui/src/pages/__tests__/QueuesPage.autoload.spec.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 (Setup) → Phase 2 (Foundational).
2. Phase 3 (US1): auto-load on open, verified by seeding a queue link directly.
3. **STOP and VALIDATE**: open a seeded linked queue, restart, confirm auto-load + runnable.

### Incremental Delivery

1. Setup + Foundational → field, probe, and response projections ready.
2. US1 → auto-load works for any pre-existing link → demo MVP.
3. US2 → loading/saving establishes the link with no new controls → full round trip.
4. Polish → backward-compat test + suite/quickstart verification.

---

## Notes

- [P] = different files, no dependencies on incomplete tasks.
- Both stories are P1; US1 is the demonstrable MVP, US2 closes the user-facing loop.
- No new logging (consistent with 047). No new dependencies.
- Watch the shared-file order callouts to avoid merge conflicts.
- Commit after each task or logical group; keep the build/tests green (release-blocker gate).
