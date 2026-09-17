# Contract: OpenAPI descriptions on the sequence step schema

Document: `GET /swagger/v1/swagger.json`
Schemas: `components.schemas.SequenceStep` and `components.schemas.SequenceStepContract` (same schema object).

No shape change: property names, types, required lists and every endpoint request/response are unchanged. Only
`description` strings are added.

## Canonical sentences

The filter builds every description from these sentences (C# `internal const string` fields), and the contract
test asserts them verbatim, so the two cannot drift:

| Key | Sentence |
|-----|----------|
| `StepTypesRule` | `stepType is one of Action (the default when omitted: a primitiveAction or command step), Loop, If or Break.` |
| `LoopBodyRule` | `A Loop body may contain Action, If and Break steps, but not another Loop.` |
| `IfBranchRule` | `An If branch (then branch in body, else branch in elseBody) may contain only Action steps, plus Break steps when the If itself sits inside a Loop body.` |
| `NoNestedIfRule` | `An If branch must not contain another If step, or a Loop step.` |
| `BreakRule` | `A Break step is only valid inside a Loop body: directly, or in an If branch whose If sits in a Loop body; a top-level Break is rejected.` |
| `MaxDepthRule` | `The deepest permitted nesting is Loop > If > Action or Break. A sequence that breaks these rules is rejected with 400 on create, update and patch.` |

## `SequenceStep.description`

MUST be `StepTypesRule`, `LoopBodyRule`, `IfBranchRule`, `NoNestedIfRule`, `BreakRule`, `MaxDepthRule`, joined by single spaces.

## `SequenceStep.properties.body.description`

MUST be `For a Loop step this is the loop body. ` + `LoopBodyRule` + ` For an If step this is the then branch. ` + `IfBranchRule` + ` ` + `NoNestedIfRule`.

## `SequenceStep.properties.elseBody.description`

MUST be `For an If step this is the optional else branch (null or absent means no else branch). ` + `IfBranchRule` + ` ` + `NoNestedIfRule`.

## Test assertions

For both `SequenceStep` and `SequenceStepContract` component keys, the contract test asserts that the schema
description contains all six canonical sentences; that `body` contains `LoopBodyRule`, `IfBranchRule` and
`NoNestedIfRule`; and that `elseBody` contains `IfBranchRule` and `NoNestedIfRule`. The test repeats the literal
sentences from this table rather than referencing the filter's constants (which `InternalsVisibleTo` would allow),
so a silent rewording of a published rule also fails the test.
