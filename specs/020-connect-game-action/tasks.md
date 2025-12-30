# Tasks: Connect to game action

## Phase 1: Setup

- [x] T001 Ensure .NET 9 SDK and Node 18+ available (verify locally)
- [x] T002 Restore backend deps and confirm build in src/GameBot.Service (`dotnet restore`)
- [x] T003 Restore frontend deps in src/web-ui (`npm install`)

## Phase 2: Foundational

- [x] T004 Add action type constant and model fields for connect-to-game in src/GameBot.Domain (gameId, adbSerial required)
- [x] T005 Wire JSON repository schema updates for connect-to-game action persistence in src/GameBot.Domain and data/ actions samples
- [x] T006 Document session cache keying (gameId+adbSerial) in src/GameBot.Service/README.md (command execution section)

## Phase 3: User Story 1 – Author configures Connect to game action (P1)

- [x] T007 [US1] Add UI option for "Connect to game" action type in src/web-ui/src/pages/actions/CreateActionPage.tsx and EditActionPage.tsx
- [x] T008 [US1] Require game selection via existing games list in src/web-ui/src/pages/actions/CreateActionPage.tsx, src/web-ui/src/pages/actions/EditActionPage.tsx (and shared ActionForm if present)
- [x] T009 [US1] Populate adbSerial suggestions from /api/adb/devices with manual override in src/web-ui/src/pages/actions/CreateActionPage.tsx, src/web-ui/src/pages/actions/EditActionPage.tsx (and shared ActionForm if present)
- [x] T010 [US1] Persist gameId and adbSerial for connect-to-game actions (load/save) in src/web-ui/src/pages/actions/ActionsListPage.tsx and related store/service
- [x] T011 [US1] Backend validation: reject connect-to-game actions missing gameId or adbSerial in src/GameBot.Service action endpoints
- [x] T011a [US1] Handle empty game list: block save and show guidance in src/web-ui/src/pages/actions/CreateActionPage.tsx and EditActionPage.tsx

## Phase 4: User Story 2 – Execute action to open a session (P1)

- [x] T012 [US2] Implement synchronous POST /api/sessions call for connect-to-game action execution with 30s timeout in src/GameBot.Service (command execution flow)
- [x] T013 [US2] Surface sessionId on success to UI command execution response in src/GameBot.Service and client handling in src/web-ui execution logic
- [x] T014 [US2] Cache sessionId client-side keyed by gameId+adbSerial in src/web-ui/src/lib/sessionCache.ts (create if missing)
- [x] T015 [US2] Handle timeout/failure: clear/no cache write, surface actionable error in UI in src/web-ui/src/pages/commands/ExecuteCommandPage.tsx (or the command execution component used)
- [x] T016 [US2] Tests: backend unit/integration for session timeout and success path (tests/integration or tests/unit as appropriate)
- [x] T017 [US2] Tests: frontend RTL for execution flow showing sessionId and handling timeout/error states

## Phase 5: User Story 3 – Use stored session across commands (P2)

- [x] T018 [US3] Make sessionId optional on /api/commands/{id}/force-execute and evaluate-and-execute; auto-inject cached sessionId matching gameId+adbSerial in src/GameBot.Service
- [x] T019 [US3] Reject when no matching cached sessionId is available; return clear guidance in API response (src/GameBot.Service)
- [x] T020 [US3] Frontend: when executing commands without sessionId, pull from cache (gameId+adbSerial) and block with guidance if missing in src/web-ui/src/pages/commands/ExecuteCommandPage.tsx (or the command execution component used)
- [x] T021 [US3] Tests: backend coverage for optional sessionId injection and rejection cases (tests/integration)
- [x] T022 [US3] Tests: frontend RTL/Playwright for auto-injection and missing-session guidance
- [x] T022 [US3] Tests: frontend RTL/Playwright for auto-injection and missing-session guidance (updated app-level stub to avoid async warnings)

## Phase 6: Polish & Cross-Cutting

- [x] T023 Update quickstart and any README notes with new action type usage (specs/020-connect-game-action/quickstart.md)
- [x] T024 Add logging for session creation and auto-injection paths (gameId, adbSerial, no secrets) in src/GameBot.Service
- [x] T025 Run full build/tests: `dotnet build -c Debug`, `dotnet test -c Debug`, `npm test` in src/web-ui, Playwright suite if available
- [x] T026 Measure /api/adb/devices suggestion latency (target ≤2s) and add a check or test note in src/web-ui Playwright/RTL or perf note in docs
- [x] T027 Update Swagger/OpenAPI docs for session endpoints and optional sessionId handling in specs/openapi.json, and add connect-to-game action type example to data/actions sample JSON

## Dependencies
- Story order: US1 (authoring) enables US2 (session creation) enables US3 (reuse).
- Backend model/repo changes (T004-T005) precede UI authoring and execution changes.
- Session cache utility (T014) supports US3 tasks (T018-T020).

## Parallel Execution Examples
- Parallel: T004 (model) with T003 (frontend deps) as they touch different layers.
- Parallel: T012 (session exec backend) with T014 (session cache client) after foundational tasks are done.
- Parallel: T016 (backend tests) with T017 (frontend tests) once execution flows are built.

## Implementation Strategy
- MVP: Complete US1 and US2 (T007-T017) to allow authoring and establishing a session with visible sessionId and cache write.
- Incremental: Add US3 auto-injection and rejection handling (T018-T022).
- Finalize with polish, logging, and full test runs (T023-T025).