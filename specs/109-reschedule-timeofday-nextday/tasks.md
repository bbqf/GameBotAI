# Tasks: A reschedule-self time of day that has passed books the next day

**Input**: Design documents from `specs/109-reschedule-timeofday-nextday/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/reschedule-self-timer.md, quickstart.md

**Tests**: Required. The fix is for a defect, so each story has a regression test. Write the tests before the fix, and make sure that they fail before the fix.

**Format**: `[ID] [P?] [Story] Description`. `[P]` means that the task can run in parallel with other `[P]` tasks (different files, no dependencies).

## Phase 1: Setup

- [x] T001 Read `ResolveTimerFireAt` in `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs` and `NextTimeOfDayDue` in `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`. Make sure that the root cause in research R-001 and the reference rule in R-002 are correct.

## Phase 2: Foundational (blocks all stories)

- [x] T002 In `tests/unit/Queues/FakeTimeProvider.cs`, add an optional `TimeZoneInfo? localTimeZone = null` constructor parameter. `LocalTimeZone` returns it, or `TimeZoneInfo.Utc` when it is null (research R-005). Change the XML comment in STE.

**Checkpoint**: The solution builds. No behavior change. All existing tests pass.

## Phase 3: User Story 1 - A daily sequence books its next run at a clock time from its last step (Priority: P1)

**Goal**: A time of day that is not later than the current local time books that time on the next local day.

**Independent Test**: Set the clock after the time of day. Run the step. The booked instant is the same clock time on the next day.

### Tests for User Story 1

- [x] T003 [US1] In `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`, replace `TimerPastTimeOfDayCollapsesToNow` (research R-006) with `TimerPastTimeOfDayBooksNextDay`: with a zone of fixed offset UTC+02:00 and a local time of 2026-09-24 14:55:41, time of day 14:55 gives `FireAt` `2026-09-25T14:55:00+02:00`. `DrainDueTimerFirings` at the current moment and at 23:59:59 on the same day gives no firing; at `FireAt` it gives one firing (spec US1 scenarios 1 and 3, SC-001, SC-003).
- [x] T004 [US1] In the same file, add `TimerTimeOfDayEqualToNowBooksNextDay`: at local 12:00:00 (UTC), time of day 12:00:00 gives 12:00:00 on the next day (spec US1 scenario 2, FR-002).
- [x] T005 [US1] In the same file, add the theory `TimerTimeOfDayAtDayEdgesBooksNextDay`: at local 00:00:00 (UTC), time of day 00:00:00 gives 00:00:00 on the next day; at local 23:59:59 (UTC), time of day 23:59:59 gives 23:59:59 on the next day (spec edge cases).
- [x] T006 [US1] In the same file, add `TimerNextDayUsesOffsetOfThatDay`: create a custom zone with `TimeZoneInfo.CreateCustomTimeZone` with the CET/CEST rules (standard +01:00, +1 h from the last Sunday of March 02:00 to the last Sunday of October 03:00). At local 2026-10-24 12:00:00 (+02:00), time of day 11:00 gives `2026-10-25T11:00:00+01:00` (spec FR-004, data-model example 4). Add a second test `TimerNextDayInClockGapUsesStandardOffset`: at local 2026-03-28 12:00:00 (+01:00), time of day 02:30 gives `2026-03-29T02:30:00+01:00` (spec edge case for the gap, clarification 3).
- [x] T007 [US1] Run the tests of T003 to T006. Make sure that T003, T004, T005 and T006 fail before the fix.

### Implementation for User Story 1

- [x] T008 [US1] In `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`, change `ResolveTimerFireAt` for a time of day as data-model.md tells: make the local date and time of today at `tod`; when it is not later than the local date and time of `now`, add one day; get the offset from `_timeProvider.LocalTimeZone.GetUtcOffset(local)`. Keep the relative offset path and the defensive path. Change the XML comment in STE and name issue #227.
- [x] T009 [US1] Run the tests of T003 to T006. Make sure that they pass.

**Checkpoint**: US1 is complete. A booking for a time that has passed is on the next day.

## Phase 4: User Story 2 - A time of day that is still ahead keeps today (Priority: P1)

**Goal**: The "ahead" case does not change.

**Independent Test**: Set the clock before the time of day. Run the step. The booked instant is today at that time.

- [x] T010 [US2] In `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`, add `TimerFutureTimeOfDayKeepsToday`: with the UTC+02:00 zone and a local time of 2026-09-24 14:52:11, time of day 14:55 gives `2026-09-24T14:55:00+02:00` (spec US2, SC-002). Keep `TimerFutureTimeOfDayFiresAtThatInstant` and `TimerRelativeOffsetResolvesNowPlusOffset` without a change (SC-005). Run the file and make sure that all tests pass.

**Checkpoint**: US1 and US2 are complete.

## Phase 5: User Story 3 - The monitor shows the correct next-day time (Priority: P2)

**Goal**: `GET /api/queues/{id}/monitor` shows the next-day instant.

**Independent Test**: Book a next-day run. Read the monitor. The `SelfReschedule` item has the next-day instant.

- [x] T011 [US3] In `tests/unit/Queues/QueueMonitorServiceTests.cs`, add `SelfRescheduleTimeOfDayThatPassedShowsNextDay`: use the harness registry and clock (12:00 UTC) with a `SelfRescheduleCoordinator`. Book time of day 11:00 for sequence `R`. `BuildAsync("q1")` gives an item for `R` with `ScheduleKind.SelfReschedule` and `ExpectedAt` `2026-01-02T11:00:00Z` (spec US3, FR-005, SC-004). Run it and make sure that it passes.
- [x] T012 [P] [US3] In `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs`, add to the `reschedule-self` text: "A timerTimeOfDay that is not later than the current time books that time on the next day." (contract, FR-008).
- [x] T013 [P] [US3] In `tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs`, add `"next day"` to `RescheduleSelfStatements`.

**Checkpoint**: All stories are complete.

## Phase 6: Polish and cross-cutting

- [x] T014 [P] In `docs/architecture.md`, add one sentence to the "Self-reschedule action" item: a Timer time of day that is not later than the current local time books that time on the next day (feature 109, #227). Change the "Last reviewed" line to 2026-09-27, feature 109.
- [x] T015 [P] In `CHANGELOG.md`, add an item at the top of `### Fixed` under `[Unreleased]` for 109-reschedule-timeofday-nextday, #227 (FR-008).
- [x] T016 [P] In `specs/STATUS.md`, add the row `| 109 | A reschedule-self time of day that has passed books the next day | Implemented |`. Set **Status** in `spec.md` to `Implemented`.
- [x] T017 Build `GameBot.sln` in Release with `-warnaserror`. Run the unit and contract test projects. Make sure that there are no new failures.
- [x] T018 Mark all tasks as done in this file.

## Dependencies and Execution Order

- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6.
- T003 to T007 before T008 (tests first).
- T012, T013, T014, T015 and T016 are in different files and can run in parallel.

## Implementation Strategy

MVP: Phases 1 to 3. This removes the defect. Phases 4 and 5 add regression tests and the OpenAPI text. Phase 6 updates the documents.
