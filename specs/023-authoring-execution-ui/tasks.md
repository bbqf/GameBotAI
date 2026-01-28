# Task Plan: Authoring & Execution UI Visual Polish

## Dependencies and Order
- Story order: US1 (P1) → US2 (P2) → US3 (P3) → US4 (P4) → US5 (P5) → US6 (P6) → US7 (P7)
- Backend session endpoints (US4) depend on foundational session models and cache logic (US2).
- UI execution surfaces (US2–US4) share API client setup from Foundational phase.

## Parallel Execution Examples
- US1 backend tests (T009) can run in parallel with US1 frontend tests (T010).
- US2 backend cache logic (T011/T012) can proceed while UI defaulting (T013) is built; tests T014/T015 run in parallel once implementations land.
- US4 UI polling (T021) can develop in parallel with backend start/stop logic (T019/T020) after API contract stubs exist.
- Visual polish tasks (US5–US7) can run in parallel once core execution features (US2–US4) are stable.

## Implementation Strategy
- MVP: Complete US1 (detection persistence) to restore reliable saves, then US2/US3 to make execution flows usable with cached sessions.
- Iterate: Add running sessions list (US4), then address visual stability and consistency (US5–US7).
- Testing: Add targeted unit/UI tests per story to satisfy constitution coverage expectations.

## Phase 1 – Setup
- [x] T001 Ensure backend builds from solution root (GameBot.sln) using Debug configuration
- [x] T002 Install frontend dependencies in src/web-ui/package.json

## Phase 2 – Foundational
- [x] T003 Add session/detection DTOs and enums in src/GameBot.Domain/Sessions (RunningSession, SessionCache, status)
- [x] T004 Add sessions API controller skeleton with routes from contracts in src/GameBot.Service/Controllers/SessionsController.cs
- [x] T005 Add sessions/detection API client scaffolding in src/web-ui/src/api/sessions.ts

## Phase 3 – User Story 1 (P1): Detection settings save reliably
- [x] T006 [US1] Persist detection config in command repository in src/GameBot.Domain/Repositories/CommandRepository.cs
- [x] T007 [US1] Map detection fields in command create/edit endpoints in src/GameBot.Service/Controllers/CommandsController.cs
- [x] T008 [US1] Bind detection inputs and preserve values on validation errors in src/web-ui/src/features/authoring/CommandForm.tsx
- [x] T009 [P] [US1] Add backend unit test covering detection save/reload in tests/unit/Commands/CommandRepositoryTests.cs
- [x] T010 [P] [US1] Add frontend test for detection persistence in src/web-ui/src/features/authoring/__tests__/CommandForm.detection.test.tsx

## Phase 4 – User Story 2 (P2): Command execution reuses cached session
- [x] T011 [US2] Implement server-side session cache defaulting in src/GameBot.Service/Services/SessionService.cs
- [ ] T012 [US2] Apply cache default in execute-command endpoint in src/GameBot.Service/Controllers/ExecutionController.cs
- [x] T013 [US2] Default execution UI to cached session and prompt when missing in src/web-ui/src/features/execution/ExecutePanel.tsx
- [ ] T014 [P] [US2] Add backend test for cache fallback and stale handling in tests/unit/Sessions/SessionServiceTests.cs
- [ ] T015 [P] [US2] Add UI test for cached session auto-use and missing-session prompt in src/web-ui/src/features/execution/__tests__/ExecutePanel.session.test.tsx

## Phase 5 – User Story 3 (P3): Session banner shows cached session with stop
- [x] T016 [US3] Expose cached session fetch/stop API in src/GameBot.Service/Controllers/SessionsController.cs
- [x] T017 [US3] Render session banner with stop control in src/web-ui/src/features/execution/SessionBanner.tsx
- [ ] T018 [P] [US3] Add UI test for banner display and stop clearing in src/web-ui/src/features/execution/__tests__/SessionBanner.test.tsx

