# Data Model: Give a reschedule-self Cancel step the status success when the cancel runs (B-046)

The feature adds no entity and changes no stored format. It changes the rule that calculates the `status` of a step node of the execution log.

## Step node of a `reschedule-self` step (read time)

Sources:

- `ExecutionStepOutcome.Outcome` (stored): the action outcome (`scheduled`, `cancelled`, `noop`, `failed`).
- `ExecutionStepOutcome.StepType` (stored): `reschedule-self`.
- The detail item of the entry with `Kind` `step` and the same `stepOrder` (stored): attributes `option` (`Timer`, `OncePerRun`, `AtQueueStart`, `Cancel`, ...) and, for `Cancel`, `removed`.

Status rule (new rows in bold):

| Outcome | Option of the detail item | Status |
|---------|---------------------------|--------|
| `scheduled` | any | `success` (no change) |
| **`cancelled`** | any | **`success`** (was `failure`). Only for a `reschedule-self` step node; a step node of another type with `cancelled` keeps `failure`. |
| **`noop`** | **`Cancel`** | **`success`** (was `skipped`) |
| `noop` | not `Cancel`, or no detail item, or no `option` | `skipped` (no change) |
| **`failed`** (new outcome, only for an error during the cancel) | `Cancel` | **`failure`** |

## Dispatch result of a `Cancel` step

| Case | `Outcome` | `Message` (no change except the new row) | `Removed` |
|------|-----------|-------------------------------------------|-----------|
| Bookings removed | `cancelled` | "removed N pending booking(s) of this sequence; applies to the current run only" | `true` |
| No pending booking | `noop` | "no pending booking of this sequence; nothing to cancel" | `false` |
| Queue run not active | `noop` | "originating queue run no longer active; nothing to cancel" | `false` |
| No originating queue | `noop` | "no originating queue, no reschedule performed" | `false` |
| **Error in `CancelSelf`** | **`failed`** | **"cancel not performed: <exception message>"** | **`false`** |

The runner records each row as a `Succeeded` step, and `stepOutcomes[stepKey]` = `success`. The sequence continues.

The log message of the step node is "Self-reschedule '<label>' <outcome>: <message>" (no change in format).
