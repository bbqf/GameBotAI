# Contract Delta: Template Entry `enabled`

This feature adds one boolean field to the existing queue-template API. No new endpoints, no breaking change.

## POST /api/queue-templates (save; create or overwrite)

**Request body** — each element of `entries[]` gains an optional `enabled`:

```json
{
  "name": "PNS Daily 5558",
  "overwrite": true,
  "entries": [
    {
      "sequenceId": "3c93831cede44977a76d7b47322f53b3",
      "scheduleType": "OncePerRun",
      "enabled": false
    },
    {
      "sequenceId": "413138c1691242f684804ec72b1690dd",
      "scheduleType": "Timer",
      "timerRelativeOffset": "00:30:00"
    }
  ]
}
```

Rules:
- `enabled` is optional. **Absent or null → `true`** (entry enabled). This keeps older clients and existing round-trips working unchanged.
- `enabled` is orthogonal to `scheduleType`/timer fields; it does not affect their validation and they do not affect it. A disabled `Timer` entry keeps its timer fields.
- No new error codes. Existing validation (blank `sequenceId`, invalid `scheduleType`, timer field exclusivity) is unchanged.

## GET /api/queue-templates/{id} (detail)

**Response** — each element of `entries[]` gains `enabled`:

```json
{
  "id": "42398a1692f94c3eb9aef6013c12fe22",
  "name": "PNS Daily 5558",
  "entryCount": 20,
  "entries": [
    {
      "sequenceId": "3c93831cede44977a76d7b47322f53b3",
      "sequenceName": "PNS Alliance Donate Daily",
      "stale": false,
      "scheduleType": "OncePerRun",
      "timerTimeOfDay": null,
      "timerRelativeOffset": null,
      "enabled": false
    }
  ]
}
```

Rules:
- `enabled` is always present in the response (never null), reflecting the stored value; legacy entries with no stored value are reported as `true`.
- The detail endpoint returns **all** entries, including disabled ones — the template editor needs them to render and toggle.

## Behavioral contract (queue run)

- `GET /api/queues/{id}` and the executed run reflect only **enabled** entries of the linked template (disabled entries are excluded when the run/runtime is built from the template).
- Toggling an entry's `enabled` and saving the template takes effect on the **next** queue start; a run already in progress is unaffected.
- A template whose entries are all disabled (or empty) produces a valid, idle run (no error).

## Round-trip invariants (test anchors)

1. Save with `enabled:false` on an entry → GET returns that entry with `enabled:false`, same position, same schedule.
2. Save an entry with `enabled` omitted → GET returns `enabled:true`.
3. Load a legacy template file (no `Enabled` key) → GET returns every entry `enabled:true`.
4. Start a queue whose template has a disabled entry → that sequence never executes; enabled entries execute normally.
5. Re-enable + save + start → the previously disabled sequence executes again with its original schedule/position.
