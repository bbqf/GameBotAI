# Phase 0 Research: Composite Image Conditions

**Feature**: 088-composite-image-conditions | **Date**: 2026-09-15

The clarification session closed every `NEEDS CLARIFICATION` marker, so Phase 0 is not a hunt for missing facts. It records the design decisions taken against the existing code, and the alternatives rejected.

---

## D-001: Represent a composite as a third derived condition type

**Decision**: Add `CompositeStepCondition : SequenceStepCondition` (domain) and `CompositeConditionContract : SequenceStepConditionContract` (service), each registered with `[JsonDerivedType]` under the discriminators `all`, `any` and `none`.

**Rationale**: The condition model is already a `[JsonPolymorphic]` hierarchy with a `type` discriminator. A third derived type is the shape the model was built for. Crucially it leaves `ImageVisibleStepCondition` and `CommandOutcomeStepCondition` byte-identical in both their C# form and their serialized form, which is what makes FR-015 (existing sequences unchanged) structurally true rather than something to be verified case by case.

**Alternatives considered**:

- *Add a `Children` list and a `Combinator` to the abstract base.* Rejected: every leaf would gain two meaningless members, every stored leaf's JSON would gain fields or need suppression, and every one of the six consumer sites would have to check "is this leaf actually acting as a composite?". It converts a clean sum type into a muddled product type.
- *A single `composite` discriminator with a separate `rule` field* (`{"type":"composite","rule":"all",...}`). Rejected: it puts the thing that changes evaluation semantics one level below the discriminator, so `System.Text.Json` cannot dispatch on it and the validator has to re-check a string that the type system could have guaranteed. Three discriminators cost nothing extra and make an invalid rule unrepresentable.
- *An expression string* (`"A and not B"`). Rejected: it needs a parser, a grammar, an error-position story and an escaping story for image ids, and it is strictly harder for the web-ui to build or edit later.

---

## D-002: Do not reuse or extend the existing `ConditionExpression` tree

**Decision**: Leave `GameBot.Domain.Commands.ConditionExpression` (with `ConditionNodeType.And/Or/Not/Operand`) entirely alone. Build the composite on `SequenceStepCondition`.

**Rationale**: The repository already contains a full boolean tree with short-circuit evaluation and a `ConditionEvaluationTrace`. It is tempting to reuse. But it belongs to a **different step model**: `FlowStepType.Condition` on the block-style flow (`SequenceStep.FlowSteps`, `ConditionExpressionDto`, `MapCondition`), not to the per-step / loop / branch guards that `SequenceStepCondition` serves. The two differ in serialization (`nodeType` vs `type`), in operand shape (`ConditionOperand.TargetRef` + `ExpectedState` + `Threshold` vs distinct leaf types), and in validation rules — `ConditionExpression.Validate()` requires **at least two children** for `And`/`Or`, which contradicts the clarified decision to accept a single child.

Unifying them would change validation behaviour for flow-condition steps that exist today. Spec A-008 rules that out explicitly.

**Alternatives considered**:

- *Convert `SequenceStepCondition` guards into `ConditionExpression` at the boundary.* Rejected: it makes every guard's stored form change, breaking FR-015 outright, and it inherits the ≥2-children rule.
- *Extract a shared generic tree both models use.* Rejected as premature: the shared surface is "a list of children and a combining rule", roughly fifteen lines, and the two models disagree on validation. A shared abstraction that both sides must then override is worse than two small independent walks. Converging them remains possible later and is noted in the spec as deliberately deferred.

---

## D-003: `all` / `any` / `none` rather than `and` / `or` / `not`

**Decision**: Three discriminators, `all`, `any`, `none`.

**Rationale**: Settled in clarification. These read correctly for a rule owning an ordered *list*; `none` expresses "…and not that other dialog" directly, which is the exact idiom B-011 needs. Using `and`/`or`/`not` here would also collide conceptually with `ConditionExpression`'s identically-named node types while behaving differently (D-002), which is precisely the confusion to avoid.

**Alternatives considered**: `and`/`or`/`not` (naming collision with a different model); `allOf`/`anyOf`/`noneOf` (JSON-Schema flavoured, but this is not a schema-composition keyword and the resemblance would mislead).

---

## D-004: A single new evaluator class, reached from both existing call sites

**Decision**: Add `SequenceStepConditionEvaluator` in `GameBot.Domain/Services`. Move the existing leaf evaluation logic into it and have both `SequenceRunner` sites call it:

- `ExecuteStepAsync` (around `SequenceRunner.cs:510`), which evaluates `step.Condition`;
- `EvaluateLoopConditionAsync` (around `SequenceRunner.cs:1481`), which evaluates while / repeat-until / break conditions.

**Rationale**: Three forces point the same way.

1. *Semantics must not drift.* The two sites already implement leaf evaluation separately — one returns a bool and records a step result, the other throws. If composites were added twice, `all`/`any`/`none` could subtly diverge between a step guard and a loop guard. One evaluator makes that impossible.
2. *File size.* `SequenceRunner.cs` is 2340 lines. This repository has a recorded history of build-time taint analyzers degrading badly on very large methods, and `TreatWarningsAsErrors=true` turns that into a hard build failure rather than a slow build. Adding a recursive method to the existing giant is the risky option.
3. *Testability.* A standalone evaluator is unit-testable over a truth table with a stub leaf resolver, with no sequence, no session and no emulator — which is what the ≥80% coverage bar on touched code needs.

