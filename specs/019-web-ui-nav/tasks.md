# Tasks: Web UI Navigation Restructure

**Input**: Design documents from `/specs/019-web-ui-nav/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, contracts/

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Ensure web UI deps installed in src/web-ui (npm install) — pre-check for lint/test commands

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T002 Create navigation data model scaffolding (NavigationArea, NavigationState) in src/web-ui/src/types/navigation.ts
- [X] T003 [P] Add responsive breakpoint helper (~768px) and collapse state hook in src/web-ui/src/hooks/useNavigationCollapse.ts
- [X] T004 [P] Add placeholder Execution route entry (inactive view) in src/web-ui/src/routes/index.tsx

**Checkpoint**: Navigation shell primitives ready for user stories.

---

## Phase 3: User Story 1 - Navigate authoring workspace (Priority: P1) 🎯 MVP

**Goal**: Users reach Authoring and see Actions/Sequences/Commands grouped; no Triggers UI; tab navigation with active state and collapse to menu.
**Independent Test**: From landing, select Authoring and access Actions/Sequences/Commands with one click; no Triggers entry; collapsed menu still works; entering a legacy triggers URL falls through to the standard not-found view.

### Tests for User Story 1

- [X] T005 [P] [US1] Add RTL tests for tab bar active state and collapsed menu behavior (including keyboard/focus for menu items) in src/web-ui/src/__tests__/navigation.spec.tsx
- [ ] T006 [P] [US1] Add Playwright smoke for tab switching (Authoring/Configuration/Execution), collapsed menu keyboard access, and absence of Triggers in src/web-ui/tests/navigation.spec.ts
- [X] T007 [P] [US1] Add RTL regression to verify Actions/Sequences/Commands remain one-click (≤1 from landing) from Authoring with no added click depth in src/web-ui/src/__tests__/authoring-regression.spec.tsx
- [ ] T008 [P] [US1] Add Playwright regression to verify authoring items reachable in ≤1 click from landing without extra navigation in src/web-ui/tests/authoring-regression.spec.ts

### Implementation for User Story 1

- [X] T009 [US1] Implement top tab navigation shell with active styling and collapse menu using the collapse hook/helper in src/web-ui/src/components/Navigation.tsx
- [X] T010 [US1] Wire Authoring tab to group Actions/Sequences/Commands routes and remove Triggers route entirely in src/web-ui/src/routes/index.tsx
- [X] T011 [US1] Add accessible focus/aria-current handling for tabs and collapsed menu items in src/web-ui/src/components/Navigation.tsx

**Checkpoint**: Authoring navigation functional with responsive tabs and no Triggers UI.

---

## Phase 4: User Story 2 - Manage connection settings (Priority: P2)

**Goal**: Host/token controls visible at top of Configuration area, separate from authoring content.
**Independent Test**: Open Configuration; host/token fields editable without opening other areas; nav still one-click.

### Tests for User Story 2

- [X] T012 [P] [US2] Add RTL test ensuring host/token controls render at top of Configuration view in src/web-ui/src/__tests__/configuration.spec.tsx
- [ ] T013 [P] [US2] Add Playwright check for navigating to Configuration and editing host/token without authoring content bleed in src/web-ui/tests/configuration.spec.ts

### Implementation for User Story 2

- [X] T014 [US2] Arrange Configuration page layout with host/token header section in src/web-ui/src/pages/Configuration.tsx
- [X] T015 [US2] Ensure Configuration tab wiring/persistence with active state and collapse support in src/web-ui/src/routes/index.tsx

**Checkpoint**: Configuration accessible with host/token at top; navigation intact.

---

## Phase 5: User Story 3 - Explore execution area (Priority: P3)

**Goal**: Execution area loads placeholder explaining future functionality; navigation works both ways.
**Independent Test**: Open Execution; see clear empty-state message; can switch back to other areas in one click.

### Tests for User Story 3

- [X] T016 [P] [US3] Add RTL test for Execution empty-state messaging and links in src/web-ui/src/__tests__/execution.spec.tsx
- [ ] T017 [P] [US3] Add Playwright smoke for visiting Execution and returning to Authoring/Configuration in src/web-ui/tests/execution.spec.ts

### Implementation for User Story 3

- [X] T018 [US3] Implement Execution placeholder view with guidance/links in src/web-ui/src/pages/Execution.tsx
- [X] T019 [US3] Ensure Execution tab route uses NavigationState/aria-current and respects collapse behavior in src/web-ui/src/routes/index.tsx

**Checkpoint**: Execution placeholder live; navigation consistent across areas.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T020 [P] Document navigation behavior, collapse breakpoint, and trigger removal (routes deleted, standard not-found only) in specs/019-web-ui-nav/quickstart.md
- [ ] T021 Run quality gates from src/web-ui: npm run lint; npm test; npm run e2e; record perf note if tab switch p95 exceeds 150ms
- [ ] T022 [P] Measure tab switch performance (p95 latency, CLS) via Lighthouse/Profiler on desktop, cold first tab switch after load with cache enabled; record results in specs/019-web-ui-nav/quickstart.md or a perf note
- [ ] T023 [P] Validate host/token discoverability (SC-002) via quick usability/heuristic check and note findings in specs/019-web-ui-nav/quickstart.md

---

## Dependencies & Execution Order

- Phase dependencies: Setup → Foundational → US1 → US2 → US3 → Polish
- User stories: US1 (nav shell, Authoring) before US2/US3 to establish tabs; US2 and US3 independent after US1.
- Task dependencies: Navigation shell (T009/T010) precedes story-specific page wiring (T014/T018); tests per story depend on corresponding implementation but should be authored first.

## Parallel Opportunities

- Foundational: T003, T004 parallel after T002.
- US1 tests can run in parallel (T005, T006, T007, T008) and authored before implementation; T009/T010/T011 sequential.
- US2 tests in parallel (T012, T013); T014, T015 sequential after nav shell exists.
- US3 tests in parallel (T016, T017); T018, T019 sequential after nav shell exists.
- Polish tasks can run after all stories complete; T020 and T022 in parallel; T021 independent gate run.

## MVP Scope

- Complete through User Story 1 (navigation shell, Authoring grouping, no triggers, responsive collapse) as the MVP.

## Task Counts

- Total tasks: 23
- Per story: US1 (7), US2 (4), US3 (4); Setup/Foundational (4); Polish (4)

## Independent Test Criteria per Story

- US1: Authoring accessible via tabs/collapsed menu; Actions/Sequences/Commands present; no Triggers; active state visible; no added click depth.
- US2: Host/token fields appear at top of Configuration and are editable without other content; nav remains one-click.
- US3: Execution shows clear empty-state and allows return to other areas via tabs/menu.
