# Quickstart: Queue-Start and After-Every-Step Scheduling

**Feature**: 060-queue-start-after-every-scheduling

This feature adds the **At Queue Start** schedule option and renames the **Every Step** option's
label to **After Every Step** (label only — the API/stored value stays `EveryStep`).

## What changed for operators

- **At Queue Start**: tag template entries so they run once, in template order, the moment the
  queue starts — before any timer is evaluated and before the first normal step. Use it for
  startup/setup (open the game, dismiss pop-ups, navigate to a known screen).
- **After Every Step**: the option formerly labeled "Every Step", with the same behavior (runs
  after each normal step). Only the name changed.

## Using it in the web UI

1. Open a queue's template editor (Queues page).
2. For an entry, open the **Schedule type** dropdown. It now lists:
   `Once Per Run`, `After Every Step`, `Timer`, `At Queue Start`.
3. Choose **At Queue Start**. A badge indicates the entry runs at queue start.
4. Save. Reload to confirm the choice persisted.

## Using it via the API

Save a template whose first entry runs at queue start:

```http
POST /api/queue-templates
Content-Type: application/json

{
  "name": "Daily run",
  "overwrite": true,
  "entries": [
    { "sequenceId": "seq-open-game",    "scheduleType": "AtQueueStart" },
    { "sequenceId": "seq-collect",      "scheduleType": "OncePerRun" },
    { "sequenceId": "seq-screenshot",   "scheduleType": "EveryStep" }
  ]
}
```

- `AtQueueStart` requires no timer fields.
- `EveryStep` is still the wire value for the "After Every Step" option (unchanged).
- An unrecognized `scheduleType` returns 400 with `{ error: { code, message, hint } }`; accepted
  values are `OncePerRun`, `EveryStep`, `Timer`, `AtQueueStart`.

Read it back with `GET /api/queue-templates/{id}` — `scheduleType` round-trips unchanged.

## Behavior at run time

For the template above on a cycling queue:

1. `seq-open-game` runs first (At Queue Start), before any timer evaluation. Counts toward the
   run's executed total.
2. The run loop begins. Each cycle: `seq-collect` (Once Per Run) runs, then `seq-screenshot`
   (After Every Step) runs after it.
3. `seq-open-game` does **not** run again on later cycles (once per run).
4. `seq-screenshot` runs only after normal steps — not after `seq-open-game` and not after timer
   firings.

A failure in an At Queue Start sequence is non-fatal: it is recorded and the run continues.

## How to verify

- **Backend**: `dotnet test` — see `QueueExecutionServiceTests` (at-queue-start ordering, counting,
  once-per-run, non-fatal failure), `QueueTemplatesApiContractTests` (AtQueueStart accept/return,
  reject invalid, EveryStep backward compatibility), and the integration ordering test.
- **Web UI** (real green gate): from `src/web-ui`, run `npx vite build` and `npx jest` — see the
  updated `QueueEntryList` and `QueuesPage.templates` specs.

## Backward compatibility

- Existing templates load and run identically; `EveryStep` entries keep working under the new
  "After Every Step" label with no migration.
- The default schedule option (entries with no explicit type) remains `OncePerRun`.
