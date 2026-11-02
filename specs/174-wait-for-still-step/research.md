# Research: Wait for Still Step

All items are resolved. No NEEDS CLARIFICATION remains.

## R1. Where does the step live in the model?

**Decision**: Add `WaitForStill` as a new sequence action type (`ActionTypes.WaitForStill = "WaitForStill"`),
a service-level action. Add it to `SequenceActionTypes.All`. Store the parameters in
`SequenceActionPayload.Parameters`.

**Rationale**: `WaitForImage` has a dedicated `WaitForImageConfig` because it needs a detection target
and it must also work as a command step. `WaitForStill` has no image. The `quiz-lookup` action (feature
142) is the closest pattern: a service-level action with its own payload class, validator, handler, and
outcome. This pattern needs no change to `SequenceStep`, to the command step model, or to the persistence
format. `SequenceActionTypes.All` is the one list behind the validator, the payload validation service, and
the OpenAPI enum, so one edit updates all three.

**Alternatives considered**:
- A new `WaitForStillConfig` property on `SequenceStep` and a new `CommandStepType`. Rejected: it touches
  about 20 files (command DTOs, repository, gamepack readers, parameter resolver) for no gain.
- Reuse `WaitForImageConfig` with a flag. Rejected: it mixes two meanings and breaks FR-011.

## R2. Does the step apply to command steps too?

**Decision**: No. The step is a sequence step only. The spec says "in sequences" (FR-001). The
`WaitForImage` command step type stays as it is.

**Rationale**: The issue is about a wait after a pan inside a sequence. A command step has no outcome
branch model. This keeps the change small.

**Alternatives considered**: Add a `WaitForStill` command step type. Rejected: not in the spec.

## R3. How does the runner treat the step?

**Decision**: `SequenceRunner.IsServiceLevelAction` returns true for `WaitForStill`. The step then
follows the existing path: dry run skip (`skipped_dry_run`), dispatch through `actionDispatcher`,
`RecordServiceLevelActionAsync`. That method records the outcome of the dispatch in `stepOutcomes` for
quiz actions today. Change it to do the same for `WaitForStill`. The step status is `Succeeded` for both
outcomes, as it is for `WaitForImage` at a timeout.

**Rationale**: FR-009 asks for the same behavior as `WaitForImage` in validation, execution, and outcome
use. Dry run, step-through (`SequenceStepper` line 408 already uses `IsServiceLevelAction`), the stop
signal, and the log entry all work with no new code in the runner.

**Alternatives considered**: A new runner branch like `RecordWaitForImageStepAsync`. Rejected: it
duplicates code and adds a second place for the dry-run rule.

## R4. How are two frames compared?

**Decision**: A pure static class `FrameDifference` in `GameBot.Domain.Vision`. It takes two `Bitmap`
frames of the same size and an optional `PixelRegion`. It returns the mean absolute difference of the
R, G, and B bytes over the region, scaled to 0..1 (sum of absolute differences divided by
`255 * 3 * pixelCount`). It reads the pixels with `LockBits` (32bpp ARGB) and reads every pixel of the
region. If the two frames have different sizes, the function returns 1 (not the same).

**Rationale**: The spec fixes the metric (Assumptions): mean absolute difference, scale 0 to 1. A full
read of a 1280x720 region is about 920,000 pixels and takes a few milliseconds with `LockBits`. A
sampled compare is not needed. The poll is every 500 ms.

**Alternatives considered**: A perceptual hash. Rejected: it hides small changes inside the region and
does not match the `maxDiff` meaning. A GPU or OpenCV call. Rejected: a new dependency.

## R5. How does the stable time work?

**Decision**: A pure class `WaitForStillTracker` (no clock, no capture). The handler calls
`Observe(difference, now)` for each new frame.
- The first frame has no previous frame. It is the baseline. `Observe` is not called for it.
- For each next frame, the difference to the previous frame is computed.
- If `difference <= maxDiff`: if no stable start is set, set it to the time of the previous frame (the
  moment the region was last seen at this state). The step is `still` when `now - stableStart >= stableMs`.
