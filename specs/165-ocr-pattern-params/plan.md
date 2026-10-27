# Implementation Plan: Sequence parameters in ocrText.pattern and ocrText.expected

**Branch**: `165-ocr-pattern-params` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/165-ocr-pattern-params/spec.md`

## Summary

Now, `SequenceStepConditionResolver` fills only `imageVisible.imageId` before the system evaluates a condition. A `{{name}}` placeholder in `ocrText.pattern` or `ocrText.expected` stays as literal text. The plan has six parts:

1. Extend `SequenceStepConditionResolver` to resolve `ocrText.pattern` and `ocrText.expected`. Use the same placeholder rule (`TemplateSubstitutor`). After substitution, the resolver checks that the resolved `pattern` is a valid regular expression. All call sites (run, step-through, dry-run execution) use the resolver now. Thus they get the change with no edit.
2. Extend `ParameterReferenceScanner` to report the placeholders in the two fields. The save validation and the save dry run then reject an unknown name with the existing code `unresolvable_parameter_reference`.
3. Change `OcrTextConditionRules.Validate`. It must not compile a `pattern` that contains a placeholder.
4. Add a save-time check with the sequence defaults. When each name of a `pattern` has a default, substitute the defaults and compile the result. A bad result gives the new code `invalid_resolved_pattern`.
5. Add tests for each acceptance scenario. This includes the new scenarios: the input condition keeps its `{{...}}` text, the stored sequence does not change, and `{{3}}` is a placeholder.
6. Update the OpenAPI field descriptions, `docs/architecture.md`, `specs/STATUS.md`, and `CHANGELOG.md`. Set the spec `**Status**:` line to `Implemented` (FR-017).

The gamepack check is out of scope (FR-011). It does not change.

## Technical Context

**Language/Version**: C# on .NET (current solution)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle, `System.Text.RegularExpressions` (all in use now). No new package.
**Storage**: N/A. The stored sequence format does not change. Resolution makes a new condition for the evaluation only (FR-006).
**Testing**: xUnit and FluentAssertions in `tests/unit`, `tests/integration`, and `tests/contract`
**Target Platform**: Windows service that drives Android emulators with ADB
**Project Type**: Web service with a web UI. This feature does not change the web UI.
**Performance Goals** (SC-006): A condition with no placeholder keeps the current fast path. The resolver gives the same condition instance and makes no new allocation. A unit test asserts the same instance. A condition with a placeholder adds one substitution and one regex parse for each evaluation, less than 1 ms. The OCR read of the same condition takes much more time. The PR description gives this performance statement.
**Constraints**: The behavior of `imageVisible.imageId` MUST NOT change (FR-016). A condition with no placeholder MUST have the same validation and the same evaluation as now (FR-014).
**Scale/Scope**: About 8 source files change. No new source file. About 8 new or changed test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Result |
|------|--------|
| Build and test state | Pass. Run the full build and tests before work starts and before each commit. |
| STE (Principle VI) | Pass. All plan artifacts, new error messages, and new code comments use STE. |
| Phone check (Principle III) | Pass. No web UI change. No phone support level applies. |
| Code quality (Principle I) | Pass. The change extends 3 existing helpers. Each new method stays under 50 lines. Method names use CamelCase with no underscore. |
| Testing (Principle II) | Pass. Unit tests for the resolver, scanner, rules, and validation service. Integration tests for the run log, the step-through, and the stored sequence. Contract tests for the save errors and the OpenAPI text. |
| UX (Principle III) | Pass. Each new error names the field path and gives a remedy (see contracts). |
| Performance (Principle IV) | Pass. Goal stated above (SC-006). A condition with no placeholder keeps the fast path: same instance, no new allocation. A unit test asserts the same instance. The PR description gives the performance statement. |
| Living docs (Principle V) | Pass. Plan includes `docs/architecture.md` (two sections and the "Last reviewed" date), `specs/STATUS.md`, `CHANGELOG.md`, and the spec `**Status**:` line (FR-017). |
| Version bump | Planned. Set `patch` before the PR push. |

Post-design re-check: no new violation. The Complexity record table stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/165-ocr-pattern-params/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ocr-text-parameters.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Made by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   │   └── OcrTextConditionRules.cs          # CHANGE: TryValidatePattern; no compile when a placeholder is present
│   ├── Parameters/
│   │   ├── SequenceStepConditionResolver.cs  # CHANGE: resolve ocrText.pattern and ocrText.expected; regex check
│   │   ├── ParameterReferenceScanner.cs      # CHANGE: scan ocrText.pattern and ocrText.expected; IsRegexPattern flag
│   │   └── ParameterResolutionError.cs       # CHANGE: reason invalid_pattern and its message
│   └── Services/
│       ├── ParameterValidationService.cs     # CHANGE: invalid_resolved_pattern check with sequence defaults
│       ├── SequenceStepConditionEvaluator.cs # CHANGE: new failure kind ParameterValueInvalid
│       └── SequenceRunner.cs                 # CHANGE: ResolveCondition maps the reason; guard switch shows the message
└── GameBot.Service/
    └── Swagger/
        └── OcrTextConditionSchemaFilter.cs   # CHANGE: pattern and expected text name the placeholder rule

tests/
├── unit/Parameters/SequenceStepConditionResolverTests.cs       # CHANGE: ocrText cases, same instance, input not changed, {{3}}
├── unit/Parameters/ParameterReferenceScannerTests.cs           # CHANGE: ocrText cases
├── unit/Parameters/ParameterValidationOcrPatternTests.cs       # NEW: unknown name, {{3}}, invalid_resolved_pattern
├── unit/Sequences/OcrTextConditionRulesTests.cs                # CHANGE: no compile when a placeholder is present
├── unit/Sequences/SequenceRunnerConditionFailureKindTests.cs   # NEW: reason to failure kind map
├── integration/Sequences/OcrTextConditionParameterRunTests.cs  # NEW: run, run log, failures, break, step-through, stored sequence
├── contract/Sequences/OcrTextConditionContractTests.cs         # CHANGE: save errors, save dry run, dry-run execution
└── contract/Sequences/OcrTextConditionOpenApiTests.cs          # CHANGE: new description text

specs/165-ocr-pattern-params/spec.md        # CHANGE: Status Implemented (FR-017)
docs/architecture.md                        # CHANGE: parameter section, ocrText section, "Last reviewed"
specs/STATUS.md                             # CHANGE: add 165 as Implemented, in the last task only
CHANGELOG.md                                # CHANGE: entry for 165 (#347)
installer/versioning/version.override.json  # CHANGE: patch, before the PR push
```

