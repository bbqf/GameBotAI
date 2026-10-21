# Implementation Plan: OCR Text Condition for Sequences

**Branch**: `158-ocr-text-condition` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/158-ocr-text-condition/spec.md`

## Summary

Add a sequence condition type `ocrText` (GitHub issue #338). The condition has a `region`, exactly one of `expected` or `pattern`, and an optional `parser`. At run time it takes the current frame of the run session, reads the text in the region with the same OCR code path as `POST /api/ocr/read`, and compares the text. It is a new leaf that follows the `lastRun` leaf (feature 105). It works in `If`, `Loop`, `breakCondition`, and as a child of `all`, `any`, and `none`.

Technical approach (see [research.md](research.md)):

- A new domain type `OcrTextStepCondition` and rules class `OcrTextConditionRules` (save-time checks and match rules).
- An ambient scope `OcrTextEvaluationScope` carries a reader delegate from the service to the domain and collects one `OcrTextEvaluationRecord` for each evaluation.
- The service class `OcrTextConditionReader` calls `ISessionFrameSource.Capture`, `OcrRegionReader.Read`, and `OcrTextParsers.TryParse`. It does not copy code.
- Every failure gives false and exactly one code from the closed list in [data-model.md](data-model.md) (FR-013). The run log shows the code.
- A `pattern` time-out is a plain mismatch with no code. The run log says "pattern timed out" (FR-017).
- An empty read is compared like any text (FR-019).
- The save endpoint is the only save-time check of the parser name (FR-018). It stores the canonical name and compares names without case. Another path (for example gamepack import) with an unknown parser gives false with `parse-failed` at run time.
- Every run entry point that has an emulator session pushes the scope. An entry point with no session gives `no-session` (FR-020). The tasks list all entry points.
- Step-through does not read the screen. It shows the note `ocrText is not evaluated`.
- No web UI change. No change to `POST /api/ocr/read`.

## Technical Context

**Language/Version**: C# on .NET (the version of the solution)  
**Primary Dependencies**: ASP.NET Core minimal API, System.Text.Json polymorphism, Swashbuckle (OpenAPI), existing OCR engine services (feature 128)  
**Storage**: Sequence JSON files through `FileSequenceRepository` (no new file)  
**Testing**: xUnit (unit, contract, integration projects under `tests/`)  
**Target Platform**: Windows service host (GameBot.Service)  
**Project Type**: Web service with domain library  
**Performance Goals**: One capture and one OCR read for each evaluation. A `pattern` match has a time limit of 1 second (a time-out is a plain mismatch, FR-017).  
**Constraints**: No input sent to the emulator. A run must not crash on a read fault. The domain project must not use `System.Drawing`.  
**Scale/Scope**: One new leaf type, about 6 new files, about 10 edited files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Build and test runs: no known failure. Implementation stays blocked if a run fails (non-negotiable rule).
- Simplified Technical English (Principle VI): all artifacts of this plan use STE.
- Phone check (Principle III): the web UI does not change (clarification and FR-016). No phone support level is needed. The OpenAPI text says that the web UI does not know this type.
- Living documentation: `docs/architecture.md`, `CHANGELOG.md`, and `specs/STATUS.md` are updated (research R8).
- Scope: no change to `POST /api/ocr/read`, `reschedule-self`, `ocrOffset`, the existing condition types, or the PNS repository (FR-014, FR-015).

Result: pass before Phase 0. Pass after Phase 1 design. No violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/158-ocr-text-condition/
├── plan.md
├── research.md
├── data-model.md        # holds the closed list of failure reason codes
├── quickstart.md
├── contracts/
│   └── ocr-text-condition.md
└── tasks.md             # made by /speckit.tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/
│   ├── SequenceStepCondition.cs          # edit: add OcrTextStepCondition (discriminator ocrText)
│   ├── OcrTextConditionRules.cs          # new: Validate and Matches
│   └── FileSequenceRepository.cs         # edit: guard case
├── Services/
│   ├── OcrTextEvaluationScope.cs         # new: scope, OcrTextReadResult, OcrTextEvaluationRecord
│   ├── CompositeConditionValidator.cs    # edit: Walk case
│   ├── SequenceStepConditionEvaluator.cs # edit: EvaluateLeafAsync and Describe
│   ├── SequenceRunner.cs                 # edit: DescribeBreakCondition, drain records to step log
│   └── StepThrough/SequenceStepper.cs    # edit: note "ocrText is not evaluated"
└── Gamepacks/
    └── GamepackTypeDiscriminatorCheck.cs # read only: it reads polymorphism metadata, no type list

src/GameBot.Service/
├── Endpoints/SequencesEndpoints.cs       # edit: domain/DTO mapping, parser name check, canonical name
├── Services/Ocr/OcrTextConditionReader.cs # new: capture, read, parse
├── Services/SequenceExecution/           # edit: push the scope at every run entry point that has a session (FR-020); entry points with no session give no-session
├── Swagger/OcrTextConditionSchemaFilter.cs        # new
├── Swagger/ConditionalFlowSchemaDocumentFilter.cs # edit: register schema
└── Models (SequenceStepContracts.cs)     # edit: OcrTextConditionContract

tests/
├── unit/Sequences/OcrTextConditionRulesTests.cs
├── unit/Sequences/OcrTextConditionEvaluatorTests.cs
├── unit/Services/Ocr/OcrTextConditionReaderTests.cs
├── unit/StepThrough/SequenceStepperOcrTextTests.cs       # test for the stepper note
├── unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs # edit: ocrText case
├── contract/Sequences/OcrTextConditionContractTests.cs   # includes HH:MM:SS stored as hh:mm:ss
└── integration/Sequences/OcrTextConditionRunTests.cs     # includes no-session case

docs/architecture.md, CHANGELOG.md, specs/STATUS.md       # edit
```

**Gamepack check files** (spec assumption): `src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs` and its tests in `tests/unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs`. The check reads the polymorphism data of the type metadata, so it accepts `ocrText` when `OcrTextStepCondition` has a `JsonDerivedType` entry. No source change is expected. A test loads a gamepack sequence member with an `ocrText` condition. If the check does hold a list of types, the task updates it. No other gamepack change is in scope.

**Structure Decision**: Keep the existing two-project layout (Domain and Service) with the test projects under `tests/`. The new leaf follows the `lastRun` leaf.

## Complexity Tracking

No violation. The table is empty.
