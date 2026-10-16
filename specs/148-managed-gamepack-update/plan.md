# Implementation Plan: Managed gamepack update

**Branch**: `148-managed-gamepack-update` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/148-managed-gamepack-update/spec.md`

## Summary

The platform gets one call that updates a gamepack in a safe order. The call does these steps:

1. Find the affected queues.
2. Stop them.
3. Back up the affected queue templates. Write no backup if no template is affected.
4. Install the version.
5. Book disabled entries for new sequences.
6. Restart the queues that the call stopped.

A failure after the start restores the previous version and the previous queue state from the backup. A dry run returns the plan and all problems. A dry run changes nothing. A managed rollback uses the same flow with `managed=true`.

Technical approach: a new domain class `GamepackUpdateOrchestrator` holds the flow. It is in `src/GameBot.Domain/Gamepacks/Managed/`. It uses these building blocks:

- `GamepackService` (install, rollback, dry run, signature).
- `QueueSettingsExporter` and `QueueSettingsImporter` (backup and restore).
- `GamepackCatalogService` (new `FetchAsync`).
- The queue template repository.

A small port `IManagedQueueControl` hides the internal `IQueueExecutionService`. One lock for the instance allows one managed call at a time. Endpoint code stays thin. See [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal API, existing `GameBot.Domain` gamepack and queue-settings classes (features 143, 145, 146, 147)
**Storage**: Files. One new file type: the backup JSON in `<dataRoot>\gamepacks\backups\`. No new database.
**Testing**: xUnit. Unit tests with fakes for the orchestrator. Contract tests for the routes. Integration test for the restore with an injected write fault.
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only)
**Performance Goals**: The flow adds under 2 s of its own time to the stop, install, and start time for 20 queues and 20 templates (tested with fakes).
**Constraints**: Fixed stop limit of 60 s for each queue. One managed call at a time on the instance. No unhandled error for a known failure. Keep `Program.cs` thin (build-time analyzers).
**Scale/Scope**: One instance, tens of queues and templates, gamepack files up to 100 MB.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI).

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small classes. A port separates Domain from the internal Service type. Thin endpoint handlers. |
| II. Testing | Pass | Unit, contract, and integration tests are planned. Tests cover each clarification. These are: same version (exact text match, FR-018), failed first install, dry run with a missing template, dry run with a signature problem, conflict on the whole instance, no backup when no template is affected, failed managed rollback, report completeness (SC-006), and the 147 catalog protocol (FR-017). |
| III. UX consistency | Pass | No web UI change. Phone check: not applicable. API routes mirror features 143 and 147. |
| IV. Performance | Pass | Goal stated above. No new hot path. |
| V. Living documentation | Pass | OpenAPI schemas through an `ISchemaFilter` with STE text. Docs update in the task list. |
| VI. STE | Pass | All artifacts are in STE. |

Post-design re-check: Pass. No violation. The Complexity Tracking table stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/148-managed-gamepack-update/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── managed-update-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Gamepacks/
│       ├── Managed/                 # new: orchestrator, request, report, port, backup store, options
│       └── Catalog/                 # change: GamepackCatalogService.FetchAsync
└── GameBot.Service/
    ├── Endpoints/                   # new: managed update routes (one file); rollback route gets `managed`
    └── Services/                    # new: port implementation over IQueueExecutionService

tests/
├── unit/                            # orchestrator, backup store, booking, scope tests
├── contract/                        # route and report contract tests
└── integration/                     # restore test with an injected write fault
```

**Structure Decision**: Backend only. Flow logic in Domain behind a port. Routes and the port implementation in Service. The existing test projects hold the new tests.

## Complexity Tracking

No violations.