**Structure Decision**: Keep the layout of the repo. All logic goes in `GameBot.Domain`, next to the code that resolves `imageVisible.imageId` now. The service project changes only the OpenAPI text.

## Design

### D-001 Resolver (FR-001 to FR-008, FR-015, FR-016, SC-006)

`SequenceStepConditionResolver`:

- `HasPlaceholder` gets a case for `OcrTextStepCondition`. It is true when `Pattern` or `Expected` contains a placeholder.
- When `HasPlaceholder` is false, the resolver gives the input instance. It makes no new object (SC-006).
- `TryResolveNode` gets a case for `OcrTextStepCondition`. It calls a new method `TryResolveOcrText`.
- `TryResolveOcrText` resolves `Pattern` with the field path `{prefix}.pattern` and `Expected` with `{prefix}.expected`. It copies a field with no placeholder as it is.
- A shared method `TryResolveText` does the steps that `TryResolveImage` does now. These steps are `TrySubstitute`, the empty or white-space check on the full result, and the record of each used key. `TryResolveImage` also calls this method, so the two leaf types stay the same.
- After the substitution of `Pattern`, the resolver calls `OcrTextConditionRules.TryValidatePattern`. A failure gives `ParameterResolutionError(firstKey, "{prefix}.pattern", ParameterResolutionReasons.InvalidPattern, resolvedPattern)`.
- The result is a new `OcrTextStepCondition` with the same `Region`, `Parser`, and `Negate`. The resolver does not resolve `Region` and `Parser` (FR-016).
- The value goes into the field with no regex escape (FR-015).
- The input condition does not change (FR-006). The resolver makes a new leaf and a new composite. It does not set a property of the input.
- `{{3}}` is a placeholder with the name `3` (FR-007). An unknown `3` gives the reason `unresolved`.

### D-002 Run-time errors (FR-007, FR-008)

- `ParameterResolutionReasons` gets `InvalidPattern = "invalid_pattern"`.
- `ToMessage` gets this text: `Step '<label>': field '<path>' resolved to the pattern '<pattern>', which is not a valid regular expression. Change the value of '<name>' or the pattern.`
- `ConditionEvaluationFailureKind` gets `ParameterValueInvalid`. This is the own failure kind of FR-008. The FR-007 errors keep `ParameterUnresolved`.
- `SequenceRunner.ResolveCondition` maps `InvalidPattern` to `ParameterValueInvalid`. It maps all other reasons to `ParameterUnresolved`, as now.
- Put the map in a small `internal static` method, for example `ToFailureKind(string reason)`. `ResolveCondition` calls it. Thus a unit test can examine the map before the resolver gives `invalid_pattern`.
- A unit test asserts the two maps: `invalid_pattern` gives `ParameterValueInvalid`, and `unresolved` gives `ParameterUnresolved`. A second test calls `ResolveCondition` with an unknown name in `imageVisible.imageId`. It asserts the kind `ParameterUnresolved`. Write these tests with the map change, before the run tests.
- Update the XML comment of `ResolveCondition`. It names both kinds and the `ocrText` fields.
- The step-guard switch in `EvaluateStepGuardAsync` shows `ex.Message` for the two kinds. The `If` and break paths show `ex.Message` now.
- A break condition keeps the existing rule (FR-008 exception, feature 066 FR-002a). An unresolved name, an empty result, or an invalid resolved pattern gives "No break". The run log records the error, and the loop continues. This is the same as for `imageVisible`.

