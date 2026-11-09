# Implementation Plan: Run Next for Unscheduled Tasks

**Branch**: `184-showdown-run-next` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/184-showdown-run-next/spec.md`

## Summary

An operator can queue one run of any idle template entry of a running queue. One tap or click is enough.

- The service keeps a new register of "run next" requests on the run handle. It belongs to one run.
- The run loop drains the register after each firing group. It uses the parameter scope of the entry.
- The monitor shows the state of each entry: running, queued, scheduled, or idle.
- The desktop editor and the phone queue detail show a **Run next** icon button for each idle entry.
- All buttons of the template editor become icon buttons, in the style of the Queues list. A sequence row then uses fewer lines.

The service already has a live-schedule call (feature 059). It does not fit, for three reasons:

- It takes a sequence ID, not an entry ID.
- It uses the queue scope, not the entry scope.
- It has no state check.

The new call is a small, separate path. It uses the same hold rules as feature 106.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (service and domain); TypeScript 5.6 with React 18 (web UI)  
**Primary Dependencies**: ASP.NET Core minimal API; Vite; Vitest and Testing Library; xUnit (existing)  
**Storage**: None new. The register is in memory on the run handle. The template file does not change.  
**Testing**: xUnit unit, contract, and integration tests in `tests/`; Vitest tests in `src/web-ui`  
**Target Platform**: Windows service host; browser UI at desktop and phone widths  
**Project Type**: web application (service API and web UI)  
**Performance Goals**: A request returns in 100 ms or less (p95). The entry starts when the running sequence ends, or at once if none runs. The monitor adds no extra request. A timing test checks the call, and the PR text has a perf note (Principle IV).  
**Constraints**: Phone support level `full` for **Run next**. Tap target 44 x 44 px. No sideways scroll at 320 px. The desktop layout stays the same except the editor rows. The UI never sends the call again after a connection loss.  
**Scale/Scope**: One run handle per queue. At most one request for each entry. About 10 to 50 entries for each template.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: **Run next** has phone support level `full`. It shows in the phone queue detail, and it follows docs/mobile-ui-principles.md. The template editor stays `desktop-only`.

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small methods on the handle. No underscores in method names. Lint stays clean. |
| II. Testing | Pass | Unit tests for the register and the loop drain. Contract and integration tests for the call. Vitest tests for the buttons. |
| III. UX consistency | Pass | Same icon style as the Queues list. Error messages say what to do. Phone level stated. |
| IV. Performance | Pass | Goals in the Technical Context. The loop adds one empty-check for each pass. |
| V. Living documentation | Pass | Update `docs/architecture.md`, `docs/mobile-ui-principles.md`, `specs/STATUS.md`, and the spec status. |
| VI. STE | Pass | All new text follows STE. |
| Version bump | Pending | Raise `patch` before the PR (see CLAUDE.md). |

Post-design check: no violation. Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/184-showdown-run-next/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── run-next-api.md
│   └── editor-icons-contract.md
├── checklists/requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/
│   ├── Contracts/Queues/RunNextRequest.cs            # new request and response DTOs
│   ├── Contracts/Queues/QueueMonitorResponse.cs      # add entryStates
│   ├── Endpoints/QueuesEndpoints.cs                  # new POST {id}/run-next
│   └── Services/QueueExecution/
│       ├── QueueEntryStateReader.cs                  # new: state of an entry
│       ├── QueueRunHandle.cs                         # new run-next register (PendingRunNext)
│       ├── IQueueExecutionService.cs                 # new RunNext method and outcome
│       ├── QueueExecutionService.cs                  # method and loop drain
│       ├── QueueMonitorService.cs                    # entry states and the queued item
│       └── QueueMonitorSnapshot.cs                   # new ScheduleKind value RunNext
└── web-ui/src/
    ├── services/queues.ts                            # runNextEntry call, entryStates type
    ├── pages/QueuesPage.tsx                          # stopped-queue editor: Run next off with reason
    ├── components/queues/
    │   ├── IconButton.tsx                            # new icons
    │   ├── SchedulingSequenceCard.tsx                # icon buttons, Run next
    │   ├── QueueSchedulingAreas.tsx                  # pass entry state and handler
    │   ├── RunningQueueEditor.tsx                    # poll the monitor, send the call
    │   ├── QueueTemplateControls.tsx                 # other editor buttons become icons
    │   ├── PhoneQueuesView.tsx                       # Run next in the schedule list
    │   └── queueActivity.ts                          # entry state in the schedule rows
    └── (CSS next to each component)

src/web-ui/tests/e2e/editor-row-height.spec.ts        # Playwright row height test

tests/
├── unit/Queues/                                      # handle, loop, monitor tests
├── contract/Queues/                                  # run-next API contract tests
└── integration/Queues/                               # run-next end to end
docs/architecture.md, docs/mobile-ui-principles.md, CHANGELOG.md,
specs/STATUS.md, installer/versioning/version.override.json
```

**Structure Decision**: Keep the existing split (service, domain, web UI). Add no new project. The new
code sits next to the live-schedule code, because both feed the same run loop.

## Complexity Tracking

No violation. The table stays empty.
