# Phase 0 Research: Queue Failure Policy and Outbound Notification

**Feature**: 087-queue-failure-policy | **Date**: 2026-09-14

Every unknown in the Technical Context is resolved below. Each entry records the decision, why it
was chosen, and what was rejected.

---

## R1 — Where the policy is evaluated

**Decision**: At the single existing `handle.Cycles.CompleteOpen(...)` call site in
`QueueExecutionService.ExecuteRunAsync` (currently line 517), immediately after it, as **one call**
to a new method on a collaborator — never as an inline block.

**Rationale**: `CompleteOpen` is the exact instant the engine decides a cycle has ended and the
ledger's `_consecutiveFailedCycles` is updated. Evaluating anywhere else would need a second notion
of "a cycle ended". The one-call rule is not stylistic: `ExecuteRunAsync` is an ~280-line method
that the repo's `gamebot-build-time-analyzers` lesson identifies as a known scaling hazard for the
Roslyn taint analyzers, and feature 086 established the precedent of touching it only with one-line
calls.

**Alternatives considered**:
- *Evaluate per sequence failure* — rejected: the spec (FR-007) and the issue both put the policy at
  cycle granularity, and per-sequence evaluation would fire on a single flaky firing.
- *A background sweeper polling every run's ledger* — rejected: introduces a second clock and a
  race against run teardown for no benefit; the run loop already has the exact moment.
- *Inside `QueueCycleLedger.Seal`* — rejected: `Seal` runs under the ledger's lock. Anything that
  can trip an action or start an HTTP request must not run under a lock the monitor also takes.

---

## R2 — Preserving "the ledger is a pure observer"

**Decision**: The ledger stays a pure observer. `CompleteOpen` gains **no** policy awareness.
Instead it returns nothing still, and the new `QueueFailurePolicyEvaluator` reads
`handle.Cycles.SnapshotHealth()` (an existing, lock-safe, copy-returning method) immediately after
`CompleteOpen` returns, and decides from the snapshot.

**Rationale**: Feature 086's spec and the class's own XML docs make purity a load-bearing property —
"removing it entirely would not change which sequences run". That claim stays literally true: the
ledger still has no policy knowledge and no scheduling decision reads it *from inside*. What changes
is that a **separate** component now reads the published snapshot and may act — which is exactly the
"deliberate and explicit" change the issue asked for. The ledger's class doc is updated to say so
rather than left claiming something that has quietly stopped being true (Constitution V).

**Alternatives considered**:
- *Have `CompleteOpen` return the new consecutive count* — rejected: it makes the ledger's mutator
  non-void, which is precisely the property 086 documented as the safety guarantee. Reading the
  existing snapshot costs one extra lock acquisition per cycle and keeps the guarantee intact.
- *Leave the 086 doc comment as-is* — rejected: it would become misleading documentation, which the
  constitution names as worse than none.

---

## R3 — Delivering the notification without blocking the run

**Decision**: `IFailureNotifier` is resolved from DI and called with a fire-and-forget pattern the
run loop does not await: the evaluator starts the delivery on the thread pool and returns
immediately. The notifier itself is fully guarded — a `try/catch` around everything, a 5-second
`HttpClient` timeout per attempt, at most 2 attempts with a 1-second backoff, and a
`CancellationToken` that is **not** the run's (so a stop does not cancel an in-flight alert about
why the run is stopping).

**Rationale**: FR-011 and SC-005 require that a black-hole endpoint cost the run nothing. Awaiting
even a bounded 10-second worst case inside the loop would delay every cycle of a failing queue.
Using the run's own token would be actively wrong for `notify_and_stop`, where the run cancels
itself milliseconds later — the notification about the stop would be cancelled by the stop.

**Alternatives considered**:
- *Await with a timeout* — rejected: still costs the run up to the timeout each episode, and gains
  nothing because the run cannot act on a delivery failure anyway.
- *A hosted background queue/channel of pending notifications* — rejected as over-engineering for an
  at-most-one-per-episode event; it would add a hosted service, a channel, and shutdown-drain
  semantics to send what is typically a single HTTP request per outage.

---

## R4 — `HttpClient` lifetime

**Decision**: Register `builder.Services.AddHttpClient<HttpFailureNotifier>(...)` in
`GameBotServiceSetup.ConfigureServices`, configuring `Timeout` on the typed client. This is the
first `AddHttpClient` in the service.

**Rationale**: `IHttpClientFactory` is the standard answer to socket exhaustion and stale DNS from
`new HttpClient()` per call, and the typed-client form keeps the timeout configuration next to the
consumer. No new package: `Microsoft.Extensions.Http` comes in via the ASP.NET Core shared
framework.

