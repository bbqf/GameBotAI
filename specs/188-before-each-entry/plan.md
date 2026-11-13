# Implementation Plan: Before Each Run Before Every Firing

**Branch**: `188-before-each-entry` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/188-before-each-entry/spec.md`

## Summary

Today the Before Each Run pass runs at most one time in each wake-up of the scheduler loop. This feature removes that limit. The pass runs immediately before each triggering firing that runs, also when several firings are due in one wake-up (issue #393).

The technical approach: in `QueueExecutionService`, remove the flag `beforeEachRunRanThisIteration` and its reset at the start of each loop iteration. `RunBeforeEachRunPassAsync` then runs the full pass each time that `FireGroupAsync` (with `beforeEachRun: true`) or the daily-retry site calls it. The set of call sites does not change, so the set of triggering firings does not change. The accounting, the run log nodes, the liveness hold, and the operator gate stay as they are. Update the comments, the API descriptions, `docs/architecture.md`, and the unit tests that check the old rule.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core minimal API (no change), no new package
**Storage**: N/A (no change to stored templates or to any file format)
**Testing**: xUnit (`tests/unit/Queues`), with the existing `Harness` and `FakeSequenceExecution` fakes
**Target Platform**: Windows service host (GameBot.Service)
**Project Type**: web-service with web-ui (only the service changes)
**Performance Goals**: The engine adds no measurable cost. The run time of a wake-up with N triggering firings increases by (N - 1) times the run time of the Before Each Run pass. The issue accepts this cost (about 10 s for each pass, about 3 minutes for a full roster of about 20 entries).
**Constraints**: No new schedule type, flag, or template option. Stored templates load with no change. A wake-up with one triggering firing behaves exactly as before.
**Scale/Scope**: 1 changed engine file (about 5 lines of code), 4 changed comment or description files, 1 changed doc, 2 changed test files (new and changed tests in `QueueExecutionServiceBeforeEachRunTests.cs`, one new test in `QueueLivenessGateTests.cs`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: Build and test runs must pass before implementation progresses. Plan: run `dotnet build -c Debug` and the unit tests of `tests/unit/Queues` before each commit.

*NON-NEGOTIABLE*: All text in this plan and in its artifacts obeys STE (Principle VI). Status: obeyed.

*Phone check (Principle III)*: The feature does not change the web UI. The UI shows only the label "Before each run" and no text about the frequency of the pass. No phone check is necessary.

- I. Code quality: the change removes code (one flag and one reset). No new method. No dead code stays. Pass.
- II. Testing: the unit tests of the old "once per wake-up" rule change to check the new rule. New unit tests cover three co-due firings, two entries in template order, the After Every Step order, failure accounting for each pass, the run log nodes, mixed firing kinds, and a liveness hold. Pass.
- III. UX consistency: no UI change. The queue monitor text stays "Before Each Run". Pass.
- IV. Performance: the added run time is the run time of the configured sequences, and the issue accepts it. The PR gets a short performance note. Pass.
- V. Living documentation: update `docs/architecture.md` (the *Before each run* paragraph and "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, and the spec `Status`. Pass.
- VI. STE: obeyed in all new text. Pass.
- Version bump and CHANGELOG entry: before the PR.

**Result after Phase 1 design**: no violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/188-before-each-entry/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── execution-order.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/QueueExecution/
├── QueueExecutionService.cs        # remove beforeEachRunRanThisIteration; update class and pass comments
└── QueueMonitorSnapshot.cs         # update the BeforeEachRun comment
src/GameBot.Service/Contracts/QueueTemplates/
├── TemplateEntrySaveRequest.cs     # update the BeforeEachRun API description
└── QueueTemplateDetailResponse.cs  # update the BeforeEachRun API description
src/GameBot.Domain/QueueTemplates/
└── ScheduleType.cs                 # update the BeforeEachRun comment

tests/unit/Queues/
├── QueueExecutionServiceBeforeEachRunTests.cs   # change the once-per-wake-up test; add the feature 188 tests
└── QueueLivenessGateTests.cs                    # add one test: held co-due firings run no pass

specs/095-before-each-run-schedule/spec.md       # add a pointer from FR-003 to feature 188

docs/architecture.md                # update the "Before each run" paragraph
specs/STATUS.md                     # add the row for 188
CHANGELOG.md                        # add the entry
installer/versioning/version.override.json      # version bump before the PR
```

**Structure Decision**: The change is in the queue execution engine of `GameBot.Service` only. The domain model, the API shape, the storage, and the web UI do not change.

## Complexity Tracking

No violations.
