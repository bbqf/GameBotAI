# Contract: Composite Sequence Step Conditions

**Feature**: 088-composite-image-conditions | **Date**: 2026-09-15

Applies to every position that accepts a `SequenceStepCondition` on the sequence endpoints:

- `POST /api/sequences`, `PUT /api/sequences/{id}`, `PATCH /api/sequences/{id}` (request)
- `GET /api/sequences`, `GET /api/sequences/{id}` (response)

## Existing shapes (unchanged)

```jsonc
{ "type": "imageVisible",   "imageId": "pns-disconnect-confirm", "minSimilarity": 0.85, "negate": false }
{ "type": "commandOutcome", "stepRef": "probe", "expectedState": "success",             "negate": false }
```

## New shape

```jsonc
{
  "type": "all",              // or "any" or "none"
  "negate": false,            // optional, default false; inverts the combined result
  "children": [ /* 1..16 conditions, any kind, in evaluation order */ ]
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `type` | `"all"` \| `"any"` \| `"none"` | yes | The combining rule. Any other value is rejected by the polymorphic reader. |
| `children` | array of condition | yes | 1–16 entries. Evaluated in the order given. A child may itself be a composite, to a total nesting depth of 4. |
| `negate` | boolean | no | Defaults to `false`. Inverts the composite's combined result. |

### Semantics

| `type` | True when | Stops evaluating at |
|--------|-----------|---------------------|
| `all` | every child is true | the first false child |
| `any` | at least one child is true | the first true child |
| `none` | no child is true | the first true child |

All children of one composite are judged against the same screen observation, so a two-image guard costs no more screen captures than a one-image guard.

## Worked example — the B-011 guard

The case from issue #191: dismiss the disconnect dialog, but never the gas dialog that draws an identical `Confirm` button at the same coordinates.

```jsonc
{
  "stepId": "dismiss-disconnect",
  "commandReference": { "commandId": "PNS.DismissDisconnectDialog" },
  "condition": {
    "type": "all",
    "children": [
      { "type": "imageVisible", "imageId": "pns-disconnect-confirm", "minSimilarity": 0.85 },
      { "type": "none", "children": [
        { "type": "imageVisible", "imageId": "pns-gas-dialog-title", "minSimilarity": 0.85 }
      ]}
    ]
  }
}
```

The same guard written with `negate` instead of a nested `none`, which is equivalent and shorter:

```jsonc
{
  "type": "all",
  "children": [
    { "type": "imageVisible", "imageId": "pns-disconnect-confirm", "minSimilarity": 0.85 },
    { "type": "imageVisible", "imageId": "pns-gas-dialog-title",   "minSimilarity": 0.85, "negate": true }
  ]
}
```

Put the cheap, selective test first: children evaluate in order and `all` stops at the first false one.

### Collapsing an N-way exit guard

Replaces the repeated single-image `Break` idiom:

```jsonc
{
  "breakCondition": {
    "type": "any",
    "children": [
      { "type": "imageVisible", "imageId": "pns-city-hud" },
      { "type": "imageVisible", "imageId": "pns-world-hud" },
      { "type": "imageVisible", "imageId": "pns-loading-spinner", "negate": true }
    ]
  }
}
```

### Mixing condition kinds

A composite may combine an image test with a prior-step outcome:

```jsonc
{
  "type": "all",
  "children": [
    { "type": "commandOutcome", "stepRef": "probe-screen", "expectedState": "success" },
    { "type": "imageVisible",   "imageId": "pns-city-hud" }
  ]
}
```

## Rejections

Every case below is a **400** with an `errors` array. None is a 500.

| Payload | Message |
|---------|---------|
| `{"type":"all","children":[]}` | `Step 'S' condition at $: 'all' requires at least one child.` |
| `{"type":"any"}` (no `children`) | `Step 'S' condition at $: 'any' requires at least one child.` |
| 17 children | `Step 'S' condition at $: 'all' allows at most 16 children (found 17).` |
| composites nested 5 deep | `Step 'S' condition at $.children[0].children[0].children[0].children[0]: condition nesting exceeds the maximum depth of 4.` |
| child `imageVisible` with no `imageId` | `Step 'S' condition at $.children[1]: imageVisible condition requires imageId.` |
| child `minSimilarity` of `1.5` | `Step 'S' condition at $.children[0]: imageVisible minSimilarity must be within 0..1.` |
| child `commandOutcome` with no `stepRef` | `Step 'S' condition at $.children[0]: commandOutcome condition requires stepRef.` |
| child `commandOutcome` with `expectedState: "maybe"` | `Step 'S' condition at $.children[0]: commandOutcome expectedState must be one of success\|failed\|skipped\|break\|no_break.` |
| `{"type":"whenever","children":[...]}` | Unknown discriminator, rejected as a malformed request body. |

Paths are `$`-rooted at the condition, matching the convention already used by the flow-condition validator. A dangling image reference inside a composite is reported by the existing dangling-reference check wherever that check already runs.

## Execution reporting

When a composite guard skips a step, the existing per-step execution record carries:

- `conditionType`: the composite's rule — `all`, `any` or `none`
- `conditionResult`: `false`
- `message`: names the child that settled the result, e.g. `condition $.children[1] (imageVisible(imageId=pns-gas-dialog-title, minSimilarity=0.85)) settled the guard`. Absent when no single child settled it (every child agreed) and for a leaf condition, which keeps a leaf's record byte-identical to what it was before this feature.

A loop break driven by a composite renders its reason as the rule and its children, e.g. `all(imageVisible(imageId=pns-disconnect-confirm, minSimilarity=0.85), NOT imageVisible(imageId=pns-gas-dialog-title, minSimilarity=0.85))`.

No new response field and no new response entity: the record's shape is unchanged.

## OpenAPI

`SequenceStepCondition` gains three members in its polymorphic set, published under the aliases `AllCondition`, `AnyCondition` and `NoneCondition`, each with a recursive `children` array referencing `SequenceStepCondition`, and each documenting the 1–16 child bound and the depth-4 limit.

## Backward compatibility

Purely additive. A request containing only `imageVisible` and `commandOutcome` conditions is parsed, validated, stored, evaluated and returned exactly as before, with byte-identical stored JSON. No version negotiation, no migration, no opt-in flag.
