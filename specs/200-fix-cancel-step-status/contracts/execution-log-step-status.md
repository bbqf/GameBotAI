# Contract: status of a `reschedule-self` step node in the execution log

Endpoint: `GET /api/execution-logs/{id}/subtree` (no change in shape).

A step node of a `reschedule-self` step has these fields (no new field):

```json
{
  "nodeKind": "step",
  "label": "<step label>",
  "status": "success | failure | skipped",
  "message": "Self-reschedule '<step label>' <outcome>: <message>"
}
```

Rules for `status`:

1. Outcome `scheduled`: `success`.
2. Outcome `cancelled` (option `Cancel`, bookings removed): `success`.
3. Outcome `noop` and option `Cancel` (nothing to remove): `success`.
4. Outcome `noop` and another option, or no known option: `skipped`.
5. Outcome `failed` (option `Cancel`, an error occurred during the cancel): `failure`.

These rules are only for `reschedule-self` step nodes. A step node of another type with the outcome `cancelled` (a primitive or WaitForImage step that a stop cut off) keeps the status `failure`.

The rules apply to each log that the endpoint reads, also to logs that the service stored before this feature.

The `actionOutcome` and `reasonCode` attributes of the stored step detail keep the outcome (`cancelled`, `noop`, `failed`). The `message` text does not change for rules 1 to 4.

Example (rule 2, from issue #414):

```json
{
  "nodeKind": "step",
  "label": "PD-3: the work is done, so the retry of step 0 is removed. No condition.",
  "status": "success",
  "message": "Self-reschedule 'PD-3: the work is done, so the retry of step 0 is removed. No condition.' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only"
}
```
