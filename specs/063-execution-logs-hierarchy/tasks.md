---

description: "Task list for feature 049 — Execution Logs Reflect What Was Actually Executed"
---

# Tasks: Execution Logs Reflect What Was Actually Executed

**Input**: Design documents from `specs/049-execution-logs-hierarchy/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/execution-logs-hierarchy.openapi.yaml, quickstart.md

**Tests**: Included — the project constitution (Principle II, Testing Standards) makes tests mandatory for executable logic.

**Organization**: Tasks are grouped by user story (US1, US2, US3) so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish carry no story label)

## Path Conventions

Web application: backend at `src/GameBot.Service/` + `src/GameBot.Domain/`; frontend at `src/web-ui/src/`; tests under `tests/{unit,integration,contract}/` and `src/web-ui/src/**/__tests__/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the constitution green-build gate before changes.

- [X] T001 Confirm baseline is green before changes: run `dotnet build`, `dotnet test`, and `npm --prefix src/web-ui test`; record any pre-existing failures so they are not attributed to this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared model/repository/DTO plumbing that ALL user stories build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Add the `running` lifecycle status: extend `NormalizeStatus` to allow `running` (alongside `success`/`failure`) in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs and document the value on `ExecutionLogEntry.FinalStatus` in src/GameBot.Domain/Logging/ExecutionLogModels.cs
- [X] T003 [P] Add a `RootsOnly` flag to `ExecutionLogQuery` (filters `ParentExecutionId is null`) in src/GameBot.Domain/Logging/ExecutionLogModels.cs
- [X] T004 Add `UpsertAsync` and `GetSubtreeAsync(rootId)` to the repository interface in src/GameBot.Domain/Logging/IExecutionLogRepository.cs
- [X] T005 [P] Unit tests (write first, expect FAIL) for repository hierarchy behavior — upsert replaces by id with no duplicate, roots-only query excludes children, subtree returns descendants ordered by `SequenceIndex` — in tests/unit/ExecutionLogs/ExecutionLogRepositoryHierarchyTests.cs
- [X] T006 Implement `UpsertAsync` (replace-by-id in memory + overwrite file), roots-only filtering in `QueryAsync`, and `GetSubtreeAsync` (descendants by `RootExecutionId`, recursive via `ParentExecutionId`) in src/GameBot.Domain/Logging/FileExecutionLogRepository.cs (depends on T004, makes T005 pass)
- [X] T007 [P] Add backend DTOs — widen `finalStatus` to include `running` and add `childCount` on `ExecutionLogEntryDto` (no separate `status` field), plus `ExecutionTreeNodeDto` and `ExecutionSubtreeResponseDto` (root `finalStatus`) — in src/GameBot.Service/Models/ExecutionLogs.cs
- [X] T008 [P] Add frontend types (widen `finalStatus` to `'running' | 'success' | 'failure'`, add `childCount`, `ExecutionTreeNodeDto`, `ExecutionSubtreeResponseDto`) and a `getSubtree(executionId)` client calling `GET /api/execution-logs/{id}/subtree` in src/web-ui/src/services/executionLogsApi.ts

**Checkpoint**: Repository/model/DTO plumbing ready — user stories can begin.

---

## Phase 3: User Story 1 - Executed sequence appears as a single entry with sub-elements nested (Priority: P1) 🎯 MVP

**Goal**: A sequence run shows as exactly one top-level row; invoked commands are recorded as linked children (kept, filtered from the list) and rendered as expandable nested tree rows with all of today's detail.

**Independent Test**: Execute a multi-command sequence, open Execution Logs → exactly one new top-level row; expand it → all steps/commands/primitives/conditions/loops/waits reachable; no standalone child-command rows in the list.

### Tests for User Story 1 (write first, expect FAIL) ⚠️

- [X] T009 [P] [US1] Integration test: a sequence run produces 1 root (`Depth 0`, no parent) + N child command entries (`Depth 1`, `ParentExecutionId` = root, `SequenceIndex` = step order); roots-only list returns only the sequence in tests/integration/ExecutionLogs/SequenceGroupingIntegrationTests.cs
- [X] T010 [P] [US1] Contract test: `GET /api/execution-logs` returns roots-only and items expose `finalStatus` (incl. `running`)/`childCount`; **sorting + per-column filtering still return correct results over roots-only with no child rows leaking** (covers FR-010/SC-006); `GET /api/execution-logs/{id}/subtree` matches contracts/execution-logs-hierarchy.openapi.yaml in tests/contract/ExecutionLogs/ExecutionLogsHierarchyContractTests.cs
- [X] T011 [P] [US1] Unit test: subtree projection builds ordered nested nodes — command-backed steps correlated to child entries by `SequenceIndex`, conditions/loops/loop-iterations/waits preserved with their attributes — **including a nested-sequence case (sequence-invoking-sequence) that yields a ≥2-level tree with no extra top-level entry** (covers FR-009) — in tests/unit/ExecutionLogs/ExecutionSubtreeProjectionTests.cs
- [X] T012 [P] [US1] Web-ui test: top-level rows are expandable; expanding a sequence shows nested sub-elements; expanding a command node shows that command's primitive/tap/wait outcomes; **a sub-element's deep link navigates to its authored sequence/step** (covers FR-011) in src/web-ui/src/pages/__tests__/ExecutionLogsTree.test.tsx

