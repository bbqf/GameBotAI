# Implementation Plan: Exclude Helper Sequences From Success Notifications

**Branch**: `122-exclude-helper-sequences-notifications` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/122-exclude-helper-sequences-notifications/spec.md`

## Summary

The operator needs to hide the "success" message of helper sequences. A helper succeeds very often and
fills the chat with noise. The plan adds one boolean attribute to the sequence:
`excludeFromSuccessNotifications` (default off).

- The notification worker reads the attribute when it handles a run job. If the attribute is on, the worker
  sends no plain "success" message. The "failure", "cancelled" and "recovered" messages do not change.
- If the worker cannot read the sequence, it treats the attribute as off. It writes the existing log event
  12033 and sends the notification as usual. The text of event 12033 also says that the attribute is treated as off.
- The sequence API reads and writes the attribute. Only JSON `true` and `false` are valid. A `null`, a string
  and a number return 400. An omitted attribute keeps the saved value on PUT and PATCH, and gives off on POST.
- The web UI editor has a checkbox with a help text. The sequence list shows the marker
  "No success notifications".

Technical details are in [research.md](research.md), [data-model.md](data-model.md) and
[contracts/sequence-exclude-flag.md](contracts/sequence-exclude-flag.md).

## Technical Context

**Language/Version**: C# on .NET (service and domain), TypeScript with React (web UI)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI), React with Vite, Jest
**Storage**: JSON files in the data directory. The sequence file gets one optional member. No migration is necessary.
**Testing**: xUnit (unit, contract, integration tests under `tests/`), Jest in `src/web-ui`
**Target Platform**: Windows service with a web UI
**Project Type**: Web service with a web UI
**Performance Goals**: No new work on the engine hot path. The worker already reads the sequence once for each notification job, so the plan adds no read. A notification decision stays under 1 ms of added work.
**Constraints**: An old sequence file with no member MUST read as off, with no change of behavior (SC-005). A change applies to the next result with no restart (FR-010). No error path returns 500 (FR-008).
**Scale/Scope**: One attribute. About 6 backend files, 3 web UI files, 1 documentation file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Result | Note |
|-----------|--------|------|
| I. Code Quality | Pass | A small, cohesive change. Public API members get descriptions in OpenAPI. Lint and format must pass. |
| II. Testing | Pass | Worker unit tests, API contract tests, integration tests and UI tests are planned (research R-008). Coverage of touched code stays at or above the baseline. |
| III. UX Consistency | Pass | The error message is actionable ("excludeFromSuccessNotifications must be true or false."). The UI uses one term: "notifications". |
| IV. Performance | Pass | The performance goal is in Technical Context. The worker adds no read. |
| V. Living Documentation | Pass | `docs/architecture.md` and its "Last reviewed" date are updated. The spec Status and `specs/STATUS.md` change to "Implemented" (FR-011). |
| VI. Simplified Technical English | Pass | All new text, log messages and UI text are in STE. |

Post-design re-check: no violation. The Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/122-exclude-helper-sequences-notifications/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── sequence-exclude-flag.md   # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Commands/CommandSequence.cs              # new member ExcludeFromSuccessNotifications
├── GameBot.Service/
│   ├── Models/SequenceStepContracts.cs                     # member in request and response contracts
│   ├── Endpoints/SequencesEndpoints.cs                     # POST, PUT, PATCH, GET: read, validate, write
│   ├── Services/Notifications/QueueNotificationWorker.cs   # read flag, change Decide, change log event 12033 text
│   └── Swagger/SwaggerConfig.cs                            # OpenAPI description of the member
└── web-ui/src/
    ├── services/sequences.ts                               # DTO types for the member
    └── pages/SequencesPage.tsx                             # checkbox in create and edit form, list marker

tests/
├── unit/Notifications/QueueNotificationWorkerTests.cs      # US1 and US2 worker tests
├── contract/                                               # API tests for the member
└── integration/                                            # file, repository reload and backup round trips

docs/architecture.md                                        # living documentation
specs/STATUS.md                                             # status entry
```

**Structure Decision**: Use the existing layout. The feature adds no project and no new file in `src`.
The domain class carries the attribute, the endpoints validate it, the worker applies it, and the web UI shows it.

## Design Notes

- **Worker (FR-002, FR-003, FR-010)**: `HandleJobAsync` reads the sequence one time before `Decide`. It takes the
  name and the flag from that read. In the success branch of `Decide`, an open streak gives "recovered".
  Without a streak, "success" is sent only at level Success+Failure with the flag off. If the read throws,
  the flag is off, event 12033 is logged, and the message path is as before (research R-002).
- **API (FR-005, FR-008, FR-012)**: PUT and PATCH read the root JSON member with `TryGetProperty`. A strict check
  runs after the version conflict check and before the first change. A value change increments `version` and
  sets `UpdatedAt` (research R-003).
- **Clone, export, import (FR-009)**: No server clone exists. POST accepts the member, so a client clone keeps it.
  Backup serializes `CommandSequence` and carries the member (research R-005).
- **UI (FR-006, FR-007)**: A checkbox with help text in "Basics". A badge "No success notifications" in the list
  (research R-006).
- **Success criteria**: SC-004 is an outcome of the US1 worker tests. It is not separate work. SC-001 is a manual
  timed step in the quickstart.
- **Requirement order**: FR-011 (status update) is the last work item. FR-012 (version and update time) is
  covered by the API contract tests.

## Complexity Tracking

No constitution violation. No entry is necessary.
