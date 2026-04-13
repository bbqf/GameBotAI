# Tasks: Command Loop Structures

**Input**: Design documents from `specs/033-command-loops/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/sequence-loop-steps.md ✅

**Tech stack**: C# 13 / .NET 9 (GameBot.Domain + GameBot.Service) · TypeScript ES2020 / React 18 (web-ui)  
**Test approach**: xUnit + coverlet (backend) · React Testing Library (frontend)

---

## Phase 1: Setup

**Purpose**: Create new files and establish shared foundations before any user-story work begins.

- [X] T001 Add `LoopConfig.cs` to `src/GameBot.Domain/Commands/` with the `[JsonPolymorphic]` hierarchy: abstract `LoopConfig` (with optional `MaxIterations`), `CountLoopConfig` (`Count`), `WhileLoopConfig` (`Condition`), `RepeatUntilLoopConfig` (`Condition`). Add XML doc comments to all public members (constitution: public APIs require documentation).
- [X] T002 [P] Extend `SequenceStepType` enum in `src/GameBot.Domain/Commands/SequenceStep.cs` with `Loop` and `Break` values
- [X] T003 *(depends on T002 — enum values must exist before referencing them)* Add `Loop`, `Body`, and `BreakCondition` properties to `SequenceStep` in `src/GameBot.Domain/Commands/SequenceStep.cs`
- [X] T004 [P] Add `LoopMaxIterations` property (default 1000) to `AppConfig` in `src/GameBot.Domain/Config/AppConfig.cs`
- [X] T005 [P] Create `TemplateSubstitutor.cs` in `src/GameBot.Domain/Utils/` with `Substitute(string template, IReadOnlyDictionary<string,string> context)` and `SubstitutePayload(SequenceActionPayload, context)` methods using compiled `{{(\w+)}}` regex. Add XML doc comments to all public members (constitution: public APIs require documentation).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Persistence, serialization round-trip, and validation gate — must be complete before any execution or UI story can be tested.

- [X] T006 Ensure `LoopConfig` polymorphic subtypes survive round-trip serialize/deserialize via `FileSequenceRepository` in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`. **Before implementing**: inspect how `FileSequenceRepository` handles `SequenceStepCondition` — if it uses `[JsonPolymorphic]` / `[JsonDerivedType]` attribute-based autodiscovery with a shared `JsonSerializerOptions`, no manual subtype registration is needed and T006 becomes: verify round-trip works with attributes alone and add a failing test to confirm. Only add manual registration if the options are built without attribute scanning.
- [X] T007 Add loop-specific validation rules to `SequenceValidator` in `src/GameBot.Domain/Validation/SequenceValidator.cs`:
  - `CountLoopConfig.Count >= 0` (FR-004)
  - `LoopConfig.MaxIterations > 0` when set (FR-008)
  - Loop body MUST NOT contain `StepType == Loop` steps (FR-012)
  - `StepType == Break` only inside a loop body (FR-021)
  - `{{iteration}}` placeholder only inside loop body parameters (FR-002a)
  - `commandOutcome` condition in loop MUST NOT forward-reference within body (FR-006)
- [X] T008 Write contract tests in `tests/contract/SequenceLoopContractTests.cs`:
  - Round-trip serialize/deserialize for `count` loop step with inner action step
  - Round-trip for `while` loop step with `imageVisible` condition
  - Round-trip for `repeatUntil` loop step with `commandOutcome` condition
  - Round-trip for `break` step (conditional and unconditional)
  - Verify `body` inner steps preserve order and `stepId` values

---

## Phase 3: User Story 1 — Count-Based Loop (P1)

**Story goal**: Author can define a "repeat N times" loop, execute it, and see the inner step run exactly N times with `{{iteration}}` substituted correctly.

**Independent test criteria**: Create sequence with one count-based loop (N=3), one inner tap step using `{{iteration}}` in a parameter, execute it, verify tap called 3 times and iteration values 1/2/3 logged.

- [X] T009 [US1] Write unit tests in `tests/GameBot.Domain.Tests/SequenceRunnerLoopTests.cs` for count-based loop:
  - N=5 inner step executes exactly 5 times
  - N=0 body is skipped, execution continues
  - N=3 with `{{iteration}}` placeholder: values 1, 2, 3 substituted per iteration
