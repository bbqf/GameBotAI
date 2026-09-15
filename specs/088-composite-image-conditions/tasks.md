---
description: "Task list for 088-composite-image-conditions"
---

# Tasks: Composite Image Conditions

**Input**: Design documents from `/specs/088-composite-image-conditions/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/composite-condition.md

**Tests**: Test tasks ARE included. The spec mandates them (FR-010, SC-002, acceptance criterion 9 of issue #191) and the constitution's Testing Standards make them non-optional for executable logic.

**Organization**: Grouped by user story. Phase 2 delivers the shared mechanism; each user story phase then adds one combining rule's semantics plus the proof that its scenario works, so every story is a shippable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)

## Path Conventions

Single .NET solution at repository root: `src/GameBot.Domain/`, `src/GameBot.Service/`, `tests/unit/`, `tests/contract/`, `tests/integration/`.

---

## Phase 1: Setup

**Purpose**: Establish the green baseline the constitution requires before any change.

- [ ] T001 Confirm a clean baseline by running `dotnet build GameBot.sln` and `dotnet test GameBot.sln` at the repository root; record the pass counts. The constitution makes a red build or test a hard stop, so any pre-existing failure must be identified now rather than blamed on this feature later.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The condition type, its plumbing through every consumer site, and the rule-agnostic validation. No user story can be implemented before this phase completes.

**⚠️ CRITICAL**: All of Phase 2 must finish before Phase 3 begins.

### Domain and contract types

- [ ] T002 [P] Add the composite condition hierarchy to `src/GameBot.Domain/Commands/SequenceStepCondition.cs`: a `CompositeConditionRule` enum (`All`, `Any`, `None`), an abstract `CompositeStepCondition : SequenceStepCondition` holding `Children` (ordered `IReadOnlyList<SequenceStepCondition>`) and an abstract `Rule`, and the sealed `AllStepCondition` / `AnyStepCondition` / `NoneStepCondition`. Register all three with `[JsonDerivedType]` under the discriminators `all`, `any`, `none`. Each sealed type must override `Type` with `[JsonIgnore]` exactly as the two existing leaves do — the file's own comment explains that omitting it produces a duplicate `type` key that fails to round-trip.
- [ ] T003 [P] Add the mirrored contract hierarchy to `src/GameBot.Service/Models/SequenceStepContracts.cs`: abstract `CompositeConditionContract : SequenceStepConditionContract` with `Children`, and sealed `AllConditionContract` / `AnyConditionContract` / `NoneConditionContract`, registered under the same three discriminators on the existing `[JsonPolymorphic]` base.
- [ ] T004 [P] Add a serialization round-trip unit test in `tests/unit/Sequences/CompositeConditionSerializationTests.cs` covering all three discriminators, a nested composite, and the absence of a duplicate `type` key. This proves the `[JsonDerivedType]` registration before anything is built on it.

### Validation

- [ ] T005 Create `src/GameBot.Domain/Services/CompositeConditionValidator.cs` implementing a single recursive walk that enforces: non-empty `Children`, at most 16 children, nesting depth at most 4, and the per-leaf shape rules (imageVisible needs `imageId`, `minSimilarity` within 0..1; commandOutcome needs `stepRef` and a known `expectedState`). Report `$`-rooted paths (`$.children[2].children[0]`) matching the convention `ConditionExpression.Validate()` already uses, with the exact message wording from `contracts/composite-condition.md`.
- [ ] T006 Call the validator from `src/GameBot.Domain/Services/SequenceStepValidationService.cs` at **all five** condition positions (FR-005): `ValidateStepCondition` (~line 305) for step guards, `ValidateIfCondition` (~line 193) for branch tests, the break-condition sites (~lines 151 and 246), and — newly — the `while` and `repeatUntil` loop conditions, which the service does not inspect at all today (it checks only `CountLoopConfig.Count` at ~line 121). Without those last two a malformed composite in a loop condition passes save and fails at runtime, breaking FR-010. Scope the two new positions to **composites only**: do not add leaf validation there, because stored sequences have never had it and adding it would reject sequences that validate today (the same reasoning as research decision D-006). `commandOutcome` children inside a composite must be resolved against the same prior-step rules the existing top-level check applies, so the "must reference a prior step" behaviour is preserved inside composites.
- [ ] T006a Add a unit test in `tests/unit/Sequences/CompositeConditionValidationTests.cs` proving a malformed composite in a `while` condition and in a `repeatUntil` condition is rejected at save time, and that a sequence with a *leaf* condition in those same positions still validates exactly as before (the D-006 boundary).

### Evaluation

- [ ] T007 Create `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`. Move the existing leaf evaluation into it (image leaves through the `Func<Condition, CancellationToken, Task<bool>>` evaluator, `commandOutcome` leaves through the `stepOutcomes` dictionary) and add the recursive composite walk with a `switch` over the rule. Leave the rule arms unimplemented here — each user story phase fills one in. Apply `Negate` to the combined result. A child that cannot be evaluated must propagate as a failure, not as `false`, matching today's single-condition behaviour. Keep methods small: this is a new file specifically so `SequenceRunner` does not grow (see plan Constraints).
- [ ] T008 Route both `SequenceRunner` condition sites through the new evaluator in `src/GameBot.Domain/Services/SequenceRunner.cs`: `ExecuteStepAsync` (~line 510, which records a step result on a false guard) and `EvaluateLoopConditionAsync` (~line 1481, which throws). Existing leaf behaviour and existing execution records must be unchanged — this is a delegation, not a rewrite.

### Mapping, persistence, schema

- [ ] T009 Extend `MapPerStepCondition` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` (~line 1203) to map composite contracts to domain recursively, preserving child order.
- [ ] T010 Extend `MapPerStepConditionToDto` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` (~line 541) to emit `type` (the rule) and a recursively mapped `children` array.
- [ ] T011 Extend the persistence backstop in `src/GameBot.Domain/Commands/FileSequenceRepository.cs` (~line 147) to walk composites recursively when applying its existing per-leaf checks. This stays a backstop — T005/T006 are the gate that returns 400, and nothing should reach the repository unvalidated.
- [ ] T012 Add a composite arm to `DescribeBreakCondition` in `src/GameBot.Domain/Services/SequenceRunner.cs` (~line 1502), rendering the rule and its children, e.g. `all(imageVisible(imageId=x, ...), NOT imageVisible(imageId=y, ...))`.
- [ ] T013 In `ValidatePerStepImageReferencesAsync` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` (~line 1291), recurse into composites found in the positions it already walks. Do **not** widen it to positions it does not walk today — research decision D-006 records why: doing so would reject previously-valid stored sequences.
- [ ] T014 [P] Register and alias the three new schemas in `src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs`, following the existing `GenerateSchema` + `AliasSchema` pairs, published as `AllCondition`, `AnyCondition` and `NoneCondition`.
- [ ] T015 Add validation unit tests in `tests/unit/Sequences/CompositeConditionValidationTests.cs` covering every row of the rejection table in `contracts/composite-condition.md`: empty children, missing `children`, 17 children, depth 5, bad child `imageId`, out-of-range `minSimilarity`, missing `stepRef`, unknown `expectedState`. Assert the exact `$`-rooted paths. Include the **positive** boundary cases too, or the limits are only half-tested: a single-child composite is accepted (FR-009), exactly 16 children is accepted, and exactly depth 4 is accepted.

