# Research: Before Each Run Before Every Firing

## R-001: Where the "once per wake-up" limit is

- **Decision**: The limit is the local flag `beforeEachRunRanThisIteration` in `QueueExecutionService`. `RunBeforeEachRunPassAsync` returns at once when the flag is true. The loop sets the flag to false at the start of each iteration. Remove the flag, the check, and the reset.
- **Rationale**: This is the only place that limits the pass. All triggering firings call the pass through `FireGroupAsync(..., beforeEachRun: true, ...)` or, for the daily retry, directly. With no flag, each call runs the full pass.
- **Alternatives considered**: Keep the flag and reset it after each triggering firing. This gives the same result with more code. Rejected.

## R-002: Set of triggering firings

- **Decision**: Keep the current call sites with no change. These call sites are: the run-next drain (feature 184), the next-cycle-start drain, time-of-day timers, daily retries, relative timers, live schedules, the drain of self-rescheduled Timer firings, and the self-rescheduled OncePerRun firings at the end of a cycle. Template AtQueueStart and template OncePerRun firings call `FireGroupAsync` with `beforeEachRun: false` and keep it.
- **Rationale**: The issue changes the frequency of the pass, not its scope (spec, Clarifications).
- **Alternatives considered**: Also run the pass before template OncePerRun steps. Rejected: feature 095 FR-005 excludes them, and the issue does not ask for it.

## R-003: Order relative to the After Every Step pass

- **Decision**: The order for each triggering firing is: operator gate, plan check, liveness gate, Before Each Run pass, main firing, then the After Every Step pass at the call site. Thus for two co-due firings T1 and T2 the order is B, T1, E, B, T2, E.
- **Rationale**: `FireGroupAsync` already has this order. The call site runs the After Every Step pass after `FireGroupAsync` returns.
- **Alternatives considered**: None. The current order already gives the wanted result.

## R-004: Liveness hold and hold-only mode

- **Decision**: No change. `FireGroupAsync` checks the liveness gate before the pass. A held firing returns `null` and gets no pass. A later firing of the same wake-up in hold-only mode also returns `null` before the pass.
- **Rationale**: Spec FR-007.

## R-005: Accounting and run log

- **Decision**: No change in `RunBeforeEachRunPassAsync` except the removed flag. Each execution increments `index`, calls `MarkEntryRan`, adds to `failed` on a failure, and calls `handle.Cycles.RecordEntry`. Thus each pass gives its own sequence nodes in the run log and its own counts.
- **Rationale**: Spec FR-005 and FR-006, and #208.

## R-006: Session re-bind (#217)

- **Decision**: No change. `EnsureSessionBound` runs before each Before Each Run execution. After the first re-bind of a wake-up, the session is bound, so a later pass does not create a new session. The test `SeveralFiringsDueAtOneWakeUpRebindOnce` stays valid.

## R-007: Text to update

- **Decision**: Update each text that says "at most once per wake-up" or "the first timed ... firing": the class comment and the pass comment in `QueueExecutionService.cs`, `QueueMonitorSnapshot.cs`, `ScheduleType.cs`, `TemplateEntrySaveRequest.cs`, `QueueTemplateDetailResponse.cs`, the header comment of `QueueExecutionServiceBeforeEachRunTests.cs`, and `docs/architecture.md`. Do not change the old CHANGELOG entry of feature 095, because it records history.
- **Rationale**: Spec FR-010.

## R-008: Web UI

- **Decision**: No web UI change. The UI shows the label "Before each run" only.
- **Rationale**: A search of `src/web-ui/src` finds no text about the frequency of the pass.
