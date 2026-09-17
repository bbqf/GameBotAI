# Data Model: Resume Queues After a Service Restart

## ExecutionQueue (existing, `data/queues/<id>.json`)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `ResumeOnServiceStart` | bool | `false` | New. Opt-in: start this queue again at service startup if it was Running when the service went down. Absent in older files → `false` (no migration). Editable only while the queue is Stopped (existing 409 rule on update). Copied by duplicate. |

## Queue run-state record (new, `data/queue-run-state.json`)

```json
{
  "runningQueueIds": ["3f2a…", "9c1b…"]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `runningQueueIds` | string[] | Distinct queue ids (ordinal) with a live run, as last known to the service. |

### Lifecycle

```text
StartAsync: claim device OK ─► MarkRunningAsync(id) ─► run launches
RunAsync finally:
   ApplicationStopping cancelled? ── yes ─► keep id   (service shutdown)
                                 └─ no  ─► ClearAsync(id)  (stop / complete / failure / policy stop)
Service start (after ApplicationStarted), for each recorded id:
   queue missing            ─► ClearAsync(id), log NotFound
   ResumeOnServiceStart off ─► ClearAsync(id), log Skipped
   otherwise                ─► StartAsync(id)
        Started         ─► id stays recorded (re-marked by StartAsync)
        AlreadyRunning  ─► log, leave as is
        DeviceInUse/NotFound ─► ClearAsync(id), log
        throws          ─► ClearAsync(id), log error
```

### Validation / robustness

- Missing or 0-byte file → empty set. Corrupt (unparseable) file → `ListRunningAsync` throws `InvalidDataException`, which the resume pass logs (7304) before resuming nothing; `MarkRunningAsync`/`ClearAsync` treat it as empty and overwrite it with a valid file.
- Writes are serialized in-process and written atomically (temp file + replace).
- `MarkRunningAsync` is idempotent; `ClearAsync` of an unknown id is a no-op that does not rewrite the file.

## IQueueRunStateStore (new interface, `GameBot.Domain.Queues`)

| Member | Description |
|--------|-------------|
| `Task MarkRunningAsync(string queueId)` | Adds the id (idempotent). |
| `Task ClearAsync(string queueId)` | Removes the id (no-op when absent). |
| `Task<IReadOnlyList<string>> ListRunningAsync()` | Recorded ids; empty when the file is missing or empty; throws `InvalidDataException` when it is corrupt. |
