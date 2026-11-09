# Implementation Plan: Any-of image condition and WaitForImage for a list parameter

**Branch**: `180-any-of-image-list` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/180-any-of-image-list/spec.md`

## Summary

An `imageVisible` condition gets an optional `imageIds` list and an optional `match` field (only value `any`). Its `imageId` field accepts a `{{name}}` placeholder that is the whole value and names a multi-value parameter (feature 179). The `WaitForImage` step gets the same two inputs on its detection target. At run time the parameter resolvers expand the list placeholder to a list in these two positions only. The condition is true, and the wait ends, when any image of the list is visible. A list placeholder in any other position that needs one value is refused at save with a clear error. The static check for a placeholder stays skipped, with the `static_check_skipped` warning. The `WaitForImage` result names the matched image in a new field `matchedImageId`.

## Technical Context

**Language/Version**: C# on .NET (existing solution), TypeScript and React in `src/web-ui`  
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (Swagger), Vitest and Testing Library  
**Storage**: Existing file-based JSON stores. New members are optional and omitted when null, so old files stay valid.  
**Testing**: xUnit (Domain unit tests, Service integration and contract tests), Vitest for the web UI  
**Target Platform**: Windows service plus browser UI (phone and desktop)  
**Project Type**: web-service with web UI  
**Performance Goals**: A list holds at most 50 items. One pass checks each item once, in list order, and stops at the first match. The cost is linear in the item count. The first-pass time for a list of one item does not change.  
**Constraints**: Conditions and steps with one image id keep their stored form, API response, and behavior. No other match mode, no other condition type, no other change to list parameters.  
**Scale/Scope**: About 12 source files in Domain and Service, 0 to 2 web UI files, docs, and tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Code quality (I): new code stays small and cohesive. One new Domain helper, `ImageListRules`, holds the list logic (normalize, merge, validate). Public members get XML docs. Method names use CamelCase with no underscore. PASS.
- Testing (II): unit tests for the rules, the resolvers, and the evaluator. Integration tests for the API save, the run, and Swagger. A web UI test only if the editor changes. PASS.
- UX consistency (III): error messages say what is wrong and what to do. Phone support level is **full** for any editor change: the change adds no new layout and follows docs/mobile-ui-principles.md. PASS.
- Performance (IV): goals declared above. PASS.
- Living documentation (V): update `docs/architecture.md` (API surface and domain model, refresh "Last reviewed"), add a `specs/STATUS.md` row, and set the spec `Status`. PASS.
- STE (VI): all new text in this plan and the artifacts is written in STE. PASS.
- Version bump: bump `patch` in `installer/versioning/version.override.json` before the PR. Add a CHANGELOG line.
- No violations. The Complexity Tracking table is not needed.

Post-design re-check: no new violation. The design adds no project, no dependency, and no new store.

## Project Structure

### Documentation (this feature)

```text
specs/180-any-of-image-list/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── image-list-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/
│   ├── SequenceStepCondition.cs            # ImageVisibleStepCondition: add ImageIds, Match
│   ├── DetectionTarget.cs                  # add optional ReferenceImageIds (WaitForImage only)
│   ├── ImageListRules.cs                   # NEW: normalize, validate written list, match value, exclusivity
│   └── WaitForImageConfigMapper.cs         # map ReferenceImageIds both ways
├── Parameters/
│   ├── SequenceStepConditionResolver.cs    # expand a whole list placeholder in imageId to ImageIds
│   ├── CommandStepResolver.cs              # expand a whole list placeholder in the WaitForImage image field
│   ├── ParameterReferenceScanner.cs        # mark list-allowed positions; scan imageIds items
│   └── ParameterResolutionError.cs         # message for a list in a one-value position (save form)
├── Services/
│   ├── SequenceStepConditionEvaluator.cs   # any-of evaluation; Describe text for a list
│   ├── SequenceStepValidationService.cs    # written-list rules for imageVisible and WaitForImage
│   └── ParameterValidationService.cs       # save error for a multi-value parameter in a one-value position
└── Images/ReferenceImageSetLoader.cs       # (reuse) load one set for each list item

src/GameBot.Service/
├── Models/Commands.cs, Models/SequenceStepContracts.cs  # DTOs: imageIds, match, matchedImageId
├── Services/CommandExecutor.cs             # WaitForImage: poll each item, set matchedImageId
├── Services/SequenceExecution/SequenceExecutionService.cs  # copy matchedImageId to the step result
├── Services/ImageReferenceExtractor.cs     # include imageIds items in image references
├── Endpoints/SequencesEndpoints.cs, CommandsEndpoints.cs, StepsEndpoints.cs  # validation hooks, image-exists check per item
└── Swagger/                                # schema text and examples for the new members

src/web-ui/src/components/ (only if needed)
├── commands/WaitForImagePanel.tsx          # allow a multi-value image parameter as the image key
└── parameters/fieldTemplatePaths.ts        # list-allowed paths

tests/ (existing test projects under the repository)
docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Keep the existing layers. Put the list logic in one new Domain class. Make the resolvers produce a list in the two allowed positions. Keep the evaluator and the executor as the only places that look at more than one image.

## Key design decisions

1. **Wire form of the condition.** `imageVisible` gets `imageIds` (array of text) and `match` (text). `imageId` stays. A condition holds `imageId` or `imageIds`, not both. A stored condition without the new members is unchanged, because both members are omitted when null.
2. **Wire form of WaitForImage.** The detection target of a `waitForImage` step gets `referenceImageIds` (array of text). `referenceImageId` stays. The result gets `matchedImageId`. For a list, `referenceImageId` of the result holds the first image of the list.
3. **Expansion.** A resolver expands a list only when the placeholder is the whole value (`IsWholePlaceholder`) and the name resolves to a multi-value parameter. The resolver reads the list from the scope text (split on `|`, safe because feature 179 refuses `|` in items). The resolved condition is a new tree with `ImageIds` set. A single-value placeholder or a text with other characters keeps the old rule (feature 179 text rule and the single-value check).
4. **Field-template overlay.** For a `waitForImage` step, the overlay key `waitForImage.detectionTarget.referenceImageId` already carries a whole placeholder. The resolver treats that key like the inline field and allows a list there.
5. **Evaluation.** The evaluator calls the image evaluator for each item in order and returns true at the first match. An item that is not a stored image never matches. `negate` flips the final result. An empty list is false before `negate`.
6. **Executor.** `ExecuteWaitForImageStepAsync` loads one template set for each item and skips an item that does not load. If no item loads, the outcome is the existing `image_unavailable` with load status `missing`. Each pass checks the items in list order and stops at the first found. `matchedImageId` is that item.
7. **Save check.** `ParameterReference` gets a flag `AllowsList`. The scanner sets it for `imageVisible.imageId` and for the WaitForImage image key (inline and overlay). `ParameterValidationService` gives an error when a reference with `AllowsList = false` names a multi-value parameter as the whole value of a field that needs one value. The message names the parameter and the field path.
8. **Static check.** Unchanged. `defeatsStaticCheck` stays true for the allowed fields, so the existing `static_check_skipped` warning stays. Items of a written list are checked for existence as one image id is today, and a placeholder item is skipped.
9. **Out of scope.** No other match mode, no new condition type, no change to feature 179 rules.

## Complexity Tracking

No violations to justify.
