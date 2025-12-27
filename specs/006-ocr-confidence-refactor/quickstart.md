# Quickstart: OCR Confidence via TSV

## Goal
Use Tesseract TSV output to obtain reliable per-word confidence and aggregate it for trigger evaluation.

## Prerequisites
- Tesseract installed and accessible on PATH or `GAMEBOT_TESSERACT_PATH` set.
- .NET 9 SDK.

## Invocation Pattern
```
tesseract input.png stdout -l eng --psm 6 --oem 1 tsv
```
Reads TSV rows including `conf` column.

## Integration Steps
1. Capture in-memory `Bitmap` from emulator session.
2. Save to temp PNG path.
3. Execute Tesseract with TSV output to stdout.
4. Parse TSV lines (skip header) into `OCRToken` list.
5. Compute aggregate confidence: arithmetic mean of confidences excluding -1 & empty text tokens.
6. Return `OcrEvaluationResult` to trigger evaluation logic.

## Error Handling
- Non-zero exit: return empty tokens, confidence=0, reason=`tesseract_error`.
- Missing `conf` column: reason=`tsv_format_unexpected`.
- No valid tokens: reason=`no_valid_tokens`.

## Testing
- Unit: parser with sample TSV fixture (normal, noise rows, malformed).
- Integration: trigger evaluation uses aggregated confidence threshold.

## Environment Variables
- `GAMEBOT_TESSERACT_PATH`: overrides tesseract executable.
- `GAMEBOT_TESSERACT_LANG`: language (default `eng`).
- `GAMEBOT_TESSERACT_PSM`: page segmentation mode.
- `GAMEBOT_TESSERACT_OEM`: OCR engine mode.

## Next
Implement parser and modify `TesseractProcessOcr` to use TSV path; update tests.