### Implementation for User Story 1

- [X] T013 [US1] Add `ForceExecuteDetailedAsync` / `ForceExecuteAsync` overloads accepting an `ExecutionLogContext` (parent/root/depth/sequenceIndex) and pass it through to `LogCommandExecutionAsync` in src/GameBot.Service/Services/CommandExecutor.cs and src/GameBot.Service/Services/ICommandExecutor.cs
- [X] T014 [US1] Add `LogSequenceStartAsync` (writes an in-progress root entry, returns its id) and finalize-via-`UpsertAsync` (final status, summary, full `StepOutcomes`); add the subtree projection builder producing `ExecutionSubtreeResponseDto`/`ExecutionTreeNodeDto` in src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs (depends on T002, T006, T007)
- [X] T015 [US1] Wire `sequences/{id}/execute`: create the root first, pass `{ParentExecutionId/RootExecutionId = rootId, Depth = 1, SequenceIndex = step order, SequenceId, SequenceLabel}` into the per-command callback (`commandExecutor.ForceExecuteAsync`), and finalize the root at the end in src/GameBot.Service/Program.cs (depends on T013, T014)
- [X] T016 [US1] Default the list endpoint to roots-only, populate `childCount` (and `finalStatus` incl. `running`), and add `GET /api/execution-logs/{id}/subtree` returning the projection in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs (depends on T006, T007, T014)
- [X] T017 [US1] Refactor the Execution Logs page into expandable tree rows: lazy-load `getSubtree` on expand and render nested nodes (steps, commands, conditions, loops/iterations, waits) preserving today's detail (applied delay, condition trace, wait attributes, deep links) in src/web-ui/src/pages/ExecutionLogs.tsx (depends on T008)
- [X] T018 [US1] Sync the published OpenAPI document (roots-only list fields + `/{id}/subtree`) in specs/openapi.json (depends on T016)

**Checkpoint**: Sequence grouping + nested tree fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Stand-alone command execution still appears as its own entry (Priority: P1)

**Goal**: A directly executed command remains its own top-level leaf entry with full detail, unaffected by the linking changes.

**Independent Test**: Execute a command directly → one top-level row with full details, `childCount = 0`, not expandable, not nested under any sequence.

### Tests for User Story 2 (write first, expect FAIL) ⚠️

- [X] T019 [P] [US2] Integration test: direct command execution yields a single root entry (`Depth 0`, `ParentExecutionId` null), `childCount = 0`, and never appears as a child in any subtree in tests/integration/ExecutionLogs/StandaloneCommandIntegrationTests.cs
- [X] T020 [P] [US2] Web-ui test: a leaf top-level command row has no expand affordance and still surfaces full detail in src/web-ui/src/pages/__tests__/ExecutionLogsStandalone.test.tsx

### Implementation for User Story 2

- [X] T021 [US2] Guarantee the context-less `ForceExecute*` path keeps `Depth 0` / no parent (regression guard from T013) and ensure leaf entries (`childCount = 0`) render non-expandable in src/GameBot.Service/Services/CommandExecutor.cs and src/web-ui/src/pages/ExecutionLogs.tsx

**Checkpoint**: Both stand-alone commands and grouped sequences behave correctly.

---

## Phase 5: User Story 3 - Progress updates live during sequence execution (Priority: P2)

**Goal**: While a sequence runs, a single in-progress (`running`) top-level entry is shown and its sub-elements update live (no manual reload); it settles into its final state on completion.

**Independent Test**: Start a longer sequence, watch Execution Logs → one `running` entry, sub-elements update via polling, no standalone child rows; on finish the same entry shows final status + complete tree.

### Tests for User Story 3 (write first, expect FAIL) ⚠️

- [X] T022 [P] [US3] Integration test: during execution an in-progress root exists with status `running`; after completion the SAME entry is upserted to `success`/`failure` (no duplicate) with full `StepOutcomes` in tests/integration/ExecutionLogs/SequenceLiveStatusIntegrationTests.cs
- [X] T023 [P] [US3] Web-ui test (fake timers): the page polls (~2s) while a `running` entry is visible, refreshes an expanded in-progress subtree without reload, and stops polling once nothing is in progress in src/web-ui/src/pages/__tests__/ExecutionLogsPolling.test.tsx

### Implementation for User Story 3

- [X] T024 [US3] Ensure the in-progress (`running`) root is returned by the roots-only list with correct `finalStatus`, ordered with completed entries, and not removed by retention until finalized in src/GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs and src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
- [X] T025 [US3] Add interval polling (~2s) gated on the presence of any `running` entry; re-fetch the list and any expanded in-progress subtree; clear the interval when none are in progress in src/web-ui/src/pages/ExecutionLogs.tsx (depends on T017)

