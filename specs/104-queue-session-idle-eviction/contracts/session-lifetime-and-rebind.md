# Contract: session lifetime, queue re-bind, screenshot 404 (#217)

## C-1 — Queue-owned sessions are not idle-retired

- A session a queue binds carries the queue's id as its owner.
- The idle sweep (`Service:Sessions:IdleTimeoutSeconds`) skips owned sessions. Ad-hoc sessions
  (`POST /api/sessions`, `POST /api/sessions/start`) are retired exactly as before.
- An owned session ends when its queue's run ends (it is stopped in the run's teardown) or when
  something calls stop on it explicitly.

## C-2 — Re-bind at a firing

- Before every firing the queue checks its session. If it is missing, the queue makes **one**
  attempt to bind a new owned session on `queue.EmulatorSerial`, moves background capture to it,
  and continues; the Warning log `SessionRebound` records queue id, serial, old and new session id.
- If that attempt fails (serial not listed by ADB, no ADB devices, or session capacity reached),
  the run fails with the unchanged reason `emulator connection lost mid-run ('<serial>')`.
- The before-each-run pass performs this check before its first entry, so a re-bound wake-up runs
  its before-each-run entries and then the due firing.
- The idle-pause resume step does not re-bind.

## C-3 — `GET /api/emulator/screenshot?serial=<serial>` with no bound session

Unchanged status and code, new message:

```json
404
{
  "error": "session_not_found",
  "message": "No running session is bound to device '<serial>'. Start a session (POST /api/sessions/start) or a queue on that device to bind one."
}
```

A running queue's serial always has a bound session (C-1), so a queue idle past the timeout keeps
serving screenshots.
