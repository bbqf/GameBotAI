# Implementation Plan: Smart Scheduling

**Branch**: `189-smart-scheduling` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/189-smart-scheduling/spec.md`

## Summary

An idle queue wakes up 85 times each day, because each sequence books its own time. Smart scheduling merges near wake-ups. When a sequence books a Timer firing of itself for a time T, and a different firing is already pending from T to T plus the window, the booking moves to the time of that firing. The setting is on the queue (default off, window 15 minutes, 1 to 120). A sequence can ignore it (default off).

The technical approach: a pure rule (`SmartSchedulingRule`) in the domain calculates the booked time. `SelfRescheduleCoordinator` collects the pending firings of the run (Timer bookings, template Timer entries, daily retries, live schedules) and applies the rule in its `Timer` case, before `AddTimerFiring`. `SequenceExecutionService` reads the stored queue at each Timer booking, so a saved setting applies immediately. The booking stores the requested time for the monitor and for the execution log. The queue and sequence routes, the settings document, and the web UI get the new settings as additive members.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`); TypeScript with React for the web UI
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle; React, Jest. No new package.
**Storage**: JSON files. Two new members in each queue file, one new optional member in each sequence file. No migration: an old file reads with the defaults.
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`) with the existing `Harness`, `FakeSequenceExecution`, and `FakeTimeProvider`; Jest for the web UI
**Target Platform**: Windows service host (GameBot.Service) and the browser web UI
**Project Type**: web-service with web-ui
**Performance Goals**: One Timer booking adds one read of one queue file and one linear scan of the pending firings (approximately 20 to 40 items). The added time for each booking is less than 10 ms. The run loop, the 250 ms poll, and `ComputeNextDue` do not change.
**Constraints**: Default off: a queue with smart scheduling off has the same booked times, execution order, and execution log content as before. A booked time is never earlier than the requested time and never later than the requested time plus the window. A booked time never changes after the booking. All API changes are additive.
**Scale/Scope**: Approximately 15 changed and 4 new source files in the service and the domain, 7 changed web UI files, 1 new doc, approximately 9 new test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: Build and test runs must pass before implementation progresses. Plan: run `dotnet build -c Debug`, the unit tests of `tests/unit/Queues`, and the changed web UI tests before each commit.

*NON-NEGOTIABLE*: All text in this plan and in its artifacts obeys STE (Principle VI). Status: obeyed.

*Phone check (Principle III)*: The phone support level is `reduced`, the level of the Queues area. The phone view shows the booked time of each pending firing and the text "postponed from HH:mm" in the schedule row, through the shared `buildScheduleRows` function, with no new control. The queue settings form and the sequence editor (Authoring, `desktop-only`) are large-screen functions, as today. The feature follows docs/mobile-ui-principles.md.

- I. Code quality: the rule is one pure class with one method. The coordinator gets one small private method for the candidates. No method exceeds approximately 50 lines. No new dependency. Pass.
- II. Testing: unit tests for the rule (the decision table of `contracts/scheduling-rule.md`), the coordinator, the new schedule read method, the monitor projection, and the queue and sequence persistence. Integration tests for a run with two sequences and for a setting change in a running queue. Contract tests for the queue, sequence, and monitor shapes and the OpenAPI text. Jest tests for the form, the monitor, and the sequence page. Pass.
- III. UX consistency: the new members follow the patterns of `pauseWhenIdle` (queue form) and `excludeFromSuccessNotifications` (sequence). The error message gives the permitted range. Phone level stated. Pass.
- IV. Performance: goals are in Technical Context. The PR gets a short performance note (research R-011). Pass.
- V. Living documentation: update `docs/architecture.md` (new "Smart scheduling" paragraph, the queue and sequence model, the monitor, the exception to feature 173, "Last reviewed"), add `docs/smart-scheduling.md` (FR-023), update `specs/STATUS.md`, `CHANGELOG.md`, and the spec `Status`. Pass.
- VI. STE: obeyed in all new text. Pass.
- Version bump and CHANGELOG entry: before the PR.

**Result after Phase 1 design**: no violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/189-smart-scheduling/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── rest-api.md
│   └── scheduling-rule.md
├── checklists/requirements.md
└── tasks.md                 # made by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Queues/
│   ├── ExecutionQueue.cs                 # add SmartScheduling, SmartSchedulingWindowMinutes
│   ├── SmartSchedulingLimits.cs          # NEW: default, minimum, maximum, IsValidWindow
│   └── SmartSchedulingRule.cs            # NEW: SmartCandidate, SmartTargetKind, SmartSchedulingDecision, Resolve
├── Commands/CommandSequence.cs           # add IgnoreSmartScheduling
├── Services/
│   ├── ActionDispatchResult.cs           # add optional SmartScheduling (SmartSchedulingLogInfo)
│   └── SequenceRunner.cs                 # carry SmartScheduling on the step record (pattern of Removed)
└── QueueSettings/
    ├── SettingsDocument.cs               # add the two members to QueueSettingsDoc
    ├── QueueSettingsExporter.cs          # write the two members
    └── QueueSettingsImporter.cs          # read and check the two members

src/GameBot.Service/
├── Services/QueueExecution/
│   ├── ISelfRescheduleCoordinator.cs     # SmartSchedulingRequest; new optional parameter; result members
│   ├── SelfRescheduleCoordinator.cs      # collect candidates, apply the rule, log event 1162
│   ├── QueueRunHandle.cs                 # SelfRescheduleEntry.Postponement (SmartPostponement)
│   ├── QueueRunSchedule.cs               # NEW read method SnapshotTimedEntryDueTimes
│   ├── QueueMonitorService.cs            # requestedAt, reason text, smartScheduling block
│   └── QueueMonitorSnapshot.cs           # item RequestedAt; snapshot SmartScheduling
├── Services/SequenceExecution/
│   └── SequenceExecutionService.cs       # read the stored queue and the sequence flag; message and attributes
├── Contracts/Queues/
│   ├── CreateQueueRequest.cs             # bool? smartScheduling, int? smartSchedulingWindowMinutes
│   ├── UpdateQueueRequest.cs             # same
│   ├── QueueResponse.cs                  # the two members
│   └── QueueMonitorResponse.cs           # smartScheduling block; item requestedAt
├── Endpoints/
│   ├── QueuesEndpoints.cs                # create, update (keep on null), duplicate, map, range check, monitor map
│   └── SequencesEndpoints.cs             # ignoreSmartScheduling on create, update, PATCH, list, detail
├── Models/SequenceStepContracts.cs       # ignoreSmartScheduling
├── Swagger/SmartSchedulingSchemaFilter.cs   # NEW: descriptions of the queue and monitor members
├── Swagger/SequenceTimeLimitSchemaFilter.cs # description of ignoreSmartScheduling
└── GameBotServiceSetup.cs                # register the new schema filter

src/web-ui/src/
├── services/queues.ts                    # types of the queue members and of the monitor members
├── services/sequences.ts                 # ignoreSmartScheduling
├── components/queues/QueueForm.tsx       # switch and window field (shown when the switch is on); running-queue note
├── components/queues/QueueMonitor.tsx    # header line; the reason text comes from the server
├── components/queues/queueActivity.ts    # schedule row text "postponed from HH:mm" (phone and large screen)
├── pages/QueuesPage.tsx                  # form state, create and update calls
└── pages/SequencesPage.tsx               # checkbox in the create and edit forms; mark in the list

tests/unit/Queues/
├── SmartSchedulingRuleTests.cs                    # NEW: decision table
├── SelfRescheduleCoordinatorSmartTests.cs         # NEW: candidates, same-sequence exclusion, keep earliest, rearm
├── QueueRunScheduleDueTimesTests.cs               # NEW: SnapshotTimedEntryDueTimes
├── QueueMonitorSmartSchedulingTests.cs            # NEW: requestedAt, reason, block
├── QueueSettingsWhileRunningTests.cs              # add the test of the immediate effect; update the comment
└── FileQueueRepositoryTests.cs                    # round trip and old-file defaults
tests/integration/Queues/SmartSchedulingRunIntegrationTests.cs      # NEW: two sequences merge; ignore flag; off = no change
tests/integration/Sequences/IgnoreSmartSchedulingPersistenceTests.cs # NEW
tests/contract/Queues/SmartSchedulingQueueContractTests.cs          # NEW: routes, 400, duplicate, OpenAPI
tests/contract/Sequences/IgnoreSmartSchedulingContractTests.cs      # NEW: routes, 400, OpenAPI
src/web-ui/src/components/queues/__tests__/                         # QueueForm and QueueMonitor tests
src/web-ui/src/pages/__tests__/SequencesPage.ignoreSmartScheduling.spec.tsx  # NEW

docs/architecture.md                      # smart scheduling paragraph, models, monitor, "Last reviewed"
docs/smart-scheduling.md                  # NEW user guide (FR-023)
specs/STATUS.md                           # add the row for 189
specs/173-queues-ui-cleanup/spec.md       # add a pointer from FR-019 to the exception of feature 189
CHANGELOG.md                              # add the entry
installer/versioning/version.override.json  # version bump before the PR
```

