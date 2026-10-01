# Research: Reschedule-Self Cancel

All decisions below resolve the open points of the plan. No NEEDS CLARIFICATION remains.

## R-001: Which registers does Cancel clear

- **Decision**: Cancel removes the entries of the current `sequenceId` from three registers of the run handle:
  the Timer list (`_pendingTimerFirings`), `PendingOncePerRun` and `PendingNextCycleStart`. It skips every
  entry in `PendingNextCycleStart` whose id starts with the template hold prefix `at-queue-start:`. It does not
  touch `EveryStepInjections`, `PendingLiveSchedules` or any template entry.
- **Rationale**: The spec (FR-001, clarification I2) limits Cancel to the one-time bookings: Timer, OncePerRun
  and AtQueueStart. An EveryStep injection is a standing schedule. A live schedule is not made by the step
  (FR-005). A device hold moves a template AtQueueStart entry into `PendingNextCycleStart` with the prefix
  `at-queue-start:` (`QueueExecutionService`). That entry is operator configuration, not a booking, so Cancel
  must keep it. The reschedule-self AtQueueStart booking in a non-cycling run goes to `PendingOncePerRun`, so
  the OncePerRun register covers it.
- **Alternatives considered**: (a) Clear the EveryStep register too. Rejected: the spec keeps EveryStep (FR-011).
  (b) Clear all of `PendingNextCycleStart`. Rejected: it removes held template entries. (c) Clear only the Timer
  list. Rejected: a OncePerRun or AtQueueStart booking would still wake the queue.

## R-002: How to remove from a `ConcurrentQueue`

- **Decision**: Add `QueueRunHandle.RemovePendingBookings(sequenceId)`. It has two parts.
  (1) Live registers: it uses `RemoveAll` under `_timerLock` on the Timer list. For each of the two queues, it
  dequeues the current count and enqueues back the entries that stay.
  (2) In-flight drain copy (FR-005, FR-015): the OncePerRun drain at `QueueExecutionService.cs` (about L737-752)
  moves all queued bookings into a local list before it fires them. A booking in that list is no longer in
  `PendingOncePerRun`, so part (1) cannot reach it. The handle gets a registered in-flight list: the loop calls
  `handle.BeginOncePerRunDrain(list)` after it fills the copy and `handle.EndOncePerRunDrain()` after the loop
  (in a `finally`). Under a lock, `RemovePendingBookings` adds the entry id of each in-flight booking of the
  sequence to a cancelled-id set. The firing loop calls `handle.TryConsumeCancelled(entry.Id)` before it fires
  each booking. When it returns true, the loop skips the booking: no firing, no `executed` count, no hold
  re-queue. The entry that runs the Cancel step is already fired, so it is not affected. The count returned
  includes the in-flight bookings that were marked, so `removed` is true when only an in-flight booking was
  cancelled. The set is cleared in `EndOncePerRunDrain`.
  The Timer drain (`DrainDueTimerFirings`) needs no such set: the Timer list has one entry for each sequence,
  so a firing of the sequence cannot leave a second Timer booking of the same sequence in its copy.
- **Rationale**: The Cancel step runs inside a firing on the one serial run loop. The loop drains the live
  queues only between firings, so the drain and the rebuild do not overlap. The rebuild keeps the order of the
  entries that stay. A set of entry ids (not of sequence ids) keeps the "last booking wins" rule: a booking that
  a later step makes after the Cancel step goes to the live queue with a new id, so it is not in the cancelled
  set, and it fires in the next drain. A handle-level set is chosen over a rebuilt local list because the loop
  iterates the list with `foreach`, and a change to the list in the loop would throw.
- **Alternatives considered**: (a) Replace the queues with a locked list. Rejected: it changes existing code
  with no need (FR-011). (b) A cancelled set of sequence ids. Rejected: it would also cancel a booking that a
  later step made. (c) Let the loop copy the list again after each firing. Rejected: more code in a method that
  is already large, and a new generation could then spin inside one cycle.

## R-003: Cancel with a held Timer firing

- **Decision**: No special handling. A held firing (feature 106) is outside the register while the hold is
  active, and `RearmTimerFiring` puts it back later.
