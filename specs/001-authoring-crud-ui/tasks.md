# Tasks: Authoring CRUD UI

**Input**: Design documents from `/specs/001-authoring-crud-ui/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Frontend logic requires unit tests per Constitution. Include Jest + React Testing Library tasks only where specified.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize shared UI structure in `src/web-ui` per plan.

- [ ] T001 Create `src/web-ui/src/components` directory and index setup
- [ ] T002 Create `src/web-ui/src/pages` directory scaffold
- [ ] T003 Create `src/web-ui/src/services` directory scaffold
- [ ] T004 [P] Add `src/web-ui/src/components/Nav.tsx` with tabs for Actions, Commands, Games, Sequences, Triggers
- [ ] T005 [P] Add `src/web-ui/src/components/List.tsx` reusable list component (name, key attributes)
- [ ] T006 [P] Add `src/web-ui/src/components/Dropdown.tsx` reusable dropdown (name display, id value)
- [ ] T007 [P] Add `src/web-ui/src/components/ConfirmDeleteModal.tsx` confirmation modal component
- [ ] T008 Configure ESLint to include new directories in `src/web-ui/.eslintrc.cjs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before user stories.

- [ ] T009 Implement `src/web-ui/src/services/apiClient.ts` (base fetch, JSON handling, error normalization)
- [ ] T010 [P] Implement `src/web-ui/src/services/actions.ts` CRUD functions mapped to contracts/openapi.yaml
- [ ] T011 [P] Implement `src/web-ui/src/services/commands.ts` CRUD functions mapped to contracts/openapi.yaml
- [ ] T012 [P] Implement `src/web-ui/src/services/games.ts` CRUD functions mapped to contracts/openapi.yaml
- [ ] T013 [P] Implement `src/web-ui/src/services/sequences.ts` CRUD functions mapped to contracts/openapi.yaml
- [ ] T014 [P] Implement `src/web-ui/src/services/triggers.ts` CRUD functions mapped to contracts/openapi.yaml
- [ ] T015 Wire `src/web-ui/src/main.tsx` to render `Nav` and active page container
- [ ] T016 Add error boundary in `src/web-ui/src/components/ErrorBoundary.tsx` (friendly messages)

**Checkpoint**: Foundation ready — user story implementation can begin.

---

## Phase 3: User Story 1 - Navigate and browse objects (Priority: P1) 🎯 MVP

**Goal**: Users select an object type via navigation and view a list of existing objects.

**Independent Test**: Selecting any type shows its list with predictable ordering; no create/edit/delete needed.

### Implementation for User Story 1

- [ ] T017 [P] [US1] Create `src/web-ui/src/pages/ActionsPage.tsx` list-only view using `List` and `services/actions.ts`
- [ ] T018 [P] [US1] Create `src/web-ui/src/pages/CommandsPage.tsx` list-only view using `List` and `services/commands.ts`
- [ ] T019 [P] [US1] Create `src/web-ui/src/pages/GamesPage.tsx` list-only view using `List` and `services/games.ts`
- [ ] T020 [P] [US1] Create `src/web-ui/src/pages/SequencesPage.tsx` list-only view using `List` and `services/sequences.ts`
- [ ] T021 [P] [US1] Create `src/web-ui/src/pages/TriggersPage.tsx` list-only view using `List` and `services/triggers.ts`
- [ ] T022 [US1] Wire `Nav` tab selection to switch pages in `src/web-ui/src/App.tsx`
- [ ] T023 [US1] Add client-side sorting by name in `List.tsx` and optional filter when items > 50

**Checkpoint**: User Story 1 independently testable.

---

## Phase 4: User Story 2 - Create new object with references (Priority: P1)

**Goal**: Users can create new items; where references are required, use name-based dropdowns that store/send IDs.

**Independent Test**: Create a new command referencing actions; verify persistence and appearance in list.

### Implementation for User Story 2

