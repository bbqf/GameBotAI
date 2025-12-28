# Tasks: API Structure Cleanup

**Input**: Design documents from `/specs/018-api-refactor/`
**Prerequisites**: plan.md, spec.md (required); research.md, data-model.md, contracts/, quickstart.md (available)

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Restore solution packages for GameBot.sln at GameBot.sln
- [X] T002 [P] Build baseline to ensure clean starting point at GameBot.sln (Debug)

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T003 Establish canonical `/api/{resource}` base path constant and route grouping scaffolding in src/GameBot.Service/Program.cs
- [X] T004 [P] Align Swagger configuration to consume domain tags and canonical base path in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [ ] T005 [P] Update route catalog reference to match planned canonical paths in specs/018-api-refactor/contracts/route-contracts.md

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Navigate a single canonical API (Priority: P1) 🎯 MVP

**Goal**: All public endpoints respond only under `/api/{resource}` with no duplicates.
**Independent Test**: Call each `/api/{resource}` endpoint and confirm no non-`/api` path returns 2xx; legacy roots return guided non-success.

### Implementation
- [X] T006 [US1] Refactor actions endpoints to `/api/actions` and remove legacy exposures in src/GameBot.Service/Program.cs
- [ ] T007 [P] [US1] Refactor sequences endpoints to `/api/sequences` and drop legacy aliases in src/GameBot.Service/Program.cs
- [X] T008 [P] [US1] Refactor sessions/emulator and configuration endpoints to `/api/...` in src/GameBot.Service/Program.cs
- [X] T009 [US1] Add legacy-path guard returning guided non-success for old roots (e.g., `/actions`) in src/GameBot.Service/Program.cs
- [ ] T010 [US1] Deduplicate route metadata/tags to ensure one canonical path per endpoint in src/GameBot.Service/Program.cs
- [ ] T021 [P] [US1] Add integration/contract check for legacy roots returning non-success and `/api/actions` succeeding in tests/integration/ApiRoutesTests.cs

**Checkpoint**: User Story 1 functional and independently testable via canonical routes only

---

## Phase 4: User Story 2 - Browse clear API documentation (Priority: P2)

**Goal**: Swagger groups endpoints by domain with schemas and example payloads per endpoint.
**Independent Test**: Open Swagger UI and verify endpoints appear under the correct domain tags with request/response schemas and at least one example payload each.

### Implementation
- [X] T011 [P] [US2] Configure Swagger tags/groups for Actions, Sequences, Sessions, Configuration, Triggers in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [X] T012 [P] [US2] Add request/response schemas and example payloads for actions endpoints in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [X] T013 [P] [US2] Add schemas and example payloads for sequences and sessions endpoints in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [X] T014 [US2] Validate Swagger UI grouping and examples; adjust summaries/descriptions as needed in src/GameBot.Service/Swagger/SwaggerConfig.cs
- [X] T022 [P] [US2] Add Swagger validation test for domain tags plus request/response schemas and example payloads in tests/contract/SwaggerDocsTests.cs

**Checkpoint**: User Story 2 documentation ready and discoverable

---

## Phase 5: User Story 3 - Keep automated checks green (Priority: P3)

**Goal**: Automated contract/integration tests exercise only `/api` routes and pass; legacy paths assert guided failure.
**Independent Test**: Run full test suite; all API-related tests target `/api` and pass, legacy-path checks assert non-success with guidance.

### Tests
- [ ] T015 [P] [US3] Update contract tests to target canonical `/api` routes and assert legacy failures in tests/contract/
- [ ] T016 [P] [US3] Update integration tests to expect `/api` success and legacy non-success in tests/integration/
- [ ] T017 [US3] Refresh shared fixtures to remove legacy paths in tests/TestAssets/
- [ ] T018 [US3] Run `dotnet test -c Debug` for GameBot.sln to confirm suite is green (no legacy path dependencies)

**Checkpoint**: User Story 3 validated by automated suites

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Update quickstart checklist to reflect canonical routes and Swagger grouping in specs/018-api-refactor/quickstart.md
- [ ] T020 Final pass to remove dead legacy routing code/comments and align documentation where referenced in src/GameBot.Service/Program.cs
- [ ] T023 [P] Capture Swagger/doc endpoint latency (<300ms p95) via local run or trace in tests/integration/SwaggerPerfTests.cs and record in specs/018-api-refactor/quickstart.md

---

## Dependencies & Execution Order
- Setup (Phase 1) → Foundational (Phase 2) → US1 (Phase 3) → US2 (Phase 4) and US3 (Phase 5) can proceed after US1; Polish after stories.
- Within US1: T006/T007/T008 can run in parallel; T009 depends on base routing; T010 after refactors.
- Within US2: T011-T013 parallel; T014 after examples in place.
- Within US3: T015-T017 parallel; T018 after test updates.

## Parallel Opportunities
- Parallel tasks marked [P] across phases (e.g., T002, T004, T005, T007, T008, T011, T012, T013, T015, T016, T017, T019) can be distributed to avoid file conflicts.
- Different user stories can proceed in parallel once US1 completes the canonical route baseline.

## Implementation Strategy
- MVP = Complete US1 after Setup/Foundational, validate canonical routes only.
- Incremental: Deliver US1 (routing), then US2 (documentation), then US3 (tests) with validations at each checkpoint.
