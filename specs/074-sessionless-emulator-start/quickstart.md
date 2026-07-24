# Quickstart: Cold-Start the Emulator From a Backend-Only State

This feature lets a scheduled queue start its LDPlayer emulator instance itself when the machine has
**only the backend service running** (no session, emulator closed) — so unattended automation
self-recovers instead of failing at session creation.

## Enable it on a queue

Set the queue's optional emulator-instance identifier (name or index). The queue reuses its existing
emulator serial for the responsiveness probe.

Via REST (create or update):

```bash
curl -X PUT http://localhost:8080/api/queues/<queueId> \
  -H "Content-Type: application/json" \
  -d '{ "name": "PNS Daily 5558", "cycleExecution": false, "emulatorInstanceName": "PNS" }'
```

Or in the web-ui: open the queue's configuration and fill in **Emulator instance name** (or **index**).

Leave both blank to keep today's behavior (no emulator management).

## Verify the cold-start (the whole point)

1. Close the emulator entirely. Ensure no session exists — only the backend service is running.
2. Start the queue (schedule fires, or start it manually).
3. Expected: the queue starts the emulator instance, waits for it to become responsive, then creates
   its session and runs its sequences — **no manual emulator launch or session start needed**.

## Behavior summary

- **Emulator already up** → no restart; queue runs exactly as today.
- **Emulator closed / hung** → started / restarted, then the queue proceeds.
- **Instance can't be brought up** (never boots, or the name/index matches nothing) → the queue run
  fails with a clear reason and does **not** create a session.
- **Non-Windows host / emulator tooling missing** → neutral "not-applied"; the queue proceeds to create
  its session exactly as before (no new failure).
- **Fields unset** → the queue does no emulator work at all (100% backward compatible).

## Notes

- No new emulator-tuning settings: the responsiveness-probe timeout (10 s), boot wait (120 s), and poll
  interval (3 s) are inherited from feature 070 and overridable via the existing configuration.
- When both name and index are set, the **name** takes precedence.
- The cold-start runs once at queue start, before the session; the outcome is recorded in the queue's
  logs / stop reason.
