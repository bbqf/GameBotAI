# Quickstart: Queue Failure Policy and Outbound Notification

**Feature**: 087-queue-failure-policy

How to configure, verify, and reason about the feature once it ships. Service base URL below is
`http://localhost:8080` (the actual port — see the project's API notes, not the 5000 in older docs).

---

## 1. Point the service at a receiver

`appsettings.json` (or environment variables in the usual `Service__Notifications__DefaultUrl` form):

```json
{
  "Service": {
    "Notifications": {
      "DefaultUrl": "http://127.0.0.1:9099/gamebot-alerts",
      "AuthHeaderName": null,
      "AuthHeaderValue": null,
      "TimeoutSeconds": 5,
      "MaxAttempts": 2
    }
  }
}
```

A local receiver needs no auth. For a receiver off this machine, set both `AuthHeaderName` and
`AuthHeaderValue` — the value is a secret and is never returned by any endpoint or written to a log.

## 2. Attach a policy to a queue

```bash
curl -X PUT http://localhost:8080/api/queues/<queueId> -H "Content-Type: application/json" -d "{\"name\":\"PNS Daily 5558\",\"cycleExecution\":true,\"failurePolicy\":{\"consecutiveFailedCycles\":5,\"action\":\"notify\"}}"
```

Start with `action: "notify"`. The issue this feature came from is explicit that a bare auto-stop is
a liability on a roster that can self-heal — earn confidence in the threshold before letting it halt
production.

Picking a threshold: it is counted in **cycles**, not minutes. A roster whose cycle takes ~3 minutes
alerts in ~15 minutes at `5`. A roster that cycles in seconds should use a much larger number.

## 3. Watch it

```bash
curl http://localhost:8080/api/queues/<queueId>
```

The `health` block (present only while Running) now carries the policy's state:

```json
"health": {
  "cyclesCompleted": 417,
  "lastCycleStatus": "failure",
  "consecutiveFailedCycles": 3,
  "failurePolicyConfigured": true,
  "failurePolicyTripped": false,
  "paused": false,
  "lastNotificationAt": null
}
```

`consecutiveFailedCycles: 3` with `failurePolicyTripped: false` means two more failed cycles to go.

## 4. Verify end to end without breaking a real farm

The honest test is a queue whose sequences genuinely fail. The cheapest way to arrange that:

1. Create a throwaway queue bound to a device serial that does not exist, or pointed at a template
   whose single entry is a sequence that waits for an image that will never appear.
2. Give it `{ "consecutiveFailedCycles": 2, "action": "notify" }`.
3. Run a receiver — anything that answers 2xx and prints the body.
4. Start the queue and watch two cycles fail.

Expect **exactly one** POST after the second failed cycle, not one per cycle. That is FR-009, and it
is the difference between an alert and an overnight alert storm.

## 5. If nothing arrives

Check in this order:

| Check | How |
|---|---|
| Did the policy trip? | `health.failurePolicyTripped` — if false, the threshold has not been reached |
| Did delivery fail? | `health.lastNotificationError` on the same block |
| Is the queue actually cycling? | `health.cyclesCompleted` climbing; a non-cycling queue completes one cycle, so a threshold above 1 can never trip |
| Are the cycles actually *failing*? | `health.lastCycleStatus`; a guard that no-ops cleanly reports success |
| Was a destination configured? | A notifying policy with no URL is rejected at save time, so this should be impossible |

The last two rows are the interesting ones. A sequence that detects a bad screen and *succeeds*
having done nothing does not register as a failed cycle — the policy counts failures, not futility.
Guard sequences intended to escalate should fail, or use a `notify` step (below).

## 6. Choosing an action

| Action | Use when |
|---|---|
| `notify` | Default. The roster can self-heal; you want to know, not to intervene. |
| `pause` | The device should stop being touched, but you want the run, its session and its schedule preserved for a quick resume. |
| `stop` | The run should end. Restarting it is a fresh run from zero. |
| `notifyAndStop` | Both — the run is over and you want to be told. |

Resume a paused run:

```bash
curl -X POST http://localhost:8080/api/queues/<queueId>/resume
```

Resuming clears the failure count and re-arms the policy. Firings that came due during the pause are
re-evaluated and fire immediately if still due — nothing is skipped.

## 7. Raising an alert from a sequence

For escalation that lives in a committed sequence rather than in service configuration, add a
`notify` action step:

```json
{ "type": "action", "action": { "type": "notify", "message": "Unrecognised screen; BACK did not dismiss it." } }
```

It never fails the sequence, even if delivery fails — so it is safe to put in a guard's recovery
path.

---

## What this feature does **not** do

- It does not tell you *why* a sequence failed — only which one did. The execution log has the
  detail; the alert is the pointer.
- Policy state does not survive a service restart. A service restarted mid-outage re-arms and will
  alert again.
- There is no aggregation across queues: three failing queues send three alerts.
- No transport other than HTTP POST. Chat or email integration is a receiver's job, not this
  service's.
