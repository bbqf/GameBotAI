# Feature Specification: Wait for Still Step

**Feature Branch**: `174-wait-for-still-step`
**Created**: 2026-11-02
**Status**: Implemented
**Input**: User description: "FR-039: A step that waits until the screen has stopped changing (GitHub issue #368). Closes #368."

## Clarifications

### Session 2026-11-02

- Q: What happens when `stableMs` is larger than `timeoutMs`? → A: Validation rejects the step with a clear error. Rationale: the step could never end with `still`, so an early error helps the author.
- Q: What happens when the region lies partly or fully outside the screen? → A: A region with a width or height of 0 or less, or a negative x or y, fails validation. At run time, a region partly outside the screen is cut to the screen. A region fully outside the screen makes the step fail with a clear error. Rationale: this is safe and gives a clear signal.
- Q: What is the default of `timeoutMs`? → A: 5000 ms. Rationale: this is the value in the example of the issue and is larger than the default `stableMs`.
- Q: When does the stable time start? → A: The first capture is the baseline. The stable time starts at the first capture whose difference from the previous capture is at most `maxDiff`. A larger difference resets the stable time. Rationale: this matches "the first moment the region stays the same".
- Q (loop 1): What does "stopped" mean for the step, and what are the defaults? → A: A stop gives the same result as for `WaitForImage`. FR-008 lists all three defaults. Rationale: FR-009 requires the same behavior.
- Q (loop 2): What is the capture interval, and what is the baseline for SC-004? → A: The capture interval is the platform capture interval used by `WaitForImage` (default 500 ms). The baseline for SC-004 is the fixed wait of 2.3 to 3 s. Rationale: this makes SC-001, SC-002, and SC-004 measurable.
- Q: How is a failed capture handled? → A: In the same way as `WaitForImage` handles it. Rationale: FR-009 requires the same behavior.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wait until a pan or scroll has ended (Priority: P1)

An author of a sequence adds a `WaitForStill` step after a pan, scroll, or swipe. The step ends as soon as the screen stops moving. The sequence does not spend a fixed time.

**Why this priority**: This is the core need. A fixed wait costs 2.3 to 3 s on each run. It also fails when the device is slow. A wait that ends on an image can end too early, while the view still moves.

**Independent Test**: Run a sequence with a pan followed by a `WaitForStill` step. Check that the step ends with outcome `still` soon after the pan stops, and that the next step reads a view that does not move.

**Acceptance Scenarios**:

1. **Given** a screen that moves and then stops, **When** the step runs, **Then** the step ends with outcome `still` at the first moment the region stays the same for `stableMs`.
2. **Given** a screen that is already still, **When** the step runs, **Then** the step ends with outcome `still` after about `stableMs`.
3. **Given** a screen that never stops, **When** `timeoutMs` passes, **Then** the step ends with outcome `timeout_elapsed`.

---

### User Story 2 - Watch only a part of the screen (Priority: P2)

The author sets a `region` (x, y, width, height). The step compares only the pixels in that region. Parts that always animate, such as a clock or a banner, do not stop the step from ending.

**Why this priority**: Many game screens have an animated part. Without a region, the step can never end on those screens.

**Independent Test**: Show a screen where only a part outside the region changes. Check that the step ends with `still`. Then change a pixel inside the region and check that the step does not end with `still` while the change continues.

**Acceptance Scenarios**:

1. **Given** a `region` is set and pixels outside it change, **When** the step runs, **Then** the outcome is not changed by these pixels.
2. **Given** no `region` is set, **When** the step runs, **Then** the whole screen is compared.

---

### User Story 3 - Use the outcome in later steps (Priority: P2)

The author uses the outcome of `WaitForStill` in later steps, for example to branch on `still` or on `timeout_elapsed`, in the same way as for `WaitForImage`.

**Why this priority**: The step must fit the existing sequence model so that authors need no new concepts.

**Independent Test**: Build a sequence that branches on the outcome of a `WaitForStill` step. Run it with a still screen and with a moving screen. Check that each run takes the correct branch.

**Acceptance Scenarios**:

1. **Given** a `WaitForStill` step with a following branch on outcome, **When** the outcome is `still`, **Then** the sequence takes the `still` branch.
2. **Given** the same sequence, **When** the outcome is `timeout_elapsed`, **Then** the sequence takes the timeout branch.
3. **Given** a `WaitForStill` step with an invalid parameter, **When** the sequence is saved or validated, **Then** the author gets a clear error, in the same way as for `WaitForImage`.

---

### Edge Cases

