# Tasks: UI Configuration Editor

**Input**: Design documents from `/specs/035-ui-config-editor/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/config-api.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Add new DTOs and route constants needed by multiple user stories

- [ ] T001 Add ConfigUpdateRequest and ConfigReorderRequest DTO records to src/GameBot.Service/Models/Config.cs
- [ ] T002 [P] Add ConfigParameters and ConfigParametersReorder route constants to src/GameBot.Service/ApiRoutes.cs

---

## Phase 2: Foundational (Backend API + Frontend Service)

**Purpose**: Implement backend service methods, wire PUT endpoints, and create frontend API client. MUST be complete before any user story work begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 Implement UpdateParametersAsync method on ConfigSnapshotService that loads saved config, validates no Environment-sourced keys, merges updates, calls RefreshAsync, and returns the new snapshot in src/GameBot.Service/Services/ConfigSnapshotService.cs
- [ ] T004 Implement ReorderParametersAsync method on ConfigSnapshotService that rebuilds the parameters dictionary in the requested key order (appending missing keys at end), persists, refreshes, and returns the new snapshot in src/GameBot.Service/Services/ConfigSnapshotService.cs
- [ ] T005 Add PUT /api/config/parameters endpoint that deserializes ConfigUpdateRequest, calls UpdateParametersAsync, returns snapshot or 400 error in src/GameBot.Service/Endpoints/ConfigEndpoints.cs
- [ ] T006 Add PUT /api/config/parameters/reorder endpoint that deserializes ConfigReorderRequest, calls ReorderParametersAsync, returns snapshot or 400 error in src/GameBot.Service/Endpoints/ConfigEndpoints.cs
- [ ] T007 [P] Create frontend config API service with getConfigSnapshot, updateParameters, and reorderParameters functions in src/web-ui/src/services/config.ts

**Checkpoint**: Backend API complete — GET returns snapshot, PUT updates values, PUT reorders keys. Frontend service ready for UI integration.

---

## Phase 3: User Story 1 — View All Backend Configuration Parameters (Priority: P1) 🎯 MVP

**Goal**: Operator opens the Configuration tab and sees every backend parameter (name, value, source badge) in persisted order. Environment parameters are visually marked read-only; secrets are masked.

**Independent Test**: Load the Configuration page and verify every parameter from GET /api/config appears in the UI in the same order as the JSON response.

### Implementation for User Story 1

- [ ] T008 [P] [US1] Create ConfigParameterRow component that renders parameter name, value (masked for secrets), and source badge (Default/File/Environment) in src/web-ui/src/components/ConfigParameterRow.tsx
- [ ] T009 [US1] Create ConfigParameterList component that accepts a parameters array and renders a scrollable list of ConfigParameterRow items in src/web-ui/src/components/ConfigParameterList.tsx
- [ ] T010 [US1] Rewrite Configuration page to fetch config snapshot via config service on mount, pass parameters to ConfigParameterList, and show loading/error states in src/web-ui/src/pages/Configuration.tsx

**Checkpoint**: Configuration tab displays all backend parameters with names, values, source badges. Secrets masked. Environment parameters visually distinct.

---

## Phase 4: User Story 2 — Edit a Parameter and Apply to Backend (Priority: P1)

**Goal**: Operator edits non-read-only parameter values inline, sees dirty-state highlighting, clicks "Apply All" to batch-send changes, and receives success/error feedback. Navigate-away prompt warns about unsaved edits.

**Independent Test**: Change a File-sourced parameter value, click Apply All, verify the backend returns the updated snapshot. Verify Environment-sourced rows are disabled. Verify dirty highlighting appears on modified rows.

### Implementation for User Story 2

- [ ] T011 [US2] Add inline text input for value editing to ConfigParameterRow; disable input for Environment-sourced parameters; show masked placeholder for secrets; emit onChange callback in src/web-ui/src/components/ConfigParameterRow.tsx
- [ ] T012 [US2] Add dirty-state tracking (original vs current values map), visual row highlight for modified parameters, and Apply All button to ConfigParameterList in src/web-ui/src/components/ConfigParameterList.tsx
- [ ] T013 [US2] Wire Apply All button to call updateParameters from config service, refresh parameter list on success, show inline error message on failure, and preserve unsaved edits on error in src/web-ui/src/components/ConfigParameterList.tsx
- [ ] T014 [US2] Add beforeunload listener and in-app navigate-away confirmation prompt when dirty edits exist in src/web-ui/src/pages/Configuration.tsx

**Checkpoint**: Full edit-apply cycle works. Dirty highlighting, Apply All, error handling, and navigate-away prompt all functional.

---

## Phase 5: User Story 3 — Collapse Backend Connection Settings (Priority: P2)

**Goal**: API Base URL and Bearer Token are inside a collapsible "Backend Connection" section that defaults to collapsed, keeping focus on backend parameters below.

**Independent Test**: Open Configuration tab, verify "Backend Connection" is collapsed. Click to expand, verify TokenGate fields appear and work. Collapse again, verify parameters remain visible.

### Implementation for User Story 3

- [ ] T015 [P] [US3] Create generic CollapsibleSection component using HTML5 details/summary elements with consistent styling in src/web-ui/src/components/CollapsibleSection.tsx
- [ ] T016 [US3] Wrap existing TokenGate inside CollapsibleSection with title "Backend Connection" and no open attribute (collapsed by default) in src/web-ui/src/pages/Configuration.tsx

**Checkpoint**: Backend Connection section is collapsed by default. Expanding reveals API Base URL and Bearer Token. Parameters visible below.

---

## Phase 6: User Story 4 — Filter Parameters by Name or Value (Priority: P2)

**Goal**: Text filter input above the parameter list narrows visible rows to those matching the substring (case-insensitive) in name or value. Empty state shown when no match.

**Independent Test**: Type a substring into the filter, verify only matching rows are shown. Clear the filter, verify all rows reappear. Type a non-matching string, verify "No matching parameters" empty state.

### Implementation for User Story 4

- [ ] T017 [US4] Add filter text input above the parameter list in ConfigParameterList; implement case-insensitive substring matching on parameter name and value; show "No matching parameters" empty-state message when no rows match in src/web-ui/src/components/ConfigParameterList.tsx

**Checkpoint**: Filter narrows parameter list incrementally. Empty-state message displayed for zero matches. Clearing filter restores full list.

---

## Phase 7: User Story 5 — Drag-and-Drop Parameter Reorder (Priority: P3)

**Goal**: Operator drags a parameter row to a new position. New order is sent to the backend immediately on drop and persists across page reloads. Optimistic UI with rollback on failure.

**Independent Test**: Drag a parameter from position 5 to position 1. Verify the list re-renders. Refresh the page and verify the new order is preserved.

### Implementation for User Story 5

- [ ] T018 [US5] Add HTML5 draggable attribute, drag handle, and dragstart/dragover/dragend event handlers to ConfigParameterRow in src/web-ui/src/components/ConfigParameterRow.tsx
- [ ] T019 [US5] Handle drop event in ConfigParameterList to compute new order (mapping filtered-view positions back to the full parameter list), optimistically re-render, call reorderParameters from config service, and rollback to previous order on API failure in src/web-ui/src/components/ConfigParameterList.tsx

**Checkpoint**: Drag-and-drop reorders parameters. Order persists across page reloads. Failed reorder rolls back with error message.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Tests, error states, Swagger examples, and end-to-end validation

- [ ] T020 [P] Add xUnit tests for UpdateParametersAsync (env-sourced rejection, value merge, Default→File promotion) and ReorderParametersAsync (key ordering, missing key append, duplicate dedup) in tests/unit/ConfigUpdateTests.cs (NOTE: write these immediately after Phase 2 to satisfy constitution §II — tests before committing implementation)
- [ ] T021 [P] Add contract tests for PUT /api/config/parameters (200 success, 400 env-sourced, 400 empty) and PUT /api/config/parameters/reorder (200 success, 400 empty) in tests/contract/ConfigEndpointTests.cs
- [ ] T022 [P] Add or update Jest + RTL tests for Configuration page: parameter list rendering, dirty-state highlight, Apply All flow, filter matching, collapsible section state in src/web-ui/src/__tests__/configuration.spec.tsx
- [ ] T023 Add error-state display with Retry button when backend is unreachable on initial config fetch in src/web-ui/src/pages/Configuration.tsx
- [ ] T024 [P] Add Swagger request/response examples for PUT /api/config/parameters and PUT /api/config/parameters/reorder in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [ ] T025 Run quickstart.md validation to verify full end-to-end flow (build, test, smoke test)

**Checkpoint**: All tests pass. Error states handled. Swagger examples added. Quickstart verified.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational completion
- **US2 (Phase 4)**: Depends on US1 completion (edits require visible rows)
- **US3 (Phase 5)**: Depends on Foundational completion — independent of US1/US2
- **US4 (Phase 6)**: Depends on US1 completion (filter operates on parameter list)
- **US5 (Phase 7)**: Depends on US1 completion (reorder operates on parameter list)
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Independence

- **US1 (P1)**: Foundation — must be first
- **US2 (P1)**: Depends on US1 (needs parameter rows to edit)
- **US3 (P2)**: Independent of US1/US2 — can be done in parallel with US1 after Foundational
- **US4 (P2)**: Depends on US1 (needs parameter list to filter)
- **US5 (P3)**: Depends on US1 (needs parameter list to reorder)

### Within Each User Story

- Components before page integration
- Service-layer wiring before UI interaction
- Core implementation before error handling / edge cases

### Parallel Opportunities

Within Phase 1: T001 and T002 modify different files — can run in parallel
Within Phase 2: T007 (frontend) can run in parallel with T003–T006 (backend)
Within Phase 3: T008 (ConfigParameterRow) can start while T007 finishes
Within Phase 5: T015 (CollapsibleSection) is independent — can be written anytime after Phase 2
Within Phase 8: T020, T021, T022, T024 are independent test/doc files — all can run in parallel

---

## Parallel Example: Foundational Phase

```
# Backend track (sequential — same files):
T003: UpdateParametersAsync in ConfigSnapshotService.cs
T004: ReorderParametersAsync in ConfigSnapshotService.cs
T005: PUT /parameters endpoint in ConfigEndpoints.cs
T006: PUT /parameters/reorder endpoint in ConfigEndpoints.cs

# Frontend track (parallel with backend):
T007: config.ts API service (new file, no backend file dependency)
```

## Parallel Example: After Foundational

```
# Track A (US1 → US2 sequential):
T008 → T009 → T010 → T011 → T012 → T013 → T014

# Track B (US3, parallel with Track A):
T015 → T016
```

---

## Implementation Strategy

- **MVP**: Phase 1 + Phase 2 + Phase 3 (US1) — operators can see all parameters
- **Core editing**: Add Phase 4 (US2) — operators can edit and apply changes
- **UX improvements**: Phase 5 (US3) + Phase 6 (US4) — collapsible section, filter
- **Advanced**: Phase 7 (US5) — drag-and-drop reorder
- **Quality gate**: Phase 8 — tests, error states, quickstart validation
