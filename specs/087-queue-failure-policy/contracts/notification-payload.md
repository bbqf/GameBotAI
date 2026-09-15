# Contract: Outbound Failure Notification

**Feature**: 087-queue-failure-policy | **Schema version**: 1

The payload this service POSTs to a configured destination. It is a **stable contract** (FR-014a):
a receiver written against version 1 keeps working until `schemaVersion` changes.

## Request

```
POST <destination url>
Content-Type: application/json; charset=utf-8
User-Agent: GameBot/<service version>
<AuthHeaderName>: <AuthHeaderValue>     ← only when configured (FR-016a)
```

The destination is `QueueFailurePolicy.NotifyUrl` when set, otherwise
`Service:Notifications:DefaultUrl`. It is always operator-configured — never derived from sequence
data or a game screen (FR-016).

## Body

### A tripped queue failure policy

```json
{
  "schemaVersion": 1,
  "eventType": "queue.failure-policy",
  "raisedAt": "2026-09-14T19:18:02.417+02:00",
  "queueId": "9f2c8a1b4d6e4f0a9c3b7d1e5a8f2c60",
  "queueName": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "consecutiveFailedCycles": 5,
  "cyclesCompleted": 417,
  "failedEntryIndex": 2,
  "failedSequenceId": "seq-alliance-help",
  "failedSequenceName": "PNS.DonateAllianceTech",
  "failedEntryCount": 2,
  "action": "notifyAndStop",
  "message": "Queue 'PNS Daily 5558' has failed 5 consecutive cycles; action: notifyAndStop."
}
```

### An author-raised alert from a sequence (User Story 3)

```json
{
  "schemaVersion": 1,
  "eventType": "sequence.notify",
  "raisedAt": "2026-09-14T19:18:02.417+02:00",
  "queueId": "9f2c8a1b4d6e4f0a9c3b7d1e5a8f2c60",
  "queueName": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "consecutiveFailedCycles": 0,
  "cyclesCompleted": 417,
  "failedEntryIndex": null,
  "failedSequenceId": "seq-ensure-city",
  "failedSequenceName": "PNS.EnsureCityScreen",
  "failedEntryCount": 0,
  "action": null,
  "message": "Unrecognised screen; BACK did not dismiss it."
}
```

## Field semantics

| Field | Type | Nullable | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | no | `1`. Increments only on a breaking change to this contract. |
| `eventType` | string | no | `"queue.failure-policy"` or `"sequence.notify"`. A receiver keys off this. |
| `raisedAt` | ISO-8601 with offset | no | Local-clock instant the event was raised (the service's clock, matching the execution log). |
| `queueId` | string | no¹ | Stable queue id. |
| `queueName` | string | no¹ | Display name at the moment of the event. |
| `emulatorSerial` | string | no¹ | The device the queue drives — usually the fastest way to identify which farm broke. |
| `consecutiveFailedCycles` | integer | no | The count that tripped the policy. `0` for `sequence.notify`. |
| `cyclesCompleted` | integer | no | Total completed cycles this run — distinguishes "broke immediately" from "ran 400 cycles then broke". |
| `failedEntryIndex` | integer | **yes** | Roster position of the first failed entry in the tripping cycle. Null for `sequence.notify`. |
| `failedSequenceId` | string | **yes** | The first failing sequence; for `sequence.notify`, the sequence that raised the alert. |
| `failedSequenceName` | string | **yes** | Resolved at send time, so never stale. Null if the sequence has since been deleted. |
| `failedEntryCount` | integer | no | How many entries failed in the tripping cycle (A-006) — tells "one flaky task" from "everything is broken". |
| `action` | string | **yes** | The action taken. Null for `sequence.notify`. |
| `message` | string | no | Human-readable. Service-composed for a policy trip; **author-written** for `sequence.notify`. |

¹ Null only for a `sequence.notify` raised outside any queue (an ad-hoc sequence run).

> **Not included: the failing sequence's error message.** The 086 cycle ledger records a boolean per
> entry, not a message (research R7). The event identifies *what* failed; the execution log holds
> *why*. Recorded as a known limitation in the plan.

## Delivery semantics

| Property | Value |
|---|---|
| Method | `POST` |
| Timeout | 5 s per attempt (`Service:Notifications:TimeoutSeconds`) |
| Attempts | 2 total, 1 s fixed backoff (`Service:Notifications:MaxAttempts`) |
| Success | Any 2xx response |
| Failure | Non-2xx, timeout, or transport error — retried once, then abandoned |
| Blocking | **Never.** Delivery runs off the run-loop thread; the run does not await it (FR-011, SC-005) |
| Cancellation | **Not** tied to the run's cancellation token — a `notifyAndStop` alert must survive the stop it is announcing |
| On abandonment | Recorded in the structured application log *and* on the run's live health as `lastNotificationError` (FR-015a) |

**The receiver is not trusted.** Its response body is never parsed, never logged in full, and never
acted upon — only the status code is read. Nothing a receiver returns can influence queue behaviour.

## Receiver expectations

A receiver should:

- Respond 2xx promptly; a slow receiver wastes the timeout but never harms the run.
- Be idempotent-tolerant: a retry after a timeout may deliver the same event twice. `queueId` +
  `raisedAt` + `eventType` is a practical dedupe key.
- Tolerate unknown fields added in a future `schemaVersion` rather than rejecting them.
