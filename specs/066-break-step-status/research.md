# Phase 0 Research: Break Step Success/Failure Execution Statuses

All Technical Context items were resolvable from the existing codebase; there were no external
NEEDS CLARIFICATION unknowns. The decisions below record how the current system behaves and the
chosen approach for each requirement.

## Decision 1 — Canonical break outcome vocabulary: `break` / `no_break`

- **Decision**: Represent a break's own outcome with two tokens carried in the existing
  `StepResult.ActionOutcome` (and the persisted `actionOutcome` attribute): `break` when the
  break fires, `no_break` when it does not (whether the condition was false or could not be
  evaluated). `StepResult.Status` stays `Succeeded` for both (it is not a failure); the neutral
  "No break" appearance is driven by the outcome token, not by a `Failed`/`Skipped` status.
- **Rationale**: `ActionOutcome` already drives the node status (`MapStepStatus(step.Outcome)` in
  `ExecutionLogService`) and the flat detail-item messages in `SequenceExecutionService`. Using a
  single dedicated token keeps both break mechanisms consistent and avoids overloading `Skipped`
  (which means "step not run") or `Failed` (which means a real failure). Keeping `Status`
  `Succeeded` guarantees the sequence/run is not marked failed (FR-004/FR-005) since
  `SequenceExecutionResult.Status` only becomes `Failed` via an explicit `Fail()` call.
- **Alternatives considered**:
  - *Reuse `Skipped`/`continue` (today's behavior)*: rejected — the spec explicitly removes the
    `Skipped` representation (FR-003) and it conflates "did not break" with "did not run".
  - *Use `Status = "Failed"` for no-break (literal success/failure framing)*: rejected by the
    2026-07-01 clarification in favor of a distinct neutral badge; a `Failed` status would also
    risk influencing failure counts and tree rollups.

## Decision 2 — Condition-error is a non-influential `no_break` (behavior reversal)

- **Decision**: In `ExecuteLoopBodyAsync`, the `catch` around break-condition evaluation records a
  `no_break` outcome (retaining the error detail in the message per FR-007) and continues the
  loop, instead of calling `result.Fail()` and returning `earlyStop = true`.
- **Rationale**: FR-002a — an unevaluable condition is treated exactly like a false condition. The
  clarification chose "Treat error as 'no break'", so no break-step outcome may influence the run.
- **Current code**: `SequenceRunner.cs` lines ~994–1002 (the `catch` block) call `result.Fail(...)`
  and return `(true, false, stepsExecuted)`. This is the branch being reversed.
- **Alternatives considered**: keep failing the run on error (rejected by clarification);
  distinct error indicator that does not fail the run (rejected by clarification in favor of a
  single "No break" state — the error detail still lives in the message).

## Decision 3 — Loop-level `breakOn`: guard evaluation, keep block-level granularity

- **Decision**: In `ExecuteWhileBlockAsync`, wrap each `breakOn` evaluation (both the
  `breakOn-start` and `breakOn-mid` checks) so an exception is treated as `false` (no break). A
  `breakOn` that evaluates true continues to end the block with `Status = "true"` (= break fired /
  success). Do **not** emit a discrete per-evaluation "No break" record for each false `breakOn`
  check.
- **Rationale**: FR-010 requires the success / no-break semantics and non-influence guarantees to
  apply to the loop-level break condition, and FR-002a's error handling to apply there too. Today
  `breakOn` is evaluated without a `try/catch` (lines ~1254 and ~1294), so a broken condition
  throws out of the block and fails the run — guarding it implements the error requirement. The
  spec's edge case explicitly leaves per-evaluation granularity to planning and only forbids
  influence on run status; a `breakOn` that stays false already just continues (it never sets
  `Failed`), so no extra record is needed and per-check "No break" rows would be log spam.
- **Alternatives considered**: emit a `no_break` detail item on every false `breakOn` check
  (rejected — noisy, and the block result already conveys the end reason); leave `breakOn`
  unguarded (rejected — violates FR-010's error handling).

## Decision 4 — Node-status mapping: add `success` for `break`, new neutral `no_break`

- **Decision**: Extend `ExecutionLogService.MapStepStatus` so `"break" → "success"` and
  `"no_break" → "no_break"` (a new neutral node status). Add `no_break` to the web-ui
  `ExecutionTreeNodeStatus` union and render it as a distinct neutral badge (not the `failure`
  styling, not `skipped`).
- **Rationale**: The tree/grid node status is derived from the outcome token via `MapStepStatus`.
  `"break"` is currently unmapped and falls through to `"failure"`, so even a *fired* break is
  mis-colored today — this fixes it. `no_break` needs its own bucket to render the distinct
  neutral indicator required by the clarification.
- **Alternatives considered**: map `no_break → skipped` and reuse the existing skipped styling
  (rejected — the clarification asked for a *distinct* "No break" indicator, and "skipped" carries
  the wrong meaning); map `break → true`-style success only at block level (insufficient for the
  discrete break step which renders as a step node).

## Decision 5 — Failure counts / health summaries (FR-008)

- **Decision**: No dedicated exclusion logic is required. Because `no_break` outcomes keep
  `StepResult.Status = "Succeeded"`, map to the neutral `no_break` node status (not `failure`),
  and never trigger `result.Fail()`, they are inherently excluded from any run-level failure
  count/alert that keys on a `Failed` status or `failure` node status.
- **Rationale**: Verified that the run's `FinalStatus` is the persisted
  `SequenceExecutionResult.Status` (not a child rollup), and metrics/health key on failure
  status — none of which `no_break` sets.
- **Alternatives considered**: add explicit filtering (rejected as unnecessary given the status
  values chosen).

## Testing approach

- **Backend**: extend `SequenceRunnerLoopTests` (fired/false/error/unconditional/nested) and add
  `breakOn` guard coverage; the existing `CountLoopBreakConditionThrowsLoopFails` is rewritten to
  assert continue + `Succeeded`. Add a focused `MapStepStatus` unit test for `break`/`no_break`.
  Fake evaluators drive true/false/throw deterministically (no ADB, <1s).
- **web-ui**: Jest tests assert the grid renders a distinct "No break" badge for a `no_break`
  node and that a run whose only "failures" are breaks renders as non-failed. Gate on
  `vite build` + `jest`.
