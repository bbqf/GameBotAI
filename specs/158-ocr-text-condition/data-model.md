# Data Model: OCR Text Condition

## OcrTextStepCondition (domain, new)

Derived from `SequenceStepCondition`. JSON discriminator: `ocrText`.

| Field | Type | Rule |
|-------|------|------|
| `type` | string | Always `ocrText`. |
| `negate` | bool | Inherited. Inverts the result. Default false. |
| `region` | `PixelRegion` (x, y, width, height) | Required. `width` and `height` more than zero. `x` and `y` zero or more. |
| `expected` | string or null | Exact match after trim. Case-sensitive. An empty string is allowed. |
| `pattern` | string or null | Regular expression. Matches anywhere in the trimmed text. |
| `parser` | string or null | Optional. Today only `hh:mm:ss`. The name is checked at save time without case. The system stores the canonical name (`HH:MM:SS` becomes `hh:mm:ss`). |

Validation rules (`OcrTextConditionRules.Validate`):

1. `region` is required. Width and height are more than zero. The check "inside the frame" is at run time only.
2. Exactly one of `expected` and `pattern` is set (not null).
3. `pattern` compiles as a regular expression.
4. `parser`, if set, is a known name. Only the sequence endpoint checks this rule (FR-018). Another path (for example gamepack import) with an unknown parser gives false with `parse-failed` at run time.

Each message names the field and gives the fix. Example: `Step 's1' condition at $: ocrText requires exactly one of expected and pattern.`

## OcrTextEvaluationRecord (domain, new, run only)

One record for each evaluation of an `ocrText` leaf. It is not stored in a sequence file.

| Field | Type | Meaning |
|-------|------|---------|
| `TextRead` | string or null | Raw text from the engine. Null when no read happened. |
| `Compared` | string or null | The text that was compared: the raw trimmed text, or the parsed `hh:mm:ss` value. |
| `Expected` | string or null | The `expected` value. |
| `Pattern` | string or null | The `pattern` value. |
| `Parser` | string or null | The parser name. |
| `Result` | bool | The result of the compare, before `negate`. |
| `Reason` | string or null | One code from the closed list below. Set only when the result is false because of a failure. Null for a plain mismatch, also for a `pattern` time-out. |
| `Note` | string or null | Free text for the log. It is "pattern timed out" after a `pattern` time-out (FR-017). It is not a reason code. |

## Failure reason codes (closed list, FR-013)

Exactly one code is set for each failure. No other code is allowed. A plain mismatch and a `pattern` time-out have no code. The code appears in the run log as `reason=<code>`.

| Code | Meaning |
|------|---------|
| `no-session` | The run entry point has no emulator session (dry run or no capture), or no scope is present (FR-020). |
| `capture-failed` | The frame capture failed or gave no frame. |
| `ocr-unavailable` | The OCR engine is not available. |
| `region-outside-frame` | The region is not fully inside the frame (run-time check). |
| `region-invalid` | The region has a width or height of zero or less at run time. |
| `ocr-error` | The OCR engine failed during the read. |
| `parse-failed` | A parser is named and the text does not parse, or the parser name is unknown at run time (FR-018). |

Log line format (one line for each record):
`ocrText: read='<TextRead>' expected='<Expected>' result=<true|false>` or `pattern='<Pattern>'`. A reason adds `reason=<Reason>`. A `pattern` time-out adds `pattern timed out` and no reason. An empty read logs `read=''` and is compared like any text (FR-019).

## OcrTextEvaluationScope (domain, new)

An ambient (`AsyncLocal`) object for the duration of one run.

| Member | Meaning |
|--------|---------|
| `Reader` | `Func<OcrTextStepCondition, CancellationToken, Task<OcrTextReadResult>>`, supplied by the service. |
| `Records` | List of `OcrTextEvaluationRecord` for the run. The runner drains it after each condition evaluation. |
| `Current` | The scope of the current async flow, or null. |
| `Push` | Makes a scope current. Dispose restores the earlier one. |

## OcrTextReadResult (domain, new)

| Field | Meaning |
|-------|---------|
| `Text` | Raw text, or null on failure. |
| `Parsed` | Parsed `hh:mm:ss` text, or null. Set only when a parser is named and the text parses. |
| `FailureReason` | One code from the closed list above. Null on success. |

## OcrTextConditionContract (service, new)

The request and response shape of the API. Same fields as the domain type, all optional in JSON, so an absent field gives a 400 from validation and not a JSON read error. `region` uses the existing `PixelRegionDto`.

## State transitions

None. The condition has no state. Each evaluation reads a new frame.
