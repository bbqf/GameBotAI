# Contract: `lastRun` step condition

**Feature**: 105-sequence-run-statistics | **Requirements**: FR-007 to FR-012, FR-013, FR-015

## Shape

```json
{ "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00" }
{ "type": "lastRun", "sequence": "seq-daily-train", "status": "failure", "within": "24:00:00", "negate": true }
```

| Field | Type | Required | Values |
|---|---|---|---|
| `type` | string | yes | `lastRun` |
| `sequence` | string | yes | `self`, or a sequence ID. The save path does not check that the ID exists. |
| `status` | string | yes | `success`, `failure`, `cancelled` (case-insensitive) |
| `since` | string | exactly one of `since`/`within` | `HH:mm`, pattern `^([01]\d\|2[0-3]):[0-5]\d$` |
| `within` | string | exactly one of `since`/`within` | `[d.]hh:mm:ss`, pattern `^(?:\d{1,3}\.)?\d{1,4}:[0-5]\d:[0-5]\d$`, more than zero, not more than `366.00:00:00`. Hours may be 24 or more: `24:00:00` is 24 hours. |
| `negate` | boolean | no | default `false` |

## Where it is permitted

Every slot that accepts a step condition today (FR-010):

- `steps[].condition` (step guard)
- `steps[].breakCondition` (Break step)
- `steps[].if.condition` (If step)
- `steps[].loop.condition` (`while` and `repeatUntil` loops)
- A Break condition inside a loop body or an If branch
- A child of `all`, `any` or `none` at any depth. The composite limits (16 children, depth 4) apply without change.

## Result of the condition

The condition is `true` when the named sequence has at least one completed run **in the current queue** with the given status. The end time of that run must also be in the window.

- `since: "HH:mm"`: the window starts at the most recent occurrence of that service-local time of day, at or before now. On a daylight-saving day, it is the most recent real occurrence of that wall-clock time.
- `within: "d.hh:mm:ss"`: the window starts at now minus the duration.
- The window ends now. Both ends are inclusive.
- `sequence: "self"` names the sequence that contains the step. The current run is not complete, so it is not in the statistics.
- At this time, no step type runs another sequence, so a nested run is not possible. If a later feature adds nested runs, `self` names the nested sequence. The queue does not record the nested run. It records only the outer run that it started.
- Only the 100 most recent runs of each (queue, sequence) pair are kept.

The condition is `false`, and the run does not fail, when:

- the run has no queue (an ad-hoc run from `POST /api/sequences/{id}/execute`, or a dry-run),
- the named sequence has no recorded run in this queue.

`negate: true` and the `none` composite invert the result, the same as for other condition types.

## Save-time validation (create, update, PATCH, `dryRun`)

A bad condition gets HTTP 400. The error message starts with `Step '<stepId or label>' condition at <path>: ` and ends with one of these texts:

| Input | Message tail |
|---|---|
| `sequence` absent, empty or blank | `lastRun condition requires sequence ('self' or a sequence id).` |
| `status` absent or not one of the three values | `lastRun status must be one of success\|failure\|cancelled.` |
| both `since` and `within` | `lastRun condition accepts only one of since or within, not both.` |
| neither `since` nor `within` | `lastRun condition requires one of since or within.` |
| `since` not `HH:mm` (for example `9:00`, `24:00`, `11:00:00`) | `lastRun since must be a time of day in HH:mm format (00:00 to 23:59).` |
| `within` zero, negative, malformed, or more than 366 days | `lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format.` |

`<path>` is `$` for a condition directly in a slot, and `$.children[i]...` inside a composite. No bad `lastRun` condition gets a 500 (SC-003).

## Read-back

`GET /api/sequences/{id}` returns the condition with the same field text that the author wrote. `since` or `within` is left out when it is not set.

## Execution log

A step with a `lastRun` guard reports `conditionType: "lastRun"`. The text form of the condition is:

```text
lastRun(sequence=self, status=success, since=11:00)
NOT lastRun(sequence=seq-daily-train, status=failure, within=24:00:00)
```

## OpenAPI

- Schema `LastRunCondition` (alias of `LastRunConditionContract`), listed in the `SequenceStepCondition` discriminator with the value `lastRun`.
- Each field has a description. `status` has an `enum`. `since` and `within` have a `pattern`.
- The schema description states the "exactly one of `since` or `within`" rule and the "no queue means false" rule.
