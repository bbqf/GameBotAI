# Feature Specification: Wait Until an Image Is Gone

**Feature Branch**: `175-wait-for-image-gone`
**Created**: 2026-11-03
**Status**: Implemented
**Input**: User description: "FR-040: Wait until an image is gone (GitHub issue #369). Closes #369."

## Clarifications

### Session 2026-11-03

- Q: What is the name of the present-mode success outcome? → A: `image_detected`. Rationale: the code and the execution log use this name. The new outcome is `image_absent`.
- Q: Which shape does the feature use: new step type or option? → A: The option `until` on `WaitForImage`. Rationale: the issue proposes it, and it reuses validation, editor, and outcome model.
- Q: When the capture source is not available in absent mode, what is the result? → A: The step ends with `image_unavailable` at the deadline, as in present mode. Rationale: "no image" from a missing capture is not proof of absence (FR-009).
- Q (loop 1): Which analyze findings are spec gaps? → A: F1 (add `image_unavailable` to absent outcomes), F2 (missing reference image gives the present-mode result), C3 (FR-009 covers log and preview text), A2 (SC-002 links the short-timeout edge case). Rationale: each was a spec inconsistency.
- Q (loop 2): Does SC-004 need an automated test? → A: Yes, a runner test with a fake clock shows that the step ends before the fixed wait time. Rationale: this makes SC-004 measurable in CI.
- Q: Does an absent-mode step need a minimum `timeoutMs`? → A: No new rule. Existing `timeoutMs` validation applies. Rationale: the edge case about short timeouts is enough.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wait until a toast or banner has cleared (Priority: P1)

An author of a sequence taps a button. A toast (for example "Auto enabled") shows. The next tap must wait until the toast has cleared. The author sets the `WaitForImage` step to wait for the image to be absent. The step ends as soon as the toast is gone. The sequence does not spend a fixed time.

**Why this priority**: This is the core need. A fixed wait costs its full time (for example 5.3 s) on each run, also when the toast cleared after 1 s.

**Independent Test**: Run a sequence with a tap that shows a toast, followed by a `WaitForImage` step with `until` set to `absent` for the toast image. Check that the step ends with outcome `image_absent` soon after the toast clears, and that the next step runs after that.

**Acceptance Scenarios**:

1. **Given** an image that is visible and then leaves the screen, **When** the step runs with `until` set to `absent`, **Then** the step ends with outcome `image_absent` at the first point where `absentCaptures` consecutive captures lack the image.
2. **Given** an image that is already not visible, **When** the step runs with `until` set to `absent`, **Then** the step ends with outcome `image_absent` after `absentCaptures` consecutive captures.
3. **Given** an image that stays visible, **When** `timeoutMs` passes, **Then** the step ends with outcome `timeout_elapsed`.

---

### User Story 2 - Ignore a short flicker (Priority: P2)

The author sets `absentCaptures` to the number of consecutive captures that must lack the image. A short flicker where the image is not seen in one capture does not end the wait too early. When `absentCaptures` is not set, the value is 2.

**Why this priority**: A single capture can miss an image that is still on the screen. Without this guard, the next step can start while the image is still shown.

**Independent Test**: Feed a capture series where the image is seen, not seen, seen, and then not seen for the required count. Check that the step ends only after the required number of consecutive captures without the image.

**Acceptance Scenarios**:

1. **Given** `absentCaptures` is 2 and one capture lacks the image between captures that show it, **When** the step runs, **Then** the step does not end at that capture.
2. **Given** `absentCaptures` is not set, **When** the step runs, **Then** it needs 2 consecutive captures without the image.
3. **Given** `absentCaptures` is 1, **When** the first capture lacks the image, **Then** the step ends with `image_absent`.

---

### User Story 3 - Watch only a part of the screen and keep old steps unchanged (Priority: P2)

The author uses the same `detectionTarget` (`referenceImageId` and optional `region`) as for the present-mode wait. A step that does not set `until` keeps its current behavior: it ends when the image appears.

**Why this priority**: Authors need no new concepts, and existing sequences must not break.

**Independent Test**: Run an existing `WaitForImage` step without `until`. Check that its behavior and outcomes are the same as before. Run an absent-mode step with a `region` and check that only the region is examined for the image.

**Acceptance Scenarios**:

1. **Given** a `WaitForImage` step without `until`, **When** the step runs, **Then** it behaves as before and ends with its existing outcomes.
2. **Given** a `region` is set in absent mode, **When** the image is shown outside the region only, **Then** the image counts as absent, in the same way as the region works for present mode.
3. **Given** a step with an invalid `until` or an invalid `absentCaptures`, **When** the sequence is saved or validated, **Then** the author gets a clear error, in the same way as for other `WaitForImage` parameters.

---

### Edge Cases

