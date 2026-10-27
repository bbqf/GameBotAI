# Research: Sequence parameters in ocrText.pattern and ocrText.expected

The Technical Context of the plan has no "NEEDS CLARIFICATION" item. This file records the design decisions and the facts from the code that support them.

## R-001 Where to resolve the placeholders

- **Decision**: Extend `SequenceStepConditionResolver` with an `ocrText` case.
- **Rationale**: Each condition position already calls this resolver through `SequenceRunner.ResolveCondition`: the step guard (`EvaluateStepGuardAsync`), the `If` condition, the loop condition (`EvaluateLoopConditionAsync`), and the break condition. `SequenceStepper` calls the same methods. One change thus covers all positions of FR-005 and the step-through mode of FR-010. The resolver already walks composites at each depth and builds the `.children[i]` field path.
- **Alternatives considered**: Resolve inside `SequenceStepConditionEvaluator.EvaluateOcrTextAsync`. Rejected: the evaluator has no parameter scope, and the run log would describe the condition before resolution.

## R-002 Placeholder rule and regex braces

- **Decision**: Use `TemplateSubstitutor.TrySubstitute` with no change.
- **Rationale**: The placeholder pattern is `\{\{(\w+(?:\.\w+)*)\}\}`. In `(Gathering.*){{{n}}}`, the regex engine finds the first match at the second `{`, so the result is `{` + value + `}`. A quantifier such as `{3}`, `{2,5}`, or `{2,}` has single braces and does not match. `{{3}}` matches with the name `3`. This is the existing rule of all fields (spec edge cases). FR-004 thus needs no new code, only tests.
- **Alternatives considered**: An escape syntax for a literal `{{`. Rejected: the spec adds no new syntax.

## R-003 Check of the resolved pattern

- **Decision**: The resolver compiles the resolved `pattern` (with `RegexOptions.CultureInvariant` and `OcrTextConditionRules.PatternTimeout`, the same options as the match). A failure is a resolution error with the new reason `invalid_pattern`. The step fails.
- **Rationale**: FR-008 requires a failure, not a silent false. Now `OcrTextConditionRules.Matches` catches `ArgumentException` and gives false. That path stays for a stored pattern with no placeholder, because save-time validation already rejects a bad one. The compile costs much less than 1 ms. The OCR read of the same leaf costs much more.
- **Fast path (SC-006)**: A condition with no placeholder does not get the compile. The resolver gives the same instance and makes no new allocation. A unit test asserts the same instance. The PR description gives this performance statement.
- **Alternatives considered**: Make `Matches` throw. Rejected: it changes the behavior of a condition with no placeholder (FR-014) and puts the failure after the screen read.

## R-004 Failure kind for a bad resolved pattern

- **Decision**: Add `ConditionEvaluationFailureKind.ParameterValueInvalid`. `SequenceRunner.ResolveCondition` sets it for the reason `invalid_pattern`.
- **Rationale**: FR-008 requires a failure kind that is different from the FR-007 kind. The FR-007 errors keep `ParameterUnresolved`. The name `ParameterUnresolved` is not correct for a value that resolved. The only switch on the kind is in `EvaluateStepGuardAsync`. It shows `ex.Message` for both kinds. The `If` and break paths catch all exceptions and show `ex.Message`.
- **Test**: Put the map in a small `internal static` method. A unit test asserts that `invalid_pattern` gives `ParameterValueInvalid` and `unresolved` gives `ParameterUnresolved` (plan D-002).
- **Alternatives considered**: Reuse `ParameterUnresolved`. Rejected: a wrong name makes later code harder to read.

## R-005 Run log and step-through

- **Decision**: No new code for FR-009 and FR-010. Add tests that prove the result.
- **Rationale**: `EvaluateOcrTextAsync` builds each `OcrTextEvaluationRecord` from the condition that it gets, which is the resolved condition. The `If` and break paths call `DescribeBreakCondition(resolved)`. `SequenceStepper` calls `SequenceRunner.ResolveCondition` for the `If` and break conditions, `EvaluateLoopConditionAsync` for a loop condition, and `ExecuteLeafAsync` (which calls `EvaluateStepGuardAsync`) for a step condition. A step-through reads no screen, but it still resolves the condition, so a bad placeholder fails the step-through step in the same way as a run.