**Checkpoint**: All three user stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Consistency, backward compatibility, performance, and validation.

- [X] T026 [P] Update existing execution-log tests that assume the old flat behavior to reflect hierarchy linking + roots-only listing in tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs, tests/integration/ExecutionLogs/ExecutionHierarchyIntegrationTests.cs, and tests/integration/ExecutionLogs/ExecutionLogConcisenessIntegrationTests.cs
- [X] T027 [P] Backward-compatibility test: historical entries (no `running` status, no children) render as completed leaf roots and open without error; keep the published contract additive in tests/contract/OpenApiBackwardCompatTests.cs (and a fixture-based case under tests/integration/ExecutionLogs/)
- [X] T028 [P] Add a CHANGELOG.md entry (user-visible: execution logs now group by what was actually executed, with live nested view) and update any execution-logs notes in docs/
- [X] T029 Performance verification: roots-only list and subtree fetch <1s at target scale, live sub-element update visible within ~2s (SC-005/006/007); add a brief perf note to the PR
- [ ] T030 Run all quickstart.md scenarios A–F and confirm expected outcomes — NOTE: the API-level checks (roots-only list, `/{id}/subtree`) are covered by automated contract/integration tests; the UI scenarios (A–F) require a live emulator/session and remain pending manual validation on a device.
- [X] T031 Final constitution gate: `dotnet build` clean (0 warnings/errors); `npm --prefix src/web-ui test` 317/317 across 74 suites with coverage thresholds met; `dotnet test` — all execution-log unit/integration/contract suites green (contract 67, integration 235, unit execution-log 28). One pre-existing flaky test unrelated to this feature (`BackgroundCaptureScreenSourceTests.ReturnsBitmapCloneWhenFrameAvailable`) fails only when its sibling capture tests run first and passes in isolation; it touches no code in this feature.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: all depend on Foundational.
  - US1 (P1) is the MVP and should land first (it establishes the create-root-at-start linkage, the subtree endpoint, and the tree UI that US2/US3 rely on).
  - US2 (P1) verifies/guards the stand-alone path on the same list/UI surface; start after US1's endpoint + page exist.
  - US3 (P2) adds the live-polling surfacing on top of US1's in-progress root + tree.
- **Polish (Phase 6)**: after the desired user stories are complete.

### User Story Dependencies

- **US1**: depends only on Foundational.
- **US2**: Foundational + reuses US1's list endpoint/page (narrower guarantee on the same surface).
- **US3**: Foundational + US1 (in-progress root + tree); UI polling depends on T017.

### Within Each User Story

- Write tests first and confirm they FAIL before implementing.
- Backend models/services before endpoints; endpoints before frontend wiring.
- Story complete and validated before moving to the next priority.

### Parallel Opportunities

- Foundational: T002, T003, T005, T007, T008 are [P] (distinct files). T004 → T006 are sequential.
- US1 tests T009–T012 are [P]. Implementation T013/T014 can proceed in parallel (different files) before T015/T016 integrate them; T017 (frontend) is [P] with backend tasks until wiring.
- US2 tests T019, T020 are [P]. US3 tests T022, T023 are [P].
- Polish T026, T027, T028 are [P].

---

## Parallel Example: User Story 1

```bash
# Launch US1 tests together (write first, expect FAIL):
Task: "Integration grouping test in tests/integration/ExecutionLogs/SequenceGroupingIntegrationTests.cs"
Task: "Contract test in tests/contract/ExecutionLogs/ExecutionLogsHierarchyContractTests.cs"
Task: "Subtree projection unit test in tests/unit/ExecutionLogs/ExecutionSubtreeProjectionTests.cs"
Task: "Tree UI test in src/web-ui/src/pages/__tests__/ExecutionLogsTree.test.tsx"

# Independent implementation files that can start together:
Task: "CommandExecutor context overloads in src/GameBot.Service/Services/CommandExecutor.cs"
Task: "Execution Logs tree page in src/web-ui/src/pages/ExecutionLogs.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (CRITICAL — blocks all stories).
2. Phase 3 US1 → **STOP and VALIDATE**: a sequence shows as one expandable entry with all sub-elements; no standalone child rows.
3. Demo the MVP.

### Incremental Delivery

1. Foundation ready → US1 (grouping + nested tree, MVP) → demo.
2. US2 (stand-alone command guarantee) → demo.
3. US3 (live polling updates) → demo.
4. Polish (compat, perf, docs, quickstart) → ship.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- The execution **queue** (specs 046–048) has no real execution path yet and produces no logs — out of scope.
- Child execution records are **kept and linked**, only filtered from the top-level list (FR-002a) — never deleted.
- Preserve the existing `{ error: { code, message, hint } }` API error shape and the phone/desktop responsive split.
- Commit after each task or logical group; keep the build/tests green (constitution NON-NEGOTIABLE gate).
