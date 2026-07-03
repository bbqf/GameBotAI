# Contract: Break Step Outcome Vocabulary & Non-Influence

This feature adds **no HTTP endpoint** and no request/response shape change. The "contract" here
is the internal outcome vocabulary and the invariants that Domain, Service, and web-ui must agree
on and that tests assert against.

## Outcome tokens (carried in `actionOutcome`)

| Token | Meaning | Emitted when |
|-------|---------|--------------|
| `break` | The break fired (loop ended). A **success**. | Unconditional break; conditional break whose condition evaluated true; loop-level `breakOn` that evaluated true (block end-status `"true"`). |
| `no_break` | The break did not fire. A distinct, neutral **"No break"** — NOT a failure, NOT skipped. | Conditional break whose condition evaluated false; conditional break whose condition could not be evaluated (runtime error); loop-level `breakOn` treated as false (including guarded errors). |

The old `Skipped` status + `continue` outcome for a non-firing conditional break is **removed**
(FR-003). The old `Failed` + `result.Fail()` path for a break-condition evaluation error is
**removed** (FR-002a).

## Node-status mapping (`ExecutionLogService.MapStepStatus`)

| `actionOutcome` | Node status |
|-----------------|-------------|
| `break` | `success` |
| `no_break` | `no_break` (new, neutral) |

`ExecutionTreeNodeStatus` (web-ui) is extended with `'no_break'`, rendered as a distinct neutral
badge, visually separate from `failure` (red) and `skipped`.

## Invariants (MUST hold — asserted by tests)

1. A `no_break` outcome keeps `StepResult.Status = "Succeeded"` and never triggers
   `SequenceExecutionResult.Fail(...)`.
2. A run whose only non-success break outcomes are `no_break` has `FinalStatus = Succeeded`
   (success), and its loop/sequence nodes are not `failure`.
3. A `no_break` outcome does not early-stop the iteration, skip subsequent body steps, or skip
   remaining iterations (flow identical to today's condition-false path).
4. Both `break` and `no_break` outcomes retain the condition detail (`conditionType`,
   `conditionResult`, and a human-readable `message`).
5. A break-condition evaluation error — for the loop-body break step **and** the loop-level
   `breakOn` — is recorded as `no_break` and never throws out to fail the run.
6. `no_break` outcomes are excluded from failure counts / health summaries / failure alerts.

## Out of scope (explicitly unchanged)

- Break authoring (`BreakStepRow`, sequence editor) — no change.
- Break *firing* behavior (which iteration/loop ends when a break fires) — no change.
- Persisted execution-log entry format — no field added or removed; only the `actionOutcome`
  values carried for break steps change.
