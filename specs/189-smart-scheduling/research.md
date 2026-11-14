# Research: Smart Scheduling

All decisions come from the spec, the execution logs, and the code of the queue engine. No item needs clarification.

## R-001: Default window

- **Decision**: The default window is 15 minutes. The permitted range is 1 to 120 whole minutes.
- **Rationale**: The execution logs of 2026-11-06 to 2026-11-13 (14.3 queue-days, 1 220 wake-ups) give 85 wake-ups for each queue-day. A simulated window of 15 minutes gives 47 (44% fewer). The curve has no sharp bend. Many sequences already use a retry time of 15 minutes. See the Background section of the spec for the full table.
- **Alternatives considered**: 10 minutes (32% fewer, too small an effect). 20 minutes (52% fewer, 5 more minutes of delay for 8 points). 30 minutes and more (the delay of a reward becomes too long for a default).

## R-002: Where the rule runs

- **Decision**: The rule runs in `SelfRescheduleCoordinator.ScheduleSelf`, in the `Timer` case, between `ResolveTimerFireAt` and `QueueRunHandle.AddTimerFiring`. A pure static class `SmartSchedulingRule` in `GameBot.Domain` calculates the booked time from the requested time, the window, and a list of candidates.
- **Rationale**: The coordinator is the one place where each `reschedule-self` Timer booking gets its fire time. The other schedule sources (template Timer entries, live schedules, run-next requests, the other booking options) do not go through this case, so FR-018 holds with no added code. A pure rule is easy to test with no clock and no run.
- **Alternatives considered**: The rule in `QueueRunHandle.AddTimerFiring` (rejected: the handle does not know the template timers of `QueueRunSchedule`, and `RearmTimerFiring` must not apply the rule). The rule in the run loop at each wake-up (rejected: the clarification says that a booked time never changes after the booking).

## R-003: Candidates (possible targets)

- **Decision**: The coordinator collects the candidates from three sources of the active run, at the time of the booking:
  1. `QueueRunHandle.SnapshotPendingTimerFirings()`: the pending Timer bookings.
  2. A new read method `QueueRunSchedule.SnapshotTimedEntryDueTimes(now)`: for each enabled template Timer entry, the next time-of-day due time, the relative-offset due time when the entry did not fire, and the due time of an armed daily retry.
  3. `QueueRunHandle.PendingLiveSchedules`: the pending live schedules.

  The coordinator removes each candidate that has the sequence ID of the sequence that makes the booking. The rule then keeps the candidates with a due time from T to T plus the window (the two ends included) and selects the earliest. When two candidates have the same due time, the first in the order above wins.
- **Rationale**: These are the same sources that `QueueRunSchedule.ComputeNextDue` uses for the next wake-up, without the sources that have no time (once-per-run and next-cycle-start work, run-next requests). Thus a target is always a firing that wakes the queue up. `ComputeNextDue` stays as it is. The new read method uses the same private helpers under the same lock, so the two cannot disagree.
- **Alternatives considered**: A change of `ComputeNextDue` to return all due times (rejected: it is on the idle-pause path, and it must not allocate a list at each poll).
- **Note on time-of-day entries**: an entry that is past its time today and did not fire has the due time "now". This time is earlier than each requested time in the future, so the entry is not a target. This is correct: the entry fires in the current wake-up.

## R-004: Settings take effect immediately

- **Decision**: `SequenceExecutionService` reads the stored queue from `IQueueRepository` each time that it dispatches a `reschedule-self` Timer step with a queue ID. It reads `SmartScheduling` and `SmartSchedulingWindowMinutes` from that copy and gives them to the coordinator as an optional `SmartSchedulingRequest`. The queue run does not keep a copy of the two settings.
- **Rationale**: The clarification says that a change applies to each booking made after the save, with no queue restart. A run reads its queue one time, at its start (feature 173, FR-019), so the run copy is stale after a save. `SequenceExecutionService` already has an optional `IQueueRepository` and already has the sequence in hand, so it also knows the "ignore smart scheduling" setting. One read of one small file for each Timer booking is cheap: the logs show approximately 300 sequence runs for each queue-day.
- **Alternatives considered**: A push of the new settings from the update route to the active `QueueRunHandle` (rejected: three write paths change a queue (create/update route, settings import, duplicate), and a missed path gives a stale value with no error). A read in the coordinator (rejected: `ScheduleSelf` is synchronous and the repository is asynchronous).
- **Effect on feature 173**: the smart scheduling settings are the one exception to "a saved setting applies at the next start". The comment in `QueueSettingsWhileRunningTests.cs`, the comment on `ExecutionQueue`, and `docs/architecture.md` say this.

## R-005: What a booking stores

- **Decision**: `SelfRescheduleEntry` gets one optional member `Postponement` (`SmartPostponement`: `RequestedAt`, `TargetSequenceId`, `TargetKind`). `FireAt` holds the booked time. `RearmTimerFiring` keeps the member (it uses `with`).
- **Rationale**: The run loop and `ComputeNextDue` read only `FireAt`, so they need no change. The monitor reads `Postponement` to show the requested time. `keep: earliest` compares `FireAt`, which is the booked time (FR-019).
- **Alternatives considered**: Two time fields on the entry and a change of each reader (rejected: more code, same result).

