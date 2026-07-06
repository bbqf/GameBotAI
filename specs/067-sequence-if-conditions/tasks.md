# Tasks: If-Then-Else Conditions in Sequences

**Input**: Design documents from `/specs/067-sequence-if-conditions/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sequences-api.md, quickstart.md

**Tests**: Included — the constitution's Testing Standards are non-negotiable for executable logic.

**Organization**: Tasks are grouped by user story. US1 (runtime) is the MVP; US3/US4 deliver the authoring UI.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = then-branch execution, US2 = else branch, US3 = loop-parity authoring UI, US4 = "Loops and Conditions" grouping

## Phase 1: Setup

*No setup tasks — existing projects, no new dependencies or tooling.*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `If` step exists in the domain model, API contracts, and web-ui types so every story can build on it.

- [ ] T001 Add `If` member to `SequenceStepType`, new `IfConfig` class (required `SequenceStepCondition Condition`, XML docs mirroring `LoopConfig`) in new file `src/GameBot.Domain/Commands/IfConfig.cs`, and add `IfConfig? If` + `IReadOnlyList<SequenceStep>? ElseBody` properties (XML docs: `Body` doubles as the then branch for if steps; `null` ElseBody = else absent) to `src/GameBot.Domain/Commands/SequenceStep.cs`
- [ ] T002 Add `IfConfigContract` record and `If` + `ElseBody` properties to `SequenceStepContract` in `src/GameBot.Service/Models/SequenceStepContracts.cs`
- [ ] T003 Wire contract parsing/mapping in `src/GameBot.Service/Program.cs`: `ParseStepType` `"if"` case; `TryReadPerStepRequest` step-shape guard treats `"if"` like loop/break (no `primitiveAction` required); `MapToLinearSteps` if-step case (condition via `MapPerStepCondition`, then branch via `MapBodySteps(step.Body)`, else via `MapBodySteps(step.ElseBody)` preserving null); `MapBodySteps` gains an `If` child case (loop bodies may contain ifs); `MapStepToDto` emits `@if` + `elseBody`; `FlattenSequenceSteps` traverses `ElseBody`
- [ ] T004 [P] Add web-ui types: `IfStepEntry` to the `StepEntry` union in `src/web-ui/src/types/stepEntry.ts`; `IfConfigDto`, `stepType: 'If'`, `if`, `elseBody` on `SequenceLinearStep` in `src/web-ui/src/types/sequenceFlow.ts`; `'if'` in `ExecutionTreeNodeKind` in `src/web-ui/src/services/executionLogsApi.ts`

**Checkpoint**: `dotnet build` green; `vite build` green. No behaviour change yet.

---

## Phase 3: User Story 1 — Author and Run an If Block with a Then Branch (P1) 🎯 MVP

**Goal**: An if step with a condition and then branch executes conditionally (branch on true, skip on false), fails like a while loop on condition error, and works inside loop bodies (break propagation, `{{iteration}}`); validation and execution logs are in place.

**Independent Test**: Upsert a sequence with an if block (imageVisible condition, two then steps) via the API, execute with the image visible and then absent, verify branch steps run/skip and the execution log names the branch taken.

- [ ] T005 [US1] Implement `ExecuteIfStepAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs`: stepKey `StepId` or `if@{Order}`; evaluate condition once via `EvaluateLoopConditionAsync` (catch → record Failed StepResult with `conditionResult: "error"`, `result.Fail`, outcome `failed`, return earlyStop); record the if StepResult (conditionType, conditionResult `true|false`, actionOutcome `then|else|none`, message naming the branch) *before* branch execution; run the selected branch (`Body` on true, `ElseBody` on false) via `ExecuteLoopBodyAsync` passing the current iteration context; set `stepOutcomes[stepKey]` = `success` (branch steps ran) / `skipped` (no branch steps) / `failed`; return `(EarlyStop, BreakTriggered)`
- [ ] T006 [US1] Dispatch if steps in `src/GameBot.Domain/Services/SequenceRunner.cs`: `ExecuteSingleStepAsync` handles `StepType == If` (empty iteration context, break impossible); `ExecuteLoopBodyAsync` handles `If` children directly (like `Break`) so a `BreakTriggered` from inside a branch exits the enclosing loop and `{{iteration}}` context flows into branch steps
- [ ] T007 [US1] Extend `src/GameBot.Domain/Services/SequenceStepValidationService.cs`: accept `If` at top level via new `ValidateIfStep(step, label, siblings, index, errors, insideLoop)` — requires `If` config + condition (`imageVisible` needs imageId; `commandOutcome` needs stepRef + allowed expectedState); validates both branches with loop-body rules (non-empty unique stepIds per branch, no `Loop` children, no `If` children, `Break` allowed only when `insideLoop`, action payload required, commandOutcome prior-sibling scoping within the branch); `ValidateLoopStep` body scan accepts `If` children via `ValidateIfStep(insideLoop: true)`
- [ ] T008 [P] [US1] Add `tests/unit/Sequences/SequenceRunnerIfTests.cs`: condition true → then steps run in order, if-step outcome `success`; condition false (no else) → branch skipped, outcome `skipped`, execution continues; condition evaluated exactly once per encounter; condition error → if step + sequence fail (while-loop parity); negate honored; if inside while/count loop → re-evaluated per iteration, `{{iteration}}` substituted in branch steps, break step inside branch exits the enclosing loop
- [ ] T009 [P] [US1] Add if-validation tests (new `tests/unit/Sequences/IfValidationTests.cs`, modeled on `LoopValidationTests.cs`): missing if config/condition rejected; loop/if children in branches rejected; break in branch of a top-level if rejected but accepted when the if is inside a loop body; duplicate branch stepIds rejected; empty both branches accepted; commandOutcome forward-reference within branch rejected; imageVisible condition without imageId rejected
- [ ] T010 [US1] Execution-log surfacing: `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` emits an if detail item (`stepType: "if"`, status, actionOutcome then/else/none, message, sequence/step ids like the loop item at line ~199); `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` `MapStepKind` maps `"if" → "if"`
- [ ] T011 [US1] Contract round-trip coverage (extend the existing sequences contract/integration test area, e.g. new `tests/contract/Sequences/IfStepContractTests.cs`): upsert if step → GET returns same `if`/`body`/`elseBody` (null preserved); legacy payload without `if` still parses with steps defaulting as before; validation errors surface as 400 with messages from contracts/sequences-api.md

**Checkpoint**: US1 acceptance scenarios pass via API; MVP delivered.

---

## Phase 4: User Story 2 — Add an Optional Else Branch (P2)

**Goal**: Else branch executes on false; empty/absent branches are no-ops.

**Independent Test**: Sequence with both branches populated — condition true runs only then; condition false runs only else; both-empty block is a saved, succeeding no-op.

- [ ] T012 [P] [US2] Extend `tests/unit/Sequences/SequenceRunnerIfTests.cs`: condition false + else present → only else steps run (outcome `success`); condition true + populated else → else untouched; else-only block (empty then) with condition true → no-op `skipped`; both branches empty → no-op `skipped`, sequence continues; else branch failure fails the sequence; break inside else branch (if inside loop) exits the loop
- [ ] T013 [P] [US2] Extend contract tests: `elseBody: []` round-trips distinct from absent/null `elseBody` (T011 file)

**Checkpoint**: Full if-then-else runtime semantics proven.

---

## Phase 5: User Story 3 — Author If Blocks Like Loop Blocks (P2)

**Goal**: The sequence editor creates/edits/reorders/deletes if blocks with the same UX as loop blocks, sharing the while-loop condition editor; else area appears via "Add else".

**Independent Test**: In the editor add an if block, configure an imageVisible condition with the same controls as a While loop, add then steps, click "Add else" and add else steps, save, reload — everything round-trips; an If button inside loop bodies nests a conditional.

- [ ] T014 [P] [US3] Extract the condition editor (NOT toggle, type select, imageVisible/commandOutcome fields) from `src/web-ui/src/components/sequences/LoopBlockHeader.tsx` into new shared `src/web-ui/src/components/sequences/ConditionFields.tsx` (keep existing `loop-condition-*` testids working via a testid-prefix prop); refactor `LoopBlockHeader` to use it
- [ ] T015 [US3] Create `src/web-ui/src/components/sequences/IfBlockHeader.tsx` ("If" badge + `ConditionFields`, no Max field) and `src/web-ui/src/components/sequences/IfBlock.tsx` mirroring `LoopBlock.tsx`: bordered block, header row with Remove If; then area always visible (add step, sortable list, empty state); "Add else" button reveals an else area with its own add/sort/empty state and a "Remove else" affordance (confirm only when else contains steps); no loop/if add buttons inside branches
- [ ] T016 [US3] Wire `src/web-ui/src/pages/SequencesPage.tsx`: `createIfStep()` (default imageVisible condition like `createLoopStep`, empty then, no else); DTO mapping both directions (`ifDtoToEntry`/`linearBodyToStepEntries` accepting if children for loop bodies, `buildIfConfigPayload`/`toLinearPayloadSteps`/`bodyEntryToPayloadStep` emitting `if`/`body`/`elseBody` with null-vs-empty else preserved); render `IfBlock` for top-level if steps with step-list chrome (label, drag handle) matching loop steps; DnD scope ids per branch (then/else) so cross-branch reordering follows the same rules as loop bodies
- [ ] T017 [US3] Add nested conditional support in `src/web-ui/src/components/sequences/LoopBlock.tsx`: render `IfBlock` for if entries in the loop body and add an "If" add-button next to the body's existing add buttons (break stays loop-only; branches of a nested if may include Break per validation)
- [ ] T018 [P] [US3] Add `src/web-ui/src/components/sequences/__tests__/IfBlock.test.tsx` (model on `LoopBlock.test.tsx`): renders condition editor identical to while (shared ConditionFields testids); then steps add/remove/reorder; Add else reveals else area, Remove else clears it; no loop/if buttons inside branches
- [ ] T019 [US3] Extend `src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx`: if step save payload shape (`stepType: 'If'`, `if.condition`, `body`, `elseBody` null vs array); load round-trip renders IfBlock with branches; nested if inside loop body round-trips

**Checkpoint**: Full authoring parity; feature usable end-to-end from the UI.

---

## Phase 6: User Story 4 — Discover If Blocks Under "Loops and Conditions" (P3)

**Goal**: The add-step column is labelled "Loops and Conditions" with the If button after the loop buttons.

**Independent Test**: Open create and edit forms; the middle add-step column reads "Loops and Conditions" and lists Count, While, Repeat‑Until, If (in that order); clicking If appends a default if block.

- [ ] T020 [US4] In `src/web-ui/src/pages/SequencesPage.tsx` rename the add-step column label `Loop` → `Loops and Conditions` and add an `If` button after `Repeat‑Until` (calling `createIfStep`) in **both** the create form (~line 1492) and the edit form (~line 1716)
- [ ] T021 [P] [US4] Assert label text and button order (Count, While, Repeat‑Until, If) and that If appends a default block, in `src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx`

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T022 [P] Render `'if'` tree nodes in the execution-logs UI: check nodeKind-keyed maps (icons/labels/styles) in `src/web-ui/src/pages/ExecutionLogs*.tsx` and related components; give `if` a sensible presentation (fallback to step styling is acceptable, label must show the branch message)
- [ ] T023 [P] Update `docs/architecture.md` (domain model: If step type/branches; API surface: if/elseBody step schema; execution-log node kinds) and refresh its "Last reviewed" date
- [ ] T024 Set `**Status**: Implemented` in `specs/067-sequence-if-conditions/spec.md` and add the 067 row to `specs/STATUS.md`
- [ ] T025 Run the full quality gate and fix regressions: `dotnet build c:\src\GameBot\GameBot.sln`, `dotnet test c:\src\GameBot\tests\unit` (plus contract/integration suites touched), `npm --prefix c:\src\GameBot\src\web-ui run build`, `npm --prefix c:\src\GameBot\src\web-ui test`

---

## Dependencies

```
Phase 2 (T001→T002→T003; T004 parallel to T002/T003)
  └─► US1 (T005→T006; T007 after T001; T008/T009 after T005–T007; T010 after T005; T011 after T003+T007)
        └─► US2 (T012/T013 — runtime already in place from T005)
        └─► US3 (T014→T015→T016→T017; T018 after T015; T019 after T016/T017) — needs T004, benefits from US1 backend for e2e
              └─► US4 (T020→T021 — trivial once IfBlock exists)
  └─► Polish (T022 after T004/T010; T023/T024 anytime after US1; T025 last)
```

- US2 depends only on US1 (runtime semantics live in the same runner method).
- US3 depends on Foundational T004 (types); saving against a real backend needs US1's T003/T007.
- US4 depends on US3's `createIfStep`/IfBlock.

## Parallel Execution Examples

- After T001: T002+T003 (service) ∥ T004 (web-ui types) ∥ T007 (validation).
- After T005–T007: T008 ∥ T009 ∥ T010 ∥ T011.
- US2: T012 ∥ T013.
- US3: T014 ∥ (T018 once T015 lands); T019 after T016/T017.
- Polish: T022 ∥ T023 ∥ T024, then T025.

## Implementation Strategy

**MVP = Phase 2 + Phase 3 (US1)**: if blocks fully functional via API with then-branch semantics, validation, and logs. US2 locks in else semantics with tests. US3 delivers the authoring UI (the biggest slice). US4 is a small labelling task. Each checkpoint leaves the branch green (`dotnet build`/`test`, `vite build`, `jest`) per the constitution's release-blocker rule.
