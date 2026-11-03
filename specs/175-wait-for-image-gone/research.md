# Research: Wait Until an Image Is Gone

## R1: Where does the option go?

- **Decision**: Add `until` and `absentCaptures` to the sequence `WaitForImage` step
  (`SequenceStep.WaitForImage`, type `WaitForImageConfig`, built from `primitiveAction.payload`).
- **Rationale**: The spec chooses the option. The sequence path reuses `detectionTarget`, the payload
  validator, the editor form, and the outcome model. The sequence runner already gives the outcome names
  `image_detected` and `timeout_elapsed` for this step.
- **Alternatives considered**: A new step type (rejected by FR-008). Support in the command-step
  `WaitForImage` (`CommandExecutor`) too: rejected. The spec talks about the sequence model and the
  sequence editor. The command path has its own DTOs, panel, and outcome states
  (`CommandStepOutcomeStates`). It stays in present mode. A follow-up feature can add it. Note: the shared
  class `WaitForImageConfig` gets the new properties, but no command DTO sets them, so they keep their
  defaults there.

## R2: How to tell "image not found" from "capture failed"?

- **Decision**: In the absent loop, call the existing condition evaluator with `Mode` `Present`. When it
  returns true, the image is seen. When it returns false, read `ImageDetectionPassScope.Current?.LastPass`.
  If `FrameAvailable` is true, the capture lacks the image. If `FrameAvailable` is false, or no pass was
  written, the capture failed.
- **Rationale**: The adapter with `Mode` `Absent` returns `!isVisible`. It would count a missing frame as
  absent, which FR-007 forbids. `ImageDetectionPassScope` (feature 166) already carries the frame state
  of each pass and the runner already pushes a scope. The match rule, threshold, and region handling stay
  the same as present mode (spec assumptions).
- **Alternatives considered**: Use `Mode` `Absent` (wrong for failed captures). Add a new evaluator
  delegate that returns a tri-state (a large change in many call sites).
- **Check in design**: Confirm in `ImageMatchEvaluator` (`src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`, line 64) that it writes `LastPass` on every pass,
  including a pass with no frame. If a test shows a gap, fix the evaluator in the same change.

## R3: Where does the count rule live?

- **Decision**: A pure class `AbsentCaptureCounter` in `GameBot.Domain.Commands.Execution` with
  `Observe(PassKind kind)` returning true when `absentCaptures` is reached. Kinds: `Seen`, `Absent`,
  `Failed`. `Seen` and `Failed` set the count to 0. `Absent` adds 1.
- **Rationale**: A pure class is easy to test (SC-003, 100% of test cases) and keeps the runner loop short.
- **Alternatives considered**: Inline counter in the loop (hard to test in isolation).

## R4: Poll schedule and deadline

- **Decision**: Use `WaitForImageSchedule.NextWait` and the same loop shape as `PollImageConditionAsync`:
  the first pass runs at once; no pass starts at or after the deadline; the last wait ends 50 ms before the
  deadline.
- **Rationale**: FR-009 and the assumption "same rate as present mode". SC-002 holds by the same proof as
  feature 166.
- **Alternatives considered**: A separate timer (more code, no gain).

## R5: Capture source not available

- **Decision**: When there is no condition evaluator, or the evaluator throws, the step waits to the
  deadline and ends with `image_unavailable`, as present mode does. A missing image id gives
  `delay_elapsed`, as before.
- **Rationale**: Clarification Q3 and FR-009. "No image" from a missing capture is not proof of absence.
- **Alternatives considered**: End with `image_absent` (wrong: false proof).

## R6: Value in `stepOutcomes` for later conditions

- **Decision**: The runner records `success` in `stepOutcomes` for a `WaitForImage` step in absent mode,
  as it does for present mode. The execution log `actionOutcome` and `exitCondition` show `image_absent`.
  `ExecutionLogService` maps `image_absent` to the status `success`.
- **Rationale**: FR-009 says later steps use the outcome as for present mode. Present mode records
  `success` for `image_detected` and for `timeout_elapsed`. A new `commandOutcome` state would widen the
  validator, the OpenAPI text, and the UI list. The spec does not ask for it.
- **Alternatives considered**: Record `image_absent` in `stepOutcomes` and add it to the allowed states
  (more change, not asked).

## R7: Validation rules

- **Decision**: In `ValidateWaitForImagePayload`: `until` must be a string equal to `present` or `absent`
  (not case-sensitive); `absentCaptures` must be an integer of 1 or more. A `{{name}}` parameter reference
  is skipped in the save check, as for `timeoutMs`. `absentCaptures` with `until` `present` or no `until`
  is accepted and ignored. The rule applies on save and on the load check in `FileSequenceRepository`.
- **Rationale**: FR-010 and the edge cases.
- **Alternatives considered**: Reject `absentCaptures` in present mode (the spec says "no effect").

## R8: Editor UI

- **Decision**: In the `WaitForImage` block of `SequencesPage.tsx` add a select "Wait until" (`The image
  appears` default, `The image is gone`) and a number field "Absent captures" (shown only when the select
  is `absent`, default empty meaning 2). The form state, the load code, and the save code carry
  `waitUntil` and `waitAbsentCaptures`. Save omits both fields when `until` is `present`, so old sequences
  stay byte-equal.
- **Rationale**: FR-011. The phone support level is `desktop-only`: the Sequences page is in the Authoring
  area (docs/mobile-ui-principles.md), and on a phone the page shows the desktop-only notice. The new
  controls use the same classes as the sibling fields.
- **Alternatives considered**: A separate component (the form is inline; a new file is not needed).

## R9: Docs and API text

- **Decision**: Update the `ActionTypes.WaitForImage` text in `PrimitiveActionSchemaFilter` with `until`,
  `absentCaptures`, the outcome `image_absent`, and the arrival-check note. Update
  `docs/architecture.md` in the WaitForImage sections. Update `CHANGELOG.md`.
- **Rationale**: FR-012 and Principle V.

## R10: Test approach

- **Decision**: Tests are red first. A failing test for the log mapping (`image_absent` to `success`) and
  a failing test for the preview text come before the code that makes them pass (FR-009). Web UI tests are
  in `src/web-ui/src/pages/__tests__`. Unit: parser of `until`
  (`tests/unit/Commands/WaitForImageUntilTests.cs`); counter rules; validator cases; runner loop with a scripted
  evaluator and a scripted pass scope (seen, absent, failed, flicker, region, timeout, unavailable).
  Contract: OpenAPI text and enum for the payload. Integration: a full sequence run through the API with
  a fake screen source. Vitest: select, number field, save body, load of an existing step, and old step
  unchanged. Existing `WaitForImage` tests stay unchanged (SC-005).
