# Implementation Plan: Quiz Answer Service for Sequences

**Branch**: `142-quiz-answer-service` | **Date**: 2026-10-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/142-quiz-answer-service/spec.md`

## Summary

Add two service-level sequence steps, `quiz-lookup` and `quiz-learn`, and a shared answer store
with list, get, and delete API routes. The lookup reads the question and answer regions with the
existing OCR reader, normalizes the text, finds the stored answer by similarity (default 0.85),
and sets the outcome `pick_N`, `unreadable`, or `service_unavailable`. The learn step stores the
pair only when exactly one answer reaches the similarity limit. Storage is JSON files under the
data root behind a domain interface. The plan covers the corrected spec: similarity match in learn
(FR-008), `store`, `game`, and `category` filters in the list API (FR-014), case-insensitive
category match, a clear unreadable rule, log tests (FR-020a), a type-list sync test (FR-022), and a
coverage check (FR-023).

## Technical Context

**Language/Version**: C# on .NET (same target as the existing solution)
**Primary Dependencies**: Existing OCR reader (`OcrRegionReader`, `ITextOcr`), `ISessionFrameSource`, ASP.NET Core minimal API, Swashbuckle. No new package.
**Storage**: JSON files under `<dataRoot>/quiz/`, one file per store, atomic replace
**Testing**: xUnit unit, contract, and integration tests in `tests/`; coverage by the repository coverage run
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service with a domain library
**Performance Goals**: Lookup on 200 records under 1 second (SC-005); the unit test uses a 50 ms in-memory budget
**Constraints**: No new database or storage package (FR-016); no change to the OCR engine or `POST /api/ocr/read` (FR-002); safe for parallel runs (FR-017)
**Scale/Scope**: At least 200 records per store; few stores

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation stops until the failures are fixed or a maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and its artifacts obeys Simplified Technical English (Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small classes: normalizer, matcher, repository, two handlers, keeper. Domain interface, service implementation. |
| II. Testing | Pass | Unit, contract, and integration tests. Random guess behind `IQuizRandom`. Coverage at least 80% line and 70% branch for the new code, checked in the polish task (FR-023, R13). |
| III. UX consistency | Pass | No new page. Phone support level does not change. Errors are clear 400 and 404 responses. |
| IV. Performance | Pass | Linear scan of 200 records; SC-005 test. |
| V. Living documentation | Pass | OpenAPI schema and payload descriptions, docs, and CHANGELOG entry. Sync test (FR-022) guards the type lists. |
| VI. STE | Pass | All artifacts in STE. |

Post-design re-check: Pass. No violation. The Complexity Tracking table stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/142-quiz-answer-service/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── quiz-api.md
│   └── quiz-steps.md
└── tasks.md          # made by /speckit.tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Actions/ActionTypes.cs                    # add quiz-lookup, quiz-learn constants
├── Actions/SequenceActionTypes.cs            # add quiz-lookup, quiz-learn
├── Commands/FileSequenceRepository.cs        # allow-list
├── Services/SequenceStepValidationService.cs # shape rules (FR-018)
└── Quiz/                                      # new
    ├── QuizRecord.cs, QuizLookupResult.cs
    ├── IQuizAnswerRepository.cs, FileQuizAnswerRepository.cs
    ├── QuizTextNormalizer.cs, QuizSimilarity.cs
    ├── IQuizRandom.cs
    └── QuizStoreUnavailableException.cs

src/GameBot.Service/
├── Services/Quiz/                             # new: QuizLookupHandler, QuizLearnHandler, QuizLookupResultKeeper
├── Services/SequenceRunner*.cs                # IsServiceLevelAction
├── Services/SequenceExecutionService*.cs      # DispatchActionAsync
├── Services/StepThrough/StepThroughActionPreview.cs
├── Swagger/PrimitiveActionSchemaFilter.cs     # PayloadDescriptions
└── Endpoints/QuizEndpoints.cs                 # new, flat in Endpoints: GET list, GET one, DELETE

src/web-ui/src/                                # sequence editor only: step type list entry and a basic field form
├── components/sequences/PrimitiveActionFields.tsx  # field form for quiz-lookup and quiz-learn
└── pages/SequencesPage.tsx                         # type list entry (no new page)

tests/unit/Quiz/, tests/unit/Sequences/, tests/contract/Quiz/, tests/integration/Quiz/
```

**Structure Decision**: Reuse the existing domain and service split. New domain code in `GameBot.Domain/Quiz`. New service code in `GameBot.Service/Services/Quiz` and an endpoint file. Keep `Program.cs` thin (build-time analyzer limit): register through one extension method.

## Corrected-spec coverage

| Spec item | Plan answer | Where |
|-----------|-------------|-------|
| FR-008 learn uses similarity | Exactly one answer must reach the limit; none gives `skipped_no_match`, more gives `skipped_ambiguous` | research R4a |
| FR-014 list filters | `store`, `game`, `category` | contracts/quiz-api.md |
| Category case | Category match ignores case; game match is exact | research R14, data-model |
| Unreadable | Empty or white-space text after normalization, or an OCR fault | research R2 |
| Learn without lookup result | `skipped_no_match`, log says why | research R7 |
| FR-020a log tests | Log-capture test per outcome plus a no-secret check | research R10, quickstart |
| FR-022 sync test | One test over `SequenceActionTypes.All` | research R1a |
| FR-019 web UI form | Type list entry and a basic field form in the sequence editor; no new page | research R11 |
| FR-019 API example | `PayloadDescriptions` entry for each new step includes an example step | research R16, contracts/quiz-steps.md |
| FR-023 coverage | Polish task checks coverage of new code | research R13 |

## Complexity Tracking

No violation. No entry.