- **Rationale**: A hold happens only when the device is not live. A step of the sequence cannot run in that
  state, so Cancel and a hold do not meet in one firing of the same sequence.
- **Alternatives considered**: Track a cancel marker for the sequence. Rejected: more state for a case that
  cannot occur.

## R-004: How the step reports `removed`, and the outcome names

- **Decision**: Add an optional `bool? Removed` member to `ActionDispatchResult` (default null). The Cancel
  dispatch returns outcome `cancelled` with `Removed = true` when it removed at least one entry. It returns
  outcome `noop` with `Removed = false` and a short message when it removed nothing, when no queue started the
  run, and when the queue run is no longer active (FR-004). The runner copies the flag into the step result.
  The execution log item of a reschedule step gets a `removed` field.
- **Rationale**: A client reads a boolean field more reliably than text. The outcome names are stable
  values. `noop` already means "success, nothing done" for the existing options, so log filters stay valid.
  The execution log code already treats `scheduled` and `noop` as reschedule outcomes. The new outcome
  `cancelled` must join that check in `SequenceExecutionService`.
- **Alternatives considered**: Put the result in the message text only. Rejected by the spec.

## R-005: Validator rules (FR-008, FR-009)

- **Decision**: `ValidateRescheduleSelfPayload` already rejects timer fields and `ocrOffset` for any option
  other than Timer. The messages say "only valid when option is Timer". Keep that branch. Report each of the
  three fields separately, so the message names the field. Add one unit test for each of `timerTimeOfDay`,
  `timerRelativeOffset` and `ocrOffset` with Cancel. Change the unknown-option text in
  `SelfReschedulePayload.TryRead` to list Cancel.
- **Rationale**: FR-008 is a firm required behavior with a unit test for each field. A test locks it even
  though the existing branch gives the result. The `else if` chain gives only one message when two fields are
  present, so the plan changes the chain to check each field and name it.
- **Alternatives considered**: Add a Cancel-specific message. Rejected: it adds a second message for the same rule.

## R-006: 400 and not 500 on every save path (FR-010)

- **Decision**: The endpoints exist: `POST /api/sequences` (create), `PUT /api/sequences/{id}` (update),
  `PATCH /api/sequences/{id}` and `POST /api/sequences/{id}/validate`. Create, update and patch call
  `ValidatePerStepForPersistenceAsync`, which runs `SequenceStepValidationService` and returns 400. The
  validate endpoint reads the saved sequence and validates its steps. It cannot receive a new step payload.
  The repository check `ValidateActionPayloads` in `FileSequenceRepository` checks only the action type, so a
  caller that skips the endpoint gate could save a bad payload. The plan adds a backstop there: for a
  `reschedule-self` step, call `SelfReschedulePayload.TryRead` and reject a Cancel option that carries a timer
  field or `ocrOffset`, with `InvalidOperationException`, as the other backstop checks do.
- **Contract tests**: (a) POST with Cancel plus each of the three fields returns 400 and the message names the
  field. (b) PUT and PATCH do the same. (c) An unknown option returns 400 and the message lists Cancel. (d) A
  repository unit test shows that `CreateAsync` and `UpdateAsync` reject the bad payload. (e) The validate
  endpoint returns `valid: true` for a saved sequence with a valid Cancel step. This test saves the sequence
  first, then calls validate: validate cannot receive a new payload. The planner confirmed the endpoint list
  before this decision, as FR-010 requires. Each test removes the sequence it created.
- **Rationale**: Project memory records that a new action option touches several backend sites. A guard test
  prevents a 500 from an unlisted site.
- **Alternatives considered**: Test only POST. Rejected: FR-010 names update, validate and the repository.

## R-007: Sites found by grep

A grep of `ActionTypes.RescheduleSelf` found: `SequenceActionTypes`, `ActionTypes`, `FileSequenceRepository`,
`SequenceStepValidationService`, `SequenceRunner` (service-level action check), `SequenceExecutionService`
(dispatch and log), `PrimitiveActionSchemaFilter` and `SwaggerConfig` (example). Cancel is a new enum value, not
a new action type. So the type-list sites need no change (FR-012 is met for them). The option sites need a
change: the enum, the payload reader text, the schema filter text, the coordinator, the repository backstop and
the web UI option list. The coordinator `ScheduleSelf` switch throws `ArgumentOutOfRangeException` for an unknown
option. The dispatch must never send Cancel to `ScheduleSelf`, and a unit test locks this.

