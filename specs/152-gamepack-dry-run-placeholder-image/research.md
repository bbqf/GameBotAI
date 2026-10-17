# Research: Gamepack dry run skips parameter placeholder image ids

## R1: Where to put the skip

- **Decision**: Filter in `GamepackValidator.CheckReferences`, in the sequence loop, before `ReportMissing`.
- **Rationale**: `CheckReferences` is the only place that reports a missing image for a sequence. `GamepackReferences.ImageIds` has other users, so it keeps giving all ids. `ImageIds` already walks nested steps (`Body`, `ElseBody`) and all condition slots (`Condition`, `BreakCondition`, `If`, loops, `ConditionExpression`). FR-002 holds with no new walk.
- **Alternatives**: Filter inside `ImageIds` (rejected: other callers need all ids). Skip the whole check for a sequence with parameters (rejected: it hides real errors, breaks FR-003).

## R2: Whole-placeholder rule

- **Decision**: Add a public `TemplateSubstitutor.IsWholePlaceholder(string? value, out string key)`. The private `ParameterValidationService.IsWholePlaceholder` calls it. The validator uses it.
- **Rationale**: One rule in one place. The rule: after trim, the text is exactly `{{key}}` and holds one key. `{{ name }}` is not matched by the engine pattern `\{\{(\w+(?:\.\w+)*)\}\}`, so it has no key and is not whole. The dry run checks it as before and reports a problem.
- **Alternatives**: Copy the method into the validator (rejected: two rules can drift). Make the method `internal` with `InternalsVisibleTo` (rejected: `TemplateSubstitutor` owns the pattern, so the helper belongs there).

## R3: Parameter name match

- **Decision**: Ordinal match against `sequence.Parameters[].Name`.
- **Rationale**: `ParameterValidationService` uses `StringComparer.Ordinal` for declared names, and `ExtractKeys` is ordinal. A different letter case is a different name.
- **Alternatives**: Ignore case (rejected: it would accept a gamepack that the run time cannot bind).

## R4: Built-in names

- **Decision**: A built-in name such as `{{iteration}}` or `{{queue.emulatorSerial}}` does not count. Only a declared parameter counts.
- **Rationale**: FR-001. Built-in values are not image ids.

## R5: Scope per sequence

- **Decision**: Build the declared set inside the per-sequence loop.
- **Rationale**: FR-006. A parameter of another sequence must not count.

## R6: Null or empty parameters

- **Decision**: A sequence with no parameter list (null or empty) gives an empty declared set. `CommandSequence.Parameters` is a non-null collection, but a gamepack file can come from JSON with `parameters` null or missing. The code reads it with a null-safe pattern, so the set is empty in each case.
- **Rationale**: Clarification (loop 2). Every placeholder image id is then checked as before.
- **Alternatives**: Treat null as "any name allowed" (rejected: it hides real errors).

## R7: Commands

- **Decision**: No change to the command image check.
- **Rationale**: Clarification. A command image id is not in scope of issue #326. A test locks this.

## R8: OpenAPI text

- **Decision**: Search `C:\src\GameBot\src\GameBot.Service\Swagger\GamepackSchemaFilter.cs` for `dry run` and `reference`. The current text has no list of image reference rules (it only lists the problem categories). Add one sentence only if a description lists those rules.
- **Rationale**: Constitution Principle V. "No change needed" is an allowed result and the task records it.
- **Alternatives**: Always add a sentence (rejected: no place lists the rules).

## R9: Test placement

- **Decision**: Integration tests (dry run, install) in `C:\src\GameBot\tests\integration\Gamepacks`, with the minimal JSON of issue #326 as fixture. Unit tests for the helper and the validator in `C:\src\GameBot\tests\unit`.
- **Rationale**: Clarification (loop 2). The existing dry run tests are in the same folder.

## R10: Unknowns

No NEEDS CLARIFICATION remain.