- If `difference > maxDiff`: clear the stable start.
- After a failed capture the handler calls `Reset()`: the previous frame time and the stable start are cleared.
- The handler compares each frame with the previous frame, not with the first stable frame. This is the
  rule of the spec (clarification 4).

**Rationale**: With the default `stableMs` of 300 and a capture interval of 500 ms, the earliest result
is two frames that are the same (one interval, 500 ms). The step cannot see a still period shorter than
the interval. SC-001 states "plus one capture interval", so this is allowed. Setting the stable start to
the time of the previous frame gives the earliest correct result.

**Alternatives considered**: Count a fixed number of same frames. Rejected: the spec uses time. Compare
with the first stable frame (drift guard). Rejected: the spec says consecutive captures.

## R6. How does the handler get frames, and how long does it wait?

**Decision**: `WaitForStillHandler` in `GameBot.Service.Services.WaitForStill` uses `ISessionFrameSource.Capture(sessionId)`
(the same source as the quiz reader; it returns a detached copy of the latest frame). The loop:
1. Compute `deadline = start + timeoutMs`.
2. Capture a frame. Compare with the previous frame. Update the tracker.
3. If still, return `still`.
4. Wait `CaptureIntervalMs` (shortened so that no pass starts at or after the deadline; use the
   `WaitForImageSchedule.NextWait` rule, so both waits share one schedule). Stop on the cancellation token.
5. At the deadline, return `timeout_elapsed`.

Dispose every bitmap. Keep only the previous frame.

**What `WaitForImage` does (checked in `SequenceRunner.PollImageConditionAsync`)**:
- The image check returns false (no match, or no frame): the loop waits one poll interval and checks
  again until the deadline. This is a skip and retry, not a failure.
- The image check throws: the loop stops, waits until the deadline, and returns `image_unavailable`.
  There is no retry after a throw.
- No evaluator (no session): the step waits until the deadline and returns `image_unavailable`.
- Cancel: `Task.Delay` throws `OperationCanceledException`. It passes up to the runner. The step records
  no outcome.

**What `WaitForStill` does (FR-009, FR-010)**: the same, except that `image_unavailable` does not exist
for this step. So:
- A failed capture (null frame or a non-cancel exception) is a skip and retry: the handler calls
  `tracker.Reset()` (this clears the previous frame time and the stable start), disposes the previous
  frame, waits one capture interval, and captures again. The next good frame is a new baseline, so the
  stable timer starts again from zero. At the deadline the result is `timeout_elapsed`. The time spent
  in a failed capture never counts as stable time.
- No session id: the handler waits until the deadline and returns `timeout_elapsed` with the message
  `the screen could not be read`.
- Cancel: the handler MUST let `OperationCanceledException` pass up (no `catch` that hides it). The step
  records no outcome, as for `WaitForImage`. A catch for capture errors MUST use a filter that excludes
  cancellation.

**Rationale**: This gives the same user-visible behavior as `WaitForImage` for a capture fault and adds
no outcome name.

**Alternatives considered**: Return `failed` on a capture error. Rejected: FR-010 and FR-009. Use the
session capture subscription. Rejected: `ISessionFrameSource` is the existing seam and is easy to fake.

## R7. How is the region validated and clipped?

**Decision**: Validate with `PixelRegion.Validate` (x and y 0 or more, width and height more than 0; all
four fields required when a region is given). At run time, clip with `PixelRegion.ClipTo(frameWidth,
frameHeight)`. A null result (region fully outside) makes the step return the outcome `failed` with a
clear message: "region is outside the screen". A partly outside region is cut to the screen.

**Rationale**: This follows clarification 2. `PixelRegion` is the one place for the region rules (feature
130). The `failed` outcome fails the step, as in the spec ("the step fails with a clear error").

## R8. Validation rules for the other fields