- [X] T010 [P] [US1] Write unit tests in `tests/GameBot.Domain.Tests/TemplateSubstitutorTests.cs`:
  - `{{iteration}}` replaced with correct string value
  - Unknown keys left as-is
  - Non-string JSON values untouched
  - Multiple placeholders in one string all substituted
  - Empty context returns template unchanged
- [X] T012 [US1] Extend `ExecutionLogModels.cs` in `src/GameBot.Domain/Logging/ExecutionLogModels.cs`:
  - Add `LoopIterationOutcome` record (`IterationIndex`, `BreakTriggered`, `StepOutcomes`). Add XML doc comments to all public members (constitution: public APIs require documentation).
  - Add `IReadOnlyList<LoopIterationOutcome>? LoopIterations` to `ExecutionStepOutcome`
- [X] T011 *(depends on T012 — `LoopIterationOutcome` must be defined before the runner references it)* [US1] Implement count-based loop execution in `SequenceRunner` in `src/GameBot.Domain/Services/SequenceRunner.cs`:
  - Add `ExecuteLoopStepAsync` dispatch branch for `StepType == Loop`
  - For `loopType == count`: iterate `Count` times, inject `{"iteration": i.ToString()}` context dict, call `SubstitutePayload` before each body step dispatch
  - Record `LoopIterationOutcome` per iteration with inner `StepOutcomes`
  - Emit loop-level `ExecutionStepOutcome` with `loopIterations` list

---

## Phase 4: User Story 5 — UI Loop Block Visualization (P1)

**Story goal**: Authors see loop blocks as visually distinct contained cards (colored border + background) with a header badge showing loop type and parameter, and an inner step list using the same add/remove/reorder interactions as top-level steps.

**Independent test criteria**: Open a sequence with a count loop + a while loop side-by-side; confirm each has its own bounded block with no visual overlap; break step inside a loop renders inside the boundary.

- [X] T013 [US5] Create `LoopBlockHeader.tsx` in `src/web-ui/src/components/sequences/`:
  - Props: `loopType: 'count' | 'while' | 'repeatUntil'`, `count?: number`, `condition?: SequenceStepCondition`, `maxIterations?: number`
  - Renders: loop type badge, human-readable parameter summary (e.g. "× 10", "while imageVisible"), `{{iteration}}` variable name hint for count/while loops
- [X] T014 [P] [US5] Create `BreakStepRow.tsx` in `src/web-ui/src/components/sequences/`:
  - Props: `breakCondition?: SequenceStepCondition`, `onEdit: () => void`, `onRemove: () => void`
  - Renders a single-row break step. Unconditional break = `breakCondition` is `undefined`/`null`; represented in the UI as an **"Always break" toggle** (checked = unconditional, unchecked = condition-based). When unconditional, the condition editor is hidden and `breakCondition` is `undefined` in the `onChange` payload.
  - Test case (include in T017): toggling "Always break" on fires `onChange` with `breakCondition: undefined`; toggling off shows the condition editor.
