# Research: If-Then-Else Conditions in Sequences (067)

All unknowns from Technical Context resolved by reading the existing implementation. No external research required — every decision reuses an in-repo precedent.

## R1. Where the then/else branches live on the domain model

- **Decision**: Reuse `SequenceStep.Body` (existing loop-body list) for the then branch; add `IReadOnlyList<SequenceStep>? ElseBody` (null = else absent). Add `IfConfig? If { get; set; }` holding the required condition.
- **Rationale**: `MapStepToDto` ([Program.cs:1028](../../src/GameBot.Service/Program.cs)) already serializes `Body` generically for any step; loop-body editing, DnD, and runner plumbing all operate on step lists, so reusing `Body` keeps the then branch behaviourally identical to loop bodies (FR-004) with the least new surface. `ElseBody` nullable preserves the authored distinction between "no else" and "empty else" for editor round-trip (FR-007a, FR-010).
- **Alternatives considered**: (a) Dedicated `ThenBody`/`ElseBody` pair — clearer names but duplicates every place that touches `Body` (DTO mapping, flattening, enrichment) for no behavioural gain. (b) A single `Branches` dictionary — over-general for exactly two branches.

## R2. Condition representation

- **Decision**: New `IfConfig { SequenceStepCondition Condition }` class, JSON property `if` on the step contract; condition type reuses `SequenceStepCondition` (`imageVisible` / `commandOutcome` + `Negate`) unchanged.
- **Rationale**: `WhileLoopConfig.Condition` is the exact model the spec requires parity with (FR-002); a config object (rather than reusing the per-step gating `Condition` property) keeps if-block conditions distinct from the per-step skip conditions of feature 032, which have different skip-vs-branch semantics, and mirrors how `Loop` config is attached.
- **Alternatives considered**: Reusing `SequenceStep.Condition` — rejected: that property means "skip this step unless true" on action steps; overloading it for branch selection would make validation and mapping ambiguous.

## R3. Runtime semantics and reuse

- **Decision**: `ExecuteIfStepAsync` evaluates the condition exactly once via the existing `EvaluateLoopConditionAsync` ([SequenceRunner.cs:1085](../../src/GameBot.Domain/Services/SequenceRunner.cs)), then executes the selected branch via the existing `ExecuteLoopBodyAsync`. Dispatch: `ExecuteSingleStepAsync` handles top-level if steps; `ExecuteLoopBodyAsync` handles if steps inside loop bodies and propagates `BreakTriggered` from inside a branch to the enclosing loop.
- **Rationale**: `EvaluateLoopConditionAsync` gives byte-for-byte while-loop condition semantics (negation, error-throwing on missing evaluator/reference) → SC-003 parity by construction. `ExecuteLoopBodyAsync` already implements break steps, `{{iteration}}` substitution, inter-step delays, and early-stop propagation — branches get identical behaviour (FR-004) with no duplicated logic. Passing the current iteration context through means an if block inside a loop still substitutes `{{iteration}}` in its branch steps.
- **Alternatives considered**: A separate `ExecuteBranchAsync` without break handling — rejected: breaks inside an if branch inside a loop ("if error visible → break") are the natural companion use case and validation permits them there.
- **Condition-error semantics** (FR-006): identical to while — catch, `AddStep(... "Failed", conditionResult: "error")`, `result.Fail`, `stepOutcomes[stepKey] = "failed"`, stop the sequence.

## R4. Step outcome + deferred clarify item (commandOutcome referencing non-executed branch steps)

- **Decision**: if-step outcome in `stepOutcomes`: `success` when a branch executed to completion, `skipped` when no branch ran (condition false with no/empty else, or true with empty then), `failed` on condition error or branch step failure. Steps inside a branch that never ran simply never enter `stepOutcomes`; a later `commandOutcome` referencing one fails evaluation with the existing "reference unavailable" behaviour (per-step: fail the step; loop/if condition: throw → fail).
- **Rationale**: Mirrors while-loop outcomes exactly (`skipped` for zero iterations, [SequenceRunner.cs:856](../../src/GameBot.Domain/Services/SequenceRunner.cs)) and resolves the deferred clarification by reusing existing unavailable-reference semantics — no new rules for authors to learn.
- **Alternatives considered**: Treating never-run branch-step references as `skipped` — rejected: invents a third semantics diverging from today's loop-body behaviour, contradicting FR-004.

