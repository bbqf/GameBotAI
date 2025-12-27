# Tasks: Unified Authoring Object Pages

## Phase 1 – Setup
- [X] T001 Ensure web UI dependencies installed (Node 18+, npm install) in src/web-ui/package.json
- [X] T002 Verify backend service runs for dropdown data (dotnet run -c Debug --project src/GameBot.Service)

## Phase 2 – Foundational
- [X] T003 Audit existing Action page components to identify reusable layout pieces in src/web-ui
- [X] T004 Extract/prepare shared form primitives (sections, headers, Save/Cancel placement) for reuse in src/web-ui
- [X] T005 Define shared dropdown component with search and create-new affordance in src/web-ui
- [X] T006 Define reusable array CRUD+reorder component with drag-and-drop in src/web-ui
- [X] T007 [P] Add Vitest/RTL unit tests for dropdown component (search, selection, create-new) in src/web-ui
- [X] T008 [P] Add Vitest/RTL unit tests for array component (add/edit/delete/reorder behavior) in src/web-ui

## Phase 3 – User Story 1 (Create any object with unified page)
Goal: Non-technical author can create/edit any object using the Action-style layout.
Independent Test: Create a new Command via unified page without JSON/IDs; save successfully.
- [X] T009 [US1] Apply unified layout to Command page (sections, Save/Cancel) in src/web-ui
- [X] T010 [US1] Wire dropdowns for references (Actions, Game profiles) using shared component in src/web-ui
- [X] T011 [US1] Ensure inline validation and live save flow for Command edit/create in src/web-ui
- [X] T012 [P] [US1] Add Vitest/RTL page-flow tests for Command create/edit covering validation and live save in src/web-ui

## Phase 4 – User Story 2 (Manage array-based fields visually)
Goal: Authors can add/edit/delete/reorder array items with consistent controls.
Independent Test: Add two items, reorder, delete one; saved order persists.
- [X] T013 [US2] Integrate array component for Command steps and detectionTargets in src/web-ui
- [X] T014 [US2] Integrate array component for Trigger actions/conditions in src/web-ui
- [X] T015 [US2] Add order-preview and persistence checks on save for arrays in src/web-ui
- [X] T016 [P] [US2] Add e2e/automation covering add/reorder/delete for arrays (Playwright) in tests/e2e
- [X] T017 [P] [US2] Add Vitest/RTL unit tests for array usage in Command/Trigger forms (order preservation, edit/delete) in src/web-ui

## Phase 5 – User Story 3 (Confidently navigate across object types)
Goal: Users recognize same structure and controls across object pages.
Independent Test: Edit three object types; locate equivalent sections without guidance.
- [X] T018 [US3] Apply unified layout to Trigger page (sections, Save/Cancel) in src/web-ui
- [X] T019 [US3] Apply unified layout to Game profile page with reference dropdowns in src/web-ui
- [X] T020 [US3] Apply unified layout to Sequence/steps page component (src/web-ui) and confirm section parity with Actions/Commands
- [X] T021 [US3] Add UX consistency audit checklist in docs/ui-audit.md covering Action/Command/Trigger/Game profile/Sequence

## Phase 6 – Polish & Cross-cutting
- [ ] T022 Add contextual help/tooltips for non-technical users on key fields in src/web-ui
- [ ] T023 Performance check: verify form edits ≲100 ms, reorder ≲200 ms, initial load <1.5 s (profiling notes) in src/web-ui
- [ ] T024 Regression pass: manual create/edit of Action/Command/Trigger/Game profile to confirm live saves and arrays persist order
- [ ] T025 Add unsaved-changes warning across unified pages (block navigation with dirty state) in src/web-ui
- [ ] T026 Audit list/search entry points and route all object links to unified detail pages; remove/redirect legacy layouts in src/web-ui
- [ ] T027 Validate SC-001: timed usability test for Command creation (<3 minutes, no JSON/ID exposure); record outcome and findings in docs/validation.md
- [ ] T028 Validate SC-004: cross-page clarity survey after multi-object edit flow (clarity ≥4/5 for 90% of users); capture responses and gaps in docs/validation.md

## Dependencies / Story Order
- Complete Phase 1 and Phase 2 before user stories.
- User Story order: US1 (P1) → US2 (P2) → US3 (P3).

## Parallel Execution Examples
- Parallel: T005 (dropdown component) and T006 (array component) after T003/T004.
- Parallel: Within US2, T010 and T011 can proceed in parallel once components exist.
- Parallel: Within US3, T014/T015/T016 can proceed in parallel after shared layout is ready.

## Implementation Strategy
- MVP = US1 completion (Command unified page with dropdowns and live save).
- Incrementally deliver US2 (arrays) then US3 (consistency across types), finishing with polish and performance checks.
