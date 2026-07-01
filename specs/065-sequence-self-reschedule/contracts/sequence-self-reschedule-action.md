# Contract: Sequence Self-Reschedule Action

This feature adds **no new HTTP endpoint**. The action is an opaque payload carried on the existing
sequence authoring endpoints, and the run-side scheduling is an in-process call. This document pins
(a) the persisted payload schema that must round-trip through the authoring API and (b) the per-option
firing semantics the execution engine must honor.

---

## A. Persisted payload (round-trips through `POST/GET/PUT /api/sequences`)

A self-reschedule step is an existing `SequenceStep` with `stepType: "Action"` whose `action` is:

```jsonc
{
  "type": "reschedule-self",
  "schemaVersion": "1",
  "parameters": {
    "option": "Timer",                  // required: AtQueueStart | OncePerRun | Timer | EveryStep
    "timerTimeOfDay": "14:30:00",       // HH:mm:ss; only when option == Timer (time-of-day mode)
    "timerRelativeOffset": "00:10:00"   // HH:mm:ss; only when option == Timer (relative mode)
  }
}
```

### Round-trip requirements (FR-001a, SC-001)
- Creating/updating a sequence containing this action and reading it back MUST return the identical
  `type` and `parameters` (option + the one timer field, if any).
- The action MUST be placeable inside the sequence's IF/conditional branches and survive the
  round-trip in that position (FR-002).
- No queue template or saved configuration is touched by authoring or running the action (FR-010,
  SC-005).

### Validation (`ActionPayloadValidationService`) — reuses the `{ error: { code, message, hint } }` envelope

| Case | Result |
|------|--------|
| `option` missing or not a known value | rejected — `invalid_request` |
| `option == Timer` with **neither** timer field | rejected (a timer needs a target) |
| `option == Timer` with **both** timer fields | rejected (mutually exclusive) |
| `option != Timer` with any timer field present | rejected (malformed) |
| `timerRelativeOffset` negative or out of range | rejected (same bound as feature 059) |
| `type == "reschedule-self"` (well-formed) | accepted — recognized action type |

---

## B. Run-time firing semantics (in-process)

Given a running sequence whose `ExecutionLogContext.OriginatingQueueId` is **non-empty**, executing
the action schedules **exactly one** additional firing of that same sequence into the current run:

| `option` | Firing semantics (MUST) | Spec |
|----------|-------------------------|------|
| `OncePerRun` | Appended after the remaining once-per-run steps of the **current cycle**; runs again before that cycle ends | FR-007 |
| `Timer` (relative) | `fireAt = now + offset`; fires once at the first iteration boundary at/after `fireAt` | FR-005 |
| `Timer` (time-of-day) | `fireAt = today@time` (if already past ⇒ next boundary); fires once at the first boundary at/after it | FR-006 |
| `EveryStep` | Registered to fire after each subsequent normal step for the rest of the run; **loop-safe**, idempotent per sequence (no unbounded self-chain) | FR-008 |
| `AtQueueStart` (cycling) | Fires at the start of the next cycle | FR-009 |
| `AtQueueStart` (non-cycling) | Falls back to firing at the next iteration boundary | FR-009 |

### Cross-cutting MUSTs
- The reschedule applies to the **current run only** and is never persisted (FR-010).
- When `OriginatingQueueId` is empty (standalone/non-queue run): the action performs **no scheduling**
  and reports **success** (no-op); the sequence continues normally (FR-011, FR-012).
- The action does not, by itself, terminate the sequence — subsequent steps still run (FR-012).
- A reschedule accepted but never due before the run ends does **not** fail the run; the log shows it
  did not fire (FR-015).
- Rescheduled firings count toward the run's executed-sequence total like other live/scheduled
  firings; run termination remains governed by once-per-run steps (FR-016).
- A pending firing not yet fired is abandoned when the run is stopped/aborted (FR-017).
- Origin propagates through nesting: a child sequence of a queue-driven run treats that run as its
  originating queue run and reschedules the child into it (FR-018).

### Execution-log MUSTs (FR-013, FR-014)
- The action entry records: `option`, resolved timing (target instant for timers; "next cycle"/"this
  cycle" otherwise), and `outcome` = `scheduled` | `noop` (with `noopReason` when no-op).
- The resulting firing appears as an executed sequence under the run, tagged as originating from the
  self-reschedule and attributable to the originating sequence.
