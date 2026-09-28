# Implementation Plan: A reschedule-self time of day that has passed books the next day

**Branch**: `claude/resolve-github-issue-uxxlah` (spec number 109) | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/109-reschedule-timeofday-nextday/spec.md`

## Summary

A `reschedule-self` step with `option: Timer` and a `timerTimeOfDay` that has passed today books the sequence for the current moment. The queue then runs the sequence again immediately, and again after each run (research R-001).

The fix changes one private method, `SelfRescheduleCoordinator.ResolveTimerFireAt`. A time of day that is later than the current local time gives today at that time, as before. Else it gives that time on the next local day (research R-002, R-003). The UTC offset comes from the service-local time zone for that date and time (research R-004).

The monitor shows `SelfRescheduleEntry.FireAt` without a change, so it shows the correct instant after the fix. The OpenAPI payload text, `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` tell the new rule.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions  
**Storage**: N/A. No storage change.  
**Testing**: xUnit unit tests (`tests/unit`), OpenAPI contract tests (`tests/contract`)  
**Target Platform**: Windows service (CI on `windows-latest`)  
**Project Type**: Web service with a web UI. This fix touches only the service.  
**Performance Goals**: No change. The rule is a few date calculations.  
**Constraints**: No change to `timerRelativeOffset`, `ocrOffset`, the other options, the template `Timer` entry, the accepted formats, or the payload validation (spec FR-006, FR-007).  
**Scale/Scope**: 2 source files, 1 test helper, 3 test files, `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in existing style. Test method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | New unit tests with the probe values before the fix (tasks in phase 3). |
| UX / API consistency | Pass | No new field. The monitor and the step result show the booked instant, as before. |
| Performance | Pass | No change. |
| Living docs | Pass | `docs/architecture.md` gets the rule and a new "Last reviewed" date. `CHANGELOG.md` and `specs/STATUS.md` change too. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/109-reschedule-timeofday-nextday/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── reschedule-self-timer.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs
    # ResolveTimerFireAt: next occurrence of the time of day; offset from LocalTimeZone
src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs
    # reschedule-self payload text: a time of day that has passed books the next day

tests/unit/Queues/FakeTimeProvider.cs                    # optional TimeZoneInfo parameter
tests/unit/Queues/SelfRescheduleCoordinatorTests.cs      # replace TimerPastTimeOfDayCollapsesToNow; add probe, equal, DST tests
tests/unit/Queues/QueueMonitorServiceTests.cs            # monitor shows the next-day instant
tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs  # expect "next day" in the payload text

docs/architecture.md
CHANGELOG.md
specs/STATUS.md
```

**Structure Decision**: Use the existing projects. All code changes are in `GameBot.Service`. Tests go in the existing unit and contract test projects.

## Complexity Tracking

No violations.