- [ ] T024 [P] [US2] Add `src/web-ui/src/components/Form.tsx` with validation hooks (required fields, constraints)
- [ ] T025 [P] [US2] Implement `src/web-ui/src/pages/ActionsPage.tsx` create form modal (uses `services/actions.ts`)
- [ ] T026 [P] [US2] Implement `src/web-ui/src/pages/GamesPage.tsx` create form modal (uses `services/games.ts`)
- [ ] T027 [P] [US2] Implement `src/web-ui/src/pages/CommandsPage.tsx` create form modal with dropdown of Actions (names → IDs)
- [ ] T028 [P] [US2] Implement `src/web-ui/src/pages/SequencesPage.tsx` create form with dropdown of Commands array (ordered)
- [ ] T029 [P] [US2] Implement `src/web-ui/src/pages/TriggersPage.tsx` create form with dropdowns (Actions, Commands, Sequence)
- [ ] T030 [US2] Refresh lists immediately after successful create; preserve inputs on error

**Checkpoint**: User Story 2 independently testable.

---

## Phase 5: User Story 3 - Edit and delete existing objects (Priority: P2)

**Goal**: Users can update fields/references and delete with confirmation; block deletion when referenced.

**Independent Test**: Edit trigger attributes and references; delete with confirmation; verify list updates.

### Implementation for User Story 3

- [ ] T031 [P] [US3] Implement edit flows in `ActionsPage.tsx` (inline or modal) using `services/actions.ts`
- [ ] T032 [P] [US3] Implement edit flows in `GamesPage.tsx` using `services/games.ts`
- [ ] T033 [P] [US3] Implement edit flows in `CommandsPage.tsx` with actions dropdown
- [ ] T034 [P] [US3] Implement edit flows in `SequencesPage.tsx` with commands ordering
- [ ] T035 [P] [US3] Implement edit flows in `TriggersPage.tsx` with reference dropdowns
- [ ] T036 [US3] Implement delete with `ConfirmDeleteModal` in all pages; if API returns 409, surface guidance to unlink/migrate
- [ ] T037 [US3] Ensure list refresh on successful edit/delete across pages

**Checkpoint**: User Story 3 independently testable.

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Documentation updates in `specs/001-authoring-crud-ui/quickstart.md`
- [ ] T039 Add empty-state messaging across pages (when no objects exist)
- [ ] T040 Performance tuning: batch fetch reference dropdown data per page
- [ ] T041 Accessibility: labels, focus management, color contrast in `Form.tsx`, `Dropdown.tsx`
- [ ] T042 Error messages: actionable guidance; use `ErrorBoundary` consistently
- [ ] T043 Add basic unit tests for `List.tsx`, `Form.tsx`, `Dropdown.tsx`, `ConfirmDeleteModal.tsx`

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → User Stories (Phases 3–5) → Polish.
- User stories are independent once foundational tasks complete; US1 and US2 can proceed in parallel; US3 can proceed in parallel after foundational tasks.

### User Story Completion Order
- US1 (P1) → US2 (P1) → US3 (P2) — or parallel after Phase 2 if staffed.

### Parallel Execution Examples
- [P] Tasks across pages (e.g., T017–T021) can be executed in parallel.
- Services per type (T010–T014) can run in parallel.
- Create/edit flows per page (T025–T029, T031–T035) can run in parallel.

---

## Implementation Strategy

- MVP: Complete Phases 1–2 and Phase 3 (US1) only; validate independently.
- Incremental: Add Phase 4 (US2) → validate → Add Phase 5 (US3) → validate.

---

## Format Validation

- All tasks follow `- [ ] TNNN [P?] [US?] Description with file path` format.
- Story labels appear only in user story phases.
- [P] marker appears only on parallelizable tasks.

---

## Summary

- Output: `specs/001-authoring-crud-ui/tasks.md`
- Total tasks: 43
- Per user story: US1 (7), US2 (7), US3 (7)
- Parallel opportunities: Services per type, page implementations, component builds
- Independent test criteria: Defined per story checkpoints
- Suggested MVP scope: Phase 3 (US1)
