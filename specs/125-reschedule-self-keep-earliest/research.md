# Research: Reschedule-Self Keep Earliest

## R-001: How the code knows "the same run"

**Decision**: Store a run id in every Timer booking entry, with or without `keep` (FR-002). The run id is the root execution id of the sequence execution (`rootExecutionId` in `SequenceExecutionService`; a child sequence execution uses the root id of its parent context, so a booking in a child sequence has the run key of the root run). The dispatcher passes it to the coordinator. A `keep: earliest` booking competes only with a pending booking that has the same non-null run id.

**Rationale**: `rootExecutionId` is unique for each execution of a sequence and is available where the action dispatcher runs. A booking from an earlier execution has another id. A booking that the engine makes (for example the step-0 retry) has no id. Both are stale, so the new booking replaces them (FR-003). A booking without `keep` must record the run too, so a later `keep: earliest` booking of the same run compares with it (spec mix rule).

**Alternatives considered**:
- Compare booking age to the run start time. Rejected: it needs a clock read and a run start time in the handle. It is fragile for a booking in the same millisecond.
- A per-sequence run counter in `QueueRunHandle`. Rejected: it needs a start and end hook in the queue engine and is not safe for nested or direct runs.
- Clear the pending booking at run start. Rejected: it changes the default behavior (FR-004, FR-011) and loses a booking if the run fails early.
- Record the run id only for `keep` bookings. Rejected: a `keep` booking would not see an earlier booking without `keep` in the same run.

## R-002: Where to make the compare atomic

**Decision**: Inside `QueueRunHandle.AddTimerFiring`, under `_timerLock`. The method gets a `keepEarliest` argument and returns a `TimerBookingResult`.

**Rationale**: The check and the write must be one step. The drain and a booking can run in parallel, and the lock already guards the list.

**Alternatives considered**: Read the snapshot in the coordinator, then write. Rejected: a race between the read and the write.

## R-003: Type of the `keep` value

**Decision**: A string key `keep` with the one value `earliest`, parsed to the enum `SelfRescheduleKeep`. Any other present value (also an empty string or a non-string value) gives a parse error that names `earliest`. JSON null counts as absent.

**Rationale**: Same style as `option`. The enum leaves room for a future value without a payload change. The stored value stays as the author wrote it (FR-007) because the payload dictionary is the storage.

**Alternatives considered**: A boolean `keepEarliest`. Rejected: the issue and the spec use `keep: earliest`.

## R-004: Result of a losing booking

**Decision**: `ScheduleSelf` returns outcome `Scheduled` with `KeptPending = true` and `PendingFireAt`. The dispatch result is `scheduled` and successful. The message gives both fire times (FR-006b).

**Rationale**: A failed step would abort the run (spec clarification). The `scheduled` outcome already maps to the reschedule log entry, so the log layout needs no change.

**Alternatives considered**: A new outcome `kept`. Rejected: more switch sites for no gain.

## R-005: Allow-list sites (FR-008)

**Decision**: Five sites need `keep`: payload reader, validator allow-list with the non-Timer rule, repository backstop, API documentation text, and dispatch. The search for `ActionTypes.RescheduleSelf` shows that the action-type lists need no change because the action type is unchanged.

**Rationale**: FR-008. A contract test covers the 400-not-500 path and a round-trip test covers storage (project memory: sequence allow-lists).

## R-006: Web UI

**Decision**: No change. The editor builds the `reschedule-self` payload from fixed fields and does not write `ocrOffset` either.

## R-007: Compare of an `ocrOffset` booking and a past booking

**Decision**: The compare uses the absolute fire time that the coordinator resolves. For an `ocrOffset` booking, the dispatcher gives the coordinator the effective offset after the resolver applies `min`, `max`, and the fallback. So the compare needs no OCR knowledge. A past fire time is earlier than a future fire time and wins.

**Rationale**: Spec clarifications (analyze loop 1, C3a and C3b). One test covers an `ocrOffset` case.

## R-008: Test of the step-0 retry replacement

**Decision**: One test uses the real engine path that makes a booking with no run id. It runs a queue, lets the engine make the pending booking, then runs a sequence with `keep: earliest` whose first booking is later. It checks that the first booking replaces the engine booking. Unit tests of `AddTimerFiring` cover the same rule with a null run id.

**Rationale**: FR-012 asks for the real engine path. A unit test with a hand-made entry could pass while the engine path is wrong.

## R-010: The re-arm path loses its run (RearmTimerFiring RunId=null)

**Decision**: `QueueRunHandle.RearmTimerFiring` stores the held entry with `RunId = null` (`entry with { RunId = null }`). The fire time stays the same. The rule "add only when the register has no entry for the sequence" stays.

**Rationale**: A re-armed booking is not a booking of the new run. With `RunId = null`, the first booking of the new run replaces it (FR-003, clarification U1). Without this rule, a re-armed booking could keep the old run id and block the new timers when the ids match.

**Alternatives considered**: Leave the run id on the re-armed entry. Rejected: a held booking from the same root run could block a later `keep: earliest` booking after the hold. Test: a unit test of `RearmTimerFiring` checks `RunId` is null, and an integration test uses the real hold and re-arm path.

## R-011: Log level of a losing booking

**Decision**: Information level. The message names the pending fire time and the new fire time. A losing booking is a normal event, not an error (clarification U2).

## R-009: Documentation updates (FR-013)

**Decision**: Update `docs/architecture.md` (self-reschedule section: the `keep: earliest` rule, the same-run rule, the "Last reviewed" date). Set the `Status` line of `spec.md` to Implemented when the code lands. Add a line for feature 125 to `specs/STATUS.md`. Add an entry to `CHANGELOG.md`. Feature 124 (OCR offset day part) is related but not superseded, so its `Status` does not change.

**Rationale**: Constitution Principle V and the Definition of Done.
