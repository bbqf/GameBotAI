# Implementation Plan: OCR Confidence via TSV

**Branch**: `001-ocr-confidence-refactor` | **Date**: 2025-11-26 | **Spec**: `specs/001-ocr-confidence-refactor/spec.md`
**Input**: Feature specification for OCR confidence refactor (TSV parsing)

**Note**: This template is filled in by the `/speckit.plan` command. Refer to the command help for execution workflow.

## Summary

Introduce accurate OCR confidence by invoking Tesseract with TSV output, parsing per-word confidence (`conf`), exposing tokens and aggregate confidence (mean of non -1 values) while preserving existing output fields. Replace heuristic character-ratio confidence with TSV-driven calculation. Provide robust error handling and deterministic aggregation.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 9 (align with existing services)  
**Primary Dependencies**: External Tesseract CLI (no new managed package)  
**Storage**: None added; transient temp files only  
**Testing**: xUnit (unit: TSV parsing, integration: trigger evaluation with OCR)  
**Target Platform**: Windows (CI), should remain cross-platform where Tesseract available  
**Project Type**: Service + domain library  
**Performance Goals**: OCR invocation time p95 ≤ 1500ms for 1080p screenshot; parsing overhead ≤ 5ms  
**Constraints**: Memory footprint per OCR ≤ 40MB; temp files cleaned; no leaked processes  
**Scale/Scope**: Up to 10 OCR evaluations per minute per session typical; design for 100 concurrent sessions

**Clarifications (Resolved in research.md)**:
1. Aggregation: Arithmetic mean chosen over median/weighted area.
2. Hierarchy: Flatten tokens with line & word indexes only.
3. Normalization: Keep 0-100 scale (integer per token, double aggregate).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: planned tooling (lint/format/static analysis), modularity approach, security scanning.
- Testing: unit/integration plan, coverage targets, determinism strategy, CI gating.
- UX Consistency: interface conventions (CLI/API/logs), error messaging, versioning for changes.
- Performance: declared budgets/targets and measurement approach for hot paths.

Initial Alignment:
- Code Quality: Encapsulate TSV parsing in dedicated class (`TesseractTsvParser`); small methods <50 LOC; no new deps.
- Testing: Add unit tests for parser (valid TSV, noise rows, empty output); integration test ensures trigger uses new confidence.
- UX Consistency: Preserve previous `OcrResult` fields; add `confidence` (aggregate) and `tokens[].confidence`; stable JSON.
- Performance: Single pass parse; avoid unnecessary allocations (reuse StringSplit); measure parsing with stopwatch in tests.

Gate Status: PASS (all clarifications resolved; no blockers.)

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: Utilize existing domain service layout; new parser in `src/GameBot.Domain/Triggers/Evaluators/TesseractTsvParser.cs`; extend `OcrResult` or introduce `OcrDetailedResult`. Modify `TesseractProcessOcr` to request TSV (`... stdout tsv` pattern) and build tokens.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |

## Phase 1 Outputs Summary
- research.md generated with decisions on aggregation, hierarchy, normalization.
- data-model.md defines OCRToken and OcrEvaluationResult.
- contracts/ocr-evaluation.openapi.yaml adds endpoint schema (if exposed for testing).
- quickstart.md documents usage & environment variables.
- Agent context updated (copilot-instructions.md).

## Post-Design Constitution Re-Check
- Code Quality: Parser planned small & test-covered (OK)
- Testing: Unit + integration additions outlined (OK)
- UX Consistency: Non-breaking schema extension (OK)
- Performance: Budget specified; single-pass parse (OK)
Gate: PASS