- The region has a width or height of 0 or less, or a negative x or y: validation rejects the step. The region is partly outside the screen: it is cut to the screen. The region is fully outside the screen: the step fails with a clear error. The step does not crash.
- `stableMs` is larger than `timeoutMs`: validation rejects the step with a clear error.
- `maxDiff` is 0: the step ends with `still` only for identical captures.
- A capture fails during the wait (no frame, or the capture call throws): `WaitForImage` does not stop and does not retry on a special rule. When the image check gives no match, it waits one capture interval and checks again until the deadline. When the check throws, it waits until the deadline and ends with `image_unavailable`. `WaitForStill` does the same, but it has no `image_unavailable` outcome (FR-010). So a failed capture gives no frame for this pass, the step waits one capture interval, and it goes on until the deadline. If no frame is ever read, the step ends with `timeout_elapsed` at the deadline. A failed capture also clears the previous frame and the stable time, because the step cannot compare across a gap. The next good frame is a new baseline.
- The sequence is stopped while the step waits: the wait for the next capture is cancelled and the cancellation passes up to the runner as an `OperationCanceledException`. The step records no outcome. `WaitForImage` does the same. `WaitForStill` MUST NOT treat the cancellation as a failed capture.
- No session is available for the step: `WaitForImage` waits until the deadline and ends with `image_unavailable`. `WaitForStill` waits until the deadline and ends with `timeout_elapsed`, with the message `the screen could not be read`.
- A short flicker inside the region resets the stable time. The step then needs a new full `stableMs`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support a new step type `WaitForStill` in sequences, in the same family as `WaitForImage`.
- **FR-002**: The `WaitForStill` step MUST accept the optional parameter `region` (x, y, width, height), and the parameters `stableMs`, `maxDiff`, and `timeoutMs`.
- **FR-003**: The step MUST compare consecutive captures inside the region.
- **FR-004**: The step MUST end with outcome `still` at the first moment the region stays the same for `stableMs`. "Same" means that the mean pixel difference between consecutive captures, from 0 to 1, is at most `maxDiff`.
- **FR-005**: The step MUST end with outcome `timeout_elapsed` when the region does not stop within `timeoutMs`.
- **FR-006**: When `region` is not set, the step MUST compare the whole screen.
- **FR-007**: When `region` is set, pixels outside the region MUST NOT change the result.
- **FR-008**: `stableMs` MUST default to 300. `maxDiff` MUST default to 0.01. `timeoutMs` MUST default to 5000.
- **FR-009**: The step MUST behave like `WaitForImage` in the sequence model, in validation, in execution, and in the way later steps use its outcome, for example to branch on the outcome.
- **FR-010**: The system MUST NOT add other wait types or other outcome names. The only outcomes of the new step are `still` and `timeout_elapsed`. Tests MUST assert that the set of outcome names is exactly `still` and `timeout_elapsed`. A failure (for example, a region fully outside the screen) uses the existing failed result and adds no outcome name.
- **FR-011**: The system MUST NOT change the behavior of existing fixed waits or of `WaitForImage`.
- **FR-012**: Where the web UI lets an author build `WaitForImage` steps, the UI MUST let the author build `WaitForStill` steps. The feature MUST state its phone support level (full, reduced, or desktop-only) and follow docs/mobile-ui-principles.md. The support level is the same as the level of the `WaitForImage` authoring UI.
- **FR-013**: The documentation of the step types MUST describe `WaitForStill` and its parameters.

### Key Entities

- **WaitForStill step**: A sequence step with the parameters `region`, `stableMs`, `maxDiff`, `timeoutMs`. Its outcomes are `still` and `timeout_elapsed`.
- **Region**: A rectangle of the screen with x, y, width, height. It is optional. When not set, it is the whole screen.

## Assumptions

- The Sequences page has an "Add wait for still step" button. It adds a step of type `WaitForStill` with the default values of FR-008. A UI test covers the button and the form fields (FR-012).
- The default for `maxDiff` is 0.01, as in the example of the issue.
- The default for `timeoutMs` is 5000 ms.
- The first capture is the baseline. Stable time is counted from the first capture that differs from the previous one by at most `maxDiff`.
- The pixel difference is the mean absolute difference over all pixels in the region, scaled to the range 0 to 1.
- The platform captures the screen at the same rate as it does for `WaitForImage`.
- The PR description includes a short timing note that shows SC-001, SC-002, and SC-004 (measured end times of the step against `stableMs`, `timeoutMs`, and a fixed wait).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a screen that stops, the step ends with `still` no later than `stableMs` plus one capture interval after the last change.
- **SC-002**: For a screen that never stops, the step ends with `timeout_elapsed` no later than `timeoutMs` plus one capture interval.
- **SC-003**: A change of any size outside the region never changes the outcome, in 100% of test cases.
- **SC-004**: An author can replace a fixed wait of 2.3 to 3 s after a pan with one `WaitForStill` step. When the screen stops sooner than the fixed wait, the step ends earlier than the fixed wait.
- **SC-005**: All existing tests of `WaitForImage` and fixed waits pass without change.