**Alternatives considered**:
- *A `static readonly HttpClient`* — rejected: does not pick up DNS changes and is untestable
  without a static seam; the factory form lets tests inject a stub handler.
- *`IHttpClientFactory` named client* — equivalent, but the typed client gives a compile-time home
  for the timeout and one fewer magic string.

---

## R5 — How `pause` is represented

**Decision**: Pause is **run state on `QueueRunHandle`**, not a third `QueueExecutionStatus`. The
handle gains a lock-guarded `PausedAt` / `PauseReason` pair (mirroring the existing `IdlePausedUntil`
pattern exactly) plus a `ManualResume` gate. The run loop checks the gate at the **top of the loop
iteration, before timer evaluation**, and awaits it there.

**Rationale**: Three reasons converge on this.
1. Adding `Paused` to `QueueExecutionStatus` is a breaking contract change for every existing
   consumer that treats the enum as two-valued — including the web UI, which the spec puts out of
   scope. A queue with a paused run is still *running* in the sense the status conveys: it holds its
   device, its session, and its run handle.
2. The handle already has the precedent: feature 073's idle pause is exactly this shape — transient,
   lock-guarded, surfaced through the monitor, never persisted.
3. Holding before timer evaluation is what makes clarification Q3's answer free: due-ness is
   computed *after* the gate, so on resume every still-due firing is simply due. No skip-list, no
   catch-up bookkeeping, no new code path (FR-018a).

**Alternatives considered**:
- *A third enum value* — rejected: breaks the persisted/JSON contract and the UI for a state that
  only ever exists in memory.
- *Pause implemented as stop-then-restart* — rejected: loses the device session, the run's schedule
  state, and every pending self-reschedule firing, so "resume" would not resume anything.
- *Checking the gate inside `RunOneSequenceAsync`* — rejected: pauses mid-cycle, leaving a cycle
  permanently open and the roster half-executed.

---

## R6 — Distinguishing a policy stop in the execution log

**Decision**: Add one value to `QueueStopReason`: `StoppedByFailurePolicy`. The terminating
execution-log record already carries the reason, so this is the whole change.

**Rationale**: FR-017 requires the stop be distinguishable from an operator stop, a completed run
and a connection failure — which is exactly the axis `QueueStopReason` already encodes. Reusing
`StoppedManually` would tell an operator a human halted production when nobody did. The enum is
serialized by name in the log, and adding a value is additive for readers.

**Alternatives considered**:
- *Reuse `Failure` with a distinguishing `failureReason` string* — rejected: forces consumers to
  parse prose to answer a categorical question, and `Failure` currently means the *run* could not
  proceed (no template, no device), which is not what happened here.

---

## R7 — Where the failure detail in the payload comes from

**Decision**: The tripping cycle's `QueueCycleRecord` already holds per-entry outcomes. The notifier
takes the first entry with `Succeeded == false` for `failedSequenceId`, its index for
`failedEntryIndex`, and reports `failedEntryCount` alongside. The human-readable sequence *name* and
the roster entry label are resolved at build time from the repositories — never stored in the ledger
— following the rule 086 set ("the ledger can never hold a stale name and never needs a repository").

**Rationale**: Satisfies FR-013 and A-006 with data already recorded. Resolving names outside the
ledger keeps the ledger repository-free.

**Alternatives considered**:
- *Capture the failure message from the sequence execution* — rejected for this feature: the ledger
  records a boolean per entry, not a message, and threading messages through would change 086's
  record shape. The payload instead names the failing sequence and points at the execution log,
  which holds the detail. Recorded as a known limitation in the plan.

---

## R8 — Notify step type (User Story 3) integration points

**Decision**: Add `ActionTypes.Notify = "notify"` and register it in **all** the places a
non-primitive action type must appear. Enumerated from tracing `ActionTypes.RescheduleSelf`, the
closest existing analogue (a non-device, service-level action):

| File | What must change |
|---|---|
| `src/GameBot.Domain/Actions/ActionTypes.cs` | the new constant |
| `src/GameBot.Domain/Services/ActionPayloadValidationService.cs` | add to the known-types set |
| `src/GameBot.Domain/Services/SequenceStepValidationService.cs` | payload shape validation |
| `src/GameBot.Domain/Commands/FileSequenceRepository.cs` | `ValidateActionPayloads` allow-list |
| `src/GameBot.Domain/Services/SequenceRunner.cs` | treat as a non-device step |
| `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` | dispatch and execute |

**Rationale**: The repo's own hard-won lesson (`sequence-action-type-allowlists`) is that missing
*either* `SequenceStepValidationService` **or** `FileSequenceRepository.ValidateActionPayloads`
produces a 500 at save time rather than a clean rejection. Tracing an existing analogue rather than
guessing found four more sites beyond those two. Every one of them gets an explicit task.

