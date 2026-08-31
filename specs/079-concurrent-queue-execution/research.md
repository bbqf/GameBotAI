# Phase 0 Research: Concurrent Queue Execution

All decisions below were reached by reading the current implementation. Each records what the code
does today, what was chosen, why, and what was rejected.

---

## R1 - Why concurrent runs interfere today

**Finding (evidence in code):**

| # | Defect | Where |
|---|---|---|
| 1 | `GetLatestScreenshot()` returns the frame of the **first** running session found by enumerating a `ConcurrentDictionary`, regardless of the caller | `src/GameBot.Emulator/Session/BackgroundCaptureScreenSource.cs` |
| 2 | Five device-resolution sites hard-fail unless **exactly one** session is running | `SequenceExecutionService.DispatchEnsureGameRunningAsync` / `DispatchGoToHomeScreenAsync` / `DispatchPrimitiveInputAsync`; `CommandExecutor.ForceExecuteStepAsync` / `ResolveSessionIdAsync` |
| 3 | Nothing prevents two queues bound to the same serial from running at once -- explicitly allowed by 051 FR-013 | `QueueExecutionService.StartAsync`, `IQueueExecutionService` doc comment |
| 4 | `MaxConcurrentSessions` defaults to 3 and the overflow throws a bare `InvalidOperationException("capacity_exceeded")` | `SessionOptions.cs`, `SessionManager.CreateSession` |
| 5 | `PickSession` chooses an arbitrary running session for screen capture / crop | `EmulatorImageEndpoints.PickSession` |

Defect 1 is the most damaging because it is silent: a run keeps executing, but its image and OCR
checks are evaluated against another emulator's screen, so it taps the right coordinates on the wrong
game. Defect 2 is the loudest: it turns the *first* queue's steps into hard failures the moment a
second queue starts. Both must be fixed for concurrency to be usable; neither alone is sufficient.

**Decision**: Treat all five as in scope; they are one bug expressed five ways ("who is *my* device?").

**Alternatives considered**: Fixing only the screen source. Rejected -- defect 2 still breaks every
primitive tap in a concurrent run, so the feature would not work.

---

## R2 - How a step learns which device it belongs to

**Decision**: Two complementary mechanisms.

- **Explicit (primary)**: a new `IScreenSourceFactory.ForSession(string sessionId)` returning an
  `IScreenSource` permanently bound to that session. Used by every call site that already has a
  session id in hand: `CommandExecutor`'s detect-and-tap paths and `GameReadinessProbe`.
- **Ambient (fallback)**: a new `IDeviceContextAccessor` backed by `AsyncLocal<DeviceContext?>`.
  `QueueExecutionService` pushes its run's context around each sequence firing;
  `SequenceExecutionService` pushes it around a sequence executed with an explicit session. The
  singleton `IScreenSource` registration consults the accessor first, then falls back to the
  single-running-session rule, then returns null.

**Rationale**: The trigger-based image/text condition path reaches the screen through
`TriggerEvaluationService -> ITriggerEvaluator -> IScreenSource`. `ITriggerEvaluator.EvaluateAsync`
has no session parameter and is also driven by the standalone `TriggerBackgroundWorker`, so threading a
session id through it would change a shared interface and every implementation and test for the sake of
one caller. `AsyncLocal` flows through `await` and through the `Task.Run` that launches a queue run
(ExecutionContext is captured), so the context is correct for the whole run without touching those
signatures.

**Alternatives considered**:
- *Ambient only.* Simpler, but leaves the paths that already know their session depending on hidden
  state, which is harder to test and to reason about.
- *Explicit only, threading `sessionId` through `ITriggerEvaluator`.* Rejected: a wide breaking change
  to a shared abstraction, plus every evaluator and condition adapter, to serve one code path.
- *Scoped DI container per run.* Rejected: the whole execution stack is registered as singletons; making
  it scoped would ripple far beyond this feature and risk changing lifetime semantics elsewhere.

---

## R3 - Behavior when no session context is available

**Decision**: Preserve the "exactly one running session" convenience, but only as a fallback, and make
the multi-session case an explicit, actionable failure rather than a silent pick or a bare error:

- explicit session supplied -> use it, regardless of how many sessions exist (removes the current
  `Count != 1` failure);
