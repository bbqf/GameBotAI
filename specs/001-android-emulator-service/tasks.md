---

description: "Task list for GameBot Android Emulator Service"
---

# Tasks: GameBot Android Emulator Service

**Input**: Design documents from `/specs/001-android-emulator-service/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED for any executable logic per the Constitution. Write tests first; ensure they fail; then implement.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- Source: `src/` with three assemblies
  - `src/GameBot.Domain/`
  - `src/GameBot.Emulator/`
  - `src/GameBot.Service/`
- Tests: `tests/{unit, integration, contract}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create solution and projects: `GameBot.Domain`, `GameBot.Emulator`, `GameBot.Service`
- [ ] T002 Add analyzers, EditorConfig, and dotnet format; enforce warnings as errors in `GameBot.*.csproj`
- [ ] T003 [P] Configure secret scanning and basic CI (build, test, coverage gate ≥80% line / ≥70% branch)
- [ ] T004 Add OpenAPI generator and minimal API skeleton in `src/GameBot.Service/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T010 Implement error envelope and global exception handler in Service (`src/GameBot.Service/Middleware/ErrorHandling.cs`); unify `{ error: { code, message, hint } }`
- [ ] T011 Implement token-based authentication middleware/config in Service; protect all non-health endpoints
- [ ] T012 [P] Implement ADB resolver (LDPlayer-first) in `src/GameBot.Emulator/Adb/AdbResolver.cs` with config override `Service:Emulator:AdbPath` and env `GAMEBOT_ADB_PATH`
- [ ] T013 [P] Add Windows registry + env + path probes for LDPlayer (`LDPLAYER_HOME`/`LDP_HOME`, common install paths, Uninstall keys) with unit tests in `tests/unit/Emulator/AdbResolverTests.cs`
- [ ] T014 [P] Implement ADB client wrapper `src/GameBot.Emulator/Adb/AdbClient.cs` (exec, shell input, screencap)
- [ ] T015 Health endpoint `GET /health` in Service with integration test `tests/integration/HealthEndpointTests.cs`
- [ ] T016 Contract baseline: export OpenAPI and snapshot test in `tests/contract/OpenApiContractTests.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Start and control emulator via REST (Priority: P1) 🎯 MVP

**Goal**: Start a session, send inputs, get status, stop — all via REST

**Independent Test**: Using only REST calls, start → control → status → stop

### Tests for User Story 1 (REQUIRED for logic) ⚠️

- [ ] T020 [P] [US1] Contract tests for `/sessions` (create/get/delete) in `tests/contract/SessionsContractTests.cs`
- [ ] T021 [P] [US1] Integration tests for inputs `/sessions/{id}/inputs` using test double for ADB in `tests/integration/SessionInputTests.cs`
- [ ] T022 [P] [US1] Integration tests for snapshot `/sessions/{id}/snapshot` validating `image/png` and latency budget in `tests/integration/SnapshotTests.cs`

### Implementation for User Story 1

- [ ] T023 [P] [US1] Domain entities: `EmulatorSession` in `src/GameBot.Domain/Sessions/EmulatorSession.cs`
- [ ] T024 [P] [US1] Session manager service in `src/GameBot.Emulator/Session/SessionManager.cs` (create, get, stop, input)
- [ ] T025 [P] [US1] Snapshot capture in `src/GameBot.Emulator/Video/SnapshotProvider.cs` using `adb exec-out screencap -p`
- [ ] T026 [US1] REST endpoints in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs` (POST/GET/DELETE, POST inputs, GET snapshot)
- [ ] T027 [US1] Resource and concurrency controls (max concurrent sessions; idle timeout)
- [ ] T028 [US1] Logging and telemetry for session lifecycle and input handling

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Manage games and automation profiles (Priority: P2)

**Goal**: Register game artifacts and define deterministic automation profiles

**Independent Test**: Register a game, create a profile, and start a session reaching expected state without manual input

### Tests for User Story 2 (REQUIRED for logic) ⚠️

- [ ] T030 [P] [US2] Contract tests for `/games` and `/profiles` endpoints in `tests/contract/GamesProfilesContractTests.cs`
- [ ] T031 [P] [US2] Integration test: session start with `profileId` executes steps to target state `tests/integration/ProfileExecutionTests.cs`

### Implementation for User Story 2

- [ ] T032 [P] [US2] Implement `GameArtifact` repo (file-backed) in `src/GameBot.Domain/Games/GameRepository.cs`
- [ ] T033 [P] [US2] Implement `AutomationProfile` repo (file-backed) in `src/GameBot.Domain/Profiles/ProfileRepository.cs`
- [ ] T034 [US2] REST endpoints for `/games` and `/profiles` in `src/GameBot.Service/Endpoints/GamesProfilesEndpoints.cs`
- [ ] T035 [US2] Profile executor (ordered inputs, timing, checkpoints) `src/GameBot.Emulator/Automation/ProfileExecutor.cs`

**Checkpoint**: User Stories 1 AND 2 work independently

---

## Phase 5: User Story 3 - Separate UI integration (Priority: P3)

**Goal**: Verify UI can integrate solely via REST

**Independent Test**: Implement thin UI (out-of-repo) to complete P1 flow using docs only

### Tests for User Story 3 (REQUIRED for logic) ⚠️

- [ ] T040 [P] [US3] CORS configuration tests in `tests/integration/CorsTests.cs`
- [ ] T041 [P] [US3] Snapshot endpoint responsiveness test (p95 < 500 ms) `tests/integration/SnapshotPerfTests.cs`

### Implementation for User Story 3

- [ ] T042 [US3] Add OpenAPI descriptions and examples for UI usage
- [ ] T043 [US3] Document auth header and error formats; publish quickstart snippets

**Checkpoint**: All user stories should now be independently functional

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Documentation updates in `specs/001-android-emulator-service/`
- [ ] T051 Code cleanup and refactoring
- [ ] T052 Performance profiling of snapshot path; file perf note; fix regressions >2%
- [ ] T053 [P] Additional unit tests in `tests/unit/`
- [ ] T054 Security hardening (token storage, logging redaction)
- [ ] T055 Run quickstart.md validation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Domain before services; services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All tasks marked [P] can run in parallel (different files/no deps)
- US1, US2 can progress in parallel after Phase 2 completion (if staffed)

---

## Constitution Alignment Gates

- Code Quality: analyzers + formatting; zero new critical warnings
- Testing: coverage ≥80% line / ≥70% branch for changed areas
- UX Consistency: stable JSON, actionable errors, versioning for breaking changes
- Performance: snapshot p95 < 500 ms; non-session REST p95 < 200 ms; perf note on hot-path PRs