**Alternatives considered**:
- *Model notify as a `command`* — rejected: commands are device interactions; a notify step performs
  no device work and must run when the device is unreachable, which is the whole point.
- *Skip User Story 3* — rejected: the issue explicitly asks for it and the filing repository calls it
  the most valuable of the three shapes. It is sequenced last so it cannot destabilise US1/US2.

---

## R9 — Configuration shape

**Decision**: A new `FailureNotificationOptions` bound to section `Service:Notifications`, following
`DetectionOptions` exactly (a `const string SectionName`, plain settable properties with defaults,
registered with `builder.Services.Configure<T>(config.GetSection(T.SectionName))`).

```
Service:Notifications:DefaultUrl          (string?, default null)
Service:Notifications:AuthHeaderName      (string?, default null)
Service:Notifications:AuthHeaderValue     (string?, default null)
Service:Notifications:TimeoutSeconds      (int, default 5)
Service:Notifications:MaxAttempts         (int, default 2)
```

**Rationale**: Matches the established options pattern in this service, so it needs no new
infrastructure and is overridable by environment variable in the usual way. Note the test-harness
lesson (`gamebot-test-harness-gotchas`): `Service:Sessions:*` cannot be overridden from
`WebApplicationFactory`. Tests therefore drive the notifier through DI substitution rather than
through configuration, which is better isolation regardless.

**Alternatives considered**:
- *Per-queue URL only, no service default* — rejected: makes every queue repeat the same endpoint,
  and the issue's phrasing ("a configured url") implies one place to set it.
- *Storing the auth value in the queue JSON* — rejected: writes a secret into a repository file that
  the backup/restore endpoints copy around. Service configuration is the right home; a per-queue
  policy may override the **URL** only.

---

## R10 — Test strategy given the shared-execution-log hazard

**Decision**: Three layers.
- **Unit** — `QueueFailurePolicyEvaluator` (threshold, reset, trip-once-per-episode, re-arm, action
  selection) and `HttpFailureNotifier` (payload shape, auth header, timeout, retry count, total
  swallowing of faults) against a stub `HttpMessageHandler`. No host, no device.
- **Contract** — configuration validation (non-positive count, missing destination, malformed URL),
  the resume endpoint's not-found / not-running / not-paused responses, and the health block's new
  fields. Follows 086's discipline of asserting the not-running contracts **without starting live
  runs**, because `tests/contract` shares a bin data directory and polluting it 500s the
  ExecutionLogs tests (`gamebot-test-harness-gotchas`).
- **Integration** — a real run over a fake device: every cycle fails, the policy trips at N, the stub
  notifier receives exactly one event, the count resets on a success and re-arms; `stop` terminates
  with `StoppedByFailurePolicy`; `pause` parks the run and `resume` restarts it.

**Rationale**: Puts the assertions that need a live run where live runs are already safe, and keeps
the contract project free of run pollution.

---

## R11 — Divergence from the issue's proposed field names

**Decision**: The persisted/API shape is `failurePolicy: { consecutiveFailedCycles, action, notifyUrl }`,
**not** the issue's sketch `onConsecutiveFailures: { count: <n>, action: ... }`.

**Rationale**: Three reasons, in order of weight.
1. `count` is meaningless at the point of use. Read back off a queue's JSON a year from now,
   `consecutiveFailedCycles: 5` says what it counts; `count: 5` does not.
2. `onConsecutiveFailures` encodes the trigger in the *property* name, which leaves nowhere to put a
   second policy later without an awkward sibling key. `failurePolicy` names the concept and lets
   the trigger be a field inside it.
3. The object needs a third field the sketch did not anticipate (`notifyUrl`), so the shape was
   never going to be verbatim regardless.

**Why this is recorded rather than silently done**: feature 086 set the precedent of taking field
names verbatim from the requesting project's issue "so the requesting project's code matches", and
this feature breaks that precedent. The divergence is small and the names are published in
`contracts/queue-api.md`, but it is a real cost to the consumer and must be a visible decision.

**Alternatives considered**:
- *Use the issue's names verbatim* — rejected on (1) and (2); the naming would outlive the
  convenience by years.
- *Accept both spellings* — rejected: two names for one field is worse than either name alone, and
  the feature has no shipped consumers yet to be compatible with.

---

## Resolved: no remaining NEEDS CLARIFICATION

All five spec clarifications (auth header, delivery budget, pause/timer semantics, failure
recording, payload versioning) plus the ten items above are decided. Nothing is deferred to
implementation.
