# Tasks: Randomized Sequence Step Delays

**Input**: Design documents from /specs/001-sequence-random-delay/
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sequence-random-delay.openapi.yaml

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm implementation baseline and synchronize feature docs used by implementation tasks.

- [X] T001 Capture baseline verification steps and expected commands in specs/001-sequence-random-delay/quickstart.md
- [X] T002 Confirm feature contract baseline and endpoint scope in specs/001-sequence-random-delay/contracts/sequence-random-delay.openapi.yaml

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared sequence-level delay model and API plumbing required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Add sequence-level inter-step delay range model fields and JSON serialization support in src/GameBot.Domain/Commands/CommandSequence.cs
- [X] T004 Add sequence-level delay range DTO contracts for create/update payloads in src/GameBot.Service/Models/SequenceStepContracts.cs
- [X] T005 Implement shared mapping between API contracts and domain sequence delay configuration in src/GameBot.Service/Program.cs
- [X] T006 Persist and hydrate sequence-level inter-step delay configuration in src/GameBot.Domain/Commands/FileSequenceRepository.cs

**Checkpoint**: Domain model, contract model, API mapping, and repository persistence are ready.

---

## Phase 3: User Story 1 - Natural Sequence Pacing by Default (Priority: P1) MVP

**Goal**: Ensure all multi-step sequence executions apply randomized inter-step delay using default 100-300 ms when no custom range is configured.

**Independent Test**: Execute a multi-step sequence without custom range and verify each inter-step delay is sampled within 100..300 ms, with no trailing delay after final step.

- [X] T007 [P] [US1] Add default inter-step delay constants and range resolver helpers in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T008 [US1] Implement uniform inclusive inter-step delay sampling helper (min <= sampled <= max) in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T009 [US1] Apply inter-step delay transitions in linear sequence execution path in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T010 [US1] Apply inter-step delay transitions in flow-graph execution path in src/GameBot.Domain/Services/SequenceRunner.cs
- [X] T011 [US1] Prevent post-terminal/post-failure trailing delay and preserve existing per-step delay behavior in src/GameBot.Domain/Services/SequenceRunner.cs

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Per-Sequence Delay Configuration (Priority: P2)

**Goal**: Allow authors to configure custom sequence-level inter-step min/max delay range and have execution honor it.

**Independent Test**: Configure custom range for one sequence, execute it and a default sequence, and verify each uses its own range.

- [X] T012 [US2] Extend create/put/patch/get/list endpoint payload mapping to include interStepDelayRangeMs in src/GameBot.Service/Program.cs
- [X] T013 [P] [US2] Add sequence-level delay range types to frontend sequence contracts in src/web-ui/src/types/sequenceFlow.ts
- [X] T014 [P] [US2] Extend sequence API request/response DTOs for interStepDelayRangeMs in src/web-ui/src/services/sequences.ts
- [X] T015 [US2] Add per-sequence delay range form fields and binding/state persistence in src/web-ui/src/pages/SequencesPage.tsx
- [X] T016 [US2] Ensure list/detail hydration preserves configured interStepDelayRangeMs in src/web-ui/src/lib/sequenceMapping.ts

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Safe Validation of Delay Settings (Priority: P3)

**Goal**: Reject invalid delay ranges with clear feedback while preserving last known valid configuration.

**Independent Test**: Attempt invalid saves (negative, non-integer, min > max) and verify request rejection with actionable error messages.

- [X] T017 [US3] Enforce backend validation rules for interStepDelayRangeMs (integer-only, min >= 0, min <= max) in src/GameBot.Service/Program.cs
- [X] T018 [US3] Add repository-level validation guard for persisted interStepDelayRangeMs on create/update in src/GameBot.Domain/Commands/FileSequenceRepository.cs
- [X] T019 [US3] Add authoring UI validation and actionable error messaging for invalid delay inputs in src/web-ui/src/pages/SequencesPage.tsx
- [X] T020 [US3] Align validation error contract examples and schema constraints in specs/001-sequence-random-delay/contracts/sequence-random-delay.openapi.yaml

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency and verification across all stories.

- [X] T021 [P] Update feature verification walkthrough with default/custom/invalid scenarios in specs/001-sequence-random-delay/quickstart.md
- [X] T022 Run full quality gate commands and capture outcomes in specs/001-sequence-random-delay/quickstart.md
- [X] T023 Run scripts/analyze-test-results.ps1 and record TESTERROR summary evidence in specs/001-sequence-random-delay/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): No dependencies; start immediately.
- Foundational (Phase 2): Depends on Setup completion; blocks all user stories.
- User Story phases (Phase 3-5): Depend on Foundational completion.
- Polish (Phase 6): Depends on completion of selected user stories.

### User Story Dependencies

- US1 (P1): Starts immediately after Foundational; no dependency on other stories.
- US2 (P2): Starts after Foundational; depends on US1 runtime delay integration to verify custom override behavior.
- US3 (P3): Starts after Foundational; depends on US2 request/UI shape for validation coverage.

### Suggested Delivery Order

- MVP: Phase 1 -> Phase 2 -> Phase 3 (US1).
- Increment 2: Phase 4 (US2).
- Increment 3: Phase 5 (US3).
- Final hardening: Phase 6.

---

## Parallel Execution Examples

### User Story 1

- Parallel candidate: T007 in src/GameBot.Domain/Services/SequenceRunner.cs can begin while endpoint mapping from Phase 2 final review is finishing.
- Then execute serial chain T008 -> T009 -> T010 -> T011 in src/GameBot.Domain/Services/SequenceRunner.cs.

### User Story 2

- Run T013 in src/web-ui/src/types/sequenceFlow.ts and T014 in src/web-ui/src/services/sequences.ts in parallel.
- Run T012 in src/GameBot.Service/Program.cs in parallel with T013/T014.
- After those complete, run T015 in src/web-ui/src/pages/SequencesPage.tsx and T016 in src/web-ui/src/lib/sequenceMapping.ts.

### User Story 3

- Run T017 in src/GameBot.Service/Program.cs and T018 in src/GameBot.Domain/Commands/FileSequenceRepository.cs in parallel.
- Run T019 in src/web-ui/src/pages/SequencesPage.tsx in parallel with T020 in specs/001-sequence-random-delay/contracts/sequence-random-delay.openapi.yaml.

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete US1 in Phase 3.
3. Validate independent US1 behavior with quickstart scenarios.
4. Stop for review/demo before taking configuration complexity.

### Incremental Delivery

1. Deliver US1 default behavior first.
2. Deliver US2 custom configuration and UI support.
3. Deliver US3 validation hardening and contract clarity.
4. Finish with Phase 6 verification and evidence.

### Parallel Team Strategy

1. One developer focuses backend mapping/persistence (T012, T017, T018).
2. One developer focuses frontend contracts/UI (T013, T014, T015, T019).
3. One developer focuses runner behavior and execution semantics (T007-T011).
4. Converge for contract and quickstart updates (T020-T023).

---

## Notes

- Task format follows required checklist pattern: checkbox, ID, optional [P], optional [USx], action with exact file path.
- [P] marks tasks that can be executed safely in parallel on distinct files.
- Each user story phase includes an independent test criterion and can be validated incrementally.
