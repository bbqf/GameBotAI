# Research: Command Loop Structures

**Feature**: 033-command-loops  
**Date**: 2026-03-31

---

## R-001: Where Loops Belong — Command vs. Sequence

**Question**: Should loop steps be added to `CommandStep` (inside `Command`) or `SequenceStep` (inside `Sequence`)?

**Decision**: Loops extend `SequenceStep` / `Sequence` only. `Command` remains the atomic unit (simple ordered action dispatch); `Sequence` is the composable, condition-aware orchestration layer.

**Rationale**:
- `SequenceStep` already carries conditions, retry, delay, gate, and the per-step condition polymorphism (feature 032). Adding loop control here is a natural continuation.
- `CommandExecutor` is intentionally simple (send inputs, tap, recurse into sub-commands). Introducing loop state management there would inflate its responsibilities.
- All current user-facing compositional features (conditional steps, per-step conditions) live in sequences. End-users already understand sequences as the authoring unit.

**Alternatives considered**:
- Add loops to `CommandStep` too — rejected; `Command` is not the composable authoring surface.
- Introduce a new entity type (`LoopCommand`) — rejected; unnecessary complexity; nesting via `SequenceStep.Body` is sufficient.

---

## R-002: Loop Representation — Flat Enum vs. Polymorphic Sub-Type

**Question**: Should each loop type be its own `SequenceStepType` value, or should `SequenceStepType.Loop` be a single discriminant with a polymorphic `LoopConfig`?

**Decision**: Single `SequenceStepType.Loop` with a JSON-polymorphic `LoopConfig` property carrying a `loopType` discriminator (`count` | `while` | `repeatUntil`).

**Rationale**:
- Mirrors the existing pattern: `SequenceStepCondition` is already `[JsonPolymorphic]` on `type`. Reusing the same pattern keeps the domain model consistent.
- Three loop types sharing a single enum slot prevents enum proliferation.
- All loop shapes share `Body: Collection<SequenceStep>`. Keeping them under one discriminant makes the runner dispatch clean: `if (step.StepType == Loop) ExecuteLoop(step.Loop, step.Body)`.

**Alternatives considered**:
- Separate enum values `LoopCount`, `LoopWhile`, `LoopRepeatUntil` — rejected; requires three dispatch branches and duplicates `Body` handling.

---

## R-003: Break Step Representation

**Question**: How should a break step be represented — as a `SequenceStepType.Break` with an optional condition, or inline in the loop config?

**Decision**: `SequenceStepType.Break` with an optional `BreakCondition: SequenceStepCondition?` property. Unconditional break when `BreakCondition` is null.

**Rationale**:
- Break is a step that sits in the loop body alongside action/conditional/loop-header steps — it belongs at the step level.
- Reusing `SequenceStepCondition` (already polymorphic with `imageVisible`/`commandOutcome`) avoids a parallel condition hierarchy.
- The runner evaluates break steps in the same loop-body pass — it sees `StepType == Break`, evaluates condition (or skips evaluation for unconditional), and signals a break.

---

## R-004: `{{iteration}}` Template Substitution Mechanism

**Question**: No variable substitution exists in the codebase today. What is the minimal, correct design?

**Decision**: Introduce a static `TemplateSubstitutor` utility in `GameBot.Domain` that replaces `{{key}}` placeholders in string parameter values using a Regex. Applied only to `SequenceActionPayload` parameter values at execution time inside a loop body. The `iteration` dictionary key carries the current 1-based index.

**Rationale**:
- `SequenceActionPayload.Parameters` is typed as `Dictionary<string, JsonElement>` or similar. The substitutor operates on string-typed values only; non-string (numeric, bool) values are left untouched.
- Regex pattern `\{\{(\w+)\}\}` is simple, well-understood, and consistent with many templating tools.
- Scoped only to loop body execution — the runner wraps `ExecuteBodyStep` calls with a `contextVars` dict injected by the loop.
- Save-time validation: scan all step parameter values in a loop body for `{{...}}` patterns and reject if found outside a loop body.

**Alternatives considered**:
- Full expression evaluator — rejected; overkill for v1 (no arithmetic, only index substitution needed).
- Dedicated `IterationIndexStep` parameter type — rejected; too narrow; the template approach generalises to future variables without schema changes.

---

## R-005: Loop Execution Safety — Where the Limit Lives

**Question**: Should the safety iteration limit (1000) be a per-loop field, a global config, or both?

**Decision**: Global config (already-existing `data/config` JSON store), overridable per-loop via an optional `MaxIterations` field on `LoopConfig`. Global default = 1000. Per-loop override replaces the global value entirely for that loop.

**Rationale**:
- The global config store already exists (`IConfigRepository`). Adding a `LoopMaxIterations` key requires no new infrastructure.
- Per-loop override handles legitimate use cases where an author knows a specific loop needs more (or fewer) iterations.
- If both are set, per-loop wins — predictable and explicit.

---

## R-006: Execution Log Shape for Loops

**Question**: How do per-iteration outcomes fit into `ExecutionStepOutcome`? Do we need a new type?

**Decision**: Extend `ExecutionStepOutcome` with an optional `LoopIterations: IReadOnlyList<LoopIterationOutcome>?` property. A new `LoopIterationOutcome` record carries `IterationIndex`, `BreakTriggered`, and `StepOutcomes: IReadOnlyList<ExecutionStepOutcome>` (recursive — the inner step results).

**Rationale**:
- The existing `ExecutionStepOutcome` is already used as the universal per-step output. Adding a nullable list preserves backward compatibility (non-loop steps leave it null).
- Recursive nesting (`StepOutcomes` within `LoopIterationOutcome`) matches the actual execution structure and enables complete audit trail.
- No new top-level log entity needed — the loop step occupies one `ExecutionStepOutcome` entry with its type-specific payload nested inside.

---

## R-007: API Surface Changes

**Question**: Do loops require new API endpoints?

**Decision**: No new endpoints. Loops are embedded in `SequenceStep.Body`. Existing sequence CRUD endpoints (`POST /api/sequences`, `PUT /api/sequences/{id}`, `PATCH /api/sequences/{id}`) accept the extended step schema. The existing `POST /api/sequences/{id}/validate` endpoint needs its validation logic extended to cover loop-specific rules.

**Rationale**:
- Sequences already carry `SequenceStep[]` in their payload. Loop steps are just additional step variants.
- Execution is triggered via the existing `POST /api/sequences/{id}/execute` endpoint.

---

## R-008: UI Rendering Strategy for Loop Blocks

**Question**: How should loop blocks be visually distinguished in the React step list?

**Decision**: Render each loop step as a visually contained card — a `LoopBlock` component with a colored left border and light background fill, a `LoopBlockHeader` showing the loop type badge and parameter summary, and an indented inner `StepList` for the body steps. Add/remove/reorder inside the body uses the same `ReorderableList` component already used for top-level steps.

**Rationale**:
- `SequencesPage.tsx` and `CommandForm.tsx` already render steps via `ReorderableList`. Wrapping loop body steps in a nested `ReorderableList` reuses the same interaction model.
- CSS-based containment (border + background) is the lightest approach — no third-party library needed.
- The `LoopBlockHeader` displaying `loopType` badge + count/condition summary satisfies FR-018 and FR-020.

**Alternatives considered**:
- Full accordion/collapsible loop body — deferred; adds interaction complexity not required by spec.
- Drag-and-drop between loop bodies and the outer list — deferred to future iteration.
