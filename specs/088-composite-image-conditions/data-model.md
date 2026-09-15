# Phase 1 Data Model: Composite Image Conditions

**Feature**: 088-composite-image-conditions | **Date**: 2026-09-15

## The condition hierarchy

`SequenceStepCondition` is an abstract, JSON-polymorphic type discriminated on `type`. It gains one new derived form. The two existing forms are unchanged in every respect — fields, defaults, serialization, validation and evaluation.

```text
SequenceStepCondition (abstract)
├── Negate : bool                       (existing, applies to every form)
│
├── ImageVisibleStepCondition           type = "imageVisible"   [UNCHANGED]
│   ├── ImageId : string
│   └── MinSimilarity : double?
│
├── CommandOutcomeStepCondition         type = "commandOutcome" [UNCHANGED]
│   ├── StepRef : string
│   └── ExpectedState : string
│
└── CompositeStepCondition (abstract)                           [NEW]
    ├── Children : IReadOnlyList<SequenceStepCondition>
    ├── Rule : CompositeConditionRule  (abstract, per subclass)
    │
    ├── AllStepCondition             type = "all"
    ├── AnyStepCondition             type = "any"
    └── NoneStepCondition            type = "none"
```

### `CompositeStepCondition` and its three sealed forms

| Field | Type | Rules |
|-------|------|-------|
| `Rule` | `CompositeConditionRule` enum (`All`, `Any`, `None`) | Abstract on the base, fixed by each sealed subclass. Not serialized — it is exactly what the `type` discriminator already encodes — so an unknown or absent rule is unrepresentable. |
| `Children` | ordered list of `SequenceStepCondition` | 1–16 entries. Order is the author's and is the evaluation order. A child may be any condition form, including another composite. |
| `Negate` | `bool` (inherited) | Applied to the composite's combined result, after the rule. |

**Why three CLR types rather than one with a rule field.** `System.Text.Json` builds a type → discriminator map for *writing*, so it needs a distinct CLR type per discriminator; registering one type under three discriminators is not a supported write configuration. Three sealed subclasses over a shared abstract base give the serializer what it needs while keeping all shared behaviour — the children list, its validation, its evaluation walk — in one place on the base. The alternative, a single `composite` discriminator with a `rule` string beside it, was rejected in [research.md](./research.md) D-001 for putting the semantics below the discriminator where neither the serializer nor the type system can enforce it.

Following the pattern the existing leaves use, `Type` is an overridden read-only property marked `[JsonIgnore]`, because the `[JsonPolymorphic]` discriminator already writes that property name. Omitting the attribute produces a duplicate `type` key that fails to round-trip — the existing file carries a comment explaining exactly this, and the new types must honour it.

```csharp
[JsonDerivedType(typeof(AllStepCondition), typeDiscriminator: "all")]
[JsonDerivedType(typeof(AnyStepCondition), typeDiscriminator: "any")]
[JsonDerivedType(typeof(NoneStepCondition), typeDiscriminator: "none")]
```

The service-side contract mirrors this exactly: an abstract `CompositeConditionContract` with sealed `AllConditionContract`, `AnyConditionContract` and `NoneConditionContract`.

## Evaluation semantics

Given a resolver that evaluates a **leaf** to a bool:

| Rule | Result | Short-circuits at |
|------|--------|-------------------|
| `all` | true when every child is true | the first **false** child |
| `any` | true when at least one child is true | the first **true** child |
| `none` | true when no child is true | the first **true** child |

Then `Negate` inverts that result.

Children are evaluated **in author order**, sequentially (spec A-004), against **one screen observation** shared by the whole guard evaluation (FR-017). A composite with one child evaluates as that child, then the rule (identity for `all`/`any`, inversion for `none`), then `Negate`.

Failure of a child is **not** the same as a false child. A leaf that cannot be evaluated at all — a missing image evaluator, an unresolvable `commandOutcome` reference, a thrown matcher — propagates as a failure exactly as it does today for a single condition, aborting rather than being treated as false. Short-circuiting therefore also determines whether a later child's failure is ever reached, which matches how a reader expects `all`/`any` to behave.

## Validation rules

