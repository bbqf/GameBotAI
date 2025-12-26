# Tasks — Web UI Authoring (MVP)

## Phase 1: Setup

- [x] T001 Create VS Code task to run web UI in .vscode/tasks.json
- [x] T002 Add Jest + React Testing Library config in src/web-ui (jest.config.ts, tsconfig.jest.json)
- [x] T003 Add ESLint config (.eslintrc.cjs) and npm script in src/web-ui/package.json

## Phase 2: Foundational

- [x] T004 Implement API client error mapping in src/web-ui/src/lib/api.ts
- [x] T005 Finalize TokenGate behavior (remember toggle) in src/web-ui/src/components/TokenGate.tsx
- [x] T006 Create reusable labeled input component in src/web-ui/src/components/FormField.tsx

## Phase 3: User Story US1 (P1) — Create a new sequence

- [x] T007 [US1] Build Create page form in src/web-ui/src/pages/SequencesCreate.tsx
- [x] T008 [P] [US1] Wire POST /api/sequences in src/web-ui/src/pages/SequencesCreate.tsx
- [x] T009 [US1] Route to View after success in src/web-ui/src/App.tsx

## Phase 4: User Story US2 (P2) — Edit an existing sequence

- [x] T010 [US2] Create Edit page in src/web-ui/src/pages/SequenceEdit.tsx
- [x] T011 [P] [US2] Wire update endpoint (PUT/PATCH) in src/web-ui/src/pages/SequenceEdit.tsx
- [x] T012 [US2] Persist and show validation on save in src/web-ui/src/pages/SequenceEdit.tsx

## Phase 5: User Story US3 (P2) — Validation feedback on invalid blocks

- [x] T013 [US3] Implement validation parser in src/web-ui/src/lib/validation.ts
- [x] T014 [P] [US3] Highlight field errors in src/web-ui/src/pages/*

## Phase 6: User Story US4 (P3) — Browse triggers/images to select targetId

- [x] T015 [US4] Implement TriggerPicker in src/web-ui/src/components/TriggerPicker.tsx
- [x] T016 [P] [US4] Implement ImagePicker in src/web-ui/src/components/ImagePicker.tsx

## Phase 7: User Story US5 (P1) — Mobile viewport

- [ ] T017 [US5] Update responsive CSS in src/web-ui/src/styles.css
- [ ] T018 [P] [US5] Ensure labels/a11y (WCAG AA) across src/web-ui/src/**

## Final Phase: Polish & Cross-Cutting

- [ ] T019 Add VS Code launch configs for service+web UI in .vscode/launch.json
- [ ] T020 Update quickstart with tasks and endpoints in specs/001-web-ui-authoring/quickstart.md

## Dependencies

- US1 → Independent (requires /api/sequences POST)
- US2 → Depends on service update endpoint availability
- US3 → Independent; relies on service returning 400 { errors[] }
- US4 → Independent; uses existing triggers/images endpoints
- US5 → Cross-cutting; can proceed in parallel

## Parallel Execution Examples

- [US1] T008 can run in parallel with [US1] UI form work (T007)
- [US3] T014 can run in parallel with [US3] parser (T013)
- [US4] T016 can run in parallel with [US4] T015
- [US5] T018 can run in parallel with UI tasks in other stories

## Independent Test Criteria

- US1: Creating a sequence returns 201 with id; UI shows success
- US2: Editing an existing sequence returns 200; UI reflects changes
- US3: Invalid payload returns 400 with errors[]; UI highlights affected fields
- US4: GET triggers/images lists work; selection populates targetId
- US5: UI usable at 375px viewport; no horizontal scroll on core flows

## Suggested MVP Scope

- Implement US1 and US5 first (Create sequence + mobile responsiveness)

## Summary

- Total tasks: 20
- Per story: US1 (3), US2 (3), US3 (2), US4 (2), US5 (2)
- Parallel opportunities: 4
