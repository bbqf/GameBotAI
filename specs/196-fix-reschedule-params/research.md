# Research: A booked run uses the current parameter values of its template entry

**Feature**: 196-fix-reschedule-params | **Date**: 2026-11-20

The Technical Context of the plan has no open items. This file records the design decisions and the code facts that they come from.

## Code facts

- `QueueExecutionService.RunAsync` builds the scope of each template entry firing with the local function `EntryScope(entry)`: the queue scope plus an `entry` layer with `entry.ParameterValues` (only when the entry has values).
- `SequenceExecutionService` sends this scope (before the runner adds the sequence layer) to the action dispatcher. `DispatchSelfReschedule` gives it to `ISelfRescheduleCoordinator.ScheduleSelf` (feature 116).
- `SelfRescheduleCoordinator` stores the scope in `SelfRescheduleEntry.Scope`. The run loop fires the booking with `firing.Scope ?? queueScope` at four sites: the `EveryStep` injections, the next-cycle-start register, the Timer register, and the once-per-run register.
- `ParameterScope` is immutable. A scope that the loop built from the old plan holds the old `ParameterBinding` values. `apply-template` swaps the plan (`QueueRunSchedule.SwapPlan`), but the stored scopes do not change. This is the defect of issue #409.
- `QueueRunPlan` gives each entry a stable key (the entry ID, or `idx:<position>` for a template that needs an upgrade). `TryGetFromAllByKey` also finds disabled entries.
- The upgrade of a running queue (`QueueTemplateUpgradeRunner`) renames `idx:<n>` keys to entry IDs with `QueueRunPlan.WithRenamedKeys` and `QueueRunSchedule.SwapPlan(renameMap: ...)`.
- The plan swaps only while the loop waits at the operator gate. `FireGroupAsync` passes the gate first. When the plan changed during the wait, the call returns null and the site puts the booking back. The next iteration reads the new plan.

## R-001: How does a booking know its template entry?

- **Decision**: The `entry` layer of the scope carries the key of its template entry. A new method `ParameterScope.ChildForEntry(string entryKey, IEnumerable<ParameterBinding>? values)` makes an `entry` layer with the key in the new property `EntryKey`. `EntryKey` of a scope gives the key of the nearest layer in the chain that has one, or null. The loop makes the `entry` layer for each entry firing, also when the entry has no values, so that each entry firing has a key.
- **Rationale**: The scope already goes from the loop through the sequence runner and the dispatcher to the coordinator, and from the booking back to the next run. A key on the scope needs no change of the signatures of `ISequenceExecutionService`, the dispatcher, or `ISelfRescheduleCoordinator`. The chain of bookings (FR-003) works with no extra code: the booked run gets a scope with the same key, so its bookings get the key too.
- **Alternatives considered**:
  - A new `entryKey` argument from `RunOneSequenceAsync` through `ISequenceExecutionService.ExecuteAsync`, the dispatcher, and `ScheduleSelf`: more signatures change, and each test double of these interfaces changes. Rejected.
  - A "current entry key" field on `QueueRunHandle` that the loop sets before each firing: it depends on the order of the calls and is not safe if a future change runs firings at the same time. Rejected.
  - A booking finds its entry by the sequence ID: not correct when two entries use the same sequence, and not correct for a booking of a different sequence (feature 116 tests book sequence `R` from entry `A`). Rejected (spec clarification 5).

## R-002: Where does the booked run get the current values?

- **Decision**: The run loop gets a new local function `BookingScope(SelfRescheduleEntry firing)`. When `firing.Scope?.EntryKey` is a key, and `schedule.TryGetEntry(key, out var entry, out var currentKey)` finds the entry in the current plan (also a disabled entry), the function returns `queueScope.ChildForEntry(currentKey, entry.ParameterValues)`, which is the same scope that `EntryScope(entry)` gives. In all other cases, it returns `firing.Scope ?? queueScope` (the behavior of today, FR-004). The four sites that fire a booking use `BookingScope(firing)` in place of `firing.Scope ?? queueScope`.
- **Rationale**: The booking itself does not change (FR-005): the fire time, the register, the `keep` rule, and the kept scope stay. Only the scope of the run changes, at the time of the firing (FR-002). `EntryScope(entry)` is the same function that the template firings use, so a booked run and a template firing of one entry get the same values.
- **Alternatives considered**:
  - `apply-template` rewrites the scopes of the pending bookings (the alternative solution of the issue): it needs a walk over four registers under their locks, and a booking that the run makes after the apply is still correct only by chance. Rejected; the issue prefers resolution at run time.

