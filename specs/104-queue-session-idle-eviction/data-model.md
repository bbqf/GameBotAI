# Data Model: Queue sessions survive idle gaps (#217)

## EmulatorSession (in-memory, `GameBot.Domain.Sessions`)

| Field | Type | Change | Meaning |
|---|---|---|---|
| `OwnerQueueId` | `string?` | **added** | Id of the queue that bound this session; `null` for an ad-hoc session. |
| `LastActivity` | `DateTimeOffset` | unchanged | Refreshed on lookup/input/snapshot. |

**Rule**: the idle sweep retires a session only when `OwnerQueueId` is `null` and
`now - LastActivity > IdleTimeout`. A queue-owned session ends only through `StopSession`.

Not persisted and not exposed by any existing DTO; no migration.

## Queue run handle (`QueueRunHandle`)

`SessionId` already exists and is settable. It changes when a re-bind happens; the monitor and
every later firing read the new value.

## State transitions (queue run's session)

```text
start ──BindQueueSession──▶ Bound(id1, owner=queue)
Bound ──pre-firing check: present──▶ Bound (no change)
Bound ──pre-firing check: missing──▶ BindQueueSession once
        ├─ success ──▶ Bound(id2, owner=queue), capture moved id1→id2, SessionRebound logged
        └─ failure ──▶ run fails "emulator connection lost mid-run ('<serial>')"
Bound ──run ends (any reason)──▶ StopCapture + StopSession(current id)
```
