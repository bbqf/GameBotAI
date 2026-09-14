# Phase 1 Data Model: Queue Failure Policy and Outbound Notification

**Feature**: 087-queue-failure-policy | **Date**: 2026-09-14

Three tiers, distinguished by lifetime — this is the organising idea of the whole feature:

| Tier | Lives in | Survives restart | Examples |
|---|---|---|---|
| **Configuration** | Queue JSON / `appsettings` | Yes | threshold, action, destination URL |
| **Run state** | `QueueRunHandle` (in-memory) | No | tripped-this-episode, paused-at, last notification |
| **Transport** | The HTTP request body | n/a | the notification event |

---

## 1. Configuration

### `QueueFailurePolicy` (new, `GameBot.Domain.Queues`)

Optional; `null` on `ExecutionQueue.FailurePolicy` means no policy and no evaluation (FR-004).

| Field | Type | Rules |
|---|---|---|
| `ConsecutiveFailedCycles` | `int` | **Required.** MUST be ≥ 1. A non-positive value is rejected at the API boundary with a message naming the value (FR-005). |
| `Action` | `QueueFailureAction` | **Required.** Rejected if not one of the four values. |
| `NotifyUrl` | `string?` | Optional override of the service-wide default. When supplied MUST be an absolute `http`/`https` URI (FR-016). |

**Cross-field rule (FR-006)**: if `Action` can notify (`Notify` or `NotifyAndStop`) and neither
`NotifyUrl` nor `Service:Notifications:DefaultUrl` is set, the save is rejected — a notifying policy
that can never notify is a configuration error, not a silent no-op.

**Deliberately absent**: any auth credential. The auth header lives only in service configuration
(research R9), so a secret is never written into a queue JSON file that the backup/restore endpoints
copy around.

### `QueueFailureAction` (new enum)

| Value | JSON | Effect on trip |
|---|---|---|
| `Notify` | `"notify"` | Deliver the event. Run continues. |
| `Stop` | `"stop"` | Cancel the run; stop reason `StoppedByFailurePolicy`. No event. |
| `Pause` | `"pause"` | Park the run (resumable). No event. |
| `NotifyAndStop` | `"notifyAndStop"` | Deliver the event, then cancel the run. |

Exactly the four the issue named; `notifyAndPause` is deliberately not introduced (spec A-005).

### `ExecutionQueue` (modified)

One added property: `public QueueFailurePolicy? FailurePolicy { get; set; }`. Absent from existing
stored JSON deserialises to `null`, so every queue written before this feature keeps working with no
migration (the same back-compat route `PauseWhenIdle` and `IdleThresholdSeconds` took).

### `QueueStopReason` (modified)

One added value, `StoppedByFailurePolicy`. Serialised by name, so existing log readers see a new
string rather than a shifted number — **append at the end of the enum**, never in the middle.

### `FailureNotificationOptions` (new, section `Service:Notifications`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `DefaultUrl` | `string?` | `null` | Service-wide destination |
| `AuthHeaderName` | `string?` | `null` | e.g. `X-GameBot-Token` |
| `AuthHeaderValue` | `string?` | `null` | **Secret.** Never logged, never returned by any endpoint |
| `TimeoutSeconds` | `int` | `5` | Per attempt |
| `MaxAttempts` | `int` | `2` | Total attempts, not retries |

---

## 2. Run state (on `QueueRunHandle`, lock-guarded, discarded with the run)

Mirrors the existing `IdlePausedUntil` pattern exactly (research R5).

### Policy trip state

| Field | Type | Meaning |
|---|---|---|
| `PolicyTripped` | `bool` | True from the moment the threshold is crossed until a **successful cycle** clears it. This single flag is what makes FR-009 (one alert per episode) and FR-008 (re-arm) both true. |

**State transitions**:

