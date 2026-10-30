# Implementation Plan: End a run at its time limit without a failure status

**Branch**: `170-end-run-at-time-limit` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/170-end-run-at-time-limit/spec.md`

## Summary

A sequence gets the optional setting `onTimeLimit` (`fail` default, or `end`). When a run reaches its time limit
and the sequence has `end`, the run gets the log status `success`. The reason `sequence_time_limit` and the limit
value stay in the log entry, so a reader separates a planned stop from a fault with structured fields only (status
and reason). The summary text SHOULD also say it, but it is not a contract. The queue counts such a firing as
successful (FR-004a): no failure notification and no daily retry. Sequences without the option keep today's
behaviour. The limit value and its default (240000 ms) do not change. The in-flight step is cancelled as today;
only the final status changes.

Approach (see research.md): store the option on `CommandSequence`; read it with the limit in
`QueueExecutionService.RunOneSequenceAsync`; pass it to `SequenceTimeLimitScope`; set the final status in
`SequenceExecutionService` at the two finalize sites; validate in the API and the repository with one shared error
message; add a select to the sequence form in the web UI (phone support level: full).

## Technical Context

**Language/Version**: C# on .NET (service and domain); TypeScript with React (web UI)  
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (OpenAPI), Vite, Vitest  
**Storage**: Sequence JSON files (`FileSequenceRepository`); the new member is omitted when `fail`  
**Testing**: `dotnet test` (unit, integration, contract); `npm test` in `src/web-ui`  
**Target Platform**: Windows service with a browser UI (desktop and phone)  
**Project Type**: Web service with web UI  
**Performance Goals**: No change. One sequence read per firing (already done today).  
**Constraints**: No new log status or condition type (FR-008). `watchdogTimeoutMs` and its default (240000 ms) do
not change (FR-007). Parent sequence handling does not change. Old files load with no change (FR-010).  
**Scale/Scope**: Small. About 8 source files, one UI form, docs. New tests go in new test files, apart from the
existing time-limit regression file.

No item is marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: Passed before Phase 0 and again after Phase 1 design.*

- **I. Code quality**: Small change that follows the pattern of feature 122 (optional member, omitted when
  default). One shared constant for the allowed values and the error message, used by the API and the repository.
  No new abstraction. Pass.
- **II. Testing**: Unit tests for the scope and the finalize sites (the in-flight step is cancelled, only the
  final status changes); integration tests for the queue (success record, no notification, no retry, default limit
  240000 ms, same-time fault keeps `failure`); a parent test that compares the parent run status and the queue
  record with the `fail` case; API tests for validation on POST, PUT, and PATCH, and a test that the API and the
  repository give the same error message; repository and round-trip tests; UI tests. A test on the summary text is
  a soft check. Pass.
- **III. UX consistency**: The web UI changes. Phone support level: full. The select follows
  docs/mobile-ui-principles.md: one column, no horizontal scroll, a hit area of at least 44 x 44 CSS px, and an
  input font of at least 16 px. Pass.
- **IV. Performance**: No new work on the hot path. Pass.
- **V. Living documentation**: Update OpenAPI text, `docs/architecture.md`, the authoring guide, `CHANGELOG.md`,
  and `specs/STATUS.md` (the spec Status line becomes "Implemented" when the work ends). Pass.
- **VI. STE**: All new text uses STE. Pass.
- **Build and test gate**: No known failing run. Pass.

No violation. The Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/170-end-run-at-time-limit/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── api-changes.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Made by /speckit.tasks, not by this command
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/
├── CommandSequence.cs                     # new member OnTimeLimit
└── SequenceTimeLimits.cs                  # allowed values, check helper, shared error message (default unchanged)

src/GameBot.Domain/ (repository)
└── FileSequenceRepository                 # reject unknown value on save with the shared message

src/GameBot.Service/
├── Services/SequenceExecution/
│   ├── SequenceTimeLimitScope.cs          # new member EndsRunAtLimit
│   └── SequenceExecutionService.cs        # status at FinalizeAbandonedAsync and normal finalize
├── Services/QueueExecution/
│   └── QueueExecutionService.cs           # read option with limit; record Success for an end stop
├── Endpoints/ (sequence endpoints)        # POST, PUT, PATCH validation; response shapes
├── Models/ (sequence DTOs)                # request and response member
└── Swagger/                               # SequenceTimeLimitSchemaFilter, SwaggerConfig text

src/web-ui/src/pages/SequencesPage.tsx     # select in create and edit forms (44x44 px hit area, 16 px font)

tests/ (unit, integration, contract)       # new test files; see Constitution Check II
docs/architecture.md, CHANGELOG.md, specs/STATUS.md, sequence authoring guide
```

**Structure Decision**: Use the existing service, domain, and web UI projects. No new project.

## Phase 0 and Phase 1 Output

- [research.md](research.md): decisions R-001 to R-008.
- [data-model.md](data-model.md): sequence field, scope field, log result table, queue record.
- [contracts/api-changes.md](contracts/api-changes.md): request, response, error, and log changes.
- [quickstart.md](quickstart.md): manual check steps.
- Agent context: `CLAUDE.md` already points to this plan between the SPECKIT markers.

## Complexity Tracking

No violation. Not used.
