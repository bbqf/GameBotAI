# Phase 1 Contracts: Concurrent Queue Execution

All changes are additive except where explicitly marked. No API version bump.

Error bodies keep each endpoint's existing envelope, which differs by area:

- **Queues** (`/api/queues/*`): `{ "error": { "code": "...", "message": "...", "hint": null } }`
- **Emulator images** (`/api/emulator/*`, `/api/images/*`): flat `{ "error": "...", "message": "..." }`

Both shapes are pre-existing; this feature adds codes, not envelopes.

---

## 1. `POST /api/queues/{id}/start`

### New failure: device already claimed (FR-009, FR-010)

**Status**: `409 Conflict`

```json
{
  "error": {
    "code": "device_in_use",
    "message": "Emulator 'emulator-5558' is already in use by queue 'PNS Daily 5558'. Stop that queue before starting this one.",
    "hint": null
  }
}
```

Distinct from the existing refusal for the same queue, which is unchanged:

```json
{ "error": { "code": "already_running", "message": "The queue is already running.", "hint": null } }
```

**Preconditions checked, in order** (unchanged order except for the new step 3):

1. Queue exists -> else `404 not_found`.
2. Feature 078 required-parameter preflight -> else `409` with the existing parameter error.
3. **NEW** Device claim -> else `409 device_in_use`.
4. Run registry accepts the queue -> else `409 already_running`.

**Guarantees**: a `device_in_use` refusal starts no run, changes no queue's status, and does not touch
the holding run.

**Logging** (FR-017): because no run starts, there is no execution-log entry. The refusal is instead
written to the service's application log at Warning level, naming the refused queue id, the device
serial and the holding queue:

```text
Queue {QueueId} start refused: emulator {DeviceSerial} is held by queue {HoldingQueueId} ({HoldingQueueName}).
```

### Success

Unchanged: `200 OK` with the existing queue response body.

---

## 2. `GET /api/queues/{id}/monitor`

### New optional field (FR-018)

```jsonc
{
  "queueId": "…",
  "name": "PNS Daily 5558",
  "running": true,
  "deviceSerial": "emulator-5558",   // NEW: null when the queue is not running
  "cycleExecution": true,
  "runStartedAt": "2026-08-30T09:00:00+02:00",
  "current": { /* unchanged */ },
  "upcoming": [ /* unchanged */ ],
  "nothingScheduled": false,
  "lastOutcome": null
}
```

Additive and nullable; clients that ignore it are unaffected.

---

## 3. `GET /api/emulator/screenshot`

### Changed selector behavior (FR-022, FR-023, FR-024) - **intentionally breaking in one case**

| Query | Sessions active | Before | After |
|---|---|---|---|
| `?sessionId=<id>` | any | that session | unchanged |
| `?serial=<adb-serial>` | any | (not supported) | **NEW** that device's running session, else `404 session_not_found` |
| none | 0 | `503 emulator_unavailable` | unchanged |
| none | 1 | that session | unchanged |
| none | >1 | **arbitrary session** | **`409 ambiguous_session`** |

"Running sessions" here means every running session, including those with no bound ADB serial —
in stub/non-ADB mode that is all of them, and excluding them would break single-session capture there.

```json
{
  "error": "ambiguous_session",
  "message": "3 device sessions are active; specify sessionId or serial."
}
```

The previous behavior in the last row returned an unpredictable device, so nothing depended on it
correctly. `PickSession`'s `FirstOrDefault` chain is removed.

---

## 4. Sequence-step failure messages (FR-006, FR-007)

Not a wire contract, but these strings are user-facing in execution logs and are asserted by tests.

| Situation | Before | After |
|---|---|---|
| Explicit session supplied, N sessions active | `no session available for '<step>' step; start a session or pass a sessionId` (**failed**) | step executes normally |
| No session, 0 active | `no session available for '<step>' step; start a session or pass a sessionId` | unchanged |
| No session, 1 active | (resolved silently) | unchanged |
| No session, N > 1 active | `no session available for '<step>' step; …` | `3 device sessions are active; specify a sessionId for '<step>'` |

Applies to `ensure-game-running`, `go-to-home-screen`, primitive `tap`/`swipe`/`key`, and the
`CommandExecutor` `missing_session_context` path (whose exception message gains the same text).

---

## 5. Session capacity message (FR-016)

`SessionManager.CreateSession` still throws `InvalidOperationException` and the API still maps it to
the existing `capacity_exceeded` code. Only the message changes, and it is what a failed queue run
records:

```text
session capacity reached: 8 of 8 sessions are open (Service:Sessions:MaxConcurrentSessions)
```

The queue run's execution-log failure reason becomes:

```text
emulator could not be reached ('emulator-5566'): session capacity reached: 8 of 8 sessions are open (Service:Sessions:MaxConcurrentSessions)
```

---

## 6. Configuration

| Key | Before | After |
|---|---|---|
| `Service:Sessions:MaxConcurrentSessions` | default `3` | default `8` |

An explicitly configured value continues to win; only the default changes.