**Decision** (in `WaitForStillPayload.Validate`, called from `SequenceStepValidationService` and from the
`FileSequenceRepository` load check, as `QuizLookupPayload` is):
- `stableMs`: integer, 1 or more, default 300.
- `maxDiff`: number from 0 to 1, default 0.01.
- `timeoutMs`: integer, 1 or more, default 5000.
- `stableMs` greater than `timeoutMs`: error (clarification 1).
- `region`: object with x, y, width, height, as above. Optional.
- A value that is a `{{name}}` parameter reference is not checked at save time (numeric slots accept a
  reference, as in feature 078). The parameter resolver substitutes the value before the handler reads it.
- An unknown top-level payload field is an error, as for `reschedule-self`.

**Rationale**: Clear errors at save time (User Story 3, scenario 3). The numeric rules match the spec.

**Alternatives considered**: Allow `stableMs` 0. Rejected: the step would end with the first pair of
frames, which is not a wait.

## R9. How do later steps use the outcome?

**Decision**: The recorded outcome of the step is `still` or `timeout_elapsed`. Extend
`StepOutcomeStates.IsExpectedStateAllowed` to accept both names. `Matches` also treats `still` as
`success`, so a `commandOutcome` condition on `success` works for a still screen. A condition on
`timeout_elapsed` matches only a timeout. Update `docs` text, the OpenAPI text for `commandOutcome`, and
`validation.ts` in the web UI for the same two names.

**Rationale**: User Story 3 needs branching on the two outcomes. `WaitForImage` records `success` in
`stepOutcomes` today and keeps the exit condition only in the log, so there is no precedent for outcome
names there. The quiz precedent (feature 142) is the pattern for new names. `WaitForImage` and its
recording stay as they are (FR-011).

**Alternatives considered**: Record `success` only and branch on a log field. Rejected: a condition
cannot read a log field.

## R10. Execution log

**Decision**: Add an execution-log detail entry for the new step in `SequenceExecutionService`
(next to the quiz block). Its `stepType` is `WaitForStill`, its `actionOutcome` and `reasonCode` are the
outcome, and its message holds the elapsed time and the last difference, for example "still after 620 ms;
last difference 0.0021".

**Rationale**: Without a special case the step shows as a generic command step with a missing command
name. The quiz block shows the correct pattern.

## R11. Web UI

**Decision**: Add `WaitForStillFields.tsx` (inputs: region x, y, width, height; `stableMs`; `maxDiff`;
`timeoutMs`; each as a `ParameterizableField`). Route the type in `PrimitiveActionFields.tsx` as quiz does
(`isQuizActionType` pattern). Add one "Add wait for still step" button next to the quiz buttons in
`SequencesPage.tsx`. Show the default values. Phone support level: **desktop-only**, the same as the
`WaitForImage` field form (the Authoring area is desktop-only).

**Rationale**: The sequence editor has no `WaitForStill` form without it. `WaitForImage` has fields only
on the desktop editor and in the Commands page panel. The Commands page is out of scope (R2).

## R12. Poll interval and region size

**Decision**: Use `AppConfig.CaptureIntervalMs` as the poll interval (default 500 ms, minimum 50 ms
through `IConfigApplier`). The handler takes `AppConfig` through its constructor.

**Rationale**: The Assumptions say the capture rate equals the rate of `WaitForImage`. Both use this
value.

## R13. Docs and contracts

**Decision**: Update `docs/architecture.md` (the action list near lines 768 and 1232, a new paragraph
"WaitForStill step (feature 174)", and "Last reviewed"). Add a `CHANGELOG.md` entry with the version note.
Update the OpenAPI payload text in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs` (this is the only file for the schema change). Write the contract in
`contracts/wait-for-still-step.md`. Set the spec status to Implemented at the end and update
`specs/STATUS.md`. No separate step-type doc exists besides `docs/architecture.md` and the OpenAPI text
(`docs/parameters.md` lists parameter paths for `WaitForImage` only; the new step has no image or
parameter path list change, because the generic action form handles numeric slots).

## R14. Test order

Tests are written red first: write the test, see it fail, then write the code. A test that must compile
green needs the code it uses to exist as a stub. So the task order places a test task after the task
that creates the type it needs (for example, the payload class before the payload test), and the
behavior is filled in after the red run.