## R-003: The plan changes while a booking waits at the gate

- **Decision**: No new code. `BookingScope` reads the local `plan` of the loop iteration. If a plan swap happens while `FireGroupAsync` waits at the operator gate, `FireGroupAsync` returns null and the site puts the booking back (existing behavior, feature 171). The next iteration reads the new plan and builds the scope again.
- **Rationale**: The existing replan rule makes sure that a firing never runs with a scope from an old plan.

## R-004: Key rename by a template upgrade

- **Decision**: `QueueRunSchedule` records each rename of `SwapPlan(renameMap: ...)` in a map from the old key to the new key. The new method `QueueRunSchedule.TryGetEntry(string key, out QueueTemplateEntry entry)` looks up the key in the current plan (`TryGetFromAllByKey`). When the key is not there, it follows the rename map (also more than one step) and looks up the new key.
- **Rationale**: Without this, a booking that the run made before an upgrade (`idx:3`) does not find its entry after the upgrade (`e-1234`). It then uses its kept values for each next generation of the chain, and a later `apply-template` has no effect on it. That is the defect of #409 again.
- **Alternatives considered**: Accept the fallback after an upgrade: simple, but it keeps the defect for queues that the operator upgrades while they run. Rejected.

## R-005: A removed entry

- **Decision**: When `TryGetEntry` does not find the key (the operator removed the entry with `apply-template`), `BookingScope` returns the kept scope (FR-004, spec clarification 2). The booking fires as today.
- **Rationale**: A change of when bookings fire is out of scope (issue non-goal 3).

## R-006: The scope of an entry with no values

- **Decision**: `EntryScope(entry)` always makes an `entry` layer with `ChildForEntry(key, entry.ParameterValues)`. Before the fix, an entry with no values got the queue scope itself.
- **Rationale**: Each entry firing needs the key (spec edge case: "the entry had no parameter values at the time of the booking and gets values later"). An `entry` layer with no values and no declarations adds no name to `Describe()`, `ToSubstitutionContext()`, or `TryResolve()`, so the resolved values and the execution log do not change. No code reads `LayerName` of the outer node except `FindDeclaringLayer`, which only runs for a declared name.

## R-007: Other sources of a run scope

- **Decision**:
  - A run-next request (feature 184): `RunNext` builds its scope with `ChildForEntry(entryId, entry.ParameterValues)`. The values stay fixed at the time of the request (feature 184, FR-005). The key makes the bookings of a run-next run follow FR-002 when they fire.
  - An at-queue-start entry that a liveness hold moved to the next-cycle register (`MoveHeldAtStartEntriesAsync`): its `SelfRescheduleEntry` gets `EntryScope(entry)`, which has the key. The next-cycle-start site uses `BookingScope`, so it gets the current values (FR-009).
  - Live schedules and other firings with the queue scope: no key, so a booking from them uses its kept scope (FR-004).
- **Rationale**: Each path that runs a template entry gives a scope with the key of the entry. Each path that does not run an entry gives no key.

## R-008: Tests

- **Decision**: Unit tests in `tests/unit/Queues/QueueParameterScopeTests.cs` (the feature 116 harness: a fake sequence runner records the scope of each firing, and the test calls `h.Coordinator.ScheduleSelf` with the current scope). A unit test changes the plan of the run with `schedule.SwapPlan` while the queue is paused through the operator gate, in the same way as `QueueLiveApplyService.ApplyAsync`. One integration test uses the real HTTP path (`pause`, `template-diff`, `apply-template`, `resume`). Unit tests for `ParameterScope.ChildForEntry`/`EntryKey` in `tests/unit/Parameters`, and for `QueueRunSchedule.TryGetEntry` in `tests/unit/Queues/QueueRunScheduleSwapTests.cs`.
- **Rationale**: The harness already covers each booking variant with a fake clock. A bug fix needs a test that fails before the fix (Constitution II): the test "a Timer booking resolves the new value after a plan swap" fails with the current code.

## R-009: Performance

- **Decision**: No performance budget changes. `BookingScope` does one dictionary lookup (plus at most a few rename lookups) and one `ParameterScope` allocation for each booking firing. An entry firing with no values now allocates one empty `entry` layer. A firing runs a sequence of seconds to minutes, so the cost is not measurable.
