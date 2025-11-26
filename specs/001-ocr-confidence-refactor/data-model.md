# Data Model: OCR Confidence via TSV

## Entities

### OCRToken
| Field | Type | Rules |
|-------|------|-------|
| text | string | Trimmed; may be empty; if empty excluded from aggregation |
| left | int | >=0 |
| top | int | >=0 |
| width | int | >0 |
| height | int | >0 |
| lineIndex | int | >=0 |
| wordIndex | int | >=0 within line |
| confidence | int | -1 or 0..100; -1 excluded from aggregation |

### OcrEvaluationResult
| Field | Type | Rules |
|-------|------|-------|
| tokens | List<OCRToken> | Ordered by (lineIndex, wordIndex) |
| confidence | double | Mean of token.confidence (excluding -1, empty text) 0..100; fractional allowed |
| text | string | Concatenation of token.text with single spaces per line word separation |
| reason | string? | Non-empty when confidence==0 due to failure or no text |

## State & Transitions
- Generation: Produced per OCR invocation; immutable snapshot.
- Failure Path: tokens empty; confidence=0; reason populated (e.g., `tesseract_error`, `no_text_detected`).

## Validation Summary
- Aggregation excludes tokens where confidence == -1 or text is empty/whitespace.
- If all tokens excluded → confidence=0; reason=`no_valid_tokens`.

## Derived Values
- text constructed during parse to avoid second pass.

## Notes
- Future extension: Add block/paragraph grouping without altering existing fields.
