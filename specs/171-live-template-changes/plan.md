# Implementation Plan: Apply template changes to a running queue

**Branch**: `171-live-template-changes` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/171-live-template-changes/spec.md`

## Summary

An operator pauses a running queue at a firing boundary. Then the operator reviews the differences between
the running queue and its linked template, selects some of them, and applies them as one atomic change. Then
the operator resumes the same run. No timer is reset, moved, or skipped, unless the operator selects a
schedule change for that entry.

Approach (details in [research.md](research.md)):

1. **Operator pause**: a new pause register on `QueueRunHandle`, separate from the idle pause (073) and the
   failure-policy pause (087). States: running, pausing, paused. The wire value of `pauseKind` is `operator`.
   The run loop checks it at each **firing-group boundary** (R-001). The existing `POST /api/queues/{id}/resume`
   releases it.
2. **Entry ID**: `QueueTemplateEntry.EntryId` (new, stored). The runtime entries use the same ID. The old
   templates show "needs upgrade" and get IDs only from an explicit upgrade action (R-003).
3. **Run plan**: the partitions that the run loop keeps in locals today move into one immutable `QueueRunPlan`.
   The consumed-work state in `QueueRunSchedule` is keyed by entry ID, not by list index (R-002). An apply
   action swaps the plan under one lock. The run loop uses the "held iteration" path of feature 106 to read the
   new plan without a repeat of work that is done (R-004).
4. **Diff and apply**: two new endpoints compute the differences and apply the selected ones. A base token (a
   hash of the template content) detects an edit during the review (R-005).
5. **Upgrade**: a manual action, in the API and in the web UI, plus a documented procedure (R-006).
6. **Web UI**: Pause, Resume, "Review changes" dialog, and the "needs upgrade" mark. Phone support level:
   **full**.

## Technical Context

**Language/Version**: C# on .NET (service and domain); TypeScript with React (web UI)  
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (OpenAPI), Vite, Vitest  
**Storage**: Queue template JSON files (`FileQueueTemplateRepository`). New optional member `entryId` on each
entry. No new file. The pause and the plan are run state in memory, as all run state is today.  
**Testing**: `dotnet test` (unit, integration, contract); `npm test` in `src/web-ui`  
**Target Platform**: Windows service with a browser UI (desktop and phone)  
**Project Type**: Web service with web UI  
**Performance Goals**: The queue starts 0 new firing groups after the pause request. The state changes to
"paused" at most 1 s after the running firing group ends (an engineering goal, not a spec criterion). The diff
and the apply for a template of 200 entries take under 200 ms. The pause check adds no I/O to the loop. T059
records a timing note (constitution, Principle IV).  
**Constraints**: No change to schedule semantics. No change to restart behavior (FR-007). No change to the
time limit of a sequence (FR-032): the pause has no time limit of its own. The liveness watch and the device
recovery keep running during a pause (FR-034). The pause never changes `Running` / `Stopped` status.  
**Scale/Scope**: About 14 source files, 2 new endpoints groups, one dialog, one doc. The loop change is the
main risk (R-004). New tests go in new files, apart from small edits to the schedule tests.

No item is marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: Passed before Phase 0 and again after Phase 1 design.*

- **I. Code quality**: `QueueRunPlan` replaces 6 loop locals with one type. The loop gets one new helper for the
  gate. Each new method stays under about 50 lines. No underscore in method names. Pass.
- **II. Testing**: Unit tests for the pause register, the plan, the diff, the apply, the schedule keyed by ID,
  and the upgrade. Integration tests for the run loop (pause at a boundary, atomic apply, no schedule change,
  resume with a late entry, time limit during a pause, restart). Contract tests for the new endpoints. UI tests
  with Vitest. A regression test first for each changed behavior of the loop. Pass.
- **III. UX consistency**: The web UI changes. **Phone support level: full.** Follows
  docs/mobile-ui-principles.md: one column, no horizontal scroll, a hit area of at least 44 x 44 CSS px, input
  font of at least 16 px. Error messages say what to do (for example "Pause the queue first."). Pass.
- **IV. Performance**: See Performance Goals. No hot-path I/O. Pass.
- **V. Living documentation**: Update `docs/architecture.md` (domain model, API surface, persistence; refresh
  "Last reviewed"), add `docs/live-template-changes.md`, update `CHANGELOG.md` and `specs/STATUS.md`. Pass.
- **VI. STE**: All new text uses STE. Pass.
- **Version**: The release needs a minor bump (FR-031). Set `minor` to `12` and `patch` to `"0"` in
  `installer/versioning/version.override.json` before the push. Pass.
- **Build and test gate**: No known failing run. Pass.

No violation. The Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/171-live-template-changes/
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
src/GameBot.Domain/QueueTemplates/
├── QueueTemplateEntry.cs                  # new member EntryId (omitted when empty)
├── QueueTemplate.cs                       # NeedsUpgrade (computed, not stored)
└── QueueTemplateUpgrade.cs                # new: gives IDs; safe to repeat

src/GameBot.Domain/Queues/
├── QueueEntry.cs                          # runtime entry reuses the template EntryId
├── IQueueRuntimeStore.cs / QueueRuntimeStore.cs   # SetEntries and a new atomic ReplaceEntries take IDs

src/GameBot.Service/Services/QueueExecution/
├── QueueRunPlan.cs                        # new: immutable plan and partitions
├── QueueRunSchedule.cs                    # state keyed by entry ID; plan swap; ComputeNextDue reads the plan
├── QueueRunHandle.cs                      # operator pause register; plan reference; PlanVersion
├── QueueExecutionService.cs               # plan at iteration top; gate in FireGroupAsync; replan path
├── QueueMonitorService.cs                 # read entries by ID, not by index
├── QueuePauseSnapshot.cs                  # new kind `operator`
├── QueueTemplateDiff.cs                   # new: compute differences, base token
└── QueueLiveApplyService.cs               # new: validate, lock, swap plan and runtime entries

src/GameBot.Service/
├── Endpoints/QueuesEndpoints.cs           # POST pause; resume extended; GET template-diff; POST apply-template
├── Endpoints/QueueTemplatesEndpoints.cs   # entryId in save and detail; POST upgrade
├── Endpoints/QueueSettingsEndpoints.cs    # export carries entryId (feature 145)
├── Contracts/Queues/                      # pause, diff, apply, upgrade DTOs; health fields
└── Swagger/                               # schema text for the new members

src/GameBot.Domain/QueueSettings/          # exporter, importer, validator carry entryId
src/GameBot.Domain/Gamepacks/              # seed template creation gives IDs

src/web-ui/src/
├── components/queues/QueuePauseControls.tsx        # new: Pause, Resume, state text
├── components/queues/ReviewTemplateChangesDialog.tsx   # new: differences, selection, apply
├── components/queues/QueueTemplateControls.tsx     # "needs upgrade" mark and action
├── services/ (queues, queueTemplates)              # new calls
└── pages/QueuesPage.tsx, components/queues/PhoneQueuesView.tsx   # wire the new controls

tests/unit/QueueExecution/ , tests/unit/QueueTemplates/ , tests/integration/ , tests/contract/
docs/live-template-changes.md ; docs/architecture.md ; CHANGELOG.md ; specs/STATUS.md
installer/versioning/version.override.json      # minor 12, patch 0
```

**Structure Decision**: The change follows the existing layout. The domain holds the stored shape (entry ID,
upgrade). The service holds the run state and the endpoints. The web UI holds one new control and one new
dialog. No new project.

## Complexity and Risk Notes

- **R-004 (loop re-plan)** is the main risk. The run loop is one method of about 500 lines. The plan swaps
  only at a pause point, and the re-plan reuses the feature 106 "held iteration" path. A dedicated integration
  test set (one test for each schedule type) guards it. If this proves too hard, ship US1 to US3 and US5 with
  enable, disable, parameters, add, and remove only, and defer order (US4).
- **Firing-group granularity** (R-001) is a small reading of FR-002: a firing group is one scheduled sequence
  plus the helper sequences of that firing (before-each-run and every-step). The spec Assumptions get one
  line for this.
