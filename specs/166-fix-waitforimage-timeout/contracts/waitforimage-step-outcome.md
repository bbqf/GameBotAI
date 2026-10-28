# Contract: WaitForImage step outcome (behavior only)

No field, status, or reason is added or removed. This contract states the required behavior.

## Found (FR-001 to FR-005, FR-011)

When the session frame contains the reference image (or a stored alternate) with a score at or above `confidence`:

| Path | Result |
|------|--------|
| `POST /api/steps/execute` | `stepOutcomes[0].status` = `executed`, `reason` = `image_detected`, `detectionConfidence` and `resolvedPoint` set |
| `POST /api/commands/{id}/force-execute` | the `WaitForImage` step outcome has `status` = `executed`, `reason` = `image_detected` |
| `POST /api/sequences/{id}/execute`, inline step | step `actionOutcome` = `image_detected`; `waitForImageDetails` is not null; `waitForImageDetails.exitCondition` = `image_detected` |
| `POST /api/sequences/{id}/execute`, command step that contains the `WaitForImage` step | the command step outcome is `executed`; the nested `WaitForImage` outcome is `executed` / `image_detected`; `waitForImageDetails` stays null for a command step |

When the image is on screen at the start of the step, the step completes before `timeoutMs` elapses (FR-002).

For the same frame and the same `confidence`, the step result agrees with `POST /api/images/detect` (FR-003).

## Not found (FR-006, FR-010)

- Step and command paths: `status` = `completed_timeout`, `reason` = `timeout_elapsed`.
- Inline sequence step: `actionOutcome` = `timeout_elapsed`, `waitForImageDetails.exitCondition` = `timeout_elapsed`.
- The step does not start a detection pass at or after the deadline (the start of the step plus `timeoutMs`). A pass that started before the deadline can finish.
- When the remaining time is not more than the poll interval, the last wait ends 50 ms before the deadline (or at once, if less than 50 ms remain). Then the step does one last pass.
- Thus a step with `timeoutMs` more than 50 ms does at least two passes, also when `timeoutMs` is not more than the poll interval.
- The step completes in not more than `timeoutMs` plus one pass (SC-003). The automated tests use the limits `timeoutMs` + 500 ms and less than 2 × `timeoutMs`.
- On all three paths, the service writes one debug log line with the frame state and the best score of the last pass (FR-012). `CommandExecutor` writes it for the step-execute path, the force-execute path, and a sequence command step. `SequenceRunner` writes it for an inline sequence step. The API response does not change.

## Unchanged

- Image id not uploaded: `completed_image_unavailable` / `image_unavailable` (FR-007).
- No detection target: `executed` / `delay_elapsed` (FR-008).
