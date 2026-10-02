# Research: Step-Through Sequence Execution

All unknowns from the Technical Context are resolved below.

## R1: How to run one step at a time

**Decision**: Add a `SequenceStepper` in `GameBot.Domain`. It keeps an explicit cursor over the step tree.
It runs one leaf step per call. It reuses the leaf code of `SequenceRunner` through one new internal entry point.

**Rationale**:
- `SequenceRunner` runs loops and branches in nested private methods. The method call stack holds the position.
  A call stack cannot pause at a step, and it cannot jump to a chosen step (FR-007).
- A cursor with path addresses allows both the automatic order (FR-006) and the manual choice (FR-007).
- The leaf code (guard, delay, gate, wait-for-image, action dispatch, command dispatch) stays in one place.
  The stepper does not copy it.

**Alternatives considered**:
- *Gate inside the real runner*: the runner waits at each leaf for a permit. This is exact for the automatic order.
  It cannot jump to a chosen step or repeat a step, because the runner cannot move back. Rejected.
- *Copy the runner logic into a second class*: it gives drift between real runs and step-through runs. Rejected.
- *Rewrite the runner as a state machine for both uses*: it has the best result, but the risk to real runs is high.
  The runner has 2,500 lines and many features. Rejected for this feature.

**Drift control**: A parity test runs the real runner (dry run) and the stepper on the same fixtures.
It compares the order of visited steps (SC-002). The test fails when the two paths differ.

## R2: Step addressing and nesting

**Decision**: Each step node has a `path` string. A path is the list of order indexes from the root,
with a branch marker for an if step. Examples: `2` (third top-level step), `1/body/0`, `3/else/1`.
The service returns a flat list of nodes with `path`, `depth`, `type`, and `label`.

**Rationale**: `StepId` can be empty on a step (the runner makes keys such as `loop@3`).
A path is always unique and stable for one saved version. The flat list with `depth` gives the nested view (FR-004).

**Alternatives considered**: Use `StepId` only. Rejected: it is not always set.

## R3: Loop and branch state in the cursor

**Decision**: The cursor holds a stack of frames. A frame is one open loop or one open if branch.
A loop frame holds the iteration number and the loop kind. The stepper evaluates the loop condition,
the count, `maxIterations`, and the break rules with the same helper methods as the runner.
These helpers (`EvaluateLoopConditionAsync`, `ResolveCondition`, `EvaluateStepGuardAsync`, `DescribeBreakCondition`)
become `internal` and shared. A manual jump into a loop body opens a frame at iteration 1 (FR-007).

**Rationale**: FR-004a says each action in a loop is a step, and each iteration has its own history entry.

**Alternatives considered**: Execute a whole loop as one step. Rejected by the clarification in the spec.

## R4: Effects that must not apply (FR-015, FR-015a)

**Decision**: Add a `PreviewEffects` flag. It is different from the existing `dryRun` flag of feature 082.
`dryRun` skips all device input. `PreviewEffects` skips only actions with outside effects:
`reschedule-self` and `notify`. All device actions still run.
- Top-level steps: the stepper does not call the action dispatcher for these two types.
  It writes the intended effect in the history entry. The reschedule text is "would reschedule at HH:mm".
  The coordinator has a pure "compute fire time" part. The stepper uses it without a write.
- Command steps: `ICommandExecutor.ForceExecuteDetailedAsync` gets an optional `ExecutionOptions` argument
  with `PreviewEffects`. `CommandExecutor` checks it before it calls the coordinator or the notifier.
  The result carries a list of previewed effects.

**Rationale**: The user wants to see the real effect of the steps on the emulator, so device actions must run.

**Alternatives considered**: Reuse `dryRun`. Rejected: it does not touch the emulator, so it has no use here.

**Risk**: A new sequence action type needs a decision here. The existing guide in memory says a new action type
touches six backend sites. A contract test lists every action type and states if it is a preview type.

## R5: `lastRun` condition (Clarification)

**Decision**: `PushRunContext` returns no context when `originatingQueueId` is empty. The stepper passes no queue id.
The existing rule then gives `false`. The stepper adds the text "lastRun is not evaluated" to the history entry
when a step condition has a `lastRun` leaf. No new evaluator code is needed.

## R6: Same-session protection and queue pause (FR-012, FR-012a-c)

