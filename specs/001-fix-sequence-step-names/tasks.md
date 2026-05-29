# Tasks: Preserve Sequence Step Command Names

**Input**: Design documents from `/specs/001-fix-sequence-step-names/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Bug-fix regression tests are required by the constitution and are included for each affected user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this belongs to (`[US1]`, `[US2]`, `[US3]`)
- Include exact file paths in each task description

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the baseline reproduction and validation flow before code changes

- [ ] T001 Run the baseline validation and reproduction steps documented in `specs/001-fix-sequence-step-names/quickstart.md`
- [X] T002 [P] Reproduce the saved-step and execution-log regressions from `specs/001-fix-sequence-step-names/quickstart.md` against `src/web-ui/src/pages/SequencesPage.tsx` and `src/web-ui/src/pages/ExecutionLogs.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared sequence-step contract and mapping work required before user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Extend the shared sequence step model with command-name snapshot and unresolved-reference support in `src/GameBot.Domain/Commands/SequenceStep.cs`
- [X] T004 Persist and reload the new sequence-step command reference fields safely in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`
- [X] T005 [P] Align frontend sequence DTOs with the command reference view contract in `src/web-ui/src/types/sequenceFlow.ts` and `src/web-ui/src/services/sequences.ts`
- [X] T006 [P] Centralize command reference extraction and fallback mapping in `src/web-ui/src/lib/sequenceMapping.ts`

**Checkpoint**: Shared sequence-step contract and mapping are ready for story-specific work

---

## Phase 3: User Story 1 - Reopen a Sequence Without Losing Step Identity (Priority: P1) 🎯 MVP

**Goal**: Save and reopen sequences without losing each step's selected command or user-visible label

**Independent Test**: Create a multi-step sequence with different command selections, save it, reopen it, and confirm each step still shows the original command and step identity.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T007 [P] [US1] Add a contract regression for per-step command round-tripping in `tests/contract/Sequences/SequencePerStepConditionsContractTests.cs`
- [X] T008 [P] [US1] Add an integration regression for saved step metadata round-tripping in `tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs`
- [X] T009 [P] [US1] Add a web UI regression for reopening saved sequences with preserved command selections in `src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx`
- [X] T010 [P] [US1] Add an explicit unchanged-resave regression in `tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs` that saves, reloads, re-saves unchanged steps, and asserts no step is reassigned to a different command

### Implementation for User Story 1

- [X] T011 [US1] Preserve canonical per-step object payloads for sequence create/update/patch in `src/GameBot.Service/Program.cs`
- [X] T012 [US1] Populate saved `commandReference` response data for command-backed steps in `src/GameBot.Service/Program.cs`
- [X] T013 [US1] Restore saved command selections and step labels on editor load in `src/web-ui/src/pages/SequencesPage.tsx`

**Checkpoint**: User Story 1 is functional when saved sequences reopen with the same step-to-command mappings the author selected

---

## Phase 4: User Story 2 - Identify Executed Commands in Logs (Priority: P2)

**Goal**: Make execution logs identify both the sequence step and the command that actually ran

**Independent Test**: Execute a sequence with labeled steps and confirm the execution-log detail view includes both the step label and command name for each command-backed step.

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T014 [P] [US2] Add an execution-log integration regression for step label plus command name in `tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs`
- [X] T015 [P] [US2] Add an execution-log projection unit regression in `tests/unit/ExecutionLogs/SequenceExecutionLogProjectionTests.cs`

### Implementation for User Story 2

- [X] T016 [US2] Enrich sequence execution detail items with command names in `src/GameBot.Service/Program.cs`
- [X] T017 [US2] Project command-aware sequence step details through execution-log responses in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` and `src/GameBot.Service/Models/ExecutionLogs.cs`
- [X] T018 [US2] Align execution-log client types and rendering with command-aware step messages in `src/web-ui/src/services/executionLogsApi.ts` and `src/web-ui/src/pages/ExecutionLogs.tsx`

**Checkpoint**: User Story 2 is functional when sequence execution logs identify both the step and the executed command without relying on internal step ids alone

---

## Phase 5: User Story 3 - Preserve Existing Sequences During Repair (Priority: P3)

**Goal**: Keep existing valid sequences intact while showing deleted or missing commands as unresolved instead of blank

**Independent Test**: Reopen a previously saved sequence after removing one referenced command and confirm valid steps remain intact while the missing command appears as unresolved with its last saved name.

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T019 [P] [US3] Add an integration regression for deleted-command unresolved states in `tests/integration/Sequences/SequenceMissingCommandReferenceIntegrationTests.cs`
- [X] T020 [P] [US3] Add a web UI regression for unresolved saved command display in `src/web-ui/src/pages/__tests__/SequencesPage.unresolvedCommand.spec.tsx`

### Implementation for User Story 3

- [X] T021 [US3] Return unresolved command-reference metadata when saved command ids no longer resolve in `src/GameBot.Service/Program.cs`
- [X] T022 [US3] Render unresolved saved commands with snapshot names in `src/web-ui/src/pages/SequencesPage.tsx` and `src/web-ui/src/components/SearchableDropdown.tsx`
- [X] T023 [US3] Preserve valid assignments and unresolved snapshots during sequence resaves in `src/GameBot.Domain/Commands/FileSequenceRepository.cs` and `src/GameBot.Service/Program.cs`