```
  not tripped ──[cycle fails, consecutive == threshold]──► tripped  (act: notify/stop/pause)
  not tripped ──[cycle fails, consecutive  < threshold]──► not tripped
      tripped ──[cycle fails, consecutive  > threshold]──► tripped   (no action — the key rule)
      tripped ──[cycle succeeds]───────────────────────► not tripped (re-armed)
```

Note the evaluator compares `consecutive >= threshold` **and** `!PolicyTripped`. Using `==` alone
would be a latent bug if a cycle ever incremented the count by more than one.

### Pause state

| Field | Type | Meaning |
|---|---|---|
| `PausedAt` | `DateTimeOffset?` | When the pause began; `null` when not paused |
| `PauseReason` | `string?` | Why, e.g. `"failure policy: 5 consecutive failed cycles"` |
| `IsPaused` | `bool` | Derived: `PausedAt is not null` |

Released by `Resume()`, which clears both fields **and** `PolicyTripped`, so the resumed run is
evaluated afresh (FR-020).

> **Distinct from feature 073's `IdlePausedUntil`.** That is a short, self-releasing hold with a
> known resume instant. This one has no resume instant and is released only by an operator. They are
> separate fields and must not be conflated in the monitor projection.

### Last-notification state

| Field | Type | Meaning |
|---|---|---|
| `LastNotificationAt` | `DateTimeOffset?` | When the most recent attempt finished |
| `LastNotificationSucceeded` | `bool?` | Outcome |
| `LastNotificationError` | `string?` | Failure detail; `null` on success |

Written by the notifier's completion callback (off the run-loop thread), read by the health
projection — hence lock-guarded like everything else on the handle. Satisfies FR-015a and FR-026.

---

## 3. Transport

### `FailureNotificationEvent` (new)

The wire contract. Full JSON shape and field semantics in
[contracts/notification-payload.md](./contracts/notification-payload.md). Summary of fields:

`schemaVersion`, `eventType`, `raisedAt`, `queueId`, `queueName`, `emulatorSerial`,
`consecutiveFailedCycles`, `cyclesCompleted`, `failedEntryIndex`, `failedSequenceId`,
`failedSequenceName`, `failedEntryCount`, `action`, `message`.

`eventType` distinguishes a tripped policy (`"queue.failure-policy"`) from an author's explicit
alert (`"sequence.notify"`), satisfying FR-014 and letting one receiver serve both user stories.

---

## 4. Health projection (`QueueHealthResponse`, modified)

Added to the existing block, which is present only while the queue is Running (086's rule, unchanged):

| Field | Type | Source |
|---|---|---|
| `failurePolicyConfigured` | `bool` | Queue config |
| `failurePolicyTripped` | `bool` | Handle |
| `paused` | `bool` | Handle |
| `pausedAt` | `DateTimeOffset?` | Handle |
| `pauseReason` | `string?` | Handle |
| `lastNotificationAt` | `DateTimeOffset?` | Handle |
| `lastNotificationSucceeded` | `bool?` | Handle |
| `lastNotificationError` | `string?` | Handle |

All additive; every field 086 already published keeps its name and meaning. `AuthHeaderValue` appears
here nowhere, by construction — a contract test asserts its absence from the serialised response.

---

## 5. Sequence notify step (User Story 3)

An action step with `type: "notify"` and payload:

| Field | Type | Rules |
|---|---|---|
| `message` | `string` | **Required**, non-empty, max 1000 chars. Rejected at save time if missing or blank (FR-023). |
| `url` | `string?` | Optional per-step destination override; same absolute-URI rule as the policy. |

Raises a `FailureNotificationEvent` with `eventType: "sequence.notify"`, the author's `message`, and
the originating sequence in `failedSequenceId`/`failedSequenceName` (the field pair is reused rather
than duplicated — the name is generic enough and a receiver keys off `eventType`). Queue fields are
populated when the sequence runs inside a queue and null when it does not. Delivery failure never
fails the step (FR-024).
