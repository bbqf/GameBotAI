# Tasks: Semantic Actions UI

**Input**: Design documents from `/specs/001-semantic-actions-ui/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Required for executable logic per constitution. Included per story as applicable.

**Organization**: Tasks are grouped by user story to keep each slice independently deliverable and testable.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no blocking dependency)
- **[Story]**: User story label (US1, US2, US3)
- Every task includes an exact file path

---

## Phase 1: Setup (Shared Infrastructure)
**Purpose**: Prepare configuration needed by all stories.

- [x] T001 Create environment sample with API base URL in src/web-ui/.env.local.example
- [x] T002 Document action API base URL usage in src/web-ui/README.md

---

## Phase 2: Foundational (Blocking Prerequisites)
**Purpose**: Shared types, clients, hooks, and base UI shell required by all stories.

- [x] T003 Define Action/ActionType/AttributeDefinition types in src/web-ui/src/types/actions.ts
- [x] T004 [P] Implement action API client (get types, get action, create, update, duplicate, validate) in src/web-ui/src/services/actionsApi.ts
- [x] T005 [P] Add action-type fetch hook with 5-minute cache/etag handling in src/web-ui/src/services/useActionTypes.ts
- [x] T006 [P] Implement validation utilities derived from AttributeDefinitions in src/web-ui/src/services/validation.ts
- [x] T007 [P] Build dynamic field renderer component (text/number/boolean/enum, help text, inline errors) in src/web-ui/src/components/actions/FieldRenderer.tsx
- [x] T008 Create reusable action form shell (title, loading/error, save controls) in src/web-ui/src/components/actions/ActionForm.tsx
- [x] T009 Register Actions routes (list/create/edit) in src/web-ui/src/router.tsx and stub pages in src/web-ui/src/pages/actions/

**Checkpoint**: Foundation ready; user stories can proceed.

---

## Phase 3: User Story 1 - Create a valid action without JSON (Priority: P1)

Goal: Enable users to create a valid action via forms without raw JSON.
Independent Test: Tester creates a new action selecting a type, completing required fields, and saving successfully; invalid inputs block save with field-level errors.

### Tests for User Story 1
- [x] T010 [P] [US1] Add hook/component tests for action-type fetch/cache in src/web-ui/src/services/__tests__/useActionTypes.spec.ts
- [x] T011 [P] [US1] Add form rendering and validation tests for required/range/enum fields in src/web-ui/src/components/actions/__tests__/ActionForm.spec.tsx

### Implementation for User Story 1
- [x] T012 [US1] Implement create action form submission using POST /actions in src/web-ui/src/components/actions/ActionForm.tsx
- [x] T013 [US1] Add create page wiring (load definitions, render form, success/error states) in src/web-ui/src/pages/actions/CreateActionPage.tsx
- [x] T014 [US1] Enforce client-side validation (requiredness, ranges, patterns, enums) with inline guidance in src/web-ui/src/components/actions/ActionForm.tsx
- [x] T015 [US1] Ensure accessibility (labels, keyboard navigation, focus on errors) and loading/error handling in src/web-ui/src/components/actions/ActionForm.tsx and src/web-ui/src/pages/actions/CreateActionPage.tsx

**Checkpoint**: User Story 1 independently testable (create flow works, invalid inputs blocked, no JSON editing needed).

---

## Phase 4: User Story 2 - Edit an existing action safely (Priority: P2)

Goal: Allow editing existing actions with safeguards when changing types.
Independent Test: Tester opens an action, edits values, optionally changes type, confirms discard prompt for incompatible fields, and saves successfully when valid.

### Tests for User Story 2
- [ ] T016 [P] [US2] Add edit flow tests (load existing action, type change confirmation, save) in src/web-ui/src/pages/actions/__tests__/EditActionPage.spec.tsx

### Implementation for User Story 2
- [ ] T017 [US2] Load existing action data and populate form in src/web-ui/src/pages/actions/EditActionPage.tsx using actionsApi.get
- [ ] T018 [US2] Implement type-change compatibility check and confirmation modal in src/web-ui/src/components/actions/ActionForm.tsx
- [ ] T019 [US2] Add update submission via PUT /actions/{id} with success/error handling in src/web-ui/src/pages/actions/EditActionPage.tsx and src/web-ui/src/services/actionsApi.ts

**Checkpoint**: User Story 2 independently testable (edit + type-change safeguards).

---

## Phase 5: User Story 3 - Browse and duplicate actions (Priority: P3)

Goal: Let users browse/filter actions and duplicate an action to accelerate authoring.
Independent Test: Tester filters list by type, selects an action, duplicates it, and saves the duplicate after validation.

### Tests for User Story 3
- [ ] T020 [P] [US3] Add list filtering and duplicate-flow tests in src/web-ui/src/pages/actions/__tests__/ActionsListPage.spec.tsx

### Implementation for User Story 3
- [ ] T021 [US3] Implement actions list view with type filter and empty/error states in src/web-ui/src/pages/actions/ActionsListPage.tsx
- [ ] T022 [US3] Wire duplicate action call POST /actions/{id}/duplicate in src/web-ui/src/services/actionsApi.ts and UI trigger in ActionsListPage.tsx
- [ ] T023 [US3] Support create-from-duplicate prefilled form handoff in src/web-ui/src/pages/actions/CreateActionPage.tsx

**Checkpoint**: User Story 3 independently testable (browse/filter/duplicate flow).

---

## Phase 6: Polish & Cross-Cutting

- [ ] T024 [P] Add lightweight perf logging/metrics around definition fetch and form render in src/web-ui/src/services/actionsApi.ts and src/web-ui/src/components/actions/ActionForm.tsx
- [ ] T025 [P] Update documentation (quickstart and src/web-ui/README.md) for new flows and env config
- [ ] T026 Run full test suites (npm test in src/web-ui, dotnet test -c Debug) and record coverage per constitution

---

## Dependencies & Execution Order
- Phase 1 -> Phase 2 -> User Stories (US1 -> US2 -> US3) -> Polish.
- User stories can start after Phase 2; US2/US3 can run in parallel but US1 is the MVP baseline.

## Parallel Execution Examples
- After Phase 2: T010 and T011 can run in parallel; T012-T015 can proceed once tests exist.
- US2: T016 can run while T017-T019 are built, then integrate.
- US3: T020 can run parallel to T021-T023.
- Polish: T024 and T025 can run in parallel; T026 runs last.

## Implementation Strategy
- MVP first: Complete Phases 1-2, then US1; validate create flow end-to-end.
- Incremental: Ship US1, then add US2 safeguards, then US3 browse/duplicate.
- Keep tasks small; commit after each task or checkpoint.