## R5. Execution-log representation (FR-011, SC-005)

- **Decision**: Record the if step as a `StepResult` *before* its branch steps: `conditionType` = condition type, `conditionResult` = `"true"|"false"|"error"`, `actionOutcome` = `"then"|"else"|"none"`, message like `If 'stepKey': condition true → then branch (2 steps)`. `SequenceExecutionService` emits a detail item with `stepType: "if"`; `ExecutionLogService.MapStepKind` maps `"if" → "if"`; web-ui `ExecutionTreeNodeKind` gains `'if'` (falls back to `"step"` rendering style).
- **Rationale**: Loops append their summary *after* iterations because the iteration count is only known at the end; an if block's branch decision is known up front, and logging it first makes the history read causally ("condition true → these steps"), satisfying SC-005 without a schema change to `StepResult`.
- **Alternatives considered**: A `LoopIterations`-style nested result — rejected: branches run at most once; the flat step stream (as loop bodies already use) plus the decision record carries the same information.

## R6. Validation rules

- **Decision**: `ValidateIfStep(step, label, siblings, index, errors, insideLoop)` mirroring `ValidateLoopStep`:
  - `If` config + condition required; `imageVisible` condition requires `imageId` (matches break-condition check at [SequenceStepValidationService.cs:104](../../src/GameBot.Domain/Services/SequenceStepValidationService.cs)); `commandOutcome` gets the same stepRef/expectedState checks as per-step conditions.
  - Branch steps: non-empty unique stepIds per branch, no `Loop` steps, no `If` steps (flat rule), `Break` allowed only when `insideLoop == true`, action payload required for action steps, same commandOutcome prior-sibling scoping as loop bodies.
  - `ValidateLoopStep`'s body scan accepts `If` children (currently only rejects `Loop`), delegating to `ValidateIfStep(insideLoop: true)`.
  - Empty/absent branches valid (clarified 2026-07-06) — no minimum-content rule, matching loop bodies.
- **Rationale**: Exactly the loop-body rule set applied to branches (FR-004/FR-004a); condition validation parity with while loops keeps SC-003.
- **Note**: `FileSequenceRepository.ValidateActionPayloads` only inspects top-level `Action` payloads and per-step conditions — if steps carry no `Action`, so no change needed there (verified; the memory note about dual allow-lists applies to new *action types*, and this feature adds none).

## R7. Web-UI structure

- **Decision**: Extract the while/repeat-until condition editor from `LoopBlockHeader` into a shared `ConditionFields` component; build `IfBlockHeader` (If badge + `ConditionFields`, no Max field) and `IfBlock` (then area always visible; else area behind "Add else" / removable, mirroring `LoopBlock`'s body list, DnD, and add-step buttons). `LoopBlock` bodies gain an "If" add button; `SequencesPage` add-step column renamed "Loops and Conditions" with the If button after Repeat‑Until. New `IfStepEntry` in the `StepEntry` union; `SequenceLinearStep` gains `if` + `elseBody`.
- **Rationale**: Sharing `ConditionFields` makes FR-008 (identical condition controls) true by construction instead of by copy; `IfBlock` mirroring `LoopBlock` satisfies the visual-parity requirement (FR-007) and reuses `SortableStepItem`/`DropIndicator` DnD plumbing with per-branch scope ids.
- **Alternatives considered**: Reusing `LoopBlock` with a pseudo loop type `'if'` — rejected: overloads `LoopStepEntry` with branch semantics, leaks "Max iterations"/count concepts into ifs, and makes the flat-nesting validation awkward.

## R8. Backward compatibility

- **Decision**: No migration. Old sequence JSON lacks `if`/`elseBody` → deserializes with `If = null`, `ElseBody = null`; `ParseStepType` continues to default unknown/absent types to `Action`. Existing loop DTO shapes unchanged.
- **Rationale**: Additive optional properties in System.Text.Json round-trip cleanly in both directions (SC-004 / FR-010).
