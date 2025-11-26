# Research: OCR Confidence via TSV

## Clarifications Resolved

### 1. Aggregation Method
- Decision: Use arithmetic mean of non -1 token confidences.
- Rationale: Simple, widely understood; avoids bias toward longer words; stable across varied token counts.
- Alternatives Considered:
  - Median: More robust to outliers but less sensitive to overall quality distribution.
  - Weighted by bbox area: Risks inflating large noisy regions; adds complexity without clear benefit.

### 2. Token Hierarchy Exposure
- Decision: Flatten tokens but retain line index and word order; do not surface block/paragraph hierarchy initially.
- Rationale: Current consumers only need per-word confidence; hierarchy adds complexity; can extend later without breaking.
- Alternatives Considered: Full hierarchy model (block/para/line/word) increases payload and parsing overhead; not needed yet.

### 3. Confidence Normalization Format
- Decision: Keep integer 0-100 as provided by Tesseract; aggregate will also be double 0-100 (may include fractional).
- Rationale: Consistency with raw Tesseract output; avoids confusion converting scales; existing consumers already treat confidence as (0-1 or percentage?) will adapt with doc.
- Alternatives Considered: 0-1 double scale introduces conversion overhead and potential misinterpretation.

## Additional Decisions

### TSV Invocation Pattern
- Decision: Invoke: `tesseract <input> stdout -l <lang> --psm <psm> --oem <oem> tsv` capturing stdout;
  parse rows skipping header line; fallback to legacy .txt if TSV fails.
- Rationale: Direct stdout reduces temp file clutter; TSV required for per-word confidence.
- Alternatives: Output to temp file `<output>.tsv`; more cleanup required.

### Error Handling
- Decision: On non-zero exit or empty TSV, return empty tokens, confidence=0, reason set accordingly.
- Rationale: Simplifies downstream; explicit reason allows trigger logic.
- Alternatives: Throw exceptions—would complicate trigger evaluation.

### Parsing Strategy
- Decision: Split lines; skip header; use `StringSplitOptions.None` for positional fields; validate column count; parse conf as int; treat conf <0 as -1.
- Alternatives: Regex parse slower; CSV library unnecessary overhead.

### Performance Considerations
- Decision: Single pass parse; allocate token list with capacity guessed from line count; avoid boxing for loops.
- Rationale: Minimizes overhead under high session concurrency.

## Risks & Mitigations
- Risk: Tesseract version differences in TSV columns — Mitigation: Validate header contains `conf` and expected columns; abort if mismatch.
- Risk: Large images slow OCR — Mitigation: PSM setting (default 6) documented; encourage cropping upstream.
- Risk: Memory leaks via unreleased Bitmaps — Mitigation: Caller responsibility; document in quickstart.

## Open Questions (None Remaining)
- All previous NEEDS CLARIFICATION markers resolved.

## Summary
Decisions prioritize simplicity, determinism, and extensibility. TSV parsing replaces heuristic confidence, providing reliable data for trigger evaluation.
