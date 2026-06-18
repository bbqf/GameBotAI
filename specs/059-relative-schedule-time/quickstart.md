# Quickstart: Relative-Time Sequence Scheduling

Feature 059 lets you schedule a queue sequence by a **relative offset** ("in 10 min 0 sec from now")
in two ways: persisted in a template (anchored to run start) or live against a running queue
(anchored to the call moment).

## A. Template relative-offset timer (anchored to run start)

Save a template whose Timer entry uses `timerRelativeOffset` instead of `timerTimeOfDay`:

```bash
curl -X POST http://localhost:5000/api/queue-templates \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "Farm",
    "overwrite": true,
    "entries": [
      { "sequenceId": "seq-main", "scheduleType": "OncePerRun" },
      { "sequenceId": "seq-collect", "scheduleType": "Timer", "timerRelativeOffset": "00:10:00" }
    ]
  }'
```

- Link the template to a queue and start the queue. `seq-collect` fires **once**, at the first
  iteration boundary at or after 10 minutes since the run started, then not again that run.
- Restart the queue later → it fires again ~10 minutes into the new run (recomputed each run).
- An offset of `00:00:00` fires at the very first iteration boundary.
- A `Timer` entry must set **exactly one** of `timerTimeOfDay` / `timerRelativeOffset` → otherwise 400.

**UI**: in the queue-template editor, set an entry's schedule type to **Timer**, choose **Relative**,
and enter minutes/seconds. The entry shows a relative-timer badge.

## B. Live relative schedule (anchored to now)

With a queue **running**, schedule any library sequence to fire after an offset from now:

```bash
curl -X POST http://localhost:5000/api/queues/<queueId>/live-schedule \
  -H 'Content-Type: application/json' \
  -d '{ "sequenceId": "seq-collect", "offset": "00:10:00" }'
# 200 -> { "sequenceId": "seq-collect", "offset": "00:10:00", "expectedFireAt": "2026-06-17T14:10:00+00:00" }
```

- Fires **once** at the first iteration boundary at or after `expectedFireAt`, then clears.
- Re-run by POSTing again (a new call for the same sequence while one is still pending **replaces** it).
- Not written to the template; applies only to the current run.
- Errors: `400` malformed/negative offset; `404` unknown queue or unknown sequence; `409` no active run.

**UI**: in the running-queue view, use the **Schedule in mm:ss** control on a sequence; a pending
indicator shows the expected fire time until it fires.

## Counting

Both template relative-offset firings and live firings **count** as completed steps in the run's
step total (so a run reports N once-per-run + K relative/live = N+K). The run still **ends** based
only on the once-per-run steps. (Time-of-day timers and every-step entries remain uncounted.)

## Validate (developer)

```powershell
# Backend
dotnet test c:\src\GameBot\GameBot.sln

# Web UI (the real green gate). `vite build` runs via --prefix, but jest must run from the
# web-ui directory so it can load its ESM jest.config.ts.
npm --prefix c:\src\GameBot\src\web-ui run build
Push-Location c:\src\GameBot\src\web-ui; npx jest; Pop-Location
```

Key tests to expect green: relative timer fires once after offset and counts toward `executed`
(fake `TimeProvider`); live `ScheduleRelative` upsert / most-recent-wins / not-running rejection /
fires-once; template API accepts+returns `timerRelativeOffset` and rejects both-modes/negative;
live-schedule API 200/400/404/409; web-ui editor relative mode + running-queue live-schedule control.
