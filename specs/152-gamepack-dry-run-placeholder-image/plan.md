# Implementation Plan: Gamepack dry run skips parameter placeholder image ids

**Branch**: `152-gamepack-dry-run-placeholder-image` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/152-gamepack-dry-run-placeholder-image/spec.md`

## Summary

The reference check of a gamepack dry run reports a `reference` problem for an image id such as `{{novaOptionImage}}`, although the sequence declares that parameter (GitHub issue #326). The fix has three parts:

1. Make the whole-placeholder rule public and shared: `TemplateSubstitutor.IsWholePlaceholder(string?, out string key)`. The private copy in `ParameterValidationService` calls it. The field-template result does not change.
2. In `GamepackValidator.CheckReferences`, for each sequence, build the set of declared parameter names (ordinal). Remove each image id that is a declared whole placeholder before `ReportMissing`. A sequence with no parameters gives an empty set (null is also an empty set), so all ids are checked as before.
3. Add tests: integration tests in `tests\integration\Gamepacks` (dry run and install), and unit tests for the helper and the validator. Check the OpenAPI text in `GamepackSchemaFilter.cs`. Add one sentence only if it lists the image reference rules.

No new type, no stored data, no change to command checks, to `POST /api/sequences`, or to the queue template save.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (ASP.NET Core minimal API)
**Primary Dependencies**: None new. Uses `System.Text.RegularExpressions` (already in `TemplateSubstitutor`)
**Storage**: N/A (no stored data change)
**Testing**: xUnit. Unit tests in `tests\unit`, contract tests in `tests\contract`, integration tests in `tests\integration\Gamepacks`
**Target Platform**: Windows service host (GameBot.Service)
**Project Type**: Web service (backend only; no web UI change)
**Performance Goals**: No change. The filter is one set lookup for each image id
**Constraints**: Field-template result (`invalid_field_template_value`) stays the same. Problem message text stays the same
**Scale/Scope**: 2 source files change (`TemplateSubstitutor.cs`, `ParameterValidationService.cs`) plus `GamepackValidator.cs`; tests; OpenAPI text check

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI).

*Phone check (Principle III)*: The feature does not change the web UI. Not applicable.

| Principle | Result |
|-----------|--------|
| I. Code quality | Pass. One shared helper replaces a private copy. The method is small and has XML docs |
| II. Testing | Pass. Unit tests for the helper and the validator. Integration tests for the dry run and the install. Existing field-template tests guard the moved rule |
| III. UX consistency | Pass. No UI change. Problem text stays the same |
| IV. Performance | Pass. One set lookup for each image id |
| V. Living documentation | Pass. OpenAPI text is checked (R7). The task records the result. The version bump is done before the push |
| VI. STE | Pass. All new text uses STE |

Post-design re-check: Pass. The design adds no new type and no new dependency.

## Project Structure

### Documentation (this feature)

```text
specs/152-gamepack-dry-run-placeholder-image/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── dry-run-reference-check.md
└── tasks.md             # Created by /speckit.tasks, not by this command
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Utils/TemplateSubstitutor.cs                  # add public IsWholePlaceholder
│   ├── Services/ParameterValidationService.cs        # private copy calls the shared helper
│   └── Gamepacks/GamepackValidator.cs                # filter in CheckReferences
└── GameBot.Service/Swagger/GamepackSchemaFilter.cs   # check only; edit only if a rule list exists

tests/
├── unit/                                             # helper tests, validator tests
├── contract/Sequences/                               # existing field-template tests (guard, no edit)
└── integration/Gamepacks/                            # new dry run test and install test (fixture: issue #326 JSON)
```

**Structure Decision**: Existing single-solution layout. The change is in the Domain project. New gamepack tests are integration tests in `tests\integration\Gamepacks`, next to `GamepackDryRunIntegrationTests.cs`.

## Complexity Tracking

No constitution violation. No entry needed.
