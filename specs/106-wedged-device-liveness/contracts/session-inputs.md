# Contract: `POST /api/sessions/{id}/inputs` (FR-012, FR-013)

The request does not change. The service sends each action with the limit `InputTimeoutMs`. The ADB retries of one action share this limit.

## Order of the rules

Rule 1 comes before the dispatch. The service sends nothing in that case. Rules 2 to 6 come after the dispatch.

| # | Condition | Status | `error.code` |
|---|---|---|---|
| 1 | No actions in the request | 400 | `invalid_request` (no change) |
| 2 | The session was not found | 409 | `not_running` (no change) |
| 3 | One action timed out | 504 | `device_timeout` (new) |
| 4 | After the dispatch, the session liveness is `not_live`, and at least one action was dispatched | 503 | `device_not_live` (new) |
| 5 | No action was dispatched | 400 | `invalid_input_actions` (no change) |
| 6 | Otherwise | 202 | none (no change) |

Rule 4 uses the data-only report (no probe). Thus a live device gets no extra delay.

## 504: an action timed out

The service kills the `adb` process of that action. It does not send the actions after it. The response time is at most `InputTimeoutMs` plus 2 s (SC-003).

```json
{
  "error": {
    "code": "device_timeout",
    "message": "The device did not answer action 1 in 10000 ms. The actions after it were not sent.",
    "hint": "Get GET /api/sessions/{id}/health to see the device liveness."
  },
  "results": [
    { "index": 0, "dispatched": true, "failureReason": null },
    { "index": 1, "dispatched": false, "failureReason": "tap: device did not answer in 10000 ms" }
  ]
}
```

The results array has one item for each action that the service tried. Actions that were not sent have no item.

The 504 rule has precedence over the 503 rule (spec FR-013). Thus the actions before the timed-out action keep their `dispatched` value, also when the device is not live. The timed-out action is `dispatched: false`. The service does not send the actions after it.

## 503: the device is not live

The service sent the inputs. It reports them as not dispatched, because it knows that the device is not live.

```json
{
  "error": {
    "code": "device_not_live",
    "reason": "no_change_after_input",
    "message": "The device is not live (no_change_after_input). The inputs were sent, but the device does not apply them.",
    "hint": "Get GET /api/sessions/{id}/health for details. Restart the emulator if the fault stays."
  },
  "results": [
    { "index": 0, "dispatched": false, "failureReason": "device_not_live: no_change_after_input" }
  ]
}
```

`error.reason` is one of `capture_stalled`, `input_timeout`, `no_change_after_input`, `transport_not_ready`.

## 202 (no change)

```json
{ "accepted": 1, "results": [ { "index": 0, "dispatched": true, "failureReason": null } ] }
```

## Sequence input (no API change)

Sequence and command inputs go through `ISessionManager.SendInputsAsync`. They get no new time limit and no new result. They only update the last-input data of the session (FR-014).
