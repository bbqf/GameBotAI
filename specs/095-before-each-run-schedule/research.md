# Research: Before-Each-Run Schedule Type

## R-001 Where the pass hooks into the run loop

- **Decision**: A local function `RunBeforeEachRunPassAsync()` inside `QueueExecutionService.RunAsync`, next to `RunEveryStepPassAsync`, guarded by a `beforeEachRunRanThisIteration` flag reset at the top of each `do { … }` iteration. It is awaited immediately before `RunOneSequenceAsync` at each triggering firing site: (a0) self-reschedule AtQueueStart next-cycle firings, (a) time-of-day timers, (a1b) daily retries, (a2) relative timers, (a3) live schedules, (a4) self-reschedule Timer firings, and the self-reschedule OncePerRun drain inside (b).
- **Rationale**: Every firing already passes through one of these sites and each already calls `RunEveryStepPassAsync` afterwards, so the mirror placement is mechanical and auditable. Lazy invocation guarantees the pass never runs on an iteration with no triggering firing (spec US2-AS2) and runs *after* an idle-pause hold ends, because the hold happens at the bottom of the previous iteration.
- **Alternatives considered**: (1) Run the pass at the top of every iteration when anything is due — requires duplicating every due-ness predicate and races with firings becoming due during the pass. (2) Once per firing — rejected by the issue ("once per wake-up").

## R-002 Which firings trigger the pass

- **Decision**: Triggering = template Timer (time-of-day + daily retry + relative), live schedules, self-reschedule Timer / AtQueueStart / OncePerRun firings. Non-triggering = template AtQueueStart pre-pass, template OncePerRun steps, every-step entries and every-step injections.
- **Rationale**: Issue wording "timer-fired, live-scheduled or self-rescheduled entry"; spec clarification Q1. The AtQueueStart pre-pass runs before the loop exists and already *is* the establishing phase for a fresh start.

## R-003 Accounting, logging and cycle ledger

- **Decision**: Identical to the every-step pass: `RunOneSequenceAsync(...)` (so each execution is logged under the run root and goes through the foreground guard and watchdog), `failed++` on failure, `handle.Cycles.RecordEntry(sequenceId, ok)`, no `executed++`. The triggering firing runs regardless of pass outcome. Connection-lost and cancellation checks precede each entry.
- **Rationale**: FR-006/FR-015; consistency with After Every Step.

## R-004 Run-lifetime and cycle counting

- **Decision**: Do not add BeforeEachRun entries to the loop-entry condition (`oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0 || HasPendingSelfRescheduleWork`) nor to `cycleHasWork`. `HasPendingRelativeOrLive` and `ComputeNextDue` are untouched.
- **Rationale**: FR-009. The pass only runs directly before a firing, which already advances `index`, so the feature-093 "did anything run" check is unaffected.

## R-005 Enum value and persistence

- **Decision**: `ScheduleType.BeforeEachRun = 4`. Templates persist the enum by name (`JsonStringEnumConverter`), so no migration; older builds would not read the new name, which is acceptable for a new opt-in value. `TryParseScheduleType` uses `Enum.TryParse` + `IsDefined`, so acceptance is automatic; only the error message's accepted-values list needs the new name.
- **Alternatives considered**: A flag on EveryStep ("before" vs "after") — rejected: the issue proposes a distinct schedule type, and the editor model is one area per type.

## R-006 Monitor projection

- **Decision**: `ScheduleKind.BeforeEachRun` appended to the enum; `KindFor` maps the type; `ReasonFor` returns "Before Each Run"; `BuildUpcoming` lists enabled BeforeEachRun entries once each (like every-step items), trailing the timed firings and placed before the every-step items; `Repeats` = false (they ride along with timed firings, not with cycles).
- **Rationale**: They carry no time of their own; they precede the next timed firing. Placing them after timed items keeps the run's real next action at the top (feature 072/073 lesson).
- **Note**: The monitor's upcoming list already reads only the run schedule's (enabled) entries.

## R-007 Web editor

- **Decision**: New area id `beforeEachRun`, label "Before each run", mapped to `BeforeEachRun`; canonical inter-area order `startOfExecution, beforeEachRun, oncePerRun, scheduled, afterEveryStep`; rendered at the top of the right column above "After every step". Card/entry-list label map gains `BeforeEachRun: 'Before Each Run'` plus a badge.
- **Rationale**: Canonical order only affects the flattened save order; templates with no BeforeEachRun entries flatten identically, so existing positional save/restore is unchanged. Before-each-run pairs visually with after-every-step.

## R-008 Self-reschedule option

- **Decision**: No `BeforeEachRun` option for `reschedule-self` (spec Out of scope). `SelfRescheduleOption` and `SequencesPage` option list unchanged.
