# Implementation Plan: A booked run uses the current parameter values of its template entry

**Branch**: `196-fix-reschedule-params` | **Date**: 2026-11-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/196-fix-reschedule-params/spec.md` (GitHub issue #409)

## Summary

A `reschedule-self` booking keeps the parameter scope of the run that made it (feature 116). The scope holds the values of the template entry at the time of the booking. After `apply-template` changes the values of the entry, the booking and each booking of its chain still use the old values. The fix puts the key of the template entry on the `entry` layer of the scope (`ParameterScope.ChildForEntry`, `ParameterScope.EntryKey`). When a booking fires, the run loop finds the entry by this key in the current run plan (`QueueRunSchedule.TryGetEntry`, which also follows key renames of a template upgrade) and builds the scope from the current values. When the entry is no longer in the plan, or the booking has no key, the booking uses its kept scope as today. The fire times, the registers, and the `keep` rule do not change.

## Technical Context

**Language/Version**: C# (`LangVersion` preview) / .NET 9 (domain and service); no web UI change
**Primary Dependencies**: ASP.NET Core minimal APIs (no change), `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) in tests
**Storage**: N/A (bookings and plans are in memory only; no stored data changes)
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/integration`)
**Target Platform**: Windows service host with Android emulators
**Project Type**: web service (backend) with a web UI (not changed)
**Performance Goals**: No measurable change. One dictionary lookup and one scope allocation for each booking firing (research R-009).
**Constraints**: No change of the signatures of `ISequenceExecutionService`, the action dispatcher, or `ISelfRescheduleCoordinator`. No change of fire times, registers, `keep` rule, stop/start, parameter types, or number validation (FR-005, FR-008). The `resolvedParameters` log items stay the same for the same values (FR-010).
**Scale/Scope**: A queue has 1 to 30 entries and 0 to 10 pending bookings.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Phone support level: not applicable.

| Gate | Status | Note |
|---|---|---|
| I. Code quality | PASS | Small changes in `ParameterScope`, `QueueRunPlan`, `QueueRunSchedule`, and `QueueExecutionService`. No new dependency. Doc comments on the new public and internal members. |
| II. Testing | PASS | Bug fix: the test `TimerBookingUsesTheNewEntryValueAfterAnApply` fails before the fix. Unit tests for each booking register, the chain, the type change, the removed entry, the renamed key, and other entries. The existing feature 116 tests stay green. |
| III. UX consistency | PASS | No API, log text, or UI change. |
| IV. Performance | PASS | Declared in Technical Context (research R-009). The perf note goes into the PR. |
| V. Living documentation | PASS | Update `docs/architecture.md` (self-reschedule paragraph and "Last reviewed"), `CHANGELOG.md`. Spec 116 `Status` becomes "Implemented (iterated by 196)", because its FR-008 changes. `specs/STATUS.md` gets row 196 and the changed row 116. |
| VI. STE | PASS | All artifacts are in STE. Code comments are in STE. |
| Version bump | PASS (phase 2) | Bump `patch` in `installer/versioning/version.override.json` before the push (pipeline step 10). |

Post-design re-check: PASS. The design adds no project, no pattern, and no stored data.

## Project Structure

### Documentation (this feature)

```text
specs/196-fix-reschedule-params/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── booked-run-scope.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Parameters/ParameterScope.cs                        # + ChildForEntry, + EntryKey
├── GameBot.Service/Services/QueueExecution/QueueRunPlan.cs            # + KeyFor
├── GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs        # + rename map, + TryGetEntry
├── GameBot.Service/Services/QueueExecution/QueueExecutionService.cs   # EntryScope with key, + BookingScope at 4 sites, RunNext scope with key
├── GameBot.Service/Services/QueueExecution/QueueRunHandle.cs          # doc comment of SelfRescheduleEntry.Scope
├── GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs   # doc comment (class summary)
└── GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs  # doc comment (scope parameter)

tests/
├── unit/Parameters/ParameterScopeEntryKeyTests.cs                     # new: ChildForEntry, EntryKey
├── unit/Queues/QueueRunScheduleSwapTests.cs                           # + TryGetEntry tests
├── unit/Queues/QueueRunPlanTests.cs                                   # + KeyFor tests
├── unit/Queues/QueueParameterScopeTests.cs                            # + feature 196 tests (harness of feature 116)
├── unit/Queues/QueueLivenessGateTests.cs                              # + held at-queue-start entry test (FR-009)
├── unit/Queues/QueueExecutionServiceRunNextTests.cs                   # + booking of a run-next run (FR-011)
└── integration/Queues/SelfRescheduleRunIntegrationTests.cs            # + apply on the real dispatch path

