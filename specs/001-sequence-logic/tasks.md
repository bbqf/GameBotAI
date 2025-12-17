# Tasks — Sequence Logic Blocks (Loops & Conditionals)

Feature: Sequence Logic Blocks (Loops & Conditionals)
Branch: 001-sequence-logic

## Phase 1: Setup

- [x] T001 Create domain folders for blocks and conditions in src/GameBot.Domain/Commands/Blocks
- [x] T002 [P] Create service DTO stubs for blocks in src/GameBot.Service/Contracts/Sequences

## Phase 2: Foundational

- [x] T003 Define `Block` union (`repeatCount`,`repeatUntil`,`while`,`ifElse`) in src/GameBot.Domain/Commands/Blocks/Block.cs
- [x] T004 Define `Condition` and `BlockResult` types in src/GameBot.Domain/Commands/Blocks/Condition.cs and src/GameBot.Domain/Commands/Blocks/BlockResult.cs
- [x] T005 Extend `CommandSequence` with `blocks` and serialization in src/GameBot.Domain/Commands/CommandSequence.cs
- [x] T006 Persist `blocks` in repository load/save in src/GameBot.Domain/Commands/Repositories/SequenceRepository.cs
- [x] T007 Add API model binding + validation for `blocks` in src/GameBot.Service/Program.cs

## Phase 3: User Story 1 — Repeat N Times (P1)

- Story Goal: Execute grouped steps exactly N iterations.
- Independent Test Criteria: A sequence with `repeatCount: 3` runs the step group three times; `repeatCount: 0` skips and records 0 iterations.

- [x] T008 [US1] Implement `repeatCount` execution in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T009 [P] [US1] Validate `repeatCount >= 0` and reject negative in src/GameBot.Service/Program.cs
- [x] T010 [US1] Capture `iterations` and `durationMs` telemetry per block in src/GameBot.Domain/Services/SequenceRunner.cs

## Phase 4: User Story 2 — Repeat Until Detected (P1)

- Story Goal: Poll condition (image/text/trigger) until success or timeout.
- Independent Test Criteria: Stop-on-success when detection meets `Present`; timeout causes `Failed` status.

- [x] T011 [US2] Implement `repeatUntil` with `timeoutMs` and `cadenceMs` in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T012 [P] [US2] Integrate detection/trigger evaluation services in src/GameBot.Domain/Services/TriggerEvaluationService.cs
- [x] T013 [US2] On timeout, mark sequence `Failed` and exit early in src/GameBot.Domain/Services/SequenceRunner.cs

## Phase 5: User Story 3 — If/Then/Else Branch (P2)

- Story Goal: Execute then/else branches based on condition outcome.
- Independent Test Criteria: Present → then branch only; Absent → else branch only.

- [x] T014 [US3] Implement `ifElse` branching in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T015 [P] [US3] Validate `elseSteps` only allowed for `ifElse` in src/GameBot.Service/Program.cs
- [x] T016 [US3] Record `branchTaken` and timings in src/GameBot.Domain/Services/SequenceRunner.cs

## Phase 6: User Story 4 — Loop Control (P2)

- Story Goal: Support `break` and `continue` within loops for precise control.
- Independent Test Criteria: `continue` skips remaining steps this iteration; `break` exits loop immediately.

- [ ] T017 [US4] Implement `breakOn` evaluation in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T018 [P] [US4] Implement `continueOn` evaluation in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T019 [US4] Telemetry for control decisions and iteration counts in src/GameBot.Domain/Services/SequenceRunner.cs

## Final Phase: Polish & Cross-Cutting

- [ ] T020 [P] Add LoggerMessage events for block start/end, evaluations, and decisions in src/GameBot.Domain/Services/SequenceRunner.cs
- [x] T021 Ensure backward compatibility for sequences without `blocks` in src/GameBot.Domain/Services/SequenceRunner.cs
- [ ] T022 [P] Refresh contracts doc with examples in specs/001-sequence-logic/contracts/sequences-blocks.md

## Dependencies (Story Order)

- US1 (Repeat N) → US2 (Repeat Until) → US3 (If/Else) → US4 (Loop Control)
- Foundational tasks must complete before any user story tasks.

## Parallel Execution Examples

- US1: T009 (validation) can run in parallel with T008 (runner) since files differ.
- US2: T012 (service integration) can run in parallel with T011 (runner loop).
- US3: T015 (validation) can run in parallel with T014 (branch logic).
- US4: T018 (continue) can run in parallel with T017 (break) within separate implementations.
- Polish: T020 (logging) can run in parallel with T022 (docs).

## Implementation Strategy

- MVP: Deliver US1 only (Repeat N Times) after Foundational; independently testable and valuable.
- Incremental: Add US2, then US3, then US4; validate at each phase with independent criteria.
- Safeguards: Enforce `timeoutMs`/`maxIterations`, cadence bounds, and clear validation messages.

## Format Validation

- All tasks use required checklist format with IDs and file paths.
- Story-specific tasks include `[US#]` labels; parallelizable tasks include `[P]` markers.
