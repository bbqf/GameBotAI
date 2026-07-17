# Quickstart: connect-to-game emulator pre-heal

## What changed

A `connect-to-game` action can now optionally bring the LDPlayer emulator up first. Add an instance
identifier to the action's parameters and, if the emulator is closed or hung, the connect step starts/
restarts it before attaching the session.

## Enable it

Add `instanceName` (or `instanceIndex`) to an existing connect-to-game action, alongside `gameId` and
`adbSerial`:

```json
{ "type": "connect-to-game",
  "parameters": { "gameId": "pns", "adbSerial": "emulator-5558", "instanceName": "LDPlayer-5558" } }
```

## Behavior

| Start state (with instance id) | Result |
|--------------------------------|--------|
| emulator up + responsive       | no restart; attaches + launches game |
| emulator closed                | emulator started, then attaches + launches |
| emulator hung                  | emulator restarted, then attaches + launches |
| emulator can't recover / bad id| **connect fails**, no session started |
| non-Windows / ldconsole missing| pre-heal skipped (neutral), attaches as today |
| **no instance id**             | unchanged — no emulator management |

## Not changed

- Existing connect-to-game actions with no instance id behave exactly as before.
- The interactive "connect"/`start_session` (`/api/sessions/start`) path is unchanged; to auto-heal
  there, precede it with a standalone `ensure-emulator-running` step.
- No new configuration or environment variables — reuses feature 070's emulator knobs.

## Green gate

- Backend: `dotnet build` + `dotnet test` green.
- Web-ui: unchanged (no web-ui files touched), but `vite build` + `jest` should remain green.