docs/architecture.md, CHANGELOG.md, specs/STATUS.md, specs/116-reschedule-keeps-params/spec.md
```

**Structure Decision**: The current backend layout. The change stays in the parameter scope and the queue run loop (research R-001, R-002). No new project.

## Design Details

### D-1: `ParameterScope.ChildForEntry` and `EntryKey`

- Add a private field `_entryKey` (`string?`) and a constructor parameter for it. The existing constructor calls pass null.
- `public ParameterScope ChildForEntry(string entryKey, IEnumerable<ParameterBinding>? values)`: `ArgumentException.ThrowIfNullOrEmpty(entryKey)`. Copy the values as `Child` does (skip a binding with a null name or value, layer `entry`). No declarations.
- `public string? EntryKey`: walk from this node outward; return the first `_entryKey` that is not null; else null.
- No change to `TryResolve`, `Describe`, or the other members.

### D-2: `QueueRunPlan.KeyFor`

`public string? KeyFor(QueueTemplateEntry entry)`: the index of the object (by reference) in `AllEntries`, then `AllKeys[index]`; null when not found.

### D-3: `QueueRunSchedule.TryGetEntry`

- New field `private readonly Dictionary<string, string> _renamedKeys = new(StringComparer.Ordinal);`.
- In `SwapPlan`, under the lock, for each pair of `renameMap` where the keys differ: `_renamedKeys[pair.Key] = pair.Value`.
- `public bool TryGetEntry(string key, out QueueTemplateEntry entry, out string currentKey)`: under the lock, `current = key`; repeat at most `_renamedKeys.Count + 1` times: if `_plan.TryGetFromAllByKey(current, out entry)`, set `currentKey = current` and return true; if `_renamedKeys.TryGetValue(current, out var next)` is false, stop; else `current = next`. Return false with `entry = null!` and `currentKey = key`.

### D-4: Run loop (`QueueExecutionService.RunAsync`)

```text
ParameterScope EntryScope(QueueTemplateEntry entry):
  key = plan.KeyFor(entry)
  if key is null: return values > 0 ? queueScope.Child(entry, values) : queueScope   # behavior of today
  return queueScope.ChildForEntry(key, entry.ParameterValues)

ParameterScope BookingScope(SelfRescheduleEntry firing):
  if firing.Scope?.EntryKey is { } key
     && handle.Schedule is { } s
     && s.TryGetEntry(key, out var current, out var currentKey):
    return queueScope.ChildForEntry(currentKey, current.ParameterValues)
  return firing.Scope ?? queueScope