## R-008: Coordinator API

- **Decision**: Add `CancelSelf(queueId, sequenceId)` to `ISelfRescheduleCoordinator`. It returns a result with
  `Outcome` (`Cancelled`, `NothingPending`, `NotRunning`) and the count removed. `DispatchSelfReschedule` calls
  it when the option is Cancel, after the existing "no originating queue" check.
- **Rationale**: The coordinator is the single place that owns the run handle registers for the step. This
  keeps the dispatch free of register details.
- **Alternatives considered**: Make `ScheduleSelf` handle Cancel. Rejected: its parameters (timer values,
  scope) do not apply, and its default branch is a useful guard.

## R-009: Tests

- Unit: validator (three fields, unknown option), payload reader, `RemovePendingBookings` (each register,
  other sequence kept, template hold entry kept, EveryStep kept, in-flight drain bookings marked, new booking
  after Cancel not marked), coordinator, dispatch outcomes and `removed`, owner id rule (R-012).
- Queue test (FR-015): two OncePerRun bookings of one sequence in one drain copy. The first firing runs a Cancel
  step. The second booking does not fire (one firing in total).
- Contract: 400 tests from R-006, the OpenAPI payload text, the existing guard test that `ScheduleSelf` is never
  called with Cancel.
- Integration: a queue run with Timer at step 0 and Cancel at the end gives no wake; a run that fails before Cancel
  keeps the booking (SC-001, SC-002). A run outside a queue and a run with no booking succeed (SC-003).
- UI: Jest test in `SequencesPage.reschedule.spec.tsx` for the option, the payload with no timer fields, and the
  hidden timer fields.
- Contract tests share the bin data dir; do not leave sequence files behind.

## R-010: Web UI (FR-014)

- **Decision**: Add `'Cancel'` to the `RescheduleOption` type and to `RESCHEDULE_OPTIONS` (label "Cancel pending
  booking"). The timer fields already show only when the option is Timer, so they stay hidden. The payload
  builder already adds timer fields only for Timer. The loader already reads `option` as a string.
- **Rationale**: One list and one type give the full UI change. A test proves the hidden fields and the payload.

## R-011: Documentation (FR-013)

- **Decision**: FR-013 names four items. (1) The OpenAPI description: update the payload text in
  `PrimitiveActionSchemaFilter` (option list, Cancel rules, no-op rules, `removed` field, in-flight drain rule).
  (2) The API docs in `docs/architecture.md`: update the self-reschedule paragraph and "Last reviewed".
  (3) The changelog: add a `CHANGELOG.md` entry that names FR-015 and issue #264. (4) The tracker row FR-015 in
  `docs/api-feature-requests.md`: the file does not exist in this repository, so skip it. Update the spec Status
  when the feature is implemented. A docs task MUST check the four items one by one.
- **Rationale**: Constitution Principle V and FR-013.

## R-012: Owner sequence id of a nested Cancel step (FR-001, FR-007)

- **Decision**: No new code. `DispatchSelfReschedule` receives the `sequenceId` of the sequence that owns the
  step, and the Timer option already uses this id for `ScheduleSelf`. The Cancel option passes the same
  `sequenceId` to `CancelSelf`. A Cancel step in a nested or called sequence removes the bookings of that owning
  sequence, not those of the top-level sequence.
- **Rationale**: Cancel must remove what the same sequence booked. One id source keeps both options in step.
- **Test**: A unit test calls the dispatch with an owner id that differs from the id of the firing sequence. It
  proves that the coordinator receives the owner id. If the existing dispatch passes the top-level id, the plan
  task MUST change it for both options in one place, and the Timer tests MUST stay green.
- **Alternatives considered**: Use the top-level sequence id. Rejected: the spec requires the owner id (D-clarification C7).