## R-006: Execution log and application log

- **Decision**: `SelfRescheduleResult` gets `RequestedAt` and `SmartTarget`. `ActionDispatchResult` gets one optional member `SmartScheduling` (`SmartSchedulingLogInfo`: requested time, booked time, target sequence ID, target kind, window in minutes), on the pattern of `Removed` (feature 123). The step record of `SequenceRunner` carries it to the execution log. For a postponed booking, the step message gets the text `postponed from <requested> to the firing of '<target name>' (smart scheduling, window <N> min)`, and the detail attributes get `requestedFireAt`, `bookedFireAt`, `smartSchedulingTarget`, `smartSchedulingTargetKind`, and `smartSchedulingWindowMinutes`. A booking that is not postponed has the same message and the same attributes as before (acceptance scenario 3 of User Story 4). The coordinator writes one Information log message (event ID 1162) for each postponement.
- **Rationale**: FR-020 needs the three values in the execution log. The attribute pattern is the pattern of `removed`.
- **Alternatives considered**: Text only (rejected: a tool cannot read the values with no parse of the text).

## R-007: Queue monitor

- **Decision**: `QueueMonitorItem` and `QueueMonitorItemResponse` get the optional member `requestedAt`. It is set only for a postponed self-reschedule firing. The `reason` of that item is `Rescheduled by a sequence, postponed from HH:mm (smart scheduling)`. The snapshot and the response get the member `smartScheduling` (`enabled`, `windowMinutes`), read from the stored queue that the monitor service already loads. The web UI shows the reason text as it is, and shows one line "Smart scheduling: N min" in the monitor header when it is on.
  The schedule rows of the queue detail come from `buildScheduleRows` in `queueActivity.ts`, for the phone and for the large screen. For an item with `requestedAt`, the timing text gets the suffix ` · postponed from HH:mm`. The OpenAPI descriptions of the new queue and monitor members come from one new schema filter (`SmartSchedulingSchemaFilter`), because the service does not read XML comments into the OpenAPI document.
- **Rationale**: FR-021 and FR-022. `expectedAt` already holds the booked time. One shared function keeps the phone and the large screen the same (FR-024, the parity test).
- **Alternatives considered**: A new `scheduleKind` value (rejected: clients that switch on the kind would not know it; the firing is still a self-reschedule firing).

## R-008: API shape of the queue settings

- **Decision**: `CreateQueueRequest` and `UpdateQueueRequest` get `bool? smartScheduling` and `int? smartSchedulingWindowMinutes`. A null value keeps the stored value on update and gives the default on create (off, 15). A window out of 1 to 120 gives 400 `invalid_request` with the message `smartSchedulingWindowMinutes must be from 1 to 120`. `QueueResponse` and the detail response always return the two values. The duplicate route copies them. The settings document (`QueueSettingsDoc`, exporter, importer) carries them. The format version stays 1, because the members are additive and an old document reads as off and 15.
- **Rationale**: `UpdateQueueRequest` is a full replacement for `pauseWhenIdle` (absent means false). A client from before this feature would then switch smart scheduling off on each save. The nullable form prevents this, on the pattern of the notification level (feature 120). The spec says "reject", so the window is not coerced as `idleThresholdSeconds` is.
- **Alternatives considered**: A nested object `smartScheduling: { enabled, windowMinutes }` (rejected: the other queue settings are flat members; flat members also keep the settings document simple).

## R-009: API shape of the sequence setting

- **Decision**: `CommandSequence` gets `IgnoreSmartScheduling` (`ignoreSmartScheduling`, omitted from JSON when false). The sequence routes, the contracts, the OpenAPI description, and the web UI follow the pattern of `excludeFromSuccessNotifications` (feature 122): optional on create, update, and PATCH; a non-boolean value gives 400; the list and the detail return it. One difference: on `PUT`, an absent member keeps the stored value (Clarifications). A client that does not know the member thus cannot remove the mark of a time-critical sequence.
- **Rationale**: The pattern exists, is tested, and keeps old sequence files byte-identical. A gamepack and the settings document store a sequence as its JSON object, so the member goes with the sequence (FR-009).
- **Alternatives considered**: A member on the `reschedule-self` step (rejected: the user description asks for the sequence level).

## R-010: Step-through preview

- **Decision**: No change. `StepThroughActionPreview` shows the requested time (`would reschedule at 14:30`).
- **Rationale**: A step-through does not book anything and has no queue run, so it has no candidates.

## R-011: Performance

- **Decision**: No performance work is necessary. The PR gets a short performance note.
- **Rationale**: One booking reads one queue file and three small in-memory lists (a roster has approximately 20 entries). The rule is a linear scan. The run loop, the poll, and `ComputeNextDue` do not change.