- no session supplied, exactly 1 active -> use it (today's behavior, unchanged);
- no session supplied, 0 active -> today's "no session available; start a session" message;
- no session supplied, N > 1 active -> fail with `"<N> device sessions are active; specify a
  sessionId for '<step>'"`.

**Rationale**: Matches the clarification, keeps every existing single-emulator workflow working, and
turns the ambiguous case into a message that names the fix.

**Alternatives considered**: Picking the most recently used session. Rejected -- it is still a guess,
and a wrong guess taps the wrong game.

---

## R4 - Device claim mechanism

**Decision**: `IDeviceClaimRegistry`, a singleton over
`ConcurrentDictionary<string, DeviceClaim>` keyed by the trimmed, `OrdinalIgnoreCase`-normalized
emulator serial. `TryClaim(serial, queueId, queueName)` uses `TryAdd`, which is atomic, so two
simultaneous starts cannot both succeed (FR-012). `Release(serial, queueId)` removes only when the
claim still belongs to that queue, so a late release from a finished run cannot steal a claim a newer
run has taken.

The claim is taken in `QueueExecutionService.StartAsync` **after** `QueueRunRegistry.TryAdd` succeeds
and **before** the background run is launched. It is released in `RunAsync`'s existing `finally`
block, which already runs on completion, manual stop, failure, cancellation and host shutdown
(FR-011). If the claim fails, the queue is removed from the run registry, the CTS disposed and
`QueueStartOutcome.DeviceInUse` returned -- so nothing about the refused queue changes.

**Rationale**: Mirrors the existing `QueueRunRegistry` exactly, so the code is idiomatic for this
codebase, needs no locks and cannot deadlock. In-memory only, satisfying FR-013 for free.

**Alternatives considered**:
- *Deriving the claim from the run registry* (scan running handles for a matching serial). Rejected:
  the scan is not atomic against a concurrent start, so two starts could both pass the check.
- *A named OS mutex per serial.* Rejected: cross-process coordination is not needed (one service
  process) and mutex abandonment handling is a new failure mode.
- *Claiming inside `RunAsync`.* Rejected: the start call would return `Started` and the queue would go
  Running before the refusal was known, so the operator would see a phantom run.

---

## R5 - Blank and unresolvable serials

**Decision**: A queue whose `EmulatorSerial` is blank claims nothing (no key), and its run proceeds to
the existing session-creation failure path. A queue whose serial is non-blank but unknown to ADB does
claim the serial, then fails at `CreateSession` and releases the claim in the same `finally`.

**Rationale**: A blank serial has no device identity to reserve, and blocking every other blank-serial
queue behind one shared empty key would be a new, surprising restriction. A non-blank unknown serial is
still an intended device identity, and claiming it briefly costs nothing because the run fails within a
second.

---

## R6 - Session capacity

**Decision**: Raise `SessionOptions.MaxConcurrentSessions` from 3 to 8 and change
`SessionManager.CreateSession`'s capacity exception message from the bare token `capacity_exceeded`
to `"session capacity reached: 8 of 8 sessions are open (Service:Sessions:MaxConcurrentSessions)"`.
The exception type and the existing `capacity_exceeded` API mapping are kept so no caller breaks; the
message is what the queue run records in its failure log.

**Rationale**: 8 covers any realistic single-machine LDPlayer setup and keeps the limit as a guard
rail rather than the practical ceiling (FR-015, SC-006). The existing code already catches
`InvalidOperationException` here and folds `ex.Message` into the run's failure reason, so the improved
message reaches the execution log with no other change.

**Alternatives considered**: Removing the limit. Rejected -- it is a useful guard against runaway
session creation, and the constitution favors declared budgets.

---

## R7 - Operator image tooling

**Decision**: `EmulatorImageEndpoints` gains an optional `sessionId` (and `serial`) selector. Resolution
order: explicit selector -> the single running session -> ambiguity error
`{"error":"ambiguous_session","message":"N device sessions are active; specify sessionId or serial"}`
with HTTP 409. `PickSession`'s `FirstOrDefault` chain is deleted.

**Rationale**: FR-022..FR-024. This is the only intentionally *breaking* behavior change, and only in
the multi-session case that previously returned an arbitrary device -- an outcome that silently
produced wrong reference images, so it has no behavior worth preserving.

---

## R8 - Concurrency safety of existing run state

**Finding**: `QueueRunHandle` already guards its mutable state (`_currentLock`, `_idleLock`,
`_timerLock`, concurrent collections) and `QueueRunSchedule` guards its registers with `_gate`; both
were built for the monitor-reads-while-run-writes case. `QueueRuntimeStore` locks per queue state
object. `FileExecutionLogRepository` serializes writes behind a `SemaphoreSlim`.

**Decision**: No changes required for FR-019/FR-020/FR-021; add regression tests that exercise two runs
concurrently rather than new synchronization. `ImageCaptureMetrics` and `TriggerEvaluationMetrics` are
process-wide counters by design and stay as they are (they are diagnostics, not run state).

**Rationale**: Adding locks where the code is already correct increases contention and risk with no
benefit. The gap was never the per-run state; it was the shared *device* resolution.