```

- `EntryScope` reads the loop variable `plan`. `KeyFor` searches `plan.AllEntries` by reference. For a daily retry, `schedule.Entries[retryIndex]` is an object of the current plan, so `KeyFor` finds it. When `KeyFor` gives null (an object that is not in the current plan), `EntryScope` keeps the behavior of today (no key).
- `BookingScope` uses the key that `TryGetEntry` found (`currentKey`), which is the new key after a rename. Thus the bookings of the booked run get the new key.
- `EntryScope` reads the loop variable `plan`, and `BookingScope` reads `handle.Schedule` (the current plan). The two are the same object outside a gate wait: a swap occurs only while the loop waits at the operator gate, and a swap during the gate wait of `FireGroupAsync` makes it return null, so the site puts the firing back and the next iteration reads the new plan (research R-003).
- The `EveryStep` injections run in the every-step pass, which has no gate. An injection that runs before a pause takes effect uses the plan of that time; the next injection after the apply uses the new values.
- Use `BookingScope(x)` in place of `x.Scope ?? queueScope` at the four sites: the `EveryStep` injections, the next-cycle-start drain, the Timer drain, and the once-per-run drain.
- `MoveHeldAtStartEntriesAsync` already uses `EntryScope(entry)`, so the held entry has the key (FR-009).

### D-5: `RunNext`

Build the scope with `queueScope.ChildForEntry(plan.Keys[index], entry.ParameterValues)`. The values stay fixed at request time (feature 184, FR-005), but the bookings of the run-next run get the key.

### D-6: Doc comments

Update the doc comments (STE) of `SelfRescheduleEntry.Scope`, `SelfRescheduleCoordinator` (class summary), and the `scope` parameter of `ISelfRescheduleCoordinator.ScheduleSelf`: the kept scope is the fallback; the booked run uses the current values of the entry when the entry is in the run plan (feature 196).

### D-7: Documentation

- `docs/architecture.md`: change the self-reschedule paragraph (line "A booked run keeps the parameter scope of the run that booked it ...") to the new rule, and refresh "Last reviewed".
- `CHANGELOG.md`: an entry under `[Unreleased]` (Fixed) with the issue #409.
- `specs/116-reschedule-keeps-params/spec.md`: `**Status**: Implemented (iterated by 196)`. `specs/STATUS.md`: row 116 changes the same way, and a new row 196.

## Test Plan

| Test | Location | Covers |
|---|---|---|
| A Timer booking of entry E made with `slot` = `one`; pause; swap the plan to E with `slot` = `two`; resume; the booked run resolves `two` (fails before the fix) | `QueueParameterScopeTests` | US1-1, FR-002, SC-001 |
| The same for each register: `OncePerRun`, `EveryStep`, `AtQueueStart` (cycling) | `QueueParameterScopeTests` | FR-002, edge case "other booking kinds" |
| No swap: the booked run resolves `one` (all feature 116 tests stay green) | `QueueParameterScopeTests` | US1-2 |
| The fire time of a `keep: earliest` booking does not change after a swap | `QueueParameterScopeTests` | US1-3, FR-005 |
| Chain: booked run R1 books R2; swap between; R2 resolves `two`; the step-0 retry variant also | `QueueParameterScopeTests` | US2, FR-003 |
| Type change: kept value `pns-troop-tier-max`, new value `0`; the booked run resolves `0` | `QueueParameterScopeTests` | US3, SC-001 |
| Two entries E1 and E2 with bookings; swap changes E1 only; the count and fire times of the bookings stay; E2 resolves its own value | `QueueParameterScopeTests` | US4, FR-006, SC-002 |
| Two entries on the same sequence with different values: each booking resolves its own entry | `QueueParameterScopeTests` | Edge case, clarification 5 |
| The entry is removed from the plan: the booked run resolves the kept value | `QueueParameterScopeTests` | FR-004 |
| The entry is disabled in the new plan: the booked run resolves the current values | `QueueParameterScopeTests` | Edge case "disabled" |
| The entry had no values and gets values: the booked run resolves the new values | `QueueParameterScopeTests` | Edge case "no values" |
| A booking with no scope: the queue scope (existing `BookingWithNoScopeStillGetsTheQueueScope`) | `QueueParameterScopeTests` | FR-004 |
| A stored template edit with no apply: the booked run keeps `one` (existing `TemplateEditAfterTheBookingDoesNotChangeTheBookedRun`) | `QueueParameterScopeTests` | Clarification 1 |
| A booking that a run-next run made resolves the current values; the run-next run keeps the values of the request | `QueueExecutionServiceRunNextTests` | FR-011 |
| A held at-queue-start entry resolves the current values when it fires | `QueueLivenessGateTests` (harness of feature 106) | FR-009 |
| A booked run resolves the value that the apply changed, on the real dispatch path | `SelfRescheduleRunIntegrationTests` | US3, SC-001 |
| `ChildForEntry`: values, null values skipped, key; `EntryKey` walks the chain; an empty `entry` layer adds no name to `Describe` | `ParameterScopeEntryKeyTests` | D-1, R-006 |
| `TryGetEntry`: current key, disabled entry, removed key, renamed key, two renames | `QueueRunScheduleSwapTests` | D-3, R-004 |
| `KeyFor`: an object in the plan, a disabled object, an object not in the plan | `QueueRunPlanTests` | D-2 |
| The integration tests of feature 116 (`SelfRescheduleRunIntegrationTests`) stay green | `tests/integration` | SC-004 |
| Manual check of issue #409 | quickstart.md section 2 | SC-001, SC-003 |

The plan swap in the unit tests uses the same steps as `QueueLiveApplyService.ApplyAsync`: `handle.RequestOperatorPause`, wait for the state "paused", `schedule.SwapPlan(QueueRunPlan.FromAll(entries, keys, version + 1))`, `handle.ReleaseOperatorPause`.

## Performance Note (for the PR)

The PR description MUST contain this note (Constitution IV). Each booking firing does one key lookup in the run plan (and at most a few rename lookups) and allocates one `ParameterScope` node. Each template entry firing with no values now allocates one empty `entry` node. A firing runs a sequence of seconds to minutes, so the cost is not measurable.

## Complexity Tracking

No constitution violations. No entries.
