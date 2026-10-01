# Implementation Plan: Reschedule-Self Cancel

**Branch**: `123-reschedule-self-cancel` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/123-reschedule-self-cancel/spec.md`

## Summary

A sequence must be able to remove its own pending booking when its work succeeds. The plan adds the option
`Cancel` to the `reschedule-self` step.

- Cancel removes the one-time bookings (Timer, OncePerRun, AtQueueStart) of the owning sequence in the current
  queue run. It keeps EveryStep injections, live schedules, template entries and the bookings of other sequences.
- Cancel also stops a OncePerRun booking of the same sequence that the queue already copied for firing (the
  drain copy). The handle keeps an in-flight list and a cancelled-id set. The firing loop skips a cancelled
  booking (FR-005, FR-015, research R-002). The Timer list has one entry for each sequence, so the Timer drain
  needs no set.
- The step never fails a run. It gives outcome `cancelled` with `removed` true, or `noop` with `removed` false
  (nothing pending, no queue, queue run not active).
- A Cancel step in a nested or called sequence uses the id of the owning sequence, the same id as a Timer step
  (research R-012).
- The validator rejects `timerTimeOfDay`, `timerRelativeOffset` and `ocrOffset` with Cancel and names the field.
  Create, update and PATCH return 400 (never 500). The validate call accepts a saved valid Cancel sequence. The
  repository save path has a backstop (research R-006).
- The web UI editor lists "Cancel pending booking" and hides the timer fields for it.
- The documents change: OpenAPI text, `docs/architecture.md`, `CHANGELOG.md`. The tracker file
  `docs/api-feature-requests.md` does not exist, so its row is skipped (research R-011).

Details are in [research.md](research.md), [data-model.md](data-model.md),
[contracts/reschedule-self-cancel.md](contracts/reschedule-self-cancel.md) and [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: C# on .NET (service and domain), TypeScript with React (web UI)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI), React with Vite, Jest
**Storage**: None added. Bookings stay in memory in the queue run handle. No migration is necessary.
**Testing**: xUnit (unit, contract, integration tests under `tests/`), Jest in `src/web-ui`
**Target Platform**: Windows service with a web UI
**Project Type**: Web service with a web UI
**Performance Goals**: Cancel runs once for each step on the serial run loop and touches at most a few entries. The firing loop adds one set lookup for each OncePerRun booking (under 1 ms). No change on other hot paths.
**Constraints**: Existing options and the "last booking wins" rule do not change (FR-011). A bad payload returns 400, never 500 (FR-010). Cancel never fails a run (FR-002, FR-003).
**Scale/Scope**: One enum value. About 8 backend files, 1 web UI file, 3 documentation items.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Result | Note |
|-----------|--------|------|
| I. Code Quality | Pass | Small, cohesive change. Public members get comments. The drain change stays small (three calls), because the loop method is already large. |
| II. Testing | Pass | Unit, contract, integration and UI tests are planned (research R-009), including the drain-copy test (FR-015) and the 400 tests for create, update and PATCH (FR-010). |
| III. UX Consistency | Pass | 400 messages name the field and list the known options. One term for each thing: booking, firing. |
| IV. Performance | Pass | The performance goal is in Technical Context. |
| V. Living Documentation | Pass | `docs/architecture.md` with its "Last reviewed" date, the OpenAPI text and the changelog change. The spec Status changes when the work is done. |
| VI. Simplified Technical English | Pass | All new text, messages and comments are in STE. |

Post-design re-check: no violation. The Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/123-reschedule-self-cancel/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── reschedule-self-cancel.md   # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/            # SelfRescheduleOption enum (Cancel), SelfReschedulePayload reader text,
│                              # FileSequenceRepository backstop, validator rules
├── GameBot.Service/
│   ├── Services/QueueExecution/
│   │   ├── QueueRunHandle.cs            # RemovePendingBookings, in-flight list, cancelled-id set
│   │   ├── QueueExecutionService.cs     # OncePerRun drain (about L737-752): Begin/End drain, skip cancelled
│   │   └── SelfRescheduleCoordinator.cs # CancelSelf + result
│   ├── Services/SequenceExecution/
│   │   └── SequenceExecutionService.cs  # dispatch of Cancel with owner id, log outcome check
│   └── Swagger/                         # PrimitiveActionSchemaFilter text
└── web-ui/src/                          # RescheduleOption type and option list in the sequence editor

tests/
├── GameBot.UnitTests/         # validator, payload, handle, coordinator, dispatch, owner id, drain skip
├── GameBot.IntegrationTests/  # queue run with Timer + Cancel; fail before Cancel; two OncePerRun bookings
└── GameBot.ContractTests/     # create, update, PATCH 400 tests; validate of saved Cancel sequence; OpenAPI text

docs/architecture.md, CHANGELOG.md
```

**Structure Decision**: Use the existing layout. Add no project. Exact file paths come from a grep of
`ActionTypes.RescheduleSelf` and `SelfRescheduleOption` in the tasks step (research R-007).

## Complexity Tracking

No violation. The table is empty.