- The image stays visible for the whole wait: the step ends with `timeout_elapsed`.
- A capture fails during the wait: a failed capture is not a capture that lacks the image. It does not count toward `absentCaptures`, and it resets the count of consecutive absent captures. The step handles the failure in the same way as present mode handles it.
- The sequence is stopped while the step waits: the wait is cancelled in the same way as for present mode. The step records no outcome.
- The referenced image does not exist (no usable reference image id): the step ends with the same result as present mode gives for this case. A missing reference image is not "absent".
- No capture source is available, or the image check throws: the step ends with `image_unavailable` at the deadline, as in present mode. The step does not end with `image_absent`.
- `absentCaptures` is 0 or less, or not a whole number: validation rejects the step.
- `absentCaptures` is set while `until` is `present` or not set: the value has no effect on the step.
- "Absent" is also true when the screen changed to something else. The step does not prove that the expected next screen is shown. The author adds an arrival check after the wait.
- The wait cannot end before `absentCaptures` captures are taken. A very short `timeoutMs` can therefore give `timeout_elapsed` even when the image is gone.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `WaitForImage` step MUST accept the optional parameter `until` with the values `present` and `absent`.
- **FR-002**: When `until` is not set, it MUST be `present`. A step with `until` `present` or without `until` MUST behave exactly as `WaitForImage` behaves before this feature.
- **FR-003**: When `until` is `absent`, the step MUST end with outcome `image_absent` at the first point where `absentCaptures` consecutive captures lack the image.
- **FR-004**: The step MUST accept the optional parameter `absentCaptures`, a whole number of 1 or more. When not set, it MUST be 2.
- **FR-005**: When `until` is `absent` and the image stays visible until `timeoutMs`, the step MUST end with outcome `timeout_elapsed`.
- **FR-006**: In absent mode, `detectionTarget` (`referenceImageId` and optional `region`) MUST work as it does in present mode.
- **FR-007**: The step MUST count only successful captures that lack the image. A capture where the image is seen, and a failed capture, MUST reset the count of consecutive absent captures.
- **FR-008**: The system MUST NOT add an arrival check to the step. The system MUST NOT add other step types. The only new outcome name is `image_absent`.
- **FR-009**: In absent mode, the step MUST behave like present-mode `WaitForImage` in the sequence model, in validation, in execution, and in the way later steps use its outcome, for example to branch on the outcome, in the execution log, and in the step-through preview text. The execution log MUST map `image_absent` to the status `success`. The preview text MUST name the absent mode. The tasks MUST put a failing test for the log mapping and a failing test for the preview text before the code that makes them pass.
- **FR-010**: Validation MUST reject a value of `until` that is not `present` or `absent`, and a value of `absentCaptures` that is less than 1 or is not a whole number.
- **FR-011**: Where the web UI lets an author build `WaitForImage` steps, the UI MUST let the author set `until` and `absentCaptures`. The feature MUST state its phone support level (full, reduced, or desktop-only) and follow docs/mobile-ui-principles.md. The support level is `desktop-only`. This is the level of the `WaitForImage` authoring UI: the Sequences page is part of the Authoring area, which is `desktop-only` in docs/mobile-ui-principles.md. On a phone the page shows the desktop-only notice.
- **FR-012**: The documentation of the step types MUST describe `until`, `absentCaptures`, and the outcome `image_absent`. It MUST state that "absent" is also true when the screen changed to something else and that a sequence needs its own arrival check.

### Key Entities

- **WaitForImage step**: A sequence step with `detectionTarget` (`referenceImageId`, optional `region`), `timeoutMs`, and the new optional parameters `until` and `absentCaptures`. Its outcomes are `image_detected`, `timeout_elapsed`, and `image_unavailable` in present mode (the existing names stay as they are), and `image_absent`, `timeout_elapsed`, and `image_unavailable` in absent mode.
- **Absent capture count**: The number of consecutive captures that lack the image. It resets when the image is seen or a capture fails.

## Assumptions

- The issue allows a new step type or an option on `WaitForImage`. This feature uses the option `until` on `WaitForImage`, as the issue proposes. Reason: it reuses `detectionTarget`, validation, the editor, and the outcome model with the smallest change.
- Captures are taken at the same rate as for present-mode `WaitForImage`.
- The check for "the image is visible" uses the same match rule as present mode (same threshold and same region handling).
- A plan task must find the code, the API schema, the editor UI, and the docs that cover `WaitForImage`.
- Phone support level: `desktop-only` (see FR-011). The step form is in `src/web-ui/src/pages/SequencesPage.tsx`, in the Authoring area.
- Web UI tests are in `src/web-ui/src/pages/__tests__` (for example `SequencesPageWaitForStill.test.tsx` and `SequencesPage.stepThrough.spec.tsx`). A unit test file for the parser of `until` also exists in the plan structure. Regeneration of the plan and tasks MUST include both.
- Task generation notes:
  - Correct file paths: the evaluator is `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`. The pass scope is `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`. Do not use the path `src/GameBot.Domain/Commands/Execution/ImageMatchEvaluator.cs`; it does not exist.
  - The exit-condition text is the string `image_absent`. It is a plain string, not a constant or enum. The runner sets it in `WaitForImageAsync` in `src/GameBot.Domain/Services/SequenceRunner.cs` (`detail.ExitCondition = "image_absent"`, as for `image_detected` and `timeout_elapsed`). `ExecutionLogModels.cs` and `Models/ExecutionLogs.cs` have only a `string? ExitCondition` field and no text list. The log task MUST only map `image_absent` to `success` in `ExecutionLogService.cs` (the status switch near the `image_detected` entry). It has a skip rule: skip the edit of any file that has no list of exit-condition texts.
  - The tasks MUST include a runner test with a fake clock for SC-004 (the step ends before the fixed wait time).
  - Do not write two tasks for the same counter logic (no duplicated counter tasks).
  - Each conditional task MUST have a skip rule that states when it is done or not needed.
  - The version bump task comes before the CHANGELOG entry task.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For an image that leaves the screen, the step ends with `image_absent` no later than `absentCaptures` capture intervals after the first capture that lacks the image.
- **SC-002**: For an image that stays visible (see the edge case about a short `timeoutMs`), the step ends with `timeout_elapsed` no later than `timeoutMs` plus one capture interval.
- **SC-003**: A single capture that lacks the image between captures that show it never ends the wait when `absentCaptures` is 2 or more, in 100% of test cases.
- **SC-004**: An author can replace a fixed wait of 5.3 s after a tap with one absent-mode step. When the toast clears sooner than 5.3 s, the step ends earlier than the fixed wait. A runner test with a fake clock checks this.
- **SC-005**: All existing tests of `WaitForImage` pass without change.