### D-003 Run log and step-through (FR-009, FR-010)

No new code. The callers give the resolved condition to `Describe` and to the evaluator. `OcrTextEvaluationRecord` copies `Pattern` and `Expected` from the condition that it gets. Thus the run log line shows the resolved value. `SequenceStepper` calls `SequenceRunner.ResolveCondition` and `EvaluateStepGuardAsync`, so the step-through resolves in the same way. Tests prove it (research R-005).

### D-004 Scanner (FR-011, FR-013)

- `ParameterReferenceScanner.ScanCondition` gets a case for `OcrTextStepCondition`. It adds a reference for each key in `Pattern` (field path `{prefix}.pattern`) and in `Expected` (field path `{prefix}.expected`).
- `DefeatsStaticCheck` stays `false`, because these fields name no image.
- The record `ParameterReference` gets a new optional member `IsRegexPattern` (default `false`). The scanner sets it to `true` for the `pattern` field.
- `ParameterValidationService.AddReferenceIssues` then gives the existing error `unresolvable_parameter_reference` for an unknown name. This includes `{{3}}` when no parameter `3` is known.
- The existing `{{iteration}}` rule applies. The scanner scans the loop condition and the break condition with `insideLoop: true`, so `{{iteration}}` is legal there.

### D-005 Save-time rules (FR-012, FR-014)

- `OcrTextConditionRules` gets `public static bool TryValidatePattern(string pattern, out string? problem)`.
- `Validate` calls it only when `TemplateSubstitutor.ContainsPlaceholder(Pattern)` is false. The rule "exactly one of `expected` and `pattern`" does not change.
- `ParameterValidationService.ValidateSequence` gets a new private method `AddResolvedPatternIssues`. It examines each distinct `(StepLabel, FieldPath, SourceText)` that has `IsRegexPattern`.
- For each key, the method finds a sequence declaration with a non-null `Default`. A built-in name, `iteration`, or a name with no default stops the check for that field. The run-time check then applies.
- When each key has a default, the method substitutes and calls `TryValidatePattern`. A failure gives `invalid_resolved_pattern` with the field path and the first key.

### D-006 Two dry-run kinds (FR-011, FR-012)

| Dry run | Values | Check | Error |
|---------|--------|-------|-------|
| Save dry run: `POST`, `PUT`, `PATCH` on `/api/sequences` with `"dryRun": true` | Sequence defaults only | `ValidateSequence` (D-004, D-005) | `400` with `unresolvable_parameter_reference` or `invalid_resolved_pattern` |
| Dry-run execution: `POST /api/sequences/{id}/execute` with `"dryRun": true` | Supplied values and defaults | Resolver (D-001) | The step fails with the run-time message (D-002) |

The save dry run runs the same `ValidateSequence` as the save. It skips only the write.

### D-007 OpenAPI text

`OcrTextConditionSchemaFilter.FieldDescriptions` gets new text for `pattern` and `expected`. The text says that the field can hold `{{name}}` placeholders of sequence parameters. It says that the value goes in with no escape. It says that `{{{name}}}` gives the value between literal braces. The schema shape does not change.

### D-008 Docs and spec status (FR-017)

At the end of the work, do these steps:

- Set the spec `**Status**:` line to `Implemented`.
- In the same task, add the row for feature 165 to `specs/STATUS.md` with the status `Implemented`. Do not add the row before with a different status.
- In `docs/architecture.md`, name `ocrText.pattern` and `ocrText.expected` as fields that resolve placeholders. Update the "Last reviewed" date.
- Add a `CHANGELOG.md` entry for feature 165 (issue #347).

### D-009 Performance statement (SC-006)

The PR description gives these items:

- A condition with no placeholder keeps the fast path. The resolver gives the same instance and makes no new allocation.
- A unit test asserts the same instance.
- A condition with a placeholder adds one substitution and one regex parse for each evaluation. This takes less than 1 ms.

### Out of scope: gamepacks

A gamepack install writes a sequence through `FileSequenceRepository`. Its last guard calls `OcrTextConditionRules.Validate`. After D-005, it accepts a `pattern` with a placeholder. The gamepack check does not run the parameter reference check now, also not for `imageVisible.imageId`. FR-011 keeps this scope. A gamepack author gets the unknown-name error from the save dry run of the sequence, or at run time.

## Complexity record

No violation. The table is empty.
