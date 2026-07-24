# Contract: Queue Emulator-Instance Cold-Start Configuration

Extends the existing queue REST contract (`/api/queues`) with two optional emulator-instance fields and
defines the pre-session cold-start behavior at queue start. No new endpoints.

## Request/response fields (additive)

Applies to `POST /api/queues` (create), `PUT /api/queues/{id}` (update), and the queue response body
(list/get). Field names are camelCase over the wire.

| Field | JSON type | Required | Default | Rules |
|-------|-----------|----------|---------|-------|
| `emulatorInstanceName` | string \| null | no | null | LDPlayer instance name. Blank treated as null. |
| `emulatorInstanceIndex` | integer \| null | no | null | LDPlayer instance index; when present MUST be ≥ 0. |

### Validation

- Both optional; omitting both preserves today's behavior (no emulator management).
- `emulatorInstanceIndex` < 0 ⇒ **400 Bad Request** (mirrors the connect-to-game / ensure-emulator
  negative-index rejection).
- When both are present, `emulatorInstanceName` wins at runtime (documented precedence).
- Absent fields on an older stored queue deserialize to null (back-compat; no migration).

### Examples

Create a queue that cold-starts LDPlayer instance "PNS" bound to `emulator-5558`:

```json
POST /api/queues
{
  "name": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "cycleExecution": false,
  "emulatorInstanceName": "PNS"
}
```

Response (get/list) echoes the fields:

```json
{
  "id": "…",
  "name": "PNS Daily 5558",
  "emulatorSerial": "emulator-5558",
  "cycleExecution": false,
  "pauseWhenIdle": false,
  "idleThresholdSeconds": 30,
  "emulatorInstanceName": "PNS",
  "emulatorInstanceIndex": null,
  "status": "Stopped",
  "entryCount": 0
}
```

## Runtime behavior contract (queue start)

Given a queue whose `emulatorInstanceName`/`emulatorInstanceIndex` is set, when the queue starts, the
run performs a feature-070 emulator ensure **before** creating its device session:

| Precondition | Ensure outcome | Session created? | Run result |
|--------------|----------------|:----------------:|-----------|
| instance fields unset | (not invoked) | yes | unchanged from today |
| emulator already up | already_healthy | yes | runs; no restart |
| emulator closed | started | yes | runs |
| emulator hung | restarted | yes | runs |
| non-Windows / tooling missing | platform_unsupported / control_unavailable | yes | runs (neutral not-applied) |
| never boots in time | recovery_timed_out | **no** | **Failure**, actionable reason |
| identifier matches nothing | instance_not_found | **no** | **Failure**, actionable reason |

Guarantees:
- The ensure is invoked at most once per run, before `CreateSession`.
- A genuine failure (recovery_timed_out / instance_not_found) never creates a session and never runs a
  sequence; the run ends as Failure with a reason naming the instance and the outcome.
- A stop request during the ensure aborts promptly (handler honors the cancellation token).
- No new emulator-tuning configuration; feature-070 timeouts/knobs apply unchanged.
- The cold-start outcome is recorded (queue logs / stop reason) so it is observable after the fact.
