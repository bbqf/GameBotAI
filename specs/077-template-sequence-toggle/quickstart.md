# Quickstart: Enable/Disable Template Sequences

## What this feature does

Adds an on/off switch to each sequence in a queue template. Turning a sequence off keeps it in the template (position and schedule intact) but excludes it from queue runs until you turn it back on. The state is saved to the template and survives reload and service restart.

## Try it (manual)

1. Open the Queues page and select a queue that has a linked template with several sequences.
2. In the template editor (scheduling areas), each sequence card now shows an **on/off switch**.
3. Toggle one sequence **off** — the card shows a visibly "off" (dimmed) state but stays in place.
4. Click **Save as template** (overwrite) to persist.
5. Reload the page → the sequence is still shown **off** (persistence confirmed).
6. Start the queue → the disabled sequence never runs; the others run on their schedule.
7. Toggle it back **on**, save, and start again → it runs on the next run with its original schedule.

## Verify via API

```bash
# Save a template with one entry disabled
curl -s -X POST http://localhost:8080/api/queue-templates \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo","overwrite":true,"entries":[
        {"sequenceId":"<seqA>","scheduleType":"OncePerRun","enabled":false},
        {"sequenceId":"<seqB>","scheduleType":"OncePerRun"}
      ]}'

# Read it back — entry A shows "enabled": false, entry B shows "enabled": true
curl -s http://localhost:8080/api/queue-templates/<id>
```

## Key behaviors

- **Legacy default**: templates saved before this feature report every entry as `enabled: true`; nothing changes for them.
- **Takes effect at next run**: toggling during a run affects the next run, not the one in progress (queues load runtime from the template at start).
- **Per-entry**: the same sequence appearing twice in a template toggles independently.
- **All-off is valid**: a template with every entry disabled starts and simply runs nothing.

## Tests to run

Backend:
```bash
dotnet test C:\src\GameBot\tests\unit
dotnet test C:\src\GameBot\tests\integration
```

Web UI (real green gate — see project memory):
```bash
cd C:\src\GameBot\src\web-ui
npm run build
npm test
```
