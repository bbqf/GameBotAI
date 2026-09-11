# Quickstart: Verifying the Three Bug Fixes

Run against a local `GameBot.Service` instance (`GAMEBOT_USE_ADB=false` is fine for
B-003/B-005; B-001's ADB-mode branch needs `GAMEBOT_USE_ADB=true` with a real/stub
device to exercise the previously-uncovered parse-failure path — see the added
integration tests for the exact stub setup).

## B-003 — sequence creation rejects an unresolved command reference

```bash
curl -s -X POST http://localhost:8080/api/sequences \
  -H "Content-Type: application/json" \
  -d '{
        "name": "quickstart-b003",
        "steps": [
          { "stepId": "tap-1", "primitiveAction": { "type": "Command", "payload": { "commandName": "some-command" } } }
        ]
      }'
# Expect: 400, body.errors mentions step "tap-1" and a missing commandId
```

## B-005 — `requireDispatch` fails a run when nested inside a Loop

Create a sequence with a `Loop(maxIterations: 1)` whose body has one step with
`requireDispatch: true` targeting a command that will not dispatch (e.g. an image
gate that never matches), run it, then fetch its execution status:

```bash
curl -s http://localhost:8080/api/sequences/{id}/executions/{executionId}
# Expect: the nested step's outcome and the overall run status are both "Failed"
```

## B-001 — session-input route stops misreporting `409` for a healthy session

```bash
curl -s -X POST http://localhost:8080/api/sessions/{sessionId}/inputs \
  -H "Content-Type: application/json" \
  -d '{ "actions": [ { "type": "swipe", "args": { "x1": 100, "y1": 200 } } ] }'
  # (deliberately missing x2/y2)
# Expect: 400 with error.code "invalid_input_actions", NOT 409 not_running,
# for a session confirmed Running via GET /api/sessions/{sessionId}
```
