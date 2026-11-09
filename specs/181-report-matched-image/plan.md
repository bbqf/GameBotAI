# Implementation Plan: Report which image of a list matched

**Branch**: `181-report-matched-image` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/181-report-matched-image/spec.md`

## Summary

Feature 180 lets a `WaitForImage` step and an `imageVisible` condition take a list of images. Today only the command executor reports the matched image (`matchedImageId` on its step outcome). The sequence runner does not: `WaitForImageStepResultDetails` holds no matched image, and the list check in the runner returns only true or false. This feature does four things:

1. The runner records the image that matched (id and confidence) for a `WaitForImage` step and for a step whose own condition is a top-level `imageVisible` list.
2. A per-run store, `StepImageMatchScope`, keeps the latest match of each step key.
3. A new condition type `matchedImage` (`stepRef`, `imageId`) reads that store.
4. A new placeholder form `{{step.<stepRef>.matchedImageId}}` reads the same store. The per-step run log and `WaitForImageStepResultDetails` show the matched image id.

No matching logic changes. The loop over the list items stays as feature 180 made it. The runner only keeps the item that returned true.

## Technical Context

**Language/Version**: C# on .NET (existing solution), TypeScript and React in `src/web-ui`
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (Swagger), Vitest and Testing Library
**Storage**: Existing file-based JSON stores. New condition type is added to the polymorphic condition list. New result member is optional and omitted when null, so old files and old clients stay valid.
**Testing**: xUnit (Domain unit tests, Service integration and contract tests), Vitest for the web UI
**Target Platform**: Windows service plus browser UI (phone and desktop)
**Project Type**: web-service with web UI
**Performance Goals**: No extra screen capture and no extra image pass. The store is a dictionary write after a match and a dictionary read in a condition or placeholder. Cost is O(1) for each use.
**Constraints**: Old sequences, old results, and the `referenceImageId` field keep their form and meaning. No new matching logic. No change to the reporting repository.
**Scale/Scope**: About 10 source files in Domain and Service, 0 to 2 web UI files, docs, and tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Code quality (I): one small new Domain class for the store (`StepImageMatchScope`), one new condition class, and one small rules class for the placeholder form. Public members get XML docs. PASS.
- Testing (II): unit tests for the store, the condition evaluator, the placeholder resolver, and the validation. Integration tests for the API save, the run, the run log, and Swagger. PASS.
- UX consistency (III): error messages say what is wrong and what to do. Phone support level is **reduced** for the web UI: the condition editor gets a new type with two fields in the existing form layout. The run log view shows one more text field. Both follow docs/mobile-ui-principles.md. PASS.
- Performance (IV): goals declared above. PASS.
- Living documentation (V): update `docs/architecture.md` (API surface and domain model, refresh "Last reviewed"), add a `specs/STATUS.md` row, and set the spec `Status`. PASS.
- STE (VI): all new text is in STE. PASS.
- Version bump: bump `patch` in `installer/versioning/version.override.json` before the PR. Add a CHANGELOG line.
- No violations. The Complexity Tracking table is not needed.

Post-design re-check: no new violation. No new project, dependency, or store file.

## Project Structure

### Documentation (this feature)

```text
specs/181-report-matched-image/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── matched-image-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/
│   └── SequenceStepCondition.cs            # NEW class MatchedImageStepCondition (type "matchedImage")
├── Services/
│   ├── StepImageMatchScope.cs              # NEW: per-run store, latest match for each step key
│   ├── SequenceRunner.cs                   # WaitForImageAsync + step-condition gate: record the match; add MatchedImageId to WaitForImageStepResultDetails
│   ├── SequenceStepConditionEvaluator.cs   # evaluate matchedImage; record the item that matched for imageVisible lists
│   └── SequenceStepValidationService.cs    # matchedImage rules: stepRef must name a prior step, imageId not empty
├── Parameters/
│   ├── StepResultPlaceholderRules.cs       # NEW: parse and validate {{step.<stepRef>.matchedImageId}}
│   ├── ParameterScope.cs                   # TryResolve: fall back to StepImageMatchScope for step.* names
│   └── ParameterReferenceScanner.cs        # accept step.* references (no undeclared-parameter error)
└── Services/ParameterValidationService.cs  # check stepRef of a step.* reference at save

src/GameBot.Service/
├── Models/SequenceStepContracts.cs         # DTO: matchedImage condition, matchedImageId in step result
├── Services/SequenceExecution/SequenceExecutionService.cs  # copy matchedImageId into the step log entry
├── Services/ExecutionLog/ExecutionLogService.cs            # show matchedImageId and confidence in the per-step entry
├── Endpoints/SequencesEndpoints.cs         # validation hook for matchedImage
└── Swagger/                                # schema text and example for the new condition and field

src/web-ui/src/ (reduced phone support)
├── types/sequenceFlow.ts                   # type for the matchedImage condition
├── lib/validation.ts                       # stepRef must name a prior step
└── components/ (condition editor)          # new condition type, two fields

tests/ (existing test projects), docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Keep the existing layers. One store class carries the data from the runner to the two readers (the condition evaluator and the parameter scope). The store has the same ambient (AsyncLocal) shape as the existing `CommandStepStateScope` of feature 167, so the same pattern is reused.

## Key design decisions

1. **Store.** `StepImageMatchScope` keeps `stepKey -> (MatchedImageId, Confidence)`. A new match replaces the older entry. A step run that ends with no match removes the older entry, so the latest run counts (same rule as `CommandStepStateScope.Set`). The runner pushes one scope for each run, next to the existing `CommandStepStateScope.Push`.
2. **Record the match in the runner.** In `WaitForImageAsync`, the list evaluator closure of feature 180 already knows `listId` when an item returns true. It stores that id and the confidence of the last pass. For a single image, the id is `ReferenceImageId`. On `image_detected` the runner writes `detail.MatchedImageId`, `detail.Confidence`, and the store entry. For `Until = absent`, no image matches, so no id is set.
3. **Record the match for a step condition.** When the condition of a step is a top-level, non-negated `imageVisible` and it is true, the evaluator reports the matching item to the store under the key of that step. A nested or negated condition records nothing (the matched image is not defined there).
4. **Condition `matchedImage`.** Wire form: `{ "type": "matchedImage", "stepRef": "<step id>", "imageId": "<image id>" }`. True when the store entry of `stepRef` equals `imageId` (case-sensitive, ordinal, as image ids are). With no entry, the result is false before `negate`. The run log of the gate gets a reason text.
5. **Save rules.** `stepRef` must name a prior step in the sequence (same reachability rule as `commandOutcome`, reuse `CommandStepOutcomeRules` reference scan). `imageId` must not be empty. A placeholder in `imageId` follows feature 179 rules for one-value positions.
6. **Placeholder.** Form `{{step.<stepRef>.matchedImageId}}`. The prefix `step.` is a reserved namespace like `queue.`. `ParameterScope.TryResolve` looks in `StepImageMatchScope` for these names. A name with no entry gives the usual unresolved-placeholder error. The save check accepts the form and checks that `stepRef` names a prior step.
7. **Run log.** `WaitForImageStepResultDetails` gets `MatchedImageId` (omitted when null). The per-step log entry shows `matchedImageId` and `confidence` for a match. The command-level outcome of feature 180 stays as it is.
8. **Compatibility.** `referenceImageId` and `confidence` keep their meaning. New members are optional and omitted when null. Old files load with no migration.
9. **Out of scope.** No change to matching, no other condition type, no change to the reporting repository, no match store for command-level steps inside a command.

## Complexity Tracking

No violations to justify.
