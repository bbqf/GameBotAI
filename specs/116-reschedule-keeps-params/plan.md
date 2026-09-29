# Implementation Plan: Keep the parameter values of a queue entry on a run that reschedule-self starts

**Branch**: `116-reschedule-keeps-params` | **Date**: 2026-09-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/116-reschedule-keeps-params/spec.md` (GitHub issue #249, B-031)

## Summary

A `reschedule-self` step books a new run of its sequence in the active queue run. The booked run must use the parameter scope of the run that booked it. Now, the booking has no scope, and the service starts the booked run with the queue scope only. Thus the entry layer (`parameterValues` of the template entry) is not in the scope, and a required parameter does not resolve.

The run loop already reads a scope from the booking (`SelfRescheduleEntry.Scope`, feature 078 FR-015). Only the side that makes the booking does not set it. The fix sends the scope that the service gave the run (the `scope` argument of `SequenceExecutionService.ExecuteAsync`) through `DispatchSelfReschedule` to `ISelfRescheduleCoordinator.ScheduleSelf`. The coordinator then keeps the scope on each `SelfRescheduleEntry` that it makes, for all four booking options (five variants, because Timer has a relative offset and a time of day). There is no change to the API, to the `reschedule-self` payload, to the persisted template, or to the execution log.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core minimal API host (`GameBot.Service`), domain library (`GameBot.Domain`); no new dependency
**Storage**: N/A. A booking stays in memory on `QueueRunHandle` for the life of the queue run. No persisted format changes.
**Testing**: xUnit + FluentAssertions. Unit tests in `tests/unit/Queues` and `tests/unit/Sequences`; integration tests in `tests/integration/Queues` with `WebApplicationFactory<Program>` and `GAMEBOT_USE_ADB=false`.
**Target Platform**: Windows service (local host), emulator through ADB
**Project Type**: web service (backend only; no web UI change)
**Performance Goals**: No measurable change. The booking keeps one reference to an immutable `ParameterScope` object that already exists. There is no copy and no new I/O.
**Constraints**: No new API field, payload field, log field, or persisted field (FR-006, FR-009). The resolution order and the scope layers of all other runs stay the same (FR-005).
**Scale/Scope**: 3 production files, about 20 changed lines; new unit tests and two integration tests (OncePerRun and Timer).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | Small, cohesive change in 3 files. No dead code. The new parameter is optional, so no caller that has no scope must change. Analyzers must stay clean. | PASS |
| II. Testing | Bug fix: write the failing tests first (coordinator keeps the scope; booked run gets the entry scope; dispatch sends the scope). The unit tests are deterministic (fake clock, fake sequence execution). The integration tests use the ADB stub. The Timer integration test (T007) uses the real clock, because the DI graph of the host does not let the test move a fake clock: it waits for the needed count of command log items, with a long timeout (60 seconds) for slow CI runners. | PASS |
| III. UX Consistency | No new API shape. The current error message for an unresolved parameter stays. | PASS |
| IV. Performance | Declared above: no measurable change (one reference for each booking). | PASS |
| V. Living Documentation | Update the self-reschedule paragraph in `docs/architecture.md` (the booked run keeps the scope of the run that booked it) and its "Last reviewed" line. Feature 078 FR-015 had this rule but the code did not do it: set `specs/078-sequence-parameters/spec.md` Status to "Implemented (iterated by 116)" and the same in `specs/STATUS.md`. Add row 116 to `specs/STATUS.md` with the status "Implemented", and set the Status line of `specs/116-reschedule-keeps-params/spec.md` to "Implemented". | PASS (tasks must include these edits) |
| VI. STE | All new text (plan, research, data model, contract, quickstart, code comments, docs) is in STE. | PASS |

No violations. Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: The design adds one optional parameter to one internal interface and one argument to one private method. It adds no public contract. All gates stay PASS.

## Project Structure

### Documentation (this feature)

```text
specs/116-reschedule-keeps-params/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── self-reschedule-coordinator.md   # Internal contract of ISelfRescheduleCoordinator.ScheduleSelf
└── tasks.md             # Phase 2 output (/speckit-tasks, not made by this command)
```

### Source Code (repository root)

```text
src/GameBot.Service/Services/
├── QueueExecution/
│   ├── ISelfRescheduleCoordinator.cs      # CHANGE: ScheduleSelf gets optional `ParameterScope? scope = null`
│   ├── SelfRescheduleCoordinator.cs       # CHANGE: put `scope` on each SelfRescheduleEntry (4 options)
│   ├── QueueRunHandle.cs                  # NO CHANGE: SelfRescheduleEntry.Scope already exists (update its doc comment only)
│   └── QueueExecutionService.cs           # NO CHANGE: drains already use `entry.Scope ?? queueScope`
└── SequenceExecution/
    └── SequenceExecutionService.cs        # CHANGE: DispatchActionAsync/DispatchSelfReschedule send the run `scope`