**Checkpoint**: The mechanism exists and rejects bad input; no rule evaluates yet.

---

## Phase 3: User Story 1 — Require two signals together (Priority: P1) 🎯 MVP

**Goal**: An author can guard a step on "image A **and** image B", so a button shared by two dialogs is disambiguated by the dialog's own title.

**Independent test**: Save a sequence whose step condition is `all` over two images; run it against a screen showing only the first and confirm the step is skipped; run it against a screen showing both and confirm it executes.

- [ ] T016 [US1] Implement the `All` arm in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`: evaluate children in author order, return false at the first false child without evaluating the rest, return true when every child is true.
- [ ] T017 [P] [US1] Add truth-table and short-circuit unit tests for `all` in `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs`, using a counting stub leaf resolver to assert that evaluation stops at the first false child and that children are visited in author order.
- [ ] T018 [P] [US1] Add a contract test in `tests/contract/Sequences/CompositeConditionContractTests.cs` asserting that every malformed composite payload from the rejection table returns **400 with an errors array, never 500**, through `POST /api/sequences` and `PUT /api/sequences/{id}`.
- [ ] T019 [US1] Add the composite step-guard case to `tests/unit/Sequences/PerStepConditionRunnerTests.cs`: a step guarded by `all` over two image leaves is skipped when one is absent, and the recorded execution entry carries `conditionType` = `all` with a message naming the deciding child (FR-016).
- [ ] T020 [US1] Add the end-to-end B-011 scenario to `tests/integration/Sequences/PerStepConditionExecutionPermutationIntegrationTests.cs`: a step whose guard is "image A visible AND image B not visible" does not fire when both are on screen, and does fire when only A is. This is SC-002 and the proof the issue asks for.

**Checkpoint**: `all` works end to end; B-011 is fixed. Shippable alone.

---

## Phase 4: User Story 2 — Exclude a known look-alike (Priority: P1)

**Goal**: An author can guard on "A visible and **not** B visible" using an explicit `none`, the form that fails safe when the wanted dialog has no unique anchor.

**Independent test**: Save a guard using `none` over the look-alike's title, show a screen carrying both, and confirm the step is skipped.

- [ ] T021 [US2] Implement the `None` arm in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`: return false at the first true child without evaluating the rest, return true when no child is true.
- [ ] T022 [P] [US2] Add `none` truth-table and short-circuit unit tests to `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs`.
- [ ] T023 [P] [US2] Add unit tests for `Negate` applied to a composite in `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs`, asserting the plain logical inverse of the combined result for all three rules, and that a negated `any` and a `none` over the same children agree (FR-006).
- [ ] T024 [US2] Extend the integration test in `tests/integration/Sequences/PerStepConditionExecutionPermutationIntegrationTests.cs` with the nested `all`-of-[`imageVisible`, `none`] form from `contracts/composite-condition.md`, proving it behaves identically to the `negate` form.

