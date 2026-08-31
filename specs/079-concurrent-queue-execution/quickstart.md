# Quickstart: Running Several Queues At Once

After feature 079, the rule is simple: **one queue per emulator, as many emulators as you like** (up to
the configured session limit, default 8).

---

## 1. Give each queue its own emulator

Each queue is bound to one ADB serial (`emulatorSerial`, immutable after creation). Two queues that
should run at the same time must have **different** serials.

```bash
curl -s -H "Authorization: Bearer $GAMEBOT_AUTH_TOKEN" http://localhost:8080/api/queues | jq -r '.[] | "\(.name)\t\(.emulatorSerial)"'
```

(Every call below needs the same `Authorization` header when `GAMEBOT_AUTH_TOKEN` is set; it is
omitted from the remaining examples for brevity.)

If two queues share a serial, split the work differently: put the sequences into one template on one
queue, or create a second LDPlayer instance and bind the second queue to its serial.

## 2. Start them

Start each queue as usual. Starts are independent:

```bash
curl -s -X POST http://localhost:8080/api/queues/<queueA-id>/start
curl -s -X POST http://localhost:8080/api/queues/<queueB-id>/start
```

If the second start returns `409` with:

```json
{ "error": { "code": "device_in_use", "message": "Emulator 'emulator-5558' is already in use by queue 'PNS Daily 5558'. Stop that queue before starting this one." } }
```

then both queues are bound to the same emulator. Stop the holding queue, or re-point one queue at a
different instance.

Code `already_running` is a different thing: that queue itself is already running.

## 3. Watch them

The monitor now reports which device each run holds:

```bash
curl -s http://localhost:8080/api/queues/<queueA-id>/monitor | jq '{name, running, deviceSerial, current}'
```

Each running queue shows its own device and its own current sequence. Nothing is shared between them.

## 4. Take a screenshot of a specific device

With more than one session active, screen capture needs to know which one you mean:

```bash
# by session
curl -s "http://localhost:8080/api/emulator/screenshot?sessionId=<id>" -o shot.png
# by device serial
curl -s "http://localhost:8080/api/emulator/screenshot?serial=emulator-5560" -o shot.png
```

Without a selector and with several sessions active you now get `409 ambiguous_session` instead of an
arbitrary device. With exactly one session active, nothing changes -- the bare call still works.

**Author reference images one device at a time.** Cropping from the wrong emulator produces a
reference image that fails everywhere else.

## 5. Raise the ceiling if you need more than 8

```jsonc
// appsettings.json
{ "Service": { "Sessions": { "MaxConcurrentSessions": 12 } } }
```

A run that hits the ceiling fails with an explicit message naming the limit, e.g.
`session capacity reached: 8 of 8 sessions are open (Service:Sessions:MaxConcurrentSessions)`.

---

## What did *not* change

- Single-queue behavior. With one queue running, every device-resolution path lands on the same
  session it always did, and no call needs a new parameter.
- Scheduling. AtQueueStart / OncePerRun / EveryStep / Timer semantics, self-reschedule, live schedules,
  idle pause and cycling are all untouched.
- Sequences and templates. No sequence, command or template needs editing to run concurrently. If you
  want one template to serve several emulators, that is feature 078's queue parameters
  (`{{queue.emulatorSerial}}` and friends) and it already works.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `device_in_use` on start | Two queues share an emulator serial | Stop the holder, or bind one queue to a different instance |
| Step fails: `N device sessions are active; specify a sessionId for '<step>'` | A step ran outside any queue run while several sessions were open | Run it from its queue, or pass an explicit `sessionId` |
| Run fails: `session capacity reached: N of N sessions are open` | More concurrent runs than the configured limit | Raise `Service:Sessions:MaxConcurrentSessions` |
| `409 ambiguous_session` from screenshot | Several sessions active, no selector given | Add `?sessionId=` or `?serial=` |
