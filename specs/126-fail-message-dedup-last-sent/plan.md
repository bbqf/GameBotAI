# Implementation Plan: Suppress a failure message only when the last message sent was the same

**Branch**: `126-fail-message-dedup-last-sent` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/126-fail-message-dedup-last-sent/spec.md`

## Summary

Today the notification worker drops a later failure of one queue and sequence pair for the whole life
of the failure streak. Other messages in the chat can then hide the failure. This plan changes the
rule. The worker remembers the last message that it started to send through each target. It drops a
failure message for a target only when that last message is the same. The streak keeps its meaning
for the "recovered" message. The change is in the worker and in one new in-memory state class. It adds
no endpoint, no setting, and no stored data.

## Technical Context

**Language/Version**: C# on .NET (the version of `src\GameBot.Service`)
**Primary Dependencies**: Microsoft.Extensions.Hosting and Logging. No new dependency.
**Storage**: N/A. The last-sent record is in memory only (spec Assumptions).
**Testing**: xUnit unit tests in `tests\unit\Notifications`
**Target Platform**: Windows service host (GameBot.Service)
**Project Type**: web-service (background worker inside the service)
**Performance Goals**: One dictionary lookup and one string compare for each message and target. No
extra I/O. No measurable change in the worker loop.
**Constraints**: The worker must not wait for a send. The record is written on the worker thread
only. No lock is necessary.
**Scale/Scope**: A few targets and a few queues. One new class of about 40 lines. One changed method
group in `QueueNotificationWorker`.

No item is marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Result | Note |
| --- | --- | --- |
| I. Code quality | Pass | One small cohesive class. Methods stay under 50 lines. CamelCase method names. |
| II. Testing | Pass | A failing test comes first (bug fix). Unit tests cover SC-001 to SC-005. One existing test changes. |
| III. UX consistency | Pass | Message text and format do not change. No new setting. |
| IV. Performance | Pass | Goals are declared above. The worker adds one lookup for each message and target. |
| V. Living documentation | Pass | Tasks update `docs/architecture.md`, `specs/STATUS.md`, and the Status lines (FR-010). |
| VI. STE | Pass | All new text follows STE. |

Post-design re-check: no change. The design adds no violation. Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/126-fail-message-dedup-last-sent/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── notification-dedup-behavior.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/Notifications/
├── QueueNotificationWorker.cs      # changed: Decide, StartSend, HandleAlertAsync, record calls
├── NotificationStreakState.cs      # unchanged
└── NotificationLastSentState.cs    # new: target ID to last sent message

tests/unit/Notifications/
├── NotificationLastSentStateTests.cs   # new: IsSame and Record rules
├── QueueNotificationWorkerDedupTests.cs # new: SC-001 to SC-005 on the worker
└── (existing worker tests)             # one test changes: failure, cancelled, failure, success

docs/architecture.md                # Last reviewed date and the notification rule
specs/STATUS.md                     # row for 126, row for 120
specs/120-*/spec.md                 # Status line
```

**Structure Decision**: Keep the existing single service project. Put the new state class next to
`NotificationStreakState`. The worker owns both states, as spec 120 requires.

## Design Decisions

The full list is in [research.md](research.md). The main points:

1. `Decide` returns `Failure` for every failure and opens the streak if it is not open. It no longer
   returns null for a later failure (R-003).
2. `StartSend` and `HandleAlertAsync` build the target list. For a failure message, they drop each
   target where `IsSame` is true. For each remaining target, they call `Record` and then `ChainSend`
   (R-004).
3. The send cap check stays first. A message that the cap drops does not record (FR-008).
4. A "cancelled" message and an alert record, and they are never dropped (R-005).
5. A "recovered" or a "success" message always sends and records. Only failures are compared.

## Complexity Tracking

No violation. This table stays empty.