**Checkpoint**: Both disambiguation idioms work.

---

## Phase 5: User Story 3 — Collapse a fan-out of alternatives (Priority: P2)

**Goal**: A guard needing N alternative screens is one `any` condition instead of N repeated guards.

**Independent test**: Replace a group of repeated single-image break guards with one `any` and confirm the loop exits on the same screens.

- [ ] T025 [US3] Implement the `Any` arm in `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`: return true at the first true child without evaluating the rest, return false when no child is true.
- [ ] T026 [P] [US3] Add `any` truth-table and short-circuit unit tests to `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs`.
- [ ] T027 [US3] Add a loop-exit test in `tests/unit/Sequences/PerStepConditionRunnerTests.cs` proving a composite `breakCondition` exits the loop on any listed image, and that the break reason rendered by `DescribeBreakCondition` names the rule and its children (T012).

**Checkpoint**: All three rules work in every condition position.

---

## Phase 6: User Story 4 — Existing automation keeps working (Priority: P1)

**Goal**: Every stored sequence and every single-condition payload behaves and serializes exactly as before.

**Independent test**: Load, execute, read and re-save a pre-existing single-condition sequence and confirm both runtime behaviour and stored form are unchanged.

- [ ] T028 [US4] Add a round-trip regression test to `tests/integration/Sequences/ConditionalEditRoundTripIntegrationTests.cs` covering create → read → update → read for a nested composite (depth 3, mixed leaf kinds), asserting no loss of children, order, `negate`, or per-leaf settings (FR-013).
- [ ] T029 [US4] Add a backward-compatibility assertion to the same file: a sequence containing only `imageVisible` and `commandOutcome` conditions round-trips to byte-identical stored JSON after this change (FR-015).
- [ ] T030 [P] [US4] Extend `tests/contract/Sequences/SequencePerStepConditionsOpenApiTests.cs` to assert the published schema contains the three new discriminators, the recursive `children` array, and the documented bounds (FR-014).
- [ ] T031 [US4] Run the pre-existing conditional-flow suites unchanged — `tests/unit/Sequences/IfValidationTests.cs`, `LoopValidationTests.cs`, `SequenceRunnerIfTests.cs`, `SequenceRunnerLoopTests.cs`, `tests/contract/Sequences/SequenceConditionalStepsContractTests.cs` — and confirm no assertion needed modification. Any test that *had* to change is a backward-compatibility break and must be investigated, not edited.

