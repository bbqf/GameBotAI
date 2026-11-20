# Data Model: A booked run uses the current parameter values of its template entry

**Feature**: 196-fix-reschedule-params | **Date**: 2026-11-20

The fix changes no stored data, no API contract, and no web UI. All changes are in memory types of the queue run.

## ParameterScope (changed, `src/GameBot.Domain/Parameters/ParameterScope.cs`)

| Member | Kind | Change |
|---|---|---|
| `ChildForEntry(string entryKey, IEnumerable<ParameterBinding>? values)` | new method | Returns a new `entry` layer (`LayerName` = `ParameterScopeLayers.Entry`) with the values of `values` (a binding with a null value is skipped, as in `Child`), no declarations, and `EntryKey` = `entryKey`. The receiver does not change. Throws `ArgumentException` when `entryKey` is null or empty. |
| `EntryKey` | new property, `string?` | The entry key of the nearest layer, from this node outward, that has one. Null when no layer has a key (for example the queue scope, or a scope of a live schedule). |

Rules:

- A layer from `Child`, `TryBindChild`, `WithIteration`, `FromQueue`, or `Empty` has no key of its own. A child of a keyed layer gives the key of its parent through the walk.
- An `entry` layer with no values adds no name to `Describe()`, `ToSubstitutionContext()`, `TryResolve()`, or `TryResolveEntered()`.

## QueueRunSchedule (changed, `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`)

| Member | Kind | Change |
|---|---|---|
| `_renamedKeys` | new field, `Dictionary<string, string>` (ordinal) | Old key to new key. `SwapPlan(renameMap: ...)` adds each pair under the lock. |
| `TryGetEntry(string key, out QueueTemplateEntry entry, out string currentKey)` | new method | Under the lock: when the current plan has `key` (`TryGetFromAllByKey`, so also a disabled entry), returns it with `currentKey` = `key`. If not, follows `_renamedKeys` from `key` (at most as many steps as the map has pairs, so a loop cannot occur) and looks up each new key; `currentKey` is the key that found the entry. Returns false when no key of the walk is in the plan. |

## SelfRescheduleEntry (no change of shape, `QueueRunHandle.cs`)

`Scope` keeps its meaning: the scope of the run that made the booking. The doc comment changes: the kept scope is the fallback; when `Scope.EntryKey` finds an entry in the current plan, the booked run uses the current values of that entry (feature 196).

## Run loop scope functions (changed, `QueueExecutionService.RunAsync`)

| Function | Change |
|---|---|
| `EntryScope(QueueTemplateEntry entry)` | Returns `queueScope.ChildForEntry(plan.KeyFor(entry), entry.ParameterValues)` for each entry of the current plan. When `KeyFor` gives null, the behavior of today (no key). |
| `BookingScope(SelfRescheduleEntry firing)` | New. `firing.Scope?.EntryKey` is a key and `schedule.TryGetEntry(key, out var entry, out var currentKey)` → `queueScope.ChildForEntry(currentKey, entry.ParameterValues)`. Else `firing.Scope ?? queueScope`. |

## QueueRunPlan (changed, `src/GameBot.Service/Services/QueueExecution/QueueRunPlan.cs`)

| Member | Kind | Change |
|---|---|---|
| `KeyFor(QueueTemplateEntry entry)` | new method, `string?` | The key of the entry object (by reference) in `AllEntries`, or null when the object is not in the plan. |

## State flow

```text
template firing of entry E (key k, values v1)
  scope S1 = queue + entry(k, v1)
  reschedule-self step -> booking B { Scope = S1 }
apply-template while paused: plan v2 has E (key k, values v2)
B fires:
  BookingScope(B): S1.EntryKey = k -> schedule.TryGetEntry(k) = E(v2)
  scope S2 = queue + entry(k, v2)   -> the booked run resolves v2
  reschedule-self step in the booked run -> booking B2 { Scope = S2 } (key k again)
E removed by apply-template:
  BookingScope(B): TryGetEntry(k) = false -> S1 (the kept values, behavior of today)
```