Applied by the new validator over every condition position, reporting `$`-rooted paths that match the convention `ConditionExpression.Validate()` already uses.

| Rule | Error |
|------|-------|
| Children must not be empty | `Step '<label>' condition at <path>: '<rule>' requires at least one child.` |
| At most 16 children | `Step '<label>' condition at <path>: '<rule>' allows at most 16 children (found <n>).` |
| Nesting depth at most 4 | `Step '<label>' condition at <path>: condition nesting exceeds the maximum depth of 4.` |
| `imageVisible` child needs an image id | `Step '<label>' condition at <path>: imageVisible condition requires imageId.` |
| `imageVisible` similarity in 0..1 | `Step '<label>' condition at <path>: imageVisible minSimilarity must be within 0..1.` |
| `commandOutcome` child needs a step ref | `Step '<label>' condition at <path>: commandOutcome condition requires stepRef.` |
| `commandOutcome` expected state is known | `Step '<label>' condition at <path>: commandOutcome expectedState must be one of success\|failed\|skipped\|break\|no_break.` |

Depth counts condition levels, not JSON object levels: a leaf at the top is depth 1, a composite of leaves is depth 2, and the deepest legal shape is a composite of composites of composites of leaves.

`commandOutcome` children inside a composite inherit the existing "must reference a prior step" rule, resolved the same way as a top-level `commandOutcome` condition — against every step reachable from the sequence root, in authored order (feature 081 behaviour, unchanged).

## Where a condition appears

Five positions, all of which accept a composite. Each is a site the implementation must cover:

| Position | Contract member | Domain member |
|----------|-----------------|---------------|
| Step guard | `SequenceStepContract.Condition` | `SequenceStep.Condition` |
| Loop break | `SequenceStepContract.BreakCondition` | `SequenceStep.BreakCondition` |
| Branch test | `IfConfigContract.Condition` | `IfConfig.Condition` |
| While loop | `WhileLoopConfigContract.Condition` | while loop config |
| Repeat-until loop | `RepeatUntilLoopConfigContract.Condition` | repeat-until loop config |

## Consumer sites that enumerate condition forms

Each of these currently switches over exactly the two leaf kinds and must gain a composite arm. The task list names them individually so none is missed — this repository has a recorded history of a new polymorphic kind being added to the two obvious sites and silently skipped at the rest.

| # | Site | What it does | Composite behaviour |
|---|------|--------------|---------------------|
| 1 | `SequencesEndpoints.MapPerStepConditionToDto` (~:541) | domain → response JSON | emit `type` = rule, plus mapped `children` |
| 2 | `SequencesEndpoints.MapPerStepCondition` (~:1203) | request contract → domain | build `CompositeStepCondition`, recursing |
| 3 | `SequenceStepValidationService.ValidateStepCondition` (~:305), `ValidateIfCondition` (~:193), the break sites (~:151, ~:246), **and the while / repeat-until loop conditions, which the service does not inspect at all today** | save-time 400s | delegate to the composite validator; the two loop positions are scoped to composites only, so no previously-valid sequence changes status |
| 4 | `FileSequenceRepository` (~:147) | persistence backstop | recursive leaf checks through composites |
| 5 | `SequenceRunner.ExecuteStepAsync` (~:510) and `EvaluateLoopConditionAsync` (~:1481) | runtime evaluation | delegate to the new evaluator |
| 6 | `SequenceRunner.DescribeBreakCondition` (~:1502) | break-reason text | render the rule and its children |
| 7 | `SequencesEndpoints.ValidatePerStepImageReferencesAsync` (~:1291) | dangling image refs | recurse into composites **in the positions already walked** (research D-006) |
| 8 | `ConditionalFlowSchemaDocumentFilter` | OpenAPI | register and alias the new schema |

## Persistence

No migration and no version field. A stored sequence written before this change contains only `imageVisible` and `commandOutcome` conditions, which deserialize into the same CLR types as before and re-serialize identically. A composite is simply a shape that older documents never contain. An older build reading a *newer* document would fail to deserialize the unknown discriminator — the same forward-compatibility posture every previous polymorphic addition in this codebase has taken, and not a downgrade path this project supports.
