# Research: Queue Sequence Scheduling

**Feature**: 053-schedulable-sequences  
**Date**: 2026-06-03

## Decision 1: Where schedule type lives

**Decision**: `ScheduleType` is a property of `QueueTemplateEntry` (the persisted domain model), not of the runtime queue entry (`QueueEntry`). The runtime queue entry is non-persistent and carries only `EntryId` + `SequenceId`; schedule semantics belong in the template that defines the work.

**Rationale**: Queue entries are ephemeral, in-memory, and not linked to templates after a load. Template entries are the source of truth for ordering and scheduling. Keeping schedule type in the template entry means it persists across restarts and is shared among all queues that load the template, consistent with the feature's design intent.

**Alternatives considered**:
- Attaching schedule type to `QueueEntry` (runtime): rejected because runtime entries are not persisted and are overwritten on template load, losing any schedule configuration.

---

## Decision 2: Timer "already fired" tracking

**Decision**: Use a `Dictionary<int, DateOnly>` keyed by entry index within the run-snapshot, tracking the calendar date (`DateOnly`) on which each timer entry last fired. This dictionary is per-run (instantiated in `RunAsync`, never persisted).

**Rationale**:  
- Enables "once per calendar day" semantics during a multi-day run without restarting.  
- Resets on queue restart (per clarification: per-run only state).  
- Cheaply evaluated at each iteration boundary using `DateTime.Now`.

**Alternatives considered**:
- Simple boolean `HashSet<int>` (fired in this run): rejected because it would prevent the timer from firing again on the next calendar day during a long-running cyclic queue.

---

## Decision 3: API contract shape for `SaveQueueTemplateRequest`

**Decision**: Replace `string[]? SequenceIds` with `TemplateEntrySaveRequest[]? Entries`, where each element carries `SequenceId`, `ScheduleType` (string enum, default `"OncePerRun"`), and `TimerTimeOfDay` (string `"HH:mm"` or null). The old `SequenceIds` field is removed.

**Rationale**: The previous shape had no room for per-entry metadata. Structured entries are needed for schedule type + timer time. The change is breaking, but this is a single-operator desktop tool with no external consumers and the break is confined to one endpoint.

**Alternatives considered**:
- Keeping `SequenceIds` as a parallel deprecated field: adds complexity with no consumer benefit in a single-operator context.
- Using a flat parallel arrays (`sequenceIds[]` + `scheduleTypes[]`): fragile, error-prone when the arrays have different lengths.

---

## Decision 4: Time representation for TimerTimeOfDay

**Decision**: Store and transfer timer time as a string in `HH:mm` (24-hour) format. In the domain model use `TimeOnly?`; in the API contract use `string?` (parsed and validated at the endpoint boundary).

**Rationale**:  
- `TimeOnly` serializes to `HH:mm:ss` by default in `System.Text.Json`, so storing it in the domain model and serializing to file is clean and automatically handles the full round-trip.  
- The API request/response uses `string` to allow a clean `HH:mm` format without requiring clients to know the seconds component.  
- Server-local time is used (per clarification); no timezone offset in the value.

**Alternatives considered**:
- Storing as `int` minutes-from-midnight: simpler but less readable in the JSON persistence files (which are debug-friendly and `WriteIndented`).
- Using `DateTimeOffset` with a specific date: overly complex for a time-of-day value.

---

## Decision 5: Execution order in `QueueExecutionService.RunAsync`

**Decision**: Pre-partition the template entry snapshot into three ordered lists at run start:
- `oncPerRunSnapshot`: entries with `ScheduleType.OncePerRun`
- `everyStepSnapshot`: entries with `ScheduleType.EveryStep`
- `timerSnapshot`: entries with `ScheduleType.Timer` (carrying index for timer-state dictionary)

The execution loop per iteration:
1. Evaluate all `timerSnapshot` entries against today's time; fire any that are due and haven't yet fired today.
2. For each entry in `oncPerRunSnapshot`: execute it, then immediately execute all `everyStepSnapshot` entries in order.
3. If `oncPerRunSnapshot` is empty but `everyStepSnapshot` is non-empty: execute every-step entries once (edge case — FR-009).
4. Increment `cycles`; loop if `queue.CycleExecution`.

**Rationale**: Pre-partitioning avoids per-step type checks in the inner loop and makes the scheduling semantics explicit and testable independently per partition.

**Alternatives considered**:
- Inline type check inside a single `foreach` loop: harder to reason about and test.

---

## Decision 6: `ScheduleType` C# representation

**Decision**: `ScheduleType` is a C# `enum` with `[JsonConverter(typeof(JsonStringEnumConverter))]` applied so it serializes as `"OncePerRun"` / `"EveryStep"` / `"Timer"` in JSON (both in the file-based persistence and API responses). Default value is `ScheduleType.OncePerRun` (= 0) for backward compatibility: existing entries with no `ScheduleType` field in JSON deserialize to the default.

**Rationale**: String-serialized enums are readable in the project's debug-friendly `WriteIndented` JSON files. The default-to-`OncePerRun` at the C# default-value level guarantees backward compatibility with persisted templates that predate this feature (no migration needed).

**Alternatives considered**:
- Integer-backed enum: less readable in persisted JSON files.
- Plain `string` property: loses compile-time exhaustiveness checking.
