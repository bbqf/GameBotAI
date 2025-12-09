# Tasks — Command Sequences

## Phase 1: Setup

- [X] T001 Validate constitution gates against `.specify/memory/constitution.md`
- [X] T002 Create feature directory structure confirmation `specs/001-command-sequences/`

## Phase 2: Foundational

- [X] T003 Define storage path `data/commands/sequences/` and repository interface stub in `src/GameBot.Domain/Commands`
- [X] T004 Add DTOs to `specs/openapi.json` (SequenceDto, StepDto, ExecuteResultDto)

## Phase 3: User Story 1 (P1) — Configure and run a command sequence

- [ ] T005 [US1] Create `CommandSequence` model in `src/GameBot.Domain/Commands`
- [ ] T006 [US1] Implement `SequenceRunner` in `src/GameBot.Domain/Services`
- [ ] T007 [US1] Minimal API: `POST /api/sequences`, `GET /api/sequences/{id}` in `src/GameBot.Service/Program.cs`
- [ ] T008 [US1] Minimal API: `POST /api/sequences/{id}/execute` in `src/GameBot.Service/Program.cs`
- [ ] T009 [US1] Integration test for fixed delays in `tests/integration/Sequences/FixedDelayTests.cs`

## Phase 4: User Story 2 (P2) — Randomized delay ranges

- [ ] T010 [US2] Implement delay range precedence logic in `src/GameBot.Domain/Services/SequenceRunner.cs`
- [ ] T011 [US2] Unit test for delay range selection bounds in `tests/unit/Sequences/DelayRangeTests.cs`

## Phase 5: User Story 3 (P3) — Detection gating with timeout

- [ ] T012 [US3] Extend `SequenceStep` for gating config in `src/GameBot.Domain/Commands`
- [ ] T013 [US3] Implement gating logic using detection pipeline in `src/GameBot.Domain/Services/SequenceRunner.cs`
- [ ] T014 [US3] Integration tests for gating present/absent in `tests/integration/Sequences/GatingIntegrationTests.cs`

## Final Phase: Polish & Cross-Cutting

- [ ] T015 Add logging and telemetry details per step in `src/GameBot.Domain/Services/SequenceRunner.cs`
- [ ] T016 Update `README.md` with sequences usage and examples
- [ ] T017 Update `CHANGELOG.md` for feature addition

## Dependencies

- US1 → US2 → US3 (US1 must be complete before US2; US2 before US3)

## Parallel Execution Examples

- [ ] T006 [P] [US1] Implement `SequenceRunner` while T007 API stub is created in parallel
- [ ] T011 [P] [US2] Write unit tests while `SequenceRunner` gets range logic
- [ ] T014 [P] [US3] Prepare integration test scaffolding while gating logic is wired

## Implementation Strategy

- MVP: Deliver US1 endpoints and runner with fixed delays and basic storage.
- Increment: Add delay ranges (US2), then gating with timeouts (US3); validate with tests at each phase.