**Structure Decision**: The rule and the limits are in `GameBot.Domain`, because they are pure and have no service dependency. The coordinator in `GameBot.Service` stays the one place that gives a Timer booking its time. The web UI changes are in the existing queue form, monitor, and sequence page. No new project and no new area.

## Design Notes

1. **Flow of one booking**: `SequenceExecutionService.DispatchActionAsync` gets a `reschedule-self` Timer step with a queue ID. It reads the stored queue. If the queue has smart scheduling on and the sequence does not ignore it, it makes a `SmartSchedulingRequest`. The coordinator calculates T as today, collects the candidates with no firing of the same sequence, calls `SmartSchedulingRule.Resolve`, and adds the entry with the booked time and the `Postponement`. The result goes back with the requested time and the target. The service resolves the name of the target sequence and writes the message and the attributes.
2. **No change with the setting off**: with a null request the coordinator runs the code path of today. The entry has no `Postponement`, and the message has no added text (SC-004).
3. **One exception to feature 173**: the two queue settings apply at the next booking, not at the next start (research R-004). All other queue settings stay as they are.
4. **Read failure**: if the queue read fails or gives no queue, the service gives a null request. The booking keeps the requested time. A setting problem never fails a `reschedule-self` step.

## Complexity Tracking

No violations.
