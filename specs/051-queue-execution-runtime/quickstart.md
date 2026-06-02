# Quickstart: Verifying Queue Execution Runtime

Manual verification of the queue execution engine. Backend on Windows with a connected ADB emulator exercises real execution; CI/non-Windows runs in stub mode (use the unit tests for failure paths).

## Prerequisites

- Service running (`dotnet run --project src/GameBot.Service`) and web-ui running, or use the API directly.
- At least one connected ADB device (`GET /api/adb/devices`) for real execution.
- A sequence or two authored, a queue template saved containing those sequences, and a queue bound to the connected emulator and linked to that template.

## Happy path — completed full run (US1)

1. Ensure the queue is **linked** to a non-empty template and its bound emulator is connected.
2. Start the queue: `POST /api/queues/{id}/start` → expect **200**, `status: "Running"`. (Or click Start in the Queues page.)
3. Watch the Execution Logs page: a single **Queue** top-level row appears with status `running`, then settles to `success`.
4. Expand the queue row → the template's sequences appear as ordered child rows; expand a sequence → its commands/steps nest beneath (same detail as a standalone run).
5. Confirm the queue returns to `Stopped` and the queue-run summary reads "completed full run" with the sequence count.

## Stop a run (US2)

1. Start a queue with several (or longer) sequences, or with cycle execution on.
2. While running, `POST /api/queues/{id}/stop` → expect **200**.
3. Verify within ~3 seconds: the queue returns to `Stopped`, a queue-run log entry shows "stopped manually", and the emulator session is gone (`GET /api/sessions` no longer lists the run's session).

## Cycle execution (US3)

1. Enable cycle execution on the queue (edit while stopped), link a short 2-sequence template, start it.
2. Observe the sequences repeat from the first after the last (the Execution Logs subtree grows with repeated sequence children).
3. Stop it → ends with "stopped manually". With cycle **off**, the same queue ends after one pass with "completed full run".

## Failure paths (US4)

- **No template**: unlink the queue's template (or link to one then delete it), start → run ends almost immediately with a **failure** entry "no template to run"; no sequences executed; status `Stopped`.
- **Emulator unavailable** (real ADB): stop/disconnect the emulator, start → run ends with a **failure** entry indicating the emulator could not be reached; zero sequences executed.
- **Per-sequence failure (non-fatal)**: include a sequence that fails (e.g., references a missing image/command) between two good ones; start with cycle off → the run still executes the later sequence and ends **"completed full run"**; the failed sequence's own child entry shows `failure`, and the queue summary notes how many sequences failed.

## Concurrency (FR-013 / FR-013a)

- Start the same queue twice quickly → the second `start` returns **409 already_running**; the first run is unaffected.
- Start two different queues bound to the **same** emulator → both are allowed to run (no 409 for the shared emulator).

## Automated checks

- Backend: `dotnet test` — `QueueExecutionService` tests (no-template, emulator-unavailable, in-order, per-sequence non-fatal, cycle + empty-template guard, prompt stop + disconnect, terminating entry/stop reason, already-running, concurrent same-emulator) all green; existing sequence-execution tests still green after the `ISequenceExecutionService` extraction.
- web-ui: `npm run build` + `npm test` — `executionLogGrid` shows the `queue` type label and treats queue rows as expandable; `QueuesPage.execution` reflects real Running/Stopped and a failure surface.
