# Research: Resume Queues After a Service Restart

## R1. Where to remember "this queue is Running"

- **Decision**: A separate store, `IQueueRunStateStore`, backed by one JSON file `queue-run-state.json` at the data root holding the ids of queues with a live run.
- **Rationale**: `ExecutionQueue` is configuration; writing run state into it would bump `UpdatedAt` on every start and race with `PUT /api/queues/{id}`. The queue repository enumerates every `*.json` under `data/queues`, so the file cannot live there. One file with a set of ids is enough for operator-scale queue counts.
- **Alternatives considered**: a `WasRunning` field on the queue file (rejected: config/run-state mixing, `UpdatedAt` churn, edit races); one marker file per queue under a new directory (workable but more file handling for no gain); writing only at shutdown (rejected by the issue: a crash or host reboot must be covered).

## R2. When to record and when to clear

- **Decision**: Record in `StartAsync` after the device claim succeeds and before the run task launches. Clear in `RunAsync`'s `finally` unless `ApplicationStopping` is cancelled.
- **Rationale**: Every run-ending path (completion, operator stop, failure, failure-policy stop, host shutdown) already converges on that `finally`, which is also where status flips to `Stopped`. Host shutdown is exactly the case where `ApplicationStopping` is cancelled; the run's CTS is linked to it. Recording after the claim means a refused start (not found, already running, device in use) never records anything.
- **Alternatives considered**: clearing inside `StopAsync` (misses completion/failure paths, and the device watchdog's stop+start heal would need special handling); attributing via `QueueStopReason` (an operator stop and a host stop both report `StoppedManually`, so the reason cannot tell them apart).

## R3. When to resume

- **Decision**: A `BackgroundService` whose `ExecuteAsync` awaits `IHostApplicationLifetime.ApplicationStarted`, then runs one pass; dependencies are resolved from `IServiceProvider` at that point.
- **Rationale**: FR-011 — resume only once the host has fully started (Kestrel listening, other hosted initializers done), the same state a manual API start runs in. Lazy resolution mirrors `QueueDeviceWatchdogService`, which avoids building the whole queue-execution graph during host construction.
- **Alternatives considered**: `IHostedService.StartAsync` (runs before the server is listening and before later hosted services start); an `app.Lifetime.ApplicationStarted.Register` callback in `Program.cs` (constitution/memory: keep `Program.cs` thin; not unit-testable).

## R4. Store robustness

- **Decision**: In-process `SemaphoreSlim` around read-modify-write; write to a temp file then `File.Move(temp, path, overwrite: true)`. Missing or 0-byte file → empty set. Unparseable file → `ListRunningAsync` throws `InvalidDataException` (the resume pass logs it and resumes nothing); `MarkRunningAsync`/`ClearAsync` start from empty and overwrite it, so the next queue start heals the file.
- **Rationale**: A partial write must not leave a corrupt file that disables resume forever or breaks queue starts (compare the 0-byte execution-log outage), yet corruption must be visible in the log (spec edge case). The Domain store has no logger, so it signals by exception on the one read path that is logged. Starts and run ends from different queues can overlap, so writes must serialize.
- **Alternatives considered**: no locking (lost updates when two queues start together); throwing on a corrupt file from every method (would break every queue start); silently returning empty from the read (corruption would go unlogged).

## R5. Store failures never break a run

- **Decision**: `QueueExecutionService` wraps store calls in try/catch and logs a warning (new `QueueExecutionLog` event ids 1128/1129); the start and the teardown proceed.
- **Rationale**: FR-013 — the manual start/stop behaviour must be unchanged; resume is a convenience layered on top.

## R6. Test hosts

- **Decision**: No switch to disable resume. Tests that opt a queue in use their own clean data dir (`TestEnvironment.PrepareCleanDataDir()`), and the integration test creates two hosts in sequence over the same dir.
- **Rationale**: Existing queues in shared test data dirs have the flag off, so a resume pass there only discards stale records, which is harmless.
