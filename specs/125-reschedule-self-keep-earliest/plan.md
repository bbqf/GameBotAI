# Implementation Plan: Reschedule-Self Keep Earliest

**Branch**: `125-reschedule-self-keep-earliest` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/125-reschedule-self-keep-earliest/spec.md`

## Summary

Add the optional field `keep: earliest` to the `reschedule-self` payload (GitHub issue #257). It applies to the `Timer` option only. A Timer booking with `keep: earliest` replaces the pending booking of the sequence only when both are true: the pending booking was made in the same run, and the new fire time is strictly earlier. Without `keep`, the last booking wins, as before.

Technical approach:

- Each Timer booking records a run key. The run key is the root execution id of the sequence execution. A booking made inside a child sequence uses the root execution id (FR-002).
- The check and the write are one step inside `QueueRunHandle.AddTimerFiring`, under `_timerLock`.
- **RearmTimerFiring RunId=null rule**: when `QueueRunHandle.RearmTimerFiring` puts a held Timer booking back into the register, it stores the entry with `RunId = null` (`entry with { RunId = null }`). The fire time does not change. So the first booking of the next run replaces a re-armed booking (FR-003). The step-0 retry and the other engine bookings also have no run id.
- A losing booking returns outcome `scheduled` with `KeptPending = true`. The step succeeds. The run logs one Information message with the pending fire time and the new fire time (FR-006b).
- The payload reader, the validator, the repository backstop, the API documentation text, and the dispatcher accept `keep`.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (ASP.NET Core minimal API)
**Primary Dependencies**: existing only (xUnit, Swashbuckle). No new package.
**Storage**: The `keep` value lives in the action payload dictionary of the sequence, in the existing JSON sequence store. The booking register is in memory (`QueueRunHandle`).
**Testing**: xUnit in `tests\unit`, `tests\integration`, and `tests\contract`
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: web-service (backend only)
**Performance Goals**: No change. One extra list scan under an existing lock.
**Constraints**: At most one pending Timer booking for each sequence (FR-005). No change to other schedule options (FR-011). No new key field (FR-010).
**Scale/Scope**: About 8 production files and about 8 test files. No web UI change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality Discipline | Pass | Small change in existing types. New enum `SelfRescheduleKeep`, one new method result type. No new warning. |
| II. Testing Standards | Pass | Unit, integration, and contract tests are in FR-012. Tests come before code in the task order. |
| III. User Experience Consistency | Pass | `keep` follows the style of `option` and `ocrOffset`. Errors name the accepted value. No web UI change. |
| IV. Performance Requirements | Pass | No new I/O. One list scan under the existing lock. |
| V. Living Documentation | Pass | `docs/architecture.md`, `specs/STATUS.md`, `CHANGELOG.md`, and the spec `Status` line are in FR-013 and in tasks. |
| VI. Simplified Technical English | Pass | All artifacts are written in STE. |

Post-design re-check: no change. No violation, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/125-reschedule-self-keep-earliest/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── reschedule-self-keep.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/            # SelfReschedulePayload, SelfRescheduleKeep, payload reader, validator rule
└── GameBot.Service/
    ├── Services/QueueExecution/   # QueueRunHandle, SelfRescheduleCoordinator, SelfRescheduleEntry/Result
    ├── Services/SequenceExecution/ # dispatcher passes keep and the root execution id
    └── Swagger/                   # PrimitiveActionSchemaFilter text for the payload

tests/
├── unit/          # AddTimerFiring decision table, RearmTimerFiring, coordinator, payload reader, validator
├── integration/   # real engine retry path, 15/50/30/40 run, default behavior
└── contract/      # 400 for invalid keep and for non-Timer option, round-trip, OpenAPI text
```

**Structure Decision**: Use the existing projects only: `src\GameBot.Domain`, `src\GameBot.Service`, `tests\unit`, `tests\integration`, `tests\contract`. No new project. No web UI change.

## Design Notes

1. **Run key**: the dispatcher reads the root execution id of the current sequence execution (the parent context root id if the step runs in a child sequence). It passes it as `runId` to `ScheduleSelf`. A Timer booking stores it in `SelfRescheduleEntry.RunId`, with or without `keep`.
2. **Compare**: `AddTimerFiring(entry, keepEarliest)` finds the pending entry of the same sequence. It returns `KeptPending` only when `keepEarliest` is true, the pending `RunId` is non-null and equal to `entry.RunId`, and the new `FireAt` is not earlier than the pending `FireAt`. In all other cases it replaces the pending entry (decision table in data-model.md).
3. **Re-arm**: `RearmTimerFiring` keeps its rule (add only if no entry exists) and now stores the entry with `RunId = null`.
4. **ocrOffset**: the dispatcher gives the coordinator the offset after `min`, `max`, and the fallback, so the compare uses the resolved fire time only.
5. **Log**: a losing booking logs at Information level: the pending fire time and the new fire time.
6. **Validation sites (FR-008)**: payload reader, validator allow-list with the non-Timer rule, repository backstop, API documentation text, dispatcher. The action-type lists do not change.

## Complexity Tracking

No violations.
