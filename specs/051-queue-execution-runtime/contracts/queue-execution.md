# Contract: Queue Execution (start / stop)

The endpoints already exist (feature 046) and keep their routes and success shapes. This feature changes their **behavior** (real execution) and adds failure/conflict cases. No new routes are introduced. `specs/openapi.json` is updated to reflect the new status codes.

Error envelope (existing convention): `{ "error": { "code": string, "message": string, "hint": string | null } }`.

## POST /api/queues/{id}/start

Launches a background run. Returns immediately; the run proceeds asynchronously and its outcome is observable through the execution log and the queue's status.

- **200 OK** — run launched (or queue already not-startable cases handled below). Body: `QueueDto` with `status: "Running"`.
- **404 not_found** — no queue with `{id}`.
- **409 already_running** — a run is already in progress for this queue (FR-013a). Body: error envelope. The existing run is unaffected.

Notes:
- A queue bound to an emulator already running **another** queue is **allowed** to start (FR-013); no 409 for shared-emulator.
- If the queue has **no resolvable linked template**, start still returns **200/Running**; the run then ends almost immediately with a **failure** queue-run execution-log entry ("no template to run") and the status returns to `Stopped` (FR-002). (Start does not 400 this case — the failure is observable in the log, consistent with async execution.)
- If the bound emulator is **unavailable**, start returns **200/Running**; the run ends with a **failure** entry ("emulator could not be reached") and status returns to `Stopped` (FR-004).

## POST /api/queues/{id}/stop

Requests prompt cancellation of the in-flight run.

- **200 OK** — stop requested (or queue was not running). Body: `QueueDto`. Status becomes/remains `Stopped`. The run aborts promptly (≤3 s, SC-003), disconnects the emulator session, and writes a "stopped manually" queue-run entry.
- **404 not_found** — no queue with `{id}`.
- Stopping a not-running queue is a **no-op** (200), leaving it `Stopped` (FR-022).

## Behavioral contract of a run (observable via execution log)

For every run, exactly one **queue-run** execution-log entry is produced:

| Outcome | `executionType` | `finalStatus` | Summary contains |
|---------|-----------------|---------------|------------------|
| Completed full run (cycle off) | `queue` | `success` | "completed full run", sequence/failure counts |
| Stopped manually | `queue` | `success` | "stopped manually", sequences executed |
| Failure (no template) | `queue` | `failure` | "no template to run" |
| Failure (emulator unreachable / connection lost) | `queue` | `failure` | emulator reason |

Within the run, each executed sequence appears as a **child** entry (`executionType: sequence`, `parentExecutionId` = the queue-run root id, ordered), and its invoked commands/steps nest beneath it exactly as in features 049/050. The execution-logs list (`GET /api/execution-logs?rootsOnly=true`) shows the queue run as a single top-level row; `GET /api/execution-logs/{rootId}/subtree` returns the queue → sequences → steps tree.

## Execution-log grid (web-ui) contract

- A new row type label: `queue` → **"Queue"**.
- A `queue` top-level row is **expandable** (its children are the executed sequences).
- Status column reflects `finalStatus` (`success`/`failure`/`running`) as today; a manually-stopped run shows `success` with the stop reason in the Additional-information column.

## Out-of-contract (unchanged)

- Queue CRUD, entries, template link, and template save/load endpoints are unchanged.
- No new request fields on start/stop (bodies remain empty `{}`).