**Decision**: Reuse the policy-pause gate of `QueueRunHandle` (`EnterPolicyPause`, `ResumeFromPolicyPause`).
The step-through calls `EnterPolicyPause("step-through", now)` on explicit author request. It resumes at the end.
The service finds the owning queue through `IQueueRunRegistry` by the device serial of the session (feature 079).
A new `IStepThroughSessionGuard` returns one of: free, queue-running (with queue name), queue-paused-by-us.

**Rationale**: The pause gate exists, has tests, and has an operator "resume" path. A pause stops the next firing.
A firing that already runs is not cut. FR-012c covers this: the step is refused until the run ends.

**Alternatives considered**: A new device lock. Rejected: more state, and two lock systems can deadlock.

**Open point handled**: If the queue was already paused (policy or idle), the service does not take the pause
and does not resume it (FR-012b). The step-through stores `pausedByUs` as a flag.

## R7: Lease and lost view (Clarification)

**Decision**: A step-through session has a lease of 90 seconds. Each `GET` of the state renews it.
A hosted `BackgroundService` sweeps every 10 seconds. For an expired lease it cancels the running step,
resumes the queue if `pausedByUs`, and removes the session. The view polls the state every 2 seconds.
`DELETE` ends the session at once. The browser sends `DELETE` with a `keepalive` request on page close.
`navigator.sendBeacon` cannot be used: it sends only `POST` and it cannot send the token header.

**Alternatives considered**: WebSocket or SSE. Rejected: the app uses polling for queue monitors,
and polling gives the lease for free.

## R8: Run a step without blocking the HTTP call

**Decision**: `POST .../run-next` returns `202 Accepted` at once. The step runs in a background task
with its own `CancellationTokenSource`. The view polls the state and sees `running`, then the result.
`POST .../cancel` cancels the token.

**Rationale**: A step can take minutes (wait-for-image, hero duel). SC-003 needs the status within 1 second after the end.
A poll interval of 500 ms during a step gives this.

## R9: Execution log marking (FR-016)

**Decision**: Add `ExecutionLogContext.Origin = "step-through"` (nullable string, new field).
The log entry mapper copies it to the DTO field `origin`. The execution log UI shows a badge.
Each step run writes one sequence-level entry with a child entry for the step, so the existing log views work.

**Alternatives considered**: A tag in the summary text. Rejected: not filterable.

## R10: Saved-only rule and version check (FR-002, FR-013)

**Decision**:
- UI: the entry is disabled when the form is dirty or the sequence has no id.
  The existing form already tracks the saved state. The reason text is shown on the button.
- Service: `POST /api/step-through` reads the stored sequence and records a `version` hash
  (SHA-256 of the stored JSON). Each `run-next` and `select` compares the hash with the stored sequence.
  A mismatch gives `409` with code `sequence_changed`.
- The UI cannot send unsaved content, so there is no server rule for "dirty".

## R11: Sequence kinds in scope

**Decision**: The stepper supports the step-list form (`Steps`). A legacy flow-graph sequence
(`EntryStepId` and `FlowSteps`) and `Blocks` return `400 unsupported_sequence_kind` with a clear message.

**Rationale**: The authoring UI creates the step-list form. The legacy flow graph runs a different engine path.

## R12: Parameters and variables (FR-017)

**Decision**: The session holds a `ParameterScope` layer (author values) and a `stepOutcomes` dictionary.
`PUT .../values` replaces author parameter values and manual outcome overrides. The history and the cursor stay.
A start at a middle step leaves skipped outcomes absent. A condition that reads an absent outcome uses
the existing "unavailable" failure path of the real run (FR-013a).

## R13: Time limit (FR-005a)

**Decision**: The stepper does not push `SequenceTimeLimitScope`. Step timeouts (`TimeoutMs`, wait-for-image,
gate) stay inside the leaf code. Between steps there is no timer.

## R14: Performance goals (Constitution IV)

**Decision**: Goals for the plan:
- State read: p95 under 50 ms (in memory only).
- Status visible within 1 second after a step ends (SC-003) with a 500 ms poll.
- Memory: history is capped at 1,000 entries for each session. The oldest entry drops first.
- Sessions: at most one active step-through for each game session. A start for a second one gives `409`.
