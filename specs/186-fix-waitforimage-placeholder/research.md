# Research: Resolve parameter placeholders in inline WaitForImage

## R1. Where the placeholder is lost

- Decision: The loss is in `SequenceRunner.ApplyScope`. It substitutes `Action` and `CommandId`, but it copies `WaitForImage` unchanged.
- Rationale: `CommandExecutor` calls `CommandStepResolver.TryResolve` for command steps, and that resolver handles `waitForImage.detectionTarget` with `allowList: true`. The sequence path never calls it. `imageVisible` works because `EvaluateStepGuardAsync` calls `ResolveCondition`.
- Alternatives considered: Substitute inside `ApplyScope` with `TemplateSubstitutor`. Rejected: it does not know the multi-value rule, `fieldTemplates`, or the error type.

## R2. How to resolve

- Decision: Add `CommandStepResolver.TryResolveWaitForImage` that wraps `TryResolve` with a temporary command step. The sequence runner calls it.
- Rationale: One implementation of the rules from features 114 and 180 (whole placeholder, multi-value expansion, overlay, normalization). No drift.
- Alternatives considered: Make `TryDetection` public. Rejected: it takes a `CommandStep` and the `fieldTemplates` overlay by design.

## R3. Log fields

- Decision: Add `matchedImageId` to `WaitForImageDetailAttributes` and `ExecutionLogWaitForImageDetailAttributesDto`. Keep `referenceImageId` as the first resolved id. Do not add a list field.
- Rationale: `WaitForImageStepResultDetails.matchedImageId` exists, and `SequenceExecutionService` puts it in the attribute map, but the typed record drops it. The issue asks for `matchedImageId` only. A list field is a new feature (non-goal).
- Alternatives considered: A new `referenceImageIds` field in the log. Rejected as scope growth. The log shows the first id in `referenceImageId` and the match in `matchedImageId`.

## R4. Error for an unknown parameter

- Decision: Fail the step with the unresolved-parameter message, as the guard does. Do not wait and do not give `image_unavailable`.
- Rationale: Spec clarification 1. A literal `{{name}}` must never reach the evaluator.
- Alternatives considered: Treat as `image_unavailable`. Rejected: it hides the real cause.

## R5. Dry run and step-through

- Decision: Resolve the config before the dry-run check, right after `ApplyScope`. A dry run with a valid value passes. A dry run with an unknown name reports the same error as a real run.
- Rationale: A dry run then finds a bad placeholder early, and the step key and scope are the same in both modes.
- Alternatives considered: Skip resolution in dry run. Rejected: a dry run would hide a fault that a real run shows.

## R6. Documents

- Decision: Update `docs/architecture.md` (if it names WaitForImage resolution or the log fields), `CHANGELOG.md`, and `specs/STATUS.md`. Mark features 114, 180, and 181 as iterated by 186 in their `Status` lines if the repo convention needs it.
- Rationale: Constitution Principle V.
