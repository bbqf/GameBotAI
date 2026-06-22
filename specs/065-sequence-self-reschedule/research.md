# Phase 0 Research: Sequence Self-Rescheduling

All spec ambiguities were resolved in the 2026-06-22 clarification session (5 Q&A). The remaining
unknowns are **technical** — how to land the behavior inside the existing engine without a DI cycle,
without persistence, and without breaking established option semantics. Each decision below records
what was chosen, why, and the alternatives rejected.

---

## D1. How a running sequence learns its originating queue run (FR-011, FR-018)

**Decision**: Add a nullable `OriginatingQueueId` to `ExecutionLogContext`. `QueueExecutionService`
sets it on the context it builds in `RunOneSequenceAsync` (it already builds one per firing).
`SequenceExecutionService` copies it onto the child contexts it constructs for nested invocations, so
it propagates through nesting automatically. A non-empty value ⇒ "started from a queue"; empty/absent
⇒ no-op success.

**Rationale**: `ExecutionLogContext` is *already* the per-firing context threaded from the queue run
through `SequenceExecutionService` into nested executions, and it already propagates `RootExecutionId`
and `Depth` the same way. Reusing it gives origin-through-nesting (FR-018) for free and adds exactly
one optional field. No new parameter on the public `ISequenceExecutionService.ExecuteAsync` signature.

**Alternatives rejected**:
- *AsyncLocal/ambient run context* — implicit, hard to test, and leaks across `Task.Run` boundaries
  used by the queue loop. Rejected for testability.
- *New explicit `originatingQueueId` parameter on `ExecuteAsync`* — touches every caller and the
  standalone `sequences/{id}/execute` endpoint; the context object already exists for exactly this
  kind of ambient-but-explicit data.

---

## D2. Avoiding the DI cycle (the run registry extraction)

**Decision**: Extract the active-run dictionary (today `private ConcurrentDictionary<string,
QueueRunHandle> _runs` in `QueueExecutionService`) into a singleton `IQueueRunRegistry`.
`QueueExecutionService` registers/looks up/removes handles via the registry. A new narrow
`ISelfRescheduleCoordinator` (also depending only on the registry + `ISequenceRepository` +
`TimeProvider`) performs the inject. `SequenceExecutionService` depends on the coordinator.

**Rationale**: `QueueExecutionService` already depends on `ISequenceExecutionService`. Injecting
`IQueueExecutionService` back into `SequenceExecutionService` would form a constructor cycle and fail
DI validation. Routing the self-reschedule through a dependency-free registry breaks the cycle:
`SequenceExecutionService → ISelfRescheduleCoordinator → IQueueRunRegistry ← QueueExecutionService`.
The registry is the single owner of run lifetime, so ephemeral schedules naturally die with the run.

**Alternatives rejected**:
- *Lazy<IQueueExecutionService> / property injection* — hides the cycle rather than removing it and
  is brittle under DI validation.
- *Put `ScheduleSelf` directly on `IQueueExecutionService`* — reintroduces the cycle.
- *Event/queue indirection (mediator)* — overkill for an in-process, synchronous register append.

The existing `ScheduleRelative` (feature 059) keeps working; it can later move onto the coordinator,
but this feature leaves it in place to minimize churn.

---

## D3. Mapping each schedule option to an ephemeral, run-scoped register

**Decision**: Add per-option registers to `QueueRunHandle`, all in-memory and discarded with the run:

