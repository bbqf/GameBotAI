---
description: "Task list for Execution Log Grid Cleanup"
---

# Tasks: Execution Log Grid Cleanup

**Input**: Design documents from `/specs/050-execution-log-grid/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/execution-log-grid.md, quickstart.md

**Tests**: Included — the project constitution (Testing Standards) mandates tests for executable logic, and the plan calls for specific component/unit test updates.

**Organization**: Tasks are grouped by user story. NOTE: all three stories operate on the single page component `src/web-ui/src/pages/ExecutionLogs.tsx`, so cross-story tasks that touch it are sequential (not parallel) — see Dependencies. US1 establishes the grid structure that US2 and US3 build on.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 maps to the spec's user stories

## Path Conventions

Web application; only the frontend `src/web-ui` is affected:
- Page: `src/web-ui/src/pages/ExecutionLogs.tsx`
- Helpers (new): `src/web-ui/src/pages/executionLogGrid.ts`
- Styles: `src/web-ui/src/styles.css`
- Tests: `src/web-ui/src/pages/__tests__/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, green baseline before refactoring.

- [X] T001 Establish a green baseline by running `npm test`, `npm run lint`, and `npm run build` in `src/web-ui` and confirming all pass before any changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared view-model helpers used by US1 and US2. Pure functions in a new module — no component changes here, so they do not pre-empt any story.

**⚠️ CRITICAL**: Complete before US1/US2 implementation tasks that consume the helpers.

- [X] T002 [P] Create the grid view-model helpers module `src/web-ui/src/pages/executionLogGrid.ts` exporting: the `GridRow` type; `typeLabel(nodeKind)` map (sequence→Sequence, command→Command, step→Step, condition→Condition, loop→Loop, loopIteration→Iteration, wait→Wait, tap→Tap, fallback to raw); `composeInfo(node)` (message + appended delay/condition/wait detail per data-model.md); `projectEntryRow(entry)` and `projectNodeRow(node, parentKey)`; and `nodeKey(node, parentKey)` for stable keys. CamelCase only, each function < ~50 LOC.
- [X] T003 [P] Add unit tests `src/web-ui/src/pages/__tests__/executionLogGrid.test.ts` covering: `typeLabel` mapping incl. fallback; `composeInfo` with appliedDelay, conditionTrace, and detailAttributes appended; top-level projection (name/type/status/info from summary); node projection (blank timestamp, label as name); `nodeKey` stability/uniqueness across siblings.

**Checkpoint**: Helpers exist and are unit-tested; grid component work can begin.

---

## Phase 3: User Story 1 - Single full-width grid replaces split layout (Priority: P1) 🎯 MVP

**Goal**: Replace the two-panel list/detail layout with one full-width grid whose rows carry the six columns (Expand | Timestamp | Name | Type | Status | Additional information), reusing the former detail text in the Additional information column.

**Independent Test**: Open the page; confirm a single full-width grid with the six columns, no separate Execution Detail panel, and a sequence top-level row showing its summary in the Additional information column.

### Tests for User Story 1 ⚠️

> Write/adjust first; expect failure before implementation.

- [X] T004 [US1] In `src/web-ui/src/pages/__tests__/ExecutionLogsTree.test.tsx`, add tests asserting: a single grid (`role="table"`) renders with the six column headers; no "Execution details" panel/heading is present; a top-level row shows Name, Type ("Sequence"), Status, and the summary text in the Additional information column.

### Implementation for User Story 1

- [X] T005 [US1] In `src/web-ui/src/pages/ExecutionLogs.tsx`, remove the detail panel and its supporting code: `renderDetail`, the `detail`/`loadingDetail`/`detailError`/`showPhoneDetail` state and the `selectedId`-driven detail effect, and the `getExecutionLogDetail` import and `openStepDeepLink`/`stepOutcomes` rendering. Also remove the row-selection highlight from the component: drop `selectedId`/`handleSelectRow` and the `execution-logs-row--selected` class usage (rows no longer have a selected state). The matching CSS rule is removed in T008.
- [X] T006 [US1] In `src/web-ui/src/pages/ExecutionLogs.tsx`, remove the phone/desktop layout switch and `useNavigationCollapse` usage; render one full-width grid container (single layout for all widths).
- [X] T007 [US1] In `src/web-ui/src/pages/ExecutionLogs.tsx`, render the six-column grid header and top-level rows via `projectEntryRow` from the helpers module (Expand · Timestamp · Name · Type · Status · Additional information), preserving the existing filter inputs, sort toggles, timestamp-mode select, empty/loading/error states, and pagination hint. (Depends on T002)
- [X] T008 [P] [US1] In `src/web-ui/src/styles.css`, add full-width grid styles with sensible per-column min-widths and a horizontally scrollable container for narrow viewports; remove the now-unused `.execution-logs-layout*`, `.execution-logs-detail*`, and `.execution-logs-row--selected` rules.

**Checkpoint**: The page is a single full-width six-column grid with no detail panel; top-level rows show all summary info. (FR-001..FR-004, FR-010, FR-012; SC-001, SC-002, SC-007)

---

## Phase 4: User Story 2 - Expandable commands and multi-level independent expansion (Priority: P1)

**Goal**: Make stand-alone commands expandable like sequences, let sequence command-steps expand to a second level reusing the same expandable-command rendering, and make every row's expansion state independent.

**Independent Test**: Expand a stand-alone command to see its sub-elements; expand a sequence, then a command step within it to reveal that command's own sub-elements; expand two top-level rows at once and confirm collapsing one leaves the other open.

### Tests for User Story 2 ⚠️

