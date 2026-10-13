# Research: Emulator Monitoring

## R1. Screenshot source

- Decision: Reuse `GET /api/sessions/{id}/snapshot`. It returns a PNG, answers 504 `capture_timeout` after `CaptureTimeoutMs`, and adds staleness headers when a capture loop runs (`CaptureHeaders`).
- Rationale: FR-012 asks for a new function only if none exists. This one exists and is bounded in time.
- Alternatives: A new MJPEG or WebSocket stream (out of scope per the spec). A new "latest frame" endpoint (duplicates snapshot).
- Note: Snapshot is always a direct capture. One open request for the selected session gives the load limit of SC-005.

## R2. Session list with state

- Decision: New `GET /api/sessions/monitoring`. It returns all sessions from `ISessionManager.ListSessions()`. Each item has id, name, device serial, state, owner queue, and `restartAvailable`.
- Rationale: The existing `GET /api/sessions/running` (SessionsController) gives only running sessions and no state or queue link. The spec needs all sessions (Clarification 1).
- State rule: `restarting` if a restart runs; else `live` or `no_answer` from `ISessionLivenessService.Evaluate(session)` (data only, no device probe, so the poll is cheap). The exact rules are in the state table in data-model.md.
- Alternatives: Extend `/running` (changes an existing contract; rejected). Call `/health` for each session (does a probe for each session; rejected for load).

## R3. Restart mechanism

- Decision: Use `IEmulatorControl.RebootAsync` (ldconsole `reboot`). It stops and starts the instance in place. The existing "ensure-emulator-running" feature (070) already uses it.
- Rationale: Reuses tested code. `reboot` matches "stop the emulator process and start it again" (Clarification 2).
- Alternatives: `quit` then `launch` (two commands, more failure points; keep as a fallback only if `reboot` proves not enough in the manual check). `adb reboot` (restarts Android only, not the emulator process; rejected).

## R4. Find the LDPlayer instance of a session

- Problem: A session knows only `DeviceSerial` (for example `emulator-5558`). `ldconsole` needs a name or an index.
- Decision: New helper `LdInstanceMapper`. LDPlayer uses ADB port `5554 + 2 * index`. `emulator-5558` gives index 2. Then `isrunning --index N` must answer `running`, else the restart is refused as `restart_unavailable`.
- Rationale: No new config. It is the known LDPlayer rule and the check `isrunning` guards against a wrong map (SC-007: never touch another emulator).
- Alternatives: A new config map serial -> instance name (more operator work). `ldconsole list2` and match by port (extra parse; keep as a later improvement). A serial of the form `host:port` or a physical device gives `restart_unavailable` (FR-014).

## R5. Restart run, progress, and limit

- Decision: `SessionRestartService` (singleton). `POST` starts a background task and answers 202 at once. `GET` shows the record. The UI polls the record every 2 s while state is `restarting`.
- Steps: map serial -> check `isrunning` -> `RebootAsync` -> poll `IEmulatorDeviceProbe.IsResponsiveAsync` every `RestartPollIntervalMs` (service setting, FR-022; separate from the fixed 2 s screenshot interval) -> `ISessionReconnector.ReconnectAsync` -> one direct capture succeeds -> state `success`. Total limit 2 min (`RestartTimeoutMs` = 120000), then `failure` with reason `timeout`.
- Why a new limit: `EmulatorBootWaitMs` is 5 min and serves queue recovery. The spec fixes 2 min.
- One restart for each session: `ConcurrentDictionary<string, SemaphoreSlim>` or an atomic state swap. A second `POST` answers 409 `restart_in_progress` (FR-013).
- The task does not use the request token. A closed browser does not stop the restart.
- Alternatives: A long-held POST (breaks on proxy and phone sleep; rejected). A persisted record (not needed; single operator).

## R6. Return the session to a usable state (FR-015)

- Decision: The restart ALWAYS reconnects the session. After the device answers, `SessionRestartService` calls the new seam `ISessionReconnector.ReconnectAsync(sessionId, serial)`. The real implementation calls `BackgroundScreenCaptureService.StopCapture(id)` and then `StartCapture(id, serial)`. The session keeps its id and serial, so the UI selection and the queue owner link stay valid. Then the service does one direct capture to prove the link. This does not depend on a manual check (FR-015).
- Why not `RebindSessionAsync` of the queue: it makes a new session id and needs a queue handle. The monitoring restart must work for sessions without a queue and must keep the id.
- Test: a fake `ISessionReconnector` and a fake `IEmulatorControl` prove the call order (reboot, device answers, reconnect, capture, success) in an automated test (FR-023). If the reconnect throws, the restart ends as `failure` with reason `reconnect_failed`.
- Queue effect (Clarification 3): The service does not stop the queue. The queue sees a failed or held cycle and recovers by its normal rules. The list shows `ownerQueueId` so the UI can warn.

## R7. Web UI: polling without overlap

- Decision: New hook `useSequentialPolling`. After a request ends, it waits for the interval minus the used time, then sends the next request. It stops when the page is hidden (`document.visibilityState`) or the area is not mounted, and runs one request at once when it resumes. It aborts the open request on unmount or on a change of session.
- Rationale: FR-005 and FR-006. The existing `usePolling` uses `setInterval`, so requests can overlap when the device is slow.
- The picture: `fetch` the PNG as a blob, then `URL.createObjectURL`. Revoke the old URL after the new picture loads. The UI keeps the last good picture and its time when a request fails (edge case "no answer").

## R8. Web UI: area, layout, and selection

- Decision: New area id `monitoring`, label "Monitoring", path `/monitoring`, phone level `full`, order 1 (after Queues; changed on 2026-10-13, see FR-029 and FR-030).
- One component serves both layouts; it reads `useLayoutMode()`. The selected session id is kept in page state above the layout branch and in the URL query (`session=<id>`), so a layout change keeps it (FR-020). On a phone, the list is the first screen and the detail is the second screen with its own history entry (same method as the queue detail: `pushState`, back control calls `history.back()`).
- `phone.css` classes and `PhoneCard` are reused for the list. The primary action on the detail screen is "Restart emulator". Hover rules go in `@media (hover: hover)`.
- Connection lost: the existing `ConnectionBanner` shows. The restart button is off. The UI never sends the restart again after a connection loss; it only reads the record.

## R9. Access and logging

- Access: same token gate as the other `/api/sessions` routes. No new role (Clarification 5).
- Logging: `LoggerMessage` entries (new EventIds in the 7000 range, next free) for restart requested, step done, success, and failure, with session id and reason. Snapshot success is not logged for each frame (high volume); each failed snapshot request is logged with the session id and the result (FR-017). A test proves both rules.

## R10. Open points left for implementation

- Confirm by hand that `ldconsole reboot` returns before the instance is back (the wait loop covers both cases).
- Real-emulator check is ONE final manual step, separate from the code stories (FR-025; quickstart).

## R11. Route clash for the list route (FR-021)

- Decision: Map `monitoring` as a literal route in the same group as `{id}`. ASP.NET Core ranks a literal segment above a parameter segment, so the list wins. Contract tests prove the list route and prove that a real id still reaches `{id}`, `{id}/snapshot`, and `{id}/health`.
- Alternative: Use a different path such as `/api/monitoring/sessions` (no clash). Rejected: the spec and API docs use the sessions group; the test removes the risk.

## R12. Automated proof for each requirement (FR-023, FR-024)

- Timer: Jest fake timers on `useSequentialPolling` (2 s interval, no overlap).
- Restart: unit test with fakes for control, probe, reconnector, and a fast `RestartPollIntervalMs`.
- Navigation: update the three existing test files listed in plan.md.