**Checkpoint**: User Story 3 is functional when existing sequences remain editable and missing commands are shown as unresolved instead of silently cleared

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, cleanup, and documentation touches across stories

- [ ] T024 [P] Update verification guidance and unresolved-command checks in `specs/001-fix-sequence-step-names/quickstart.md`
- [ ] T025 Run `dotnet build -c Debug` and `dotnet test -c Debug --logger trx` using `specs/001-fix-sequence-step-names/quickstart.md`, then run `scripts/analyze-test-results.ps1` if failures occur
- [ ] T026 Verify touched-area regression coverage across `tests/contract/Sequences/SequencePerStepConditionsContractTests.cs`, `tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs`, `tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs`, `tests/integration/Sequences/SequenceMissingCommandReferenceIntegrationTests.cs`, `src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx`, and `src/web-ui/src/pages/__tests__/SequencesPage.unresolvedCommand.spec.tsx`
- [ ] T027 [P] Run lint and static analysis for touched backend and frontend files including `src/GameBot.Service/Program.cs`, `src/GameBot.Domain/Commands/SequenceStep.cs`, `src/GameBot.Domain/Commands/FileSequenceRepository.cs`, `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, and `src/web-ui/src/pages/SequencesPage.tsx`
- [ ] T028 [P] Run security and secret-scan verification for touched files including `src/GameBot.Service/Program.cs`, `src/GameBot.Domain/Commands/FileSequenceRepository.cs`, `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`, and `src/web-ui/src/pages/SequencesPage.tsx`, then record the result in implementation notes or PR evidence
- [ ] T029 [P] Validate sequence load/save and execution-log detail responsiveness for a 50-step sequence using `specs/001-fix-sequence-step-names/quickstart.md`, then record a perf note in `docs/perf-checklist.md`
- [ ] T030 [P] Add a user-visible fix note to `CHANGELOG.md` and update affected validation guidance in `docs/validation.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; establishes the failing behavior and validation flow.
- **Foundational (Phase 2)**: Depends on Setup completion; blocks all user stories by defining the shared sequence-step contract and mapping.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion.
- **User Story 3 (Phase 5)**: Depends on Foundational completion.
- **Polish (Phase 6)**: Depends on the user stories selected for delivery.

### User Story Dependencies

- **User Story 1 (P1)**: No hard dependency on other stories after Foundational.
- **User Story 2 (P2)**: No hard dependency on User Story 1 after Foundational, but shares the same canonical per-step model.
- **User Story 3 (P3)**: No hard dependency on User Story 1 after Foundational, but reuses the same command snapshot and mapping surfaces.

### Within Each User Story

- Regression tests MUST be written and observed failing before implementation.
- Backend contract and persistence changes come before frontend rendering changes.
- Story validation should be rerun before moving to the next priority slice.
- No bug-fix story is complete until unchanged step-command mappings are explicitly verified on resave.

### Parallel Opportunities

- T005 and T006 can run in parallel once T003-T004 define the shared model shape.
- T007, T008, T009, and T010 can run in parallel within User Story 1.
- T014 and T015 can run in parallel within User Story 2.
- T019 and T020 can run in parallel within User Story 3.
- User Stories 2 and 3 can be staffed in parallel after Foundational, though implementation should still validate each story independently.

---

## Parallel Example: User Story 1

```text
Task T007: Add a contract regression for per-step command round-tripping in tests/contract/Sequences/SequencePerStepConditionsContractTests.cs
Task T008: Add an integration regression for saved step metadata round-tripping in tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs
Task T009: Add a web UI regression for reopening saved sequences with preserved command selections in src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx
Task T010: Add an explicit unchanged-resave regression in tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs that asserts no step is reassigned to a different command
```

---

## Parallel Example: User Story 2

```text
Task T014: Add an execution-log integration regression for step label plus command name in tests/integration/ExecutionLogs/SequenceExecutionLoggingIntegrationTests.cs
Task T015: Add an execution-log projection unit regression in tests/unit/ExecutionLogs/SequenceExecutionLogProjectionTests.cs
```

---

## Parallel Example: User Story 3

```text
Task T019: Add an integration regression for deleted-command unresolved states in tests/integration/Sequences/SequenceMissingCommandReferenceIntegrationTests.cs
Task T020: Add a web UI regression for unresolved saved command display in src/web-ui/src/pages/__tests__/SequencesPage.unresolvedCommand.spec.tsx
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Stop and validate that saved sequences reopen with preserved command selections and labels.

### Incremental Delivery

1. Finish Setup and Foundational to lock the shared sequence-step contract.
2. Deliver User Story 1 as the MVP bug fix for save/reopen behavior.
3. Add User Story 2 to make execution logs operator-readable.
4. Add User Story 3 to preserve existing sequences and unresolved references safely.
5. Finish with Phase 6 validation and cleanup.

### Parallel Team Strategy

1. One developer completes Foundational backend/domain contract work.
2. After Foundational, one developer can take User Story 1 while another handles User Story 2 test scaffolding.
3. User Story 3 can proceed in parallel once the shared contract is stable and the team can validate unresolved-reference UX independently.

---

## Notes

- `[P]` tasks indicate different files and no incomplete dependency on the same slice.
- Each user story remains independently testable after Foundational is complete.
- Keep the legacy `string[]` sequence-step path as compatibility fallback only; do not let it overwrite canonical per-step authoring data.
- Re-run the full validation commands after each story slice that changes backend persistence or execution-log projection.