**Checkpoint**: The feature is additive and proven so.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T032 [P] Update `docs/architecture.md` with the composite condition form in the domain model and the API surface, and refresh its "Last reviewed" date. Constitution Principle V makes this mandatory in the same PR for any change to the domain model or API surface.
- [ ] T033 [P] Add the row `| 088 | Composite Image Conditions | Implemented |` to `specs/STATUS.md` and change the `**Status**:` line in `specs/088-composite-image-conditions/spec.md` from `Draft` to `Implemented`.
- [ ] T034 Verify the performance claim from plan.md by asserting in `tests/unit/Sequences/CompositeConditionEvaluatorTests.cs` that a composite performs exactly as many leaf evaluations as the short-circuit requires and no more, and that a two-image guard triggers one screen observation rather than two (FR-017, SC-006).
- [ ] T035 Run `dotnet build GameBot.sln` and `dotnet test GameBot.sln` and confirm both are green with no new analyzer warnings. `TreatWarningsAsErrors=true` makes any new warning a build failure; the constitution makes a red result a hard stop on completion.
- [ ] T036 Collect coverage with the already-referenced `coverlet` collector (`dotnet test /p:CollectCoverage=true`, or `--collect:"XPlat Code Coverage"`) and confirm the files this feature touched meet the constitution's ≥80% line and ≥70% branch bar — in particular the two new files, `CompositeConditionValidator.cs` and `SequenceStepConditionEvaluator.cs`, which hold nearly all the new branching. Report the numbers; if a branch is uncovered, add the missing case rather than lowering the bar.

---

## Dependencies & Execution Order

- **Phase 1 (T001)** → blocks everything.
- **Phase 2 (T002–T015, including T006a)** → blocks all user stories. Within it: T002/T003/T004 first (the types), then T005–T006a (validation) and T007–T008 (evaluation) in parallel with T009–T014 (mapping, persistence, schema), then T015.
- **Phase 3 (US1)**, **Phase 4 (US2)**, **Phase 5 (US3)** each add one independent arm to the evaluator. They touch the same file, so run them sequentially, but none depends on another's behaviour.
- **Phase 6 (US4)** needs at least one rule implemented; run it after Phase 3 at the earliest, ideally last for full coverage.
- **Phase 7** last; within it T035 must pass before T036 is meaningful.

### Parallel opportunities

- T002, T003, T004 — different files.
- T014 — independent of all mapping and evaluation work.
- T017 + T018 (US1), T022 + T023 (US2), T026 (US3) — test files independent of each other.
- T030, T032, T033 — different files, no shared state.

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That delivers the `all` rule end to end and closes B-011 on its own, since the exclusion case is expressible with `negate` on a child before `none` exists. Phases 4 and 5 add the more readable idioms; Phase 6 proves the whole thing is additive.

Stop and fix rather than proceed if: any pre-existing test needed editing (T031), or any new analyzer warning appears (T035). Both are signals of a behaviour break, not of a test that needs adjusting.