## Phase 6 – User Story 4 (P4): Running sessions list with stop controls
- [x] T019 [US4] Implement GET /api/sessions/running and POST /api/sessions/stop per contract in src/GameBot.Service/Controllers/SessionsController.cs
- [x] T020 [US4] Implement POST /api/sessions/start with auto-stop/replace semantics in src/GameBot.Service/Controllers/SessionsController.cs
- [x] T021 [US4] Build running sessions list UI with 2s polling and stop buttons in src/web-ui/src/features/execution/RunningSessionsList.tsx
- [ ] T022 [P] [US4] Add backend tests for running list and auto-stop failure handling in tests/unit/Sessions/SessionsControllerTests.cs
- [ ] T023 [P] [US4] Add UI test for polling, stop removal, and replacement in src/web-ui/src/features/execution/__tests__/RunningSessionsList.test.tsx

## Phase 7 – User Story 5 (P5): Author edits automations without visual defects
- [ ] T024 [US5] Adjust authoring layout spacing/wrapping for 1280–1920px in src/web-ui/src/features/authoring/CommandForm.css
- [ ] T025 [P] [US5] Validate long-name handling in authoring list/grid rendering in src/web-ui/src/features/authoring/CommandList.tsx

## Phase 8 – User Story 6 (P6): Operator monitors execution states clearly
- [ ] T026 [US6] Standardize status chip colors/contrast and alignment in src/web-ui/src/features/execution/StatusChip.tsx
- [ ] T027 [P] [US6] Add stable loading skeleton for run details panel in src/web-ui/src/features/execution/RunDetails.tsx

## Phase 9 – User Story 7 (P7): Consistent look-and-feel across surfaces
- [ ] T028 [US7] Align button sizing/typography via shared tokens in src/web-ui/src/theme/tokens.ts
- [ ] T029 [P] [US7] Harmonize spacing and background tokens across authoring/execution in src/web-ui/src/theme/global.css

## Phase 10 – Polish & Cross-Cutting
- [ ] T030 Update quickstart with any new session endpoint notes in specs/023-authoring-execution-ui/quickstart.md
- [x] T031 [P] Run full test suite (backend and frontend) from C:/src/GameBot to confirm coverage and regressions addressed
- [ ] T032 [P] Add backend perf measurement for running sessions fetch p95<300ms in src/GameBot.Service (timing/logging or lightweight benchmark)
- [ ] T033 [P] Add UI perf check for banner/list render/update <100ms after data arrival in src/web-ui/src/features/execution (profiling script/test)
- [ ] T034 Add authoring detection save/reload perf check <500ms p95 in tests/integration/Commands/DetectionPerformanceTests.cs
- [ ] T035 [US5] Verify authoring forms at 125%-150% scaling (no clipped controls) in src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx
- [ ] T036 [US6] Verify execution screens at 125%-150% scaling (actions reachable, no horizontal scroll) in src/web-ui/src/features/execution/__tests__/ExecutionZoom.test.tsx
- [ ] T037 Add contract tests for GET/POST sessions (running/start/stop) in tests/contract/Sessions/SessionsContractsTests.cs
- [ ] T038 [P] Add integration test for start with auto-stop replacement and stop failure handling in tests/integration/Sessions/RunningSessionsFlowTests.cs

## Independent Test Criteria per Story
- US1: Save/edit command with detection target/parameters, reopen, and values match without loss.
- US2: Execute command with cached session present (auto-uses), and with no cache (blocks with prompt).
- US3: Cached session shows once in banner; stop clears banner and requires new session.
- US4: Running list shows all sessions; starting new session for same game/emulator replaces prior entry; stop removes entry even if prior auto-stop fails.
- US5: At 1280–1920px, no overlap/clipping; long names wrap/truncate without horizontal scroll.
- US6: Run states visibly distinct; loading skeleton stable without layout jump.
- US7: Buttons/typography/spacing consistent across authoring and execution screens.

## Format Validation
All tasks use checkbox + TaskID + optional [P] + story label (for story phases) with explicit file paths.
