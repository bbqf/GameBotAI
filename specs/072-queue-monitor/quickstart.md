# Quickstart: Live Queue Monitor View

## What this delivers

Open a **running** queue in the web UI and see a live "playlist": the sequence running **now**, the
ordered **up-next** list, and each item's schedule reason + expected time. It auto-refreshes every
~2.5s. Open a **stopped** queue and you get the existing editor unchanged.

## Try it (manual)

1. Build & run the service (port 8080) and the web-ui, or use the installed app.
2. Create/pick a queue with a linked template that mixes schedule types — e.g. a couple of
   **Once per run** sequences, one **After Every Step**, and one **Timer** (time-of-day or relative).
   Optionally enable **Cycle**.
3. From the Queues overview, **Start** the queue.
4. Open the running queue (click its name / the Monitor affordance). Expect the **monitor** panel, not
   the editor:
   - A **Now** row showing the currently executing sequence.
   - An **Up next** list: OncePerRun steps in order, "After Every Step" noted once, and any timed/live
     firings with their expected times, ordered by time. A cycling queue marks its steps **repeats**.
5. Use the overview's **Schedule** control to live-schedule a sequence a few seconds out; within ~1
   refresh it appears in **Up next** at its expected time — no manual reload.
6. **Stop** the queue (overview). The open monitor flips to an **ended / not running** state showing the
   last run outcome, with a path back to the editor / Execution Logs.
7. Open a **stopped** queue directly → the **editor** appears as before.

## Verify (automated)

Backend (from repo root):

```bash
dotnet test
```

- `QueueMonitorService` projection matrix: each schedule kind → correct `reason`/`ExpectedAt`;
  cycling → `repeats`; current-sequence highlighted as `Current`; running-but-empty →
  `nothingScheduled`; not-running → `running:false` + best-effort `lastOutcome`; timed/live/
  self-reschedule ordered by time.
- Endpoint: `GET /api/queues/{id}/monitor` returns 200 snapshot when running, 200 `running:false`
  when stopped, 404 for unknown id.

web-ui (from `src/web-ui`):

```bash
npm run build
npm test
```

- `QueueMonitor.test.tsx`: renders Now/Up-next from a mocked `getQueueMonitor`; advancing fake timers
  triggers a re-poll and updates the list; a `running:false` poll shows the ended state.
- `QueuesPage.monitor.spec.tsx`: opening a Running queue renders the monitor; opening a Stopped queue
  renders the editor.

## Notes

- Read-only: the monitor shows information only; start/stop/schedule stay in the overview.
- Expected times are best-effort for far-future timers (shown as their next eligible time); ordering and
  schedule reasons are exact.
- No new config, env vars, or persisted data.