tests/
├── unit/Queues/
│   ├── SelfRescheduleCoordinatorTests.cs            # ADD: each option keeps the given scope; null scope stays null
│   ├── QueueRunHandleTimerFiringTests.cs            # ADD: a held Timer booking keeps its scope (RearmTimerFiring)
│   └── QueueParameterScopeTests.cs                  # ADD: booked run gets the entry scope (all options, chain of 3, EveryStep replace)
└── integration/Queues/
    └── SelfRescheduleRunIntegrationTests.cs         # ADD: real DI graph; booked run resolves a required entry parameter (OncePerRun and Timer)

docs/architecture.md                                 # CHANGE: self-reschedule paragraph + Last reviewed
specs/078-sequence-parameters/spec.md                # CHANGE: Status line
specs/STATUS.md                                      # CHANGE: rows 078 and 116
specs/116-reschedule-keeps-params/spec.md            # CHANGE: Status line to "Implemented"
```

**Structure Decision**: The fix stays in the backend service (`src/GameBot.Service`). The domain library, the web UI, and the API endpoints do not change.

## Design

### Root cause

1. `QueueExecutionService` builds the scope of a template entry run with `EntryScope(entry)` (queue layer + entry layer). It gives this scope to `RunOneSequenceAsync`, which calls `ISequenceExecutionService.ExecuteAsync(..., scope, ...)`.
2. `SequenceExecutionService.ExecuteCoreAsync` gives `scope` to `SequenceRunner.ExecuteAsync`, and the runner adds the sequence layer. The action dispatcher lambda calls `DispatchActionAsync(action, sequenceId, originatingQueueId, sessionId, token)`. The lambda does not send `scope`.
3. `DispatchSelfReschedule` calls `ScheduleSelf(queueId, sequenceId, option, timeOfDay, relativeOffset)`. `SelfRescheduleCoordinator` makes `new SelfRescheduleEntry(entryId, sequenceId, option, fireAt)` with `Scope = null`.
4. The run loop fires the booking with `firing.Scope ?? queueScope` (EveryStep line ~404, AtQueueStart ~550, Timer ~650, OncePerRun ~716). Because `Scope` is null, the booked run gets `queueScope` only.

### Change

1. `ISelfRescheduleCoordinator.ScheduleSelf`: add the last parameter `GameBot.Domain.Parameters.ParameterScope? scope = null`. Document it: "The parameter scope of the run that makes the booking. The booked run uses this scope. Null means the queue scope."
2. `SelfRescheduleCoordinator.ScheduleSelf`: give `Scope: scope` to each of the four `new SelfRescheduleEntry(...)` calls, one for each booking option (OncePerRun, EveryStep, AtQueueStart, Timer). The AtQueueStart entry goes into `PendingNextCycleStart` on a cycling queue and into `PendingOncePerRun` on a queue with no cycling. No other logic change. The EveryStep register and the Timer register already replace the booking for each sequence, so the scope of the most recent booking stays (FR-010).
3. `SequenceExecutionService`: add a `ParameterScope scope` parameter to `DispatchActionAsync` and `DispatchSelfReschedule`. The runner lambda sends the `scope` argument of `ExecuteCoreAsync` (the scope that the service gave the run, before the runner adds the sequence layer). `DispatchSelfReschedule` gives it to `ScheduleSelf`.
4. `QueueRunHandle.cs`: change the doc comment of `SelfRescheduleEntry.Scope` so that it names the source (the scope that the booking run got from the queue run loop).

Why the scope before the sequence layer: the runner adds the sequence layer again when the booked run starts. If the booking kept the scope after that layer, the booked run would have two sequence layers. See research R-002.

A chain keeps the values (FR-004): the booked run gets the kept scope as its `scope` argument, so its own `reschedule-self` step keeps the same scope again.

Before-each-run and every-step passes (spec edge case): `QueueExecutionService` runs a template entry of these passes with `EntryScope(entry)` of that entry, and a self-reschedule EveryStep booking with `injection.Scope ?? queueScope`. A `reschedule-self` step in such a run thus keeps that scope. The fix needs no special code for these passes.

### Test approach

Write the tests first. They must fail before the fix (Constitution II).

1. **Coordinator unit tests** (`tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`): for each of the five variants (OncePerRun, EveryStep, AtQueueStart, Timer relative offset, Timer time of day), `ScheduleSelf(..., scope)` makes an entry with `Scope` equal to the same `ParameterScope` object. Test AtQueueStart on a cycling queue and on a queue with no cycling, because each goes into a different register. A call with no scope makes an entry with `Scope == null` (FR-007). An EveryStep booking and a Timer booking of the same sequence keep the scope of the second call.
   **Held booking** (`tests/unit/Queues/QueueRunHandleTimerFiringTests.cs`): add a Timer entry with scope S, drain it, put it back with `RearmTimerFiring`, and drain it again. The drained entry has `Scope` that is the same object as S (spec edge case "liveness gate", research R-004).
2. **Queue run unit tests** (`tests/unit/Queues/QueueParameterScopeTests.cs`, partial class `QueueExecutionServiceTests`): a template entry with `parameterValues` `slot = one`. The `FakeSequenceExecution.Handler` reads the scope of its firing from `h.Sequences.Scopes` and calls `h.Coordinator.ScheduleSelf(..., scope)` the same way that the real service does. Assert that the booked firing resolves `slot` to `one` from the entry layer, for each of the five variants (Theory). Add a Timer chain of at least 3 booked runs with the fake clock (SC-002). Add a test that a booking with no scope still gets the queue scope (FR-007, User Story 3).
3. **Dispatch path**: no unit test makes a real `SequenceExecutionService` now (its constructor has many dependencies). The unit tests of steps 1 and 2 cover all four options (five variants) at the coordinator level and at the run loop level. They use a fake handler that calls `ScheduleSelf`, so they do not test the real dispatch change of `SequenceExecutionService`. Test 4 covers the real dispatch path through the real DI graph, for OncePerRun and for Timer with a relative offset. The Timer variant also covers a chain of two booked runs on the real path.
4. **Integration test** (`tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`): real DI graph, ADB stub.
   - **Field that uses the parameter**: a step `parameterBindings` value `{{slot}}` (feature 115). Do not use an `imageVisible` condition with `imageId` = `{{slot}}`. The code trace shows why: after the runner resolves the image id, `SequenceExecutionService.EvaluateImageConditionAsync` throws `image_unavailable` when the image is not in `IImageRepository`. The If step then fails with "If '<step>' condition evaluation failed: image_unavailable" in each run, before and after the fix. Thus the sequence result does not show the defect.
   - **Seed**: a command `c116` with the required parameter `slot` and one `PrimitiveTap` step whose `FieldTemplates` sets `primitiveTap.detectionTarget.referenceImageId` to `{{slot}}` (the form of `NovaParamTap` in `BindingPlaceholderScopeIntegrationTests`). A sequence with the required parameter `slot` (no default). Its step 0 is a `reschedule-self` step. Its step 1 (`StepId` = `s1`) is a Command step to `c116` with the binding `slot` = `{{slot}}`. A queue with no cycling and one OncePerRun template entry that gives `slot` = `pns-nova-option-affinity`.
   - **OncePerRun variant**: step 0 books OncePerRun. Run the queue to completion. Assert that the queue summary has "2 sequence(s) executed". Assert that the command log of `c116` has 2 `parameters` items, and that each item resolves `slot` to `pns-nova-option-affinity` with the origin layer `entry`. Assert that no sequence log entry has "could not be resolved from any scope". Do not assert the failed count or the final status: the tap result depends on the host (the same rule as the feature 115 tests).
   - **Timer variant**: step 0 books Timer with `timerRelativeOffset` `00:00:01`. A pending Timer booking keeps a queue with no cycling alive, and each run books a new run. Thus start the queue, wait (60 seconds at most) until the command log of `c116` has 3 `parameters` items, and then stop the queue with `IQueueExecutionService.StopAsync`. Assert that each of the 3 items resolves `slot` to `pns-nova-option-affinity` with the origin layer `entry`, and that no sequence log entry has "could not be resolved from any scope".
   - **Before the fix**: the booked run fails at step 1 with "Step 's1': parameter 'slot' used by field 'parameterBindings.slot' could not be resolved from any scope." The command does not run, so the command log has only 1 `parameters` item. Make sure that both variants fail on the code before the fix.
5. Run the current tests in `tests/unit/Queues`, `tests/unit/Sequences`, and `tests/integration/Queues` with no change to their expected results (SC-004).

Build and test gate: `dotnet build -c Debug` and `dotnet test` for the unit and integration projects. The web UI does not change, so its gate does not apply.

## Complexity Tracking

No violations.