## R-006 Save validation and save dry run of the sequence

- **Decision**: Add the two fields to `ParameterReferenceScanner`. Add a check of the resolved pattern with sequence defaults to `ParameterValidationService.ValidateSequence`.
- **Rationale**: `SequencesEndpoints.ValidateSequenceParametersAsync` calls `ValidateSequence` for a create, an update, and a patch. A save dry run (`dryRun: true`) runs all checks and skips only the write. Thus one change gives FR-011 and FR-012 at save time and in the save dry run. The scanner already gives `{{iteration}}` the loop rule for the loop condition and the break condition.
- **Known values**: At save time, a sequence default is the only known value. This feature does not check a queue template entry value for a pattern. The dry-run execution and the run use the resolver, so they check each supplied value.
- **Two dry-run kinds (FR-012)**: The save dry run gives `invalid_resolved_pattern` with the defaults. The dry-run execution (`POST /api/sequences/{id}/execute` with `"dryRun": true`) gives the run-time invalid-pattern failure with the supplied values. The first clarification of the spec names "a sequence default or a supplied value". The last clarification and FR-012 make this exact, and the plan follows FR-012.
- **Alternatives considered**: A queue template check, like `FindImageValueCandidates`. Rejected for this feature: the spec does not ask for it, and the run-time check covers it.

## R-007 Save-time compile of a pattern with a placeholder

- **Decision**: `OcrTextConditionRules.Validate` skips the compile when `TemplateSubstitutor.ContainsPlaceholder(pattern)` is true.
- **Rationale**: Three callers use `Validate`: `CompositeConditionValidator` (save path), `FileSequenceRepository.GuardConditionLeaves` (last guard, also for a gamepack install), and the tests. One change covers all. Some patterns with a placeholder compile by chance (`{{n}}` is a literal), but others do not (`[{{chars}}]` with a later value). The result before substitution has no meaning, so the rule skips it for all (FR-014).

## R-008 Gamepack scope

- **Decision**: No change to `GamepackValidator`.
- **Rationale**: The gamepack check does not run `ParameterValidationService` now, also not for `imageVisible.imageId`. FR-011 says that the gamepack check does not change. A gamepack that holds a placeholder pattern installs after R-007.

## R-009 Field path form

- **Decision**: `{prefix}.pattern` and `{prefix}.expected`, with `.children[i]` for each composite level. The prefixes are `condition`, `if.condition`, `loop.condition`, and `breakCondition`.
- **Rationale**: This is the form that the resolver and the scanner use for `.imageId` now. The message gives the step label as a separate value. The field path is relative to the step, for example `condition.pattern` or `if.condition.children[0].expected` (spec FR-007). It does not use a `steps[n].` form.

## R-010 Input condition and stored sequence stay the same

- **Decision**: The resolver makes a new leaf and a new composite. It MUST NOT set a property of the input. Add tests for the acceptance scenarios 5 and 6 of User Story 1.
- **Rationale**: `OcrTextStepCondition` and `CompositeStepCondition` are classes with settable properties. Thus a write to the input can change the sequence in memory. `TryResolveImage` makes a new leaf now, and `TryResolveOcrText` must do the same. A unit test compares the input condition after `TryResolve` with its first text. An integration test reads the stored sequence after a run and finds the same `{{...}}` text.

## R-011 A name of digits only

- **Decision**: No new code. Add tests for `{{3}}` (User Story 1 scenario 7, User Story 4 scenario 4).
- **Rationale**: The name rule `\w+` matches `3`. The resolver gives the reason `unresolved` for the name `3`. The scanner reports the name `3`, so the save gives `unresolvable_parameter_reference`. The OCR check never gets the text `{{3}}`.
