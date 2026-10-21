# Contract: `ocrText` step condition

The condition is permitted in each slot that accepts a step condition: `If`, `Loop`, `breakCondition`, the per-step condition, and as a child of `all`, `any`, and `none`.

## JSON shape

```json
{
  "type": "ocrText",
  "region": { "x": 120, "y": 640, "width": 400, "height": 48 },
  "expected": "0",
  "negate": false
}
```

With a pattern and a parser:

```json
{
  "type": "ocrText",
  "region": { "x": 10, "y": 20, "width": 200, "height": 40 },
  "pattern": "^02:",
  "parser": "hh:mm:ss"
}
```

| Field | Required | Notes |
|-------|----------|-------|
| `type` | yes | `ocrText` |
| `region` | yes | `x`, `y`, `width`, `height` in capture pixels. `width` and `height` more than zero. |
| `expected` | one of two | Exact, case-sensitive match after trim. |
| `pattern` | one of two | Regular expression. Matches anywhere in the text. |
| `parser` | no | `hh:mm:ss`. The name match ignores case. The service stores the canonical name (`HH:MM:SS` is stored as `hh:mm:ss`). The compare uses the parsed text. |
| `negate` | no | Inverts the result. |

## Save-time errors (400, `invalid_request`)

| Fault | Message names |
|-------|---------------|
| `region` missing, or width or height not more than zero | `region` |
| Both or neither of `expected` and `pattern` | `expected` and `pattern` |
| `pattern` is not a valid regular expression | `pattern` |
| Unknown `parser` (only the sequence endpoint checks this, FR-018) | `parser`, and the supported parsers |

The message format is `Step '<id>' condition at <path>: <text>`, the same as the other condition types.

## Run behavior

| Case | Result | Run log |
|------|--------|---------|
| Text matches | true | text read, expected or pattern, `result=true` |
| Text does not match | false | same, `result=false` |
| Text is empty | compared like any text (true if `expected` is empty or `pattern` matches empty text) | `read=''`, `result=<true|false>` |
| `pattern` match times out | false (plain mismatch) | `pattern timed out`, no reason code |
| Parser given and text does not parse | false | `reason=parse-failed` |
| Unknown parser at run time (sequence did not enter by the sequence endpoint) | false | `reason=parse-failed` |
| Region not inside the frame | false | `reason=region-outside-frame` |
| Region width or height not more than zero at run time | false | `reason=region-invalid` |
| Run entry point with no emulator session (dry run or no capture) | false | `reason=no-session` |
| Capture fault | false | `reason=capture-failed` |
| OCR engine not available | false | `reason=ocr-unavailable` |
| OCR engine fault | false | `reason=ocr-error` |

The reason codes are a closed list of seven (`no-session`, `capture-failed`, `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`, `parse-failed`). A plain mismatch and a `pattern` time-out have no reason code (FR-013, FR-017). Every run entry point that has an emulator session reads the screen (FR-020). Step-through does not read the screen. It shows the note `ocrText is not evaluated`.

The step does not fail for a reason in this table. `negate` applies to the false result. The condition sends no input to the emulator.

## Unchanged

`POST /api/ocr/read`, `reschedule-self`, `ocrOffset`, and the other condition types do not change.
