# Data Model: Gamepack dry run skips parameter placeholder image ids

This feature adds no stored data and no new type. It adds one helper method and changes one check.

## Entities used

| Entity | Field | Use |
|--------|-------|-----|
| Sequence (`CommandSequence`) | `Parameters` (`ParameterDeclaration.Name`) | Gives the declared names. Null or empty gives an empty set |
| Sequence step | image id slots (`Gate.TargetId`, `WaitForImage`, conditions, `BreakCondition`, loops, `If`, `ElseBody`) | Source of the image ids (`GamepackReferences.ImageIds`) |
| Dry run problem (`GamepackProblem`) | category `reference`, type, name, message | Output. Not reported for a skipped id |

## New method

`TemplateSubstitutor.IsWholePlaceholder(string? value, out string key)` returns true when `value`, after trim, is exactly `{{key}}` and holds one key. Else it returns false and `key` is empty.

## Rule

For each sequence S and each image id I that `GamepackReferences.ImageIds(S)` gives:

1. If `IsWholePlaceholder(I, out key)` is true and S declares a parameter named `key` (ordinal), skip I.
2. Else check I against the image targets (file images plus installed images not removed), as before.

Command image ids are not filtered.

## State transitions

None.
