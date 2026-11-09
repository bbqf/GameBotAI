# Implementation Plan: Show the queue name in notify messages

**Branch**: `183-notify-queue-name` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/183-notify-queue-name/spec.md`

## Summary

A `notify` step with no `url` sends its plain text to the enabled notification targets. Today the
text has no prefix, so a reader cannot tell which queue sent it. This feature adds the label
`Queue name : ` before the author text when the step runs in a queue. The notification worker
makes the label. It already reads the queue repository and owns the message formatter. The worker
gets the queue ID from the send request (`ScopeKey`). A step that runs outside a queue
(`ScopeKey` is `adhoc`) sends the text with no label. The webhook path (a step with `url`, and the
fallback to the default URL) does not change.

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: None new. Uses the existing `IQueueRepository` in `QueueNotificationWorker`.
**Storage**: N/A. No new data. No saved form changes.
**Testing**: xUnit and FluentAssertions, as in the existing `tests/unit/Notifications` and `tests/integration/Notifications`.
**Target Platform**: The GameBot service (Windows host).
**Project Type**: web-service (API and background worker).
**Performance Goals**: No new wait on the step. The worker adds one queue read for each text send. The step still waits at most 35 s. The worker never waits for a send.
**Constraints**: The text stays plain, with no markup. The order of sends for one sequence stays the same. A failed queue read MUST NOT drop the message.
**Scale/Scope**: One formatter method, one worker method, tests, and one `docs/architecture.md` paragraph.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Not applicable.

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | One small formatter method, one worker method. No dead code. CamelCase method names. |
| II. Testing | Pass | Unit tests for the formatter and the worker. Integration test for the step to the target. Existing tests keep their intent. |
| III. UX consistency | Pass | The label uses the style of the other queue messages: `name : content`. |
| IV. Performance | Pass | One extra queue read for each text send, on the worker thread. No hot path. Perf note in the PR. |
| V. Living documentation | Pass | Update the "Text send" paragraph in `docs/architecture.md` and refresh "Last reviewed". Update the `Status` line of this spec and `specs/STATUS.md`. Mark feature 159 as iterated by 183. |
| VI. STE | Pass | All new text follows STE. |
| Version bump | Pass | Raise `patch` in `installer/versioning/version.override.json` before the PR. |

Post-design re-check: no change. No violation. The Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/183-notify-queue-name/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── notify-message.md
└── tasks.md             # Made by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/Notifications/
├── NotificationMessageFormatter.cs   # Add FormatNotifyText
└── QueueNotificationWorker.cs        # HandleText becomes async and reads the queue name

tests/unit/Notifications/
├── NotificationMessageFormatterTests.cs          # New cases for FormatNotifyText
└── QueueNotificationWorkerTextSendTests.cs       # Queue case, ad-hoc case, missing name, failed read

tests/integration/Notifications/                  # One end-to-end case: step in a queue to a fake target

docs/architecture.md                              # Text send paragraph
specs/STATUS.md                                   # Status of 183 and of 159
```

**Structure Decision**: Keep the existing single service project and its test projects. The change
is inside the notification worker and its formatter. No new project, class, or setting.

## Complexity Tracking

No violation. Not needed.