- [X] T009 [US2] In `src/web-ui/src/pages/__tests__/ExecutionLogsTree.test.tsx`, update/add tests: after expanding a sequence the command step is visible but its child tap is NOT until the command step is also expanded (per-node expansion); a stand-alone command row is expandable; two top-level rows can be expanded simultaneously and collapsing one keeps the other expanded; non-container nodes (tap) render no expand control.

### Implementation for User Story 2

- [X] T010 [US2] In `src/web-ui/src/pages/ExecutionLogs.tsx`, replace the single `expandedId` state with an `expandedKeys: Set<string>` and a `toggleExpand(key)` that adds/removes only that key (independent state; supports multiple open rows).
- [X] T011 [US2] In `src/web-ui/src/pages/ExecutionLogs.tsx`, implement a recursive sub-element row renderer using `projectNodeRow`/`nodeKey`, rendering the same six columns with depth indentation and an expand control only when the node has children. (Depends on T002, T007)
- [X] T012 [US2] In `src/web-ui/src/pages/ExecutionLogs.tsx`, drive subtree fetch/caching off `expandedKeys` (fetch a top-level subtree at most once on first expand, reuse cache for nested toggles) and refresh expanded running subtrees within the existing ~2 s live-poll loop.
- [X] T013 [P] [US2] In `src/web-ui/src/styles.css`, add nested-row indentation and expand-control styles consistent across all levels.

**Checkpoint**: Commands and sequences expand to arbitrary depth with independent per-row state and no extra fetches. (FR-005..FR-007a, FR-011, FR-013; SC-003, SC-006, SC-008)

---

## Phase 5: User Story 3 - Remove "Open in sequence" buttons (Priority: P2)

**Goal**: Remove all deep-link buttons from the page.

**Independent Test**: Browse and expand rows; confirm no "Open in sequence" button appears anywhere, and all other information remains visible.

### Tests for User Story 3 ⚠️

- [X] T014 [US3] In `src/web-ui/src/pages/__tests__/ExecutionLogsTree.test.tsx`, remove the existing deep-link test and add an assertion that no element with the text "Open in sequence" exists after expanding sequences and commands.

### Implementation for User Story 3

- [X] T015 [US3] In `src/web-ui/src/pages/ExecutionLogs.tsx`, remove all "Open in sequence" buttons, the `openAuthoringDeepLink` helper, and any remaining `deepLink` rendering on rows/sub-elements.

**Checkpoint**: Zero deep-link buttons; remaining info intact. (FR-008, FR-009; SC-004, SC-005)

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final cleanup and verification.

- [X] T016 [P] Remove any now-dead CSS selectors, unused imports/exports, and dead helpers across `src/web-ui/src/pages/ExecutionLogs.tsx` and `src/web-ui/src/styles.css`; run `eslint --fix` in `src/web-ui`.
- [X] T017 Run `npm run lint`, `npm run build`, and `npm test` in `src/web-ui` and confirm all are green (Quality Gate — hard stop on any failure).
- [ ] T018 Execute the manual validation in `specs/050-execution-log-grid/quickstart.md` (all 10 checks) and confirm SC-001…SC-008.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: After Setup. Blocks grid tasks that consume the helpers (T007, T011).
- **User Stories (Phase 3–5)**: After Foundational. Because all three stories edit `ExecutionLogs.tsx`, they proceed **sequentially in priority order** (US1 → US2 → US3): US1 builds the grid structure, US2 adds expansion to it, US3 strips deep links from it.
- **Polish (Phase 6)**: After all stories.

### Within Each User Story

- The story's test task is written first and expected to fail before its implementation tasks.
- US1: T005 (remove detail) → T006 (single layout) → T007 (grid + columns); T008 (CSS) parallel.
- US2: T010 (state) → T011 (recursive renderer) → T012 (fetch/caching wiring); T013 (CSS) parallel.
- US3: T015 after T014.

### Parallel Opportunities

- **T002 and T003** (Foundational) are different files → parallel.
- Within a story, the **CSS task** (`styles.css`) runs in parallel with that story's `ExecutionLogs.tsx` work (different files): T008 ∥ T005–T007; T013 ∥ T010–T012.
- All other `ExecutionLogs.tsx` tasks and all `ExecutionLogsTree.test.tsx` tasks share files → **not** parallel.

---

## Parallel Example: Foundational Phase

```bash
# Different files, no dependencies:
Task: "Create grid helpers module src/web-ui/src/pages/executionLogGrid.ts"      # T002
Task: "Unit tests src/web-ui/src/pages/__tests__/executionLogGrid.test.ts"        # T003
```

## Parallel Example: User Story 1

```bash
# CSS runs alongside the component refactor (different files):
Task: "Full-width grid + horizontal-scroll styles in src/web-ui/src/styles.css"   # T008
# while T005–T007 edit ExecutionLogs.tsx sequentially
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP and VALIDATE**: single full-width grid with six columns and no detail panel; top-level rows fully informative.
3. This is a shippable MVP: the page is already cleaner and full-width even before expansion polish.

### Incremental Delivery

1. Setup + Foundational → helpers ready.
2. US1 → full-width grid (MVP) → validate.
3. US2 → multi-level independent expansion → validate.
4. US3 → deep links removed → validate.
5. Polish → lint/build/test green + quickstart.

---

## Notes

- [P] = different files, no dependencies; most work is concentrated in one component so [P] is limited to the helpers module, its unit tests, and the CSS file.
- Frontend-only: no backend or API/contract changes; `executionLogsApi.ts` is unchanged (the page simply stops calling `getExecutionLogDetail`).
- Verify each story's tests fail before implementing it.
- Quality Gate: a red lint/build/test is a hard stop (constitution) — fix before marking a phase complete.
