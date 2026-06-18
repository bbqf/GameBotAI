---

description: "Task list for feature 060: Queue-Start and After-Every-Step Scheduling"
---

# Tasks: Queue-Start and After-Every-Step Scheduling

**Input**: Design documents from `/specs/060-queue-start-after-every-scheduling/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: REQUIRED by the project constitution (Principle II — Testing Standards). Test tasks are included per story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in each description.

## Path Conventions

Web application (per plan.md): backend under `src/GameBot.*`, frontend under `src/web-ui/src`, tests under `tests/` (backend) and colocated `__tests__/` (web-ui).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a green baseline before changes (constitution NON-NEGOTIABLE: no work proceeds on a red build/test).

- [ ] T001 Verify baseline is green: run backend `dotnet build` + `dotnet test`, and from `src/web-ui` run `npx vite build` + `npx jest`. Record that all pass before making changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared domain enum that every story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 Add `AtQueueStart = 3` (with XML doc describing run-start, template-order, once-per-run, counts-toward-executed behavior) to the `ScheduleType` enum, and add an XML-doc note on `EveryStep` that it is displayed as "After Every Step", in src/GameBot.Domain/QueueTemplates/ScheduleType.cs

**Checkpoint**: Enum extended — backend execution, API validation, and UI typing can now reference `AtQueueStart`.

---

## Phase 3: User Story 1 - Run setup sequences at queue start (Priority: P1) 🎯 MVP

**Goal**: Entries marked `AtQueueStart` run once per run, in template order, before any timer evaluation and before the first `OncePerRun` step, counting toward the executed total, with non-fatal failure handling.

**Independent Test**: Run `QueueExecutionServiceTests` and the schedule-type integration test; confirm at-queue-start sequences execute first, in order, are counted, run once per run, and a failure does not abort the run.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL before T006)

- [ ] T003 [US1] Add unit tests to tests/unit/Queues/QueueExecutionServiceTests.cs for at-queue-start ordering and counting: runs before timer evaluation AND before the first `OncePerRun` step; multiple at-queue-start entries run in template order; each firing increments `executed` (FR-003/FR-014/FR-015).
- [ ] T004 [US1] Add unit tests to tests/unit/Queues/QueueExecutionServiceTests.cs for at-queue-start lifecycle: runs once per run on a cycling queue (not per cycle, FR-004); a failing at-queue-start sequence is non-fatal (increments `failed`, run continues, FR-007); a template containing only at-queue-start entries runs them once and completes (FR-008/SC-003). (Same file as T003 — sequence after it.)
- [ ] T005 [P] [US1] Add an end-to-end ordering test to tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs asserting at-queue-start entries execute before timers/normal steps in a real run (SC-001).

### Implementation for User Story 1

- [ ] T006 [US1] Implement the at-queue-start pre-pass in src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs: partition `AtQueueStart` entries; run them once, in template order, before the `do/while` loop (after session connect) via `RunOneSequenceAsync`; `executed++` per firing and `failed++` on failure; honor cancellation and the connection-lost check; ensure a template with only at-queue-start entries completes cleanly (`cycles = 1`, no busy-loop).

**Checkpoint**: User Story 1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Rename "Every Step" to "After Every Step" (Priority: P2)

**Goal**: The existing per-step option displays as "After Every Step" in the UI, with behavior and the `EveryStep` wire/stored identifier unchanged (backward compatible). The narrow trigger (only after `OncePerRun` steps) is preserved.

**Independent Test**: Run the Jest `QueueEntryList` label test, the contract backward-compat test, and the every-step negative-trigger unit test; confirm the UI shows "After Every Step", the `EveryStep` wire value round-trips, and every-step fires only after normal steps.

### Tests for User Story 2 ⚠️

- [ ] T007 [P] [US2] Add a contract test to tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs verifying an `EveryStep` entry still saves and reads back as `EveryStep` (rename does not change the wire value, FR-002/FR-010).
- [ ] T008 [US2] Add a negative-trigger unit test to tests/unit/Queues/QueueExecutionServiceTests.cs asserting an `EveryStep` entry runs ONLY after `OncePerRun` steps — it does NOT fire after `AtQueueStart` executions and does NOT fire after timer/relative firings (FR-005/FR-006). (Same file as T003/T004; depends on T006 impl so at-start firings exist.)
- [ ] T009 [P] [US2] Add a Jest test to src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx asserting the per-step option renders with the label/badge text "After Every Step" (FR-002/FR-012).

### Implementation for User Story 2

- [ ] T010 [US2] Rename the `EveryStep` display label to "After Every Step" in `SCHEDULE_LABELS` and update the every-step badge text + `aria-label` in src/web-ui/src/components/queues/QueueEntryList.tsx (label-only; do not change the `ScheduleType` value).

**Checkpoint**: Rename complete and verified; existing `EveryStep` templates/clients unaffected; narrow trigger confirmed.

---

## Phase 5: User Story 3 - Configure the options in UI and API (Priority: P3)

**Goal**: `AtQueueStart` (and the renamed "After Every Step") are selectable in the template editor and round-trip through the API; invalid schedule types are rejected with the standard error envelope.

**Independent Test**: Run the contract tests + Jest option/round-trip specs; confirm `AtQueueStart` is a dropdown option, persists across save/reload, round-trips via the API, and an invalid type returns 400.

### Tests for User Story 3 ⚠️

- [ ] T011 [US3] Add contract tests to tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs: an `AtQueueStart` entry saves and reads back unchanged; an unrecognized `scheduleType` returns 400 with the `{ error: { code, message, hint } }` envelope (FR-009/FR-013). (Same file as T007 — sequence after it.)
- [ ] T012 [US3] Add a Jest test to src/web-ui/src/components/queues/__tests__/QueueEntryList.test.tsx: "At Queue Start" appears as a dropdown option and shows its badge when selected (FR-012). (Same file as T009 — sequence after it.)
- [ ] T013 [P] [US3] Add a Jest test to src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx asserting an `AtQueueStart` entry round-trips through save and reload (FR-009/SC-004).

### Implementation for User Story 3

- [ ] T014 [US3] Update the "accepted values" validation error string to include `AtQueueStart` (e.g. "OncePerRun, EveryStep, Timer, AtQueueStart") in src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs
- [ ] T015 [P] [US3] Update doc comments to list `AtQueueStart` as an accepted/returned schedule type in src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs and src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs
- [ ] T016 [P] [US3] Add `'AtQueueStart'` to the `ScheduleType` union in src/web-ui/src/services/queueTemplates.ts
- [ ] T017 [US3] Add `AtQueueStart: 'At Queue Start'` to `SCHEDULE_LABELS` and render an "At Queue Start" badge for such entries in src/web-ui/src/components/queues/QueueEntryList.tsx (depends on T010 same file, and T016 type)
- [ ] T018 [US3] Verify (and adjust if needed) that schedule state passes `AtQueueStart` through save/reload defaults in src/web-ui/src/pages/QueuesPage.tsx

**Checkpoint**: Both options are fully configurable in UI and API and round-trip correctly.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 Add explicit regression assertions for unchanged behavior (FR-011): an entry with no `scheduleType` defaults to `OncePerRun`, and `OncePerRun`/`Timer` ordering and counting are unchanged; and the edge case where a template has `EveryStep` entries but no `OncePerRun` steps behaves as today — in tests/unit/Queues/QueueExecutionServiceTests.cs and/or tests/contract/QueueTemplates/QueueTemplatesApiContractTests.cs.
- [ ] T020 [P] Validate the operator + API + run-time walkthrough in specs/060-queue-start-after-every-scheduling/quickstart.md against the implemented behavior.
- [ ] T021 Final green gate: backend `dotnet build` + `dotnet test`, and from `src/web-ui` `npx vite build` + `npx jest` — all pass with coverage baselines met for touched areas (≥80% line / ≥70% branch).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: After Setup. **Blocks all user stories** (shared enum).
- **User Stories (Phase 3–5)**: All depend on Foundational (T002).
  - US1 (P1) is independent and is the MVP.
  - US2 (P2) depends on US1's impl (T006) for the negative-trigger test (T008) and shares the execution test file (T003/T004).
  - US3 (P3) depends on US2 for the shared `QueueEntryList.tsx` label map (T017 after T010).
- **Polish (Phase 6)**: After all desired stories complete.

### Key task-level dependencies

- T002 → T003, T004, T005, T006, T008, T011, T014, T016 (anything referencing `AtQueueStart`).
- T003 → T004 → T008 (same execution test file, sequential).
- T003, T004, T005 → T006 (impl) — TDD: write failing tests first.
- T006 → T008 (negative-trigger test needs at-start firings to exist).
- T007 → T011 (same contract test file).
- T009 → T012 (same Jest test file).
- T010 → T017 (same `QueueEntryList.tsx`); T016 → T017 (type before use).
- T009/T012 → T010/T017 (TDD where practical).

### Parallel Opportunities

- US1: T005 (integration, different file) runs in parallel with T003/T004; T003→T004 are same-file sequential.
- US2: T007 and T009 are different files → run in parallel; T008 sequenced in the execution test file.
- US3: T013, T015, T016 are different files with no shared-file conflict → run in parallel.
- After Foundational, US1 and US2 can largely proceed together (mind the shared execution test file and T006→T008); US3 follows US2 for the shared UI file.

---

## Parallel Example: User Story 1

```bash
# Write US1 tests, confirm they FAIL, then implement T006.
# T005 (integration) is a different file and can run alongside T003/T004:
Task: "Integration test in tests/integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs"
# T003 then T004 in tests/unit/Queues/QueueExecutionServiceTests.cs (same file, sequential)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup (T001) → Phase 2 Foundational (T002) → Phase 3 US1 (T003–T006).
2. **STOP and VALIDATE**: at-queue-start behavior via backend tests. Demo if ready.

### Incremental Delivery

1. Setup + Foundational → ready.
2. US1 (At Queue Start behavior) → test → demo (MVP!).
3. US2 (rename label + narrow-trigger guarantee) → test → demo.
4. US3 (UI/API enablement) → test → demo.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- The "After Every Step" change is label-only — never change the `EveryStep` enum/wire value (FR-002/FR-010).
- `AtQueueStart` entries require no timer fields; validation only inspects timer fields under the `Timer` branch.
- Commit after each task or logical group; keep a green build/test at every checkpoint.