The evaluator takes two delegates that already exist at both call sites: the image evaluator `Func<Condition, CancellationToken, Task<bool>>` and the `stepOutcomes` dictionary. No new dependency, no DI change.

**Alternatives considered**: a recursive local function at each site (duplicates semantics — rejected); a visitor over the hierarchy (ceremony for three cases — rejected).

---

## D-005: Validate in the validation service; guard in the repository

**Decision**: All composite rules (empty children, depth > 4, children > 16, invalid child, unknown shape) are enforced in `SequenceStepValidationService`, which feeds the save path's 400 response. `FileSequenceRepository` gains a matching *recursive* guard alongside its existing per-leaf checks, but as a backstop only.

**Rationale**: `FileSequenceRepository` throws `InvalidOperationException` for a bad condition (`FileSequenceRepository.cs:147-160`). Anything that reaches it unvalidated becomes a 500. FR-010 requires 400. So the validation service must be the real gate. The repository guard still has to learn about composites, though: without it, a composite carrying an empty `imageId` would slip past the layer that currently catches exactly that for leaves, quietly weakening an existing invariant. Keeping both, with the validator first, preserves the existing division of labour instead of redrawing it.

**Alternatives considered**: repository-only (produces 500s — violates FR-010); validator-only (silently drops an existing defence-in-depth check).

---

## D-006: Do not widen image-reference validation coverage

**Decision**: `ValidatePerStepImageReferencesAsync` (`SequencesEndpoints.cs:1291`) today walks only top-level `steps` and only `step.Condition`. It will recurse **into composites found in that same position**, and nowhere else. Break conditions, `if` conditions, loop conditions and nested bodies keep exactly the coverage they have today.

**Rationale**: This is the one place the obvious "while we're here" improvement is the wrong call. Existing stored sequences have never had their break-condition or loop-condition image references checked. Widening the walk would make previously-valid sequences fail to save — a behaviour change for live scheduled automation, squarely against FR-015, arriving as a surprise in an unrelated feature. The narrow change is strictly additive: a composite in a position that is checked today gets its children checked; a position that is unchecked today stays unchecked.

The gap is real and worth closing, but it is its own change with its own blast radius. Recorded here so the next reader sees a decision rather than an oversight.

**Alternatives considered**: full traversal of every condition position (correct in isolation, breaks FR-015 — rejected); traversal with warnings instead of errors (invents a severity level the save path does not have — rejected).

---

## D-007: Depth 4 / 16 children, enforced by a counting walk

**Decision**: Enforce the clarified limits in a single recursive validation pass that carries the current depth, reporting `$` -rooted paths such as `$.children[2].children[0]`.

**Rationale**: The path convention already exists in this codebase — `ConditionExpression.Validate()` builds exactly this (`$.children[0]`). Matching it means an error message from the new validator reads like one from the old one, which is Principle III. Depth is checked on the way down so an over-deep payload is rejected before the walk itself recurses far, which is the point of having the limit at all.

**Alternatives considered**: enforcing the bound in the JSON reader via `MaxDepth` (the serializer's depth counts every object nesting level, not condition levels, so the number would be meaningless to an author — rejected); no limit (unbounded recursion on hostile input — rejected).

---

## D-008: Report the deciding child through the existing execution record

**Decision**: `conditionType` carries the composite's rule (`all` / `any` / `none`); the message names the child that settled the outcome, in the same `$.children[i]` path form plus a short description of that child. `DescribeBreakCondition` (`SequenceRunner.cs:1502`) gains a composite arm producing e.g. `all(imageVisible(imageId=pns-disconnect-confirm, ...), NOT imageVisible(imageId=pns-gas-title, ...))`.

**Rationale**: Settled in clarification. The record already carries `conditionType`, `conditionResult` and `message`, and consumers read that shape; adding a field would be a contract change for a diagnostic. Naming the deciding child is what turns "the guard was false" into "the guard was false because the gas dialog was up" — the difference between a log line and a diagnosis.

**Alternatives considered**: a per-child trace structure like `ConditionEvaluationTrace` (a new response entity for a guard that has at most 16 children — rejected as disproportionate, and it would need its own OpenAPI surface).

---

## Summary of resolved unknowns

| Unknown | Resolution | Source |
|---------|-----------|--------|
| Combining-rule vocabulary | `all` / `any` / `none` | Clarification Q1, D-003 |
| Depth and width limits | 4 deep, 16 wide | Clarification Q2, D-007 |
| Single-child composite | Valid; only empty rejected | Clarification Q3 |
| Screen consistency across children | One observation per guard evaluation | Clarification Q4, FR-017 |
| Skip reporting | Existing record; rule in `conditionType`, deciding child in message | Clarification Q5, D-008 |
| Relationship to `ConditionExpression` | Untouched, not merged | Spec A-008, D-002 |
| Scope of image-reference validation | Unchanged positions; recurse into composites only | D-006 |