| Option | Register | Drained where in the run loop | Resolved at inject time |
|--------|----------|-------------------------------|-------------------------|
| **Once Per Run** | `ConcurrentQueue<SelfRescheduleEntry> PendingOncePerRun` | Immediately after the once-per-run pass of the **current** cycle, before the cycle boundary | nothing (runs this cycle) |
| **Timer (relative)** | list of resolved instants `PendingTimerFirings` (id, seqId, fireAt) | At each iteration boundary when `now ≥ fireAt`; fired once then removed | `fireAt = now + offset` |
| **Timer (time-of-day)** | same `PendingTimerFirings` | same | `fireAt = today@time` (if already past ⇒ next boundary, i.e. `now`) |
| **After Every Step** | `set keyed by sequenceId EveryStepInjections` | After each subsequent normal step for the rest of the run | nothing (registration) |
| **At Queue Start** | `ConcurrentQueue<SelfRescheduleEntry> PendingNextCycleStart` | Top of the **next** cycle, before once-per-run (cycling); non-cycling ⇒ falls back into `PendingOncePerRun` | n/a |

**Rationale**: Each option's *existing* template/live behavior already has a drain point in the run
loop (feature 053/059/060). Resolving relative **and** time-of-day timers to an absolute `fireAt` at
inject time lets both reuse one list and matches the spec ("first iteration boundary at or after that
instant", FR-005/FR-006) — a past time-of-day collapses to `now`, firing at the next boundary.
Distinct registers (vs. one keyed dict) preserve "each accepted reschedule is an independent firing"
(edge case: multiple self-reschedules in one run) — unlike `PendingLiveSchedules`, which is
most-recent-wins per sequence and would collapse repeats.

**Alternatives rejected**:
- *Reuse `PendingLiveSchedules` for timers* — it is keyed by sequence id with most-recent-wins, which
  silently drops a second self-reschedule of the same sequence. Self-reschedules must accumulate.
- *Time-of-day kept as a clock comparison each boundary* — duplicates the template timer's
  per-calendar-day logic and complicates "fires once for this run"; resolving to an instant is simpler
  and fires-once by construction.

---

## D4. At Queue Start mid-run (the only novel semantic, FR-009)

**Decision**: On a **cycling** run, enqueue onto `PendingNextCycleStart`; the run loop drains it at
the top of the next `do/while` iteration, before the once-per-run pass — i.e. "start of the next
cycle". On a **non-cycling** run (no further start boundary), enqueue onto `PendingOncePerRun` so it
fires at the next iteration boundary instead. The coordinator decides which register based on
`queue.CycleExecution`, which the registry exposes via the handle.

**Rationale**: Directly encodes the clarification ("Cycling run → next cycle start; non-cycling →
next iteration boundary") and reuses the once-per-run drain for the fallback so there is no special
non-cycling code path.

**Alternatives rejected**: Treating non-cycling At-Queue-Start as a no-op — contradicts the
clarification, which requires a fallback firing.

---

## D5. Loop-safety for After Every Step (FR-008, feature 060)

**Decision**: `EveryStepInjections` is a **set keyed by sequence id** — registering the same sequence
again is idempotent. The after-every-step drain snapshots the set before iterating so a firing's own
self-reschedule action cannot grow the set mid-pass. The injected every-step firings count toward
`failed` but **not** `executed`, matching template `EveryStep` semantics.

**Rationale**: The risk is an after-every-step firing reaching the self-reschedule action again and
appending another after-every-step entry forever. Idempotent set membership bounds it to "this
sequence fires after every step" regardless of how many times the action runs — the same loop-safe
model feature 060 established for template every-step entries. The author remains responsible for IF
gating (per spec Assumptions: no built-in cap), but the engine itself cannot diverge.

**Alternatives rejected**: A per-run firing counter / hard cap — the spec explicitly chose
author-controlled bounding with no built-in cap (clarification Q1). A list (not a set) — would let the
same sequence stack unbounded every-step registrations.

---

## D6. Dispatching the action from `SequenceRunner` without web/queue coupling

**Decision**: Add an optional `Func<SequenceActionPayload, CancellationToken, Task<ActionDispatchResult>>?
actionDispatcher` to `SequenceRunner.ExecuteAsync`. In `ExecuteSingleStepAsync`, before the command
path, when `step.Action?.Type == ActionTypes.RescheduleSelf` and a dispatcher is supplied, call it,
record the returned outcome as the step result (executed/no-op + message), and continue (never early
-stop). `SequenceExecutionService` supplies the dispatcher, closing over `OriginatingQueueId` and the
coordinator.

**Rationale**: `SequenceRunner` lives in `GameBot.Domain` and must not reference queue/service types.
A callback keeps it agnostic, exactly as `executeCommandAsync` already does for command side effects.
Today a non-WaitForImage action step falls through to `executeCommandAsync("")` and is swallowed as a
no-op; the new branch intercepts the reschedule action before that fallback. The action does not, by
itself, terminate the sequence — the dispatcher returns and execution continues (FR-012).

**Alternatives rejected**:
- *Handle the action entirely inside `SequenceExecutionService` after the run* — too late; the action
  must execute at its position in the flow (under its IF branch) and be observable mid-sequence.
- *A new first-class `PrimitiveAction` variant* — heavier than needed; the generic
  `SequenceActionPayload` already round-trips arbitrary `{ Type, Parameters }` through the authoring
  API, satisfying FR-001a with zero API changes.

---

## D7. Execution-log representation (FR-013, FR-014, FR-015)

**Decision**: The action records a sequence **step** entry (via the existing `SequenceExecutionResult`
step + `detailItems` machinery in `SequenceExecutionService`) capturing: chosen option, resolved
timing (target instant for timers; "next cycle"/"this cycle" for the others), and outcome —
`scheduled` vs `noop` with the no-op reason ("not started from a queue"). The resulting firing is the
normal child-sequence log entry already produced by `RunOneSequenceAsync`; the run loop tags
self-reschedule-originated firings with a detail note ("scheduled by self-reschedule") so operators can
attribute the extra firing (FR-014). A reschedule accepted but never due before run end leaves only
the action entry (outcome `scheduled`, no firing) and does **not** affect the run's success/failure,
which remains governed by once-per-run completion (FR-015, FR-016).

**Rationale**: Reuses the established sequence-step and queue-child logging paths (features 059/063)
so the new entries render in the existing Execution Logs grid with no schema change; only detail
metadata is added.

**Alternatives rejected**: A separate top-level log object for reschedules — diverges from how live
firings (059) are logged and would need new grid handling.

---

## D8. Web-UI authoring surface (SC-001, FR-002, US2)

**Decision**: Extend the sequence editor's action model in `SequencesPage.tsx` from
`actionType: 'command' | 'WaitForImage'` to also include `'reschedule-self'`, with an option dropdown
(At Queue Start / Once Per Run / Timer / After Every Step) and, for Timer, the same time-of-day vs
relative-offset inputs already used by the queue-template editor (`QueueEntryList.tsx`). The action is
authorable anywhere a step is, including inside IF/conditional branches (existing conditional-flow
plumbing). It serializes to `{ action: { type: 'reschedule-self', parameters: { option, timerTimeOfDay?,
timerRelativeOffset? } } }`.

**Rationale**: Reuses the existing per-step action editor and the existing timer inputs verbatim, so
operators get option parity (US2) with no new UX vocabulary (Principle III). Persisting through the
generic `action.parameters` map means the sequence authoring API and store are unchanged.

**Alternatives rejected**: A dedicated standalone "reschedule" node type in the flow graph — more UI
surface and a new persisted shape for no functional gain over the generic action payload.

---

## D9. Determinism of timing (reuse of `TimeProvider`)

**Decision**: The coordinator and run-loop drains read wall-clock through the `TimeProvider` already
injected into `QueueExecutionService` (feature 059), not `DateTime.Now`. Unit tests use a fake
provider to assert fire-once and boundary semantics for every option.

**Rationale**: Consistency with the existing relative/live scheduling tests and deterministic,
fast unit tests (Principle II; tests <1s).

**Alternatives rejected**: Real-clock tests with sleeps — slow and flaky.
