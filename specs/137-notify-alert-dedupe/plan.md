# Implementation Plan: Notify alert dedupe window

**Branch**: `137-notify-alert-dedupe` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/137-notify-alert-dedupe/spec.md`

## Summary

The `notify` sequence step gets the optional fields `dedupeKey` and `since` or `within`. The service sends the alert at most once for each (queue, key) in the window. The design has five parts:

1. `NotifyPayload.TryRead` parses and validates the new fields (400 at save time).
2. A new store `ISentAlertStore` with the file-backed class `FileSentAlertStore` keeps the records of delivered alerts. It also keeps in-memory reservations.
3. `DispatchNotifyAsync` reserves the slot, sends, then commits or releases (FR-007, FR-013).
4. Step-through preview reads the store without a write (FR-014).
5. The OpenAPI text and the queue delete path are updated (FR-009, FR-012).

Window math reuses `LastRunWindow.SinceStart`, so `since` matches `lastRun` (SC-004). See [research.md](research.md) for each decision.

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal API, existing `TimeProvider`, existing `IFailureNotifier`
**Storage**: One JSON file, `<dataRoot>/notify-alert-dedupe/sent-alerts.json`. Reservations are in memory only.
**Testing**: xUnit unit tests, contract tests with `WebApplicationFactory`, integration test through the notify step
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service with a domain library
**Performance Goals**: The check adds one in-memory lookup for each notify step. No hot path is affected.
**Constraints**: Records live 48 hours. A reservation lives 5 minutes. `within` is at most 24 hours.
**Scale/Scope**: Few records (one for each queue and key).

No item needed clarification.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality | Pass | Small new store class. Validation stays in `NotifyPayload.TryRead`. `Program.cs` stays thin (DI line only). |
| II. Testing | Pass | Unit tests for payload and store. Contract tests for 400 cases. Integration tests for the 10-run chain and for concurrent dispatch. Existing `lastRun`, notify and channel tests run unchanged as the FR-015 check. |
| III. UX Consistency | Pass | Error messages name the field. Outcome values `sent` and `suppressed` appear in the log and OpenAPI. |
| IV. Performance | Pass | One lookup under one gate. File write only on commit. |
| V. Living Documentation | Pass | OpenAPI payload text, contract and quickstart are updated. |
| VI. STE | Pass | All new text uses STE. |

Post-design re-check: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/137-notify-alert-dedupe/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/notify-step.md
`-- tasks.md
```

### Source Code (repository root)

```text
src/
|-- GameBot.Domain/
|   |-- Commands/Notify/NotifyPayload.cs            # change: DedupeKey, Since, Within, validation
|   |-- Services/LastRunWindow.cs                   # reuse only (SinceStart); no change
|   |-- Services/SequenceStepValidationService.cs   # change only if a message needs it. Save-time
|   |                                               #   validation uses the existing NotifyPayload.TryRead call.
|   `-- Queues/
|       |-- ISentAlertStore.cs                      # new: reserve, commit, release, read, delete
|       `-- FileSentAlertStore.cs                   # new: file-backed store + reservations
`-- GameBot.Service/
    |-- Program.cs                                  # change: register ISentAlertStore (one line)
    |-- Endpoints/QueuesEndpoints.cs                # change: queue delete calls DeleteQueueAsync
    |-- Services/SequenceExecution/
    |   `-- SequenceExecutionService.cs             # change: DispatchNotifyAsync reserve/commit/release,
    |                                               #   scope (queue id or "adhoc"), TimeProvider, optional store
    |-- Services/StepThrough/StepThroughActionPreview.cs  # change: would send / would suppress
    `-- Swagger/PrimitiveActionSchemaFilter.cs      # change: notify payload text

tests/
|-- unit/      # NotifyPayload parse and validation; FileSentAlertStore (reserve, commit, release,
|              #   expiry, prune, restart, queue delete)
|-- contract/  # 400 for each invalid case (subset); OpenAPI text
`-- integration/  # 10-run chain; concurrent dispatch through the notify step (at most 1 send);
                  #   run during live reservation is suppressed, run after release sends;
                  #   preview scope rule and "would suppress" on a live reservation; restart
```

**Structure Decision**: Use the existing Domain plus Service split. The store lives in `GameBot.Domain/Queues` next to `FileSequenceRunStatisticsStore`, whose gate, temporary-file and atomic-replace pattern it copies.

## Design Notes

- **Dispatch flow** (FR-003, FR-007, FR-013): parse payload; if no dedupe fields, send as before. Otherwise compute the window start; call `TryReserveAsync`. If null, report `suppressed`. Else deliver; on success `CommitAsync` and report `sent`; on failure `ReleaseAsync` and keep today's failure text. A suppressed step does not write or extend a window.
- **Scope** (FR-006): (queue identity, trimmed key). A run outside a queue uses `adhoc`. The scope is built in the base dispatch flow (spec Assumptions).
- **Preview** (FR-014): `WasSentSinceAsync` and a reservation check, read-only. The same scope rule as a real run. A live reservation shows "would suppress". No send, no record.
- **Null store**: a step with dedupe fields always sends; preview shows "would send".
- **Task order**: the preview work comes after the store registration and the base dispatch scope rule.

## Complexity Tracking

No violation. This section is empty.
