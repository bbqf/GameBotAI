# Research: Report which image of a list matched

## R1: Where the matched image is lost today

- Decision: Record the match in `SequenceRunner.WaitForImageAsync` and in the step-condition gate.
- Finding: `CommandExecutor` (feature 180) already sets `MatchedImageId` on its step outcome for a list. `SequenceRunner.WaitForImageAsync` wraps the list in one evaluator that returns only true or false, and `WaitForImageStepResultDetails` has no matched field.
- Rationale: The issue names `WaitForImageStepResultDetails`. This class lives in `SequenceRunner`.
- Alternatives: Re-run the match after the wait to find the item (rejected: extra screen pass, can differ from the first result).

## R2: How a later condition reads a result

- Decision: New condition type `matchedImage` that reads a per-run store.
- Finding: `commandStepOutcome` (feature 167) reads `CommandStepStateScope`. The same ambient pattern fits.
- Rationale: Same lifetime (one run), same "latest run counts" rule.
- Alternatives: Add the field to `commandOutcome` (rejected: that type tests a state text, not an image id).

## R3: How a later step reads a result

- Decision: Reserved placeholder namespace `step.` with the form `{{step.<stepRef>.matchedImageId}}`.
- Finding: `queue.` is a reserved namespace with dotted names. `ParameterScope.TryResolve` is the one resolve point. Parameter names are plain identifiers, so a dotted `step.` name cannot clash with a user parameter.
- Rationale: Reuses the existing placeholder engine and error path.
- Alternatives: A new placeholder syntax such as `{{=...}}` (rejected: new parser).

## R4: Which condition shapes give a match

- Decision: Only a top-level, non-negated `imageVisible` condition of a step gives a match. Composite and negated forms give none.
- Rationale: In a composite the "matched image" can be ambiguous (several leaves). The issue asks for the any-of check only.
- Alternatives: First true leaf of a composite (rejected: unclear meaning, more scope).

## R5: Several images match at once

- Decision: The item order of feature 180 decides. The first item that returns true is the matched image.
- Rationale: No new matching logic (spec FR-010).

## R6: Run-log place

- Decision: Add the field to `WaitForImageStepResultDetails` and to the per-step log entry made by `ExecutionLogService`.
- Rationale: These are the two places that the issue names (result and per-step log).
