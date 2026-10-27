# Data Model: Sequence parameters in ocrText.pattern and ocrText.expected

The stored data does not change. The JSON shape of a sequence does not change. This file records the in-memory types that change.

## OcrTextStepCondition (no shape change)

| Field | Type | Change |
|-------|------|--------|
| `region` | `PixelRegion?` | None. Not resolved (FR-016). |
| `expected` | `string?` | Can hold `{{name}}` placeholders. Resolved before the evaluation. |
| `pattern` | `string?` | Can hold `{{name}}` placeholders. Resolved before the evaluation, then checked as a regular expression. |
| `parser` | `string?` | None. Not resolved (FR-016). |
| `negate` | `bool` | None. Copied to the resolved condition. |

Rules:

- Exactly one of `expected` and `pattern` is set. No change.
- Save time: a `pattern` with no placeholder MUST compile. A `pattern` with a placeholder is not compiled (FR-014).
- Save time and save dry run: when each placeholder of a `pattern` has a sequence default, the pattern after substitution MUST compile (FR-012).
- Save time: an unknown name in `pattern` or `expected` is an error. A name of digits only, for example `{{3}}`, has the same rule (FR-011).
- Run time and dry-run execution: each name MUST resolve, and the full field value after substitution MUST NOT be empty or white space (FR-007). A `pattern` after substitution MUST compile (FR-008).

## Resolved condition (run time only)

`SequenceStepConditionResolver.TryResolve` gives a new tree when a leaf has a placeholder. A leaf with no placeholder is the same instance. A tree with no placeholder is the same instance, with no new allocation (SC-006).

The input condition and the stored sequence do not change (FR-006). The resolver does not set a property of the input, because the condition classes have settable properties.

| Leaf type | Resolved fields |
|-----------|-----------------|
| `imageVisible` | `imageId` (no change) |
| `ocrText` | `pattern`, `expected` (new) |
| Other leaves | None |

## ParameterReference (record)

| Member | Change |
|--------|--------|
| `ParameterName`, `FieldPath`, `StepLabel`, `InsideLoop`, `DefeatsStaticCheck`, `SourceText` | None |
| `IsRegexPattern` | New. Optional, default `false`. `true` only for an `ocrText.pattern` field. |

New field paths from the scanner: `<prefix>.pattern` and `<prefix>.expected`, with `<prefix>` = `condition`, `if.condition`, `loop.condition`, or `breakCondition`, and `.children[i]` for each composite level. `DefeatsStaticCheck` is `false` for both.

## ParameterResolutionReasons (constants)

| Constant | Value | Change |
|----------|-------|--------|
| `Unresolved` | `unresolved` | None. Also used for an empty or white-space full result. |
| `NotANumber` | `not_a_number` | None |
| `InvalidPattern` | `invalid_pattern` | New. `OffendingValue` holds the resolved pattern. |

## ConditionEvaluationFailureKind (enum)

| Member | Change |
|--------|--------|
| `ParameterUnresolved` | None |
| `ParameterValueInvalid` | New. A placeholder resolved, but the result is not usable (a bad regular expression). `Detail` holds the parameter name. |

## ParameterValidationCodes (constants)

| Code | Change |
|------|--------|
| `unresolvable_parameter_reference` | Now also for a name in `ocrText.pattern` and `ocrText.expected`. |
| `invalid_resolved_pattern` | New. A `pattern` with sequence defaults in it is not a valid regular expression. |

## Spec status (FR-017)

| Item | Value at the end of the work |
|------|------------------------------|
| `**Status**:` line of `spec.md` | `Implemented` |
| Row for 165 in `specs/STATUS.md` | `Implemented` |

## State transitions

None. A step with a resolution error goes to the existing "Failed" state. A break condition with a resolution error gives the existing "No break" result.
