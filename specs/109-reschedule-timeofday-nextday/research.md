# Research: A reschedule-self time of day that has passed books the next day

## R-001: Root cause

**Decision**: The defect is in `SelfRescheduleCoordinator.ResolveTimerFireAt` (`src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`).

**Finding**: For a time of day, the method makes the instant "today at that time" with the offset of the current moment. When this instant is before the current moment, the method returns the current moment:

```csharp
var candidate = new DateTimeOffset(
  now.Year, now.Month, now.Day, tod.Hour, tod.Minute, tod.Second, now.Offset);
return candidate < now ? now : candidate;
```

The XML comment calls this "collapsing to now" and names feature 065 FR-005/FR-006. The run loop drains a Timer firing when the current instant is equal to or after `FireAt` (`QueueRunHandle.DrainDueTimerFirings`). Thus a booking for the current moment runs on the next loop iteration. When the sequence books itself again with the same step, the loop repeats. This agrees with the probe: new `SelfReschedule` entries at 14:55:41, 14:56:11 and 14:59:01.

**Alternatives considered**: A fault in the monitor. Rejected: `QueueMonitorService` shows `SelfRescheduleEntry.FireAt` without a change (`ScheduleKind.SelfReschedule`), and the sequence really ran again, so the booked instant itself is wrong.

## R-002: The reference rule of the template `Timer` entry

**Decision**: Use the same next-occurrence rule as the template entry: a time of day that is ahead today gives today; else the next day.

**Finding**: The run loop fires a template time-of-day entry when `now >= tod` and the entry did not fire today (`QueueExecutionService`, around line 574). After it fires, `QueueRunSchedule.NextTimeOfDayDue` gives the next day at the same clock time. The monitor row "template entry at `2026-09-25T14:52:00+02:00`" comes from this rule. The template entry has a "catch-up" case (a time that passed before the run started fires once). A self-reschedule booking has no such case: the step runs at the current moment, so a time that has passed means "the next occurrence is on the next day".

**Alternatives considered**: Call `QueueRunSchedule.NextTimeOfDayDue` from the coordinator. Rejected: that method needs a template entry index and its "fired today" state. The coordinator has no entry. A small local rule is simpler and has no coupling.

## R-003: Boundary when the time of day is equal to the current moment

**Decision**: "Ahead" means strictly later than the current moment. A time that is equal to the current moment, to the tick, books the next day.

**Rationale**: A booking at the current moment runs immediately, which is the defect (spec clarification 1). The step runs after the time, so an equal value means that this run is the run for that time.

## R-004: UTC offset of the booked instant

**Decision**: Make the local date and time first (today or the next day at the time of day). Then get the offset from `TimeProvider.LocalTimeZone.GetUtcOffset(localDateTime)`.

**Rationale**: The operator gives a local clock time. When daylight saving time starts or stops between today and the next day, the offset of the current moment is not the offset of the next day. With the offset of the local time zone for that date and time, the run occurs at the correct clock time (spec clarification 2, FR-004). For a clock time in the gap when the clock goes forward, `GetUtcOffset` gives the standard offset (spec clarification 3). For a clock time that occurs two times when the clock goes back, `GetUtcOffset` gives the standard offset, that is, the second occurrence. This is acceptable.

For a day without a clock change, the result is the same as the old code for the "ahead" case (FR-001).

**Alternatives considered**: Use `now.Offset` for the next day, as `NextTimeOfDayDue` does. Rejected: it is one hour off on the day after a clock change. The template entry does not change (non-goal), because its run loop compares local clock times and not instants.

## R-005: Test clock

**Decision**: Give the test `FakeTimeProvider` (`tests/unit/Queues/FakeTimeProvider.cs`) an optional `TimeZoneInfo` constructor parameter. The default stays UTC.

**Rationale**: The probe uses UTC+02:00, and the daylight saving test needs a zone with an adjustment rule. `TimeZoneInfo.CreateCustomTimeZone` works on Windows and Linux, and it needs no new package. Existing tests do not change.

## R-006: Existing test that records the defect

**Decision**: Replace `SelfRescheduleCoordinatorTests.TimerPastTimeOfDayCollapsesToNow`. The new test expects the next-day instant.

**Rationale**: The old test expects `FireAt <= now`. This is the defect behavior.

## R-007: Documents that tell the old behavior

**Finding**: The XML comment of `ResolveTimerFireAt` and the specs of feature 065 tell the "collapse to now" rule. The OpenAPI payload text of `reschedule-self` (`PrimitiveActionSchemaFilter`) says "service-local time of day" and does not tell what occurs when the time has passed. `docs/architecture.md` describes the self-reschedule action.

**Decision**: Change the XML comment, add one sentence to the OpenAPI payload text and to `docs/architecture.md`, and add a `### Fixed` item to `CHANGELOG.md`. Do not change the old spec 065 (history). Add the row for 109 to `specs/STATUS.md`.