- [X] T015 *(depends on T016 — `StepEntry` union and `LoopStepEntry` alias must be defined before `LoopBlock` imports them)* [US5] Create `LoopBlock.tsx` in `src/web-ui/src/components/sequences/`:
  - Props: `loop: LoopStepEntry` where `LoopStepEntry = Extract<StepEntry, { type: 'Loop' }>` (exported alias from T016's type definitions); `onChange`, `onRemove`, `availableImages`, `availableStepRefs`
  - Renders `LoopBlockHeader`, colored left-border container, indented inner `ReorderableList` of body steps
  - Body step types dispatched to existing `ActionStepRow` / `ConditionalStepRow` / `BreakStepRow` as appropriate. **Before coding**: confirm `ConditionalStepRow` exists at `src/web-ui/src/components/sequences/ConditionalStepRow.tsx` (feature 032); update import path if the name differs.
  - "Add step inside loop" affordance at the bottom of the body
- [X] T016 [US5] Extend `SequencesPage.tsx` in `src/web-ui/src/pages/SequencesPage.tsx`:
  - Add `'Loop'` and `'Break'` to the step type union and `StepEntry` type
  - In the step list render dispatch, handle `type === 'Loop'` → `<LoopBlock>` and `type === 'Break'` → `<BreakStepRow>`
  - Add "Add loop" option in the step-type selector (with sub-choice: count / while / repeatUntil)
- [X] T017 [US5] Write render tests in `tests/web-ui/src/components/sequences/LoopBlock.test.tsx`:
  - Count loop renders header with count value and `{{iteration}}` hint
  - While loop renders header with condition summary
  - RepeatUntil loop renders correct header label
  - Break step row renders inside loop body boundary
  - Adding a step inside loop body calls `onChange` with new body step appended
  - Reordering a step inside a loop body fires `onChange` with updated body step order (FR-019)
  - "Always break" toggle on `BreakStepRow` fires `onChange` with `breakCondition: undefined`; toggling off shows condition editor (FR-011)

---

## Phase 5: User Story 2 — While-Condition Loop (P2)

**Story goal**: Author can define a "while condition" loop; body runs only while condition is true at entry; body is skipped when false on entry; safety limit triggers hard failure; condition eval error fails immediately.

**Independent test criteria**: Execute while loop with mock condition returning true×2 then false; confirm 2 iterations, then stop. Execute with false on entry; confirm 0 iterations. Execute with never-false mock hitting limit 3; confirm failure.

- [X] T018 [US2] Extend `SequenceRunnerLoopTests.cs` with while-loop tests:
  - Condition true × 2 then false → body runs exactly twice
  - Condition false on entry → body skipped, outcome = `skipped`
  - Condition never false, limit = 3 → loop fails after 3 iterations, command fails
  - Condition evaluation throws → loop step outcome = `failed`, command stops
- [X] T019 [US2] Implement while loop execution branch in `SequenceRunner.ExecuteLoopStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs`:
  - Re-evaluate condition before each iteration; false → exit loop (`success`/`skipped`)
  - Condition eval error → fail loop step, propagate failure
  - Increment iteration counter; compare against `MaxIterations` (per-loop) or `AppConfig.LoopMaxIterations`; if exceeded → fail with `limitReached` reason code

---

## Phase 6: User Story 3 — Repeat-Until Loop (P2)

**Story goal**: Author can define a "repeat until condition" loop; body executes at least once; exits when condition becomes true; safety limit triggers hard failure.

**Independent test criteria**: Execute repeat-until with exit condition true after first iteration → body runs once. Execute with condition true after 3 iterations → body runs 3 times. Execute with never-true condition → fails at limit.

- [X] T020 [US3] Extend `SequenceRunnerLoopTests.cs` with repeat-until tests:
  - Exit condition true after iteration 1 → body runs exactly once
  - Exit condition true after iteration 3 → body runs exactly 3 times
  - Exit condition never true, limit = 3 → fails after 3 iterations
  - Condition eval error after first body execution → loop fails, command stops
- [X] T021 [US3] Implement repeat-until loop execution branch in `SequenceRunner.ExecuteLoopStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs`:
  - Execute body first; then evaluate exit condition; true → exit (`success`)
  - Condition eval error → fail loop step
  - Safety limit check after body; if exceeded → fail with `limitReached`

---

## Phase 7: User Story 4 — Break Step (P2)

**Story goal**: Author can place a break step (conditional or unconditional) inside a loop body; it exits the loop immediately on the iteration where its condition first becomes true; unconditional break exits on first reach.

**Independent test criteria**: Count loop N=10, break step with condition true on iteration 3 → 3 iterations total. Break condition never met → loop runs all 10. Unconditional break → exactly 1 iteration.

- [X] T022 [US4] Extend `SequenceRunnerLoopTests.cs` with break-step tests:
  - Conditional break triggers on iteration 3 of N=10 count loop → 3 iterations, `BreakTriggered=true` in log
  - Conditional break never triggered → loop runs to completion normally
  - Unconditional break (null condition) → loop exits after first iteration
  - Break step condition eval error → loop fails immediately
- [X] T023 [US4] Implement break step execution in `SequenceRunner` in `src/GameBot.Domain/Services/SequenceRunner.cs`:
  - In the body-step dispatch loop, detect `StepType == Break`
  - If `BreakCondition` is null → signal break immediately
  - If `BreakCondition` is set → evaluate; true → signal break; false → continue to next body step; error → fail loop
  - Break signal propagates out of body dispatch and exits the loop cleanly (not a failure)
  - Record `BreakTriggered = true` in `LoopIterationOutcome` for the iteration where break fires

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validation endpoint wiring, error messages, and integration validation.

- [X] T024 [P] Wire extended validator into `POST /api/sequences/{sequenceId}/validate` endpoint in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`: ensure loop-specific rules from T007 are invoked and errors returned with step-level context
- [X] T025 [P] Write `LoopValidationTests.cs` in `tests/GameBot.Domain.Tests/` covering all rules in T007:
  - Count < 0 rejected with message
  - MaxIterations = 0 rejected
  - Loop body containing a loop step rejected
  - Break step at top level rejected
  - `{{iteration}}` in top-level step parameter rejected
  - `commandOutcome` forward-reference in loop body rejected
- [X] T026 [P] Add `loopMaxIterations` key to `data/config/config.json` default template / seeding logic so new installs start with the 1000-iteration default
- [X] T027 Run `dotnet build -c Debug` and fix any compilation errors introduced by new types before marking implementation complete; then run `dotnet test --collect:"XPlat Code Coverage" -c Debug` and confirm new-code line coverage ≥80% and branch coverage ≥70% for `SequenceRunner`, `TemplateSubstitutor`, and `SequenceValidator` touched paths (constitution requirement)
- [X] T028 [P] Record dispatch-only timing for a 10-iteration count-based loop (no real action I/O) in the PR description and confirm the SequenceRunner loop-dispatch overhead is ≤5 ms/iteration; note result inline as a micro-benchmark comment in `SequenceRunner.cs` (constitution hot-path perf note requirement)
- [X] T029 [P] Add changelog entry to `CHANGELOG.md` describing the new loop step types (count-based, while, repeat-until), break step, and `{{iteration}}` placeholder (constitution: user-visible features require changelog entry)

---

## Dependencies

```
Phase 1 (T001–T005)
  └─► Phase 2 (T006–T008)  [persistence + validation foundations]
        ├─► Phase 3 (T009–T012)  [US1: count loop — runtime]
        │     └─► Phase 4 (T013–T017)  [US5: UI visualization]
        │           └─► Phase 5 (T018–T019)  [US2: while loop]
        │                 └─► Phase 6 (T020–T021)  [US3: repeat-until]
        │                       └─► Phase 7 (T022–T023)  [US4: break step]
        └─► Phase 8 (T024–T028)  [polish — can begin after Phase 2]
```

**Note**: T010 (`TemplateSubstitutorTests`) and T014 (`BreakStepRow.tsx`) are marked `[P]` — they can run in parallel with earlier tasks in the same phase once Phase 1 is complete.

---

## Parallel Execution Opportunities

| Group | Tasks | Can run in parallel after |
|---|---|---|
| Phase 1 domain setup (parallel) | T002, T004, T005 | T001 complete |
| Phase 1 T003 | T003 | T002 complete |
| Phase 2 tests + persistence | T006, T007 run sequentially; T008 after T006 | Phase 1 complete |
| US5 UI components | T013, T014 together | Phase 2 complete |
| Polish | T024, T025, T026 | Phase 2 complete |

---

## Implementation Strategy

**MVP** (delivers verifiable value independently): Phase 1 → Phase 2 → Phase 3 → Phase 4  
This gives authors: count-based loop authoring, persistence, execution with `{{iteration}}`, per-iteration execution logs, and visual loop blocks in the UI.

**Incremental delivery order**:
1. MVP: Phases 1–4 (count loop + UI)
2. While loop: Phase 5 (adds waiting/polling pattern)
3. Repeat-until: Phase 6 (adds do-once-then-check pattern)
4. Break: Phase 7 (adds early-exit safety for all loop types)
5. Polish: Phase 8 (validation endpoint wiring + config seeding)

**Total tasks**: 29  
**Tasks per user story**: US1=4, US2=2, US3=2, US4=2, US5=5  
**Foundational tasks**: 8 (Phases 1–2)  
**Polish tasks**: 6 (Phase 8)
