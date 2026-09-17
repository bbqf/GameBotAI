# Data Model: Publish Sequence Step Nesting Rules

No data model, persistence, or DTO shape changes. The feature only adds descriptions to an existing published schema.

## Sequence step (existing, `SequenceStepContract`, published as `SequenceStepContract` and alias `SequenceStep`)

| Field | Role in nesting | Published description added |
|-------|-----------------|-----------------------------|
| (schema) | the step itself | Full rule set: allowed children per container; If-inside-If not permitted; Loop never nested; Break only inside a loop body |
| `stepType` | `Action` (default when omitted; primitive or command step), `Loop`, `If`, `Break` | unchanged |
| `body` | Loop step: loop body. If step: then branch | Loop body: Action, If, Break — no Loop. If then branch: Action, and Break only when the If sits in a Loop body — no If, no Loop |
| `elseBody` | If step: optional else branch | Same rules as the then branch: Action, Break only when the If sits in a Loop body — no If, no Loop |

## Nesting rules (validation, unchanged)

| Container | Allowed | Rejected (400 message fragment) |
|-----------|---------|---------------------------------|
| Sequence top level | Action, Loop, If | Break — "only valid inside a loop body" |
| Loop body | Action, If, Break | Loop — "must not itself be a loop step" |
| If then/else branch | Action; Break when If is inside a Loop body | If — "must not itself be an if step"; Loop — "must not itself be a loop step"; Break outside a loop — "only valid inside a loop body" |

Deepest permitted nesting: Loop → If → (Action | Break).
