# Quickstart: Emulator Execution Queue

How to exercise the feature end to end once implemented.

## Prerequisites

- Backend service running (`GameBot.Service`) with at least one ADB device/emulator available (or note its serial; binding does not require it to stay connected).
- Web UI running (`src/web-ui`, `npm run dev`) and pointed at the service with a valid token.

## Happy path (UI)

1. Open the web UI → **Authoring** → click the new **Queues** tab.
2. Click **New**. Enter a name (e.g., `Daily Farm`), pick an emulator from the device dropdown, optionally check **Cycle execution**. Save.
3. The queue appears in the list showing name, emulator serial, cycle on/off, status **Stopped**, and 0 entries.
4. Open the queue → **Add sequence**, pick a sequence from the searchable dropdown. Add a second one; confirm it lands at the **end** of the list.
5. Back in the list, click **Start** → status flips to **Running**. Confirm **Edit** and **Delete** are now disabled, but adding/removing sequences is still allowed.
6. Click **Stop** → status returns to **Stopped**; Edit/Delete re-enabled.
7. **Edit** the queue: change the name / toggle cycle (note the emulator field is read-only). Save and reload — config changes persist.
8. **Delete** the queue (while stopped) → it disappears from the list.

## Restart behavior (manual verification)

1. Create a queue, add 2 sequences, **Start** it.
2. Restart the `GameBot.Service` process.
3. Reload the Queues tab: the queue still exists with its name, emulator, and cycle flag, but its **sequence list is empty** and its **status is Stopped**. (FR-020/021/022)

## API smoke test (curl-style)

```bash
# Create
curl -X POST $BASE/api/queues -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"Daily Farm","emulatorSerial":"emu-1","cycleExecution":true}'

# List
curl $BASE/api/queues -H "Authorization: Bearer $TOKEN"

# Add entry
curl -X POST $BASE/api/queues/$ID/entries -H "Authorization: Bearer $TOKEN" \
  -d '{"sequenceId":"seq-100"}'

# Start / Stop (status flips only)
curl -X POST $BASE/api/queues/$ID/start -H "Authorization: Bearer $TOKEN"
curl -X POST $BASE/api/queues/$ID/stop  -H "Authorization: Bearer $TOKEN"

# Edit blocked while running → expect 409 queue_running
# Detail shows stale:true for entries whose sequence was deleted
curl $BASE/api/queues/$ID -H "Authorization: Bearer $TOKEN"
```

## What is intentionally NOT implemented

- No actual execution of sequences (start/stop only flip status).
- Cycle execution has no runtime effect yet (stored flag only).
- No reordering of entries beyond append + remove.
- No persistence of entries or status across restart (by design).
