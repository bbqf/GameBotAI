# Quickstart: Verifying Dry-Run / Validate-Only Sequence Mode

Run against a local `GameBot.Service` instance with `GAMEBOT_USE_ADB=false` —
that's the whole point of this feature: no device I/O should occur, dry-run or
not, and `false` makes it easy to prove nothing tried to reach one.

## Part A — validate a candidate body without creating it (structural error)

```bash
curl -s -X POST http://localhost:8080/api/sequences \
  -H "Content-Type: application/json" \
  -d '{
        "name": "quickstart-fr002-nested-loop",
        "dryRun": true,
        "steps": [
          { "stepId": "outer-loop", "stepType": "Loop",
            "loop": { "loopType": "count", "count": 3, "maxIterations": 3 },
            "body": [
              { "stepId": "inner-loop", "stepType": "Loop",
                "loop": { "loopType": "count", "count": 2, "maxIterations": 2 },
                "body": [
                  { "stepId": "tap-1", "stepType": "Action",
                    "primitiveAction": { "type": "tap", "schemaVersion": "v1", "payload": { "x": 100, "y": 100 } } }
                ] }
            ] }
        ]
      }'
# Expect: 400 Bad Request, same error a non-dry-run create would give —
#   errors mentions nested Loop is not allowed.
```

```bash
curl -s http://localhost:8080/api/sequences | jq '.[] | select(.name == "quickstart-fr002-nested-loop")'
# Expect: no results — nothing was persisted, dry-run or not.
```

## Part B — validate a structurally valid body without creating it

```bash
curl -s -X POST http://localhost:8080/api/sequences \
  -H "Content-Type: application/json" \
  -d '{
        "name": "quickstart-fr002-valid",
        "dryRun": true,
        "steps": [
          { "stepId": "tap-1", "stepType": "Action",
            "primitiveAction": { "type": "tap", "schemaVersion": "v1", "payload": { "x": 100, "y": 100 } } }
        ]
      }'
# Expect: 200 OK  { "valid": true, "dryRun": true, "errors": [] }
```

```bash
curl -s http://localhost:8080/api/sequences | jq '.[] | select(.name == "quickstart-fr002-valid")'
# Expect: no results — a dry-run success still persists nothing.
```

## Part C — dry-run execute an existing sequence with no session running

First create the sequence for real (drop `dryRun` from Part B's body, expect
`201 Created`), noting its `id`. Then, with **no** emulator session running:

```bash
curl -s -X POST http://localhost:8080/api/sequences/{id}/execute \
  -H "Content-Type: application/json" \
  -d '{ "dryRun": true }'
# Expect: 200 OK, status "Succeeded"
#   steps[0].actionOutcome == "skipped_dry_run"
# (a real, non-dry-run execute here would instead fail immediately with
#  "no session available", since a tap step needs one)
```

## Part D — a Loop's exit reason still reflects real branch selection under `dryRun`

Author a sequence with a `Loop` whose body has a `Break` gated on a
`commandOutcome` condition referencing a parameter-driven step (not live
device state), then dry-run execute it with inputs that make the `Break` fire:

```bash
curl -s -X POST http://localhost:8080/api/sequences/{id}/execute \
  -H "Content-Type: application/json" \
  -d '{ "dryRun": true, "parameters": [ { "name": "shouldBreak", "value": "true" } ] }'
# Expect: the Loop step's entry still reports
#   "exitReason": { "brokeVia": "<break-step-id>", "exhaustedMaxIterations": false }
# exactly as a real run would — only the loop body's own dispatched action
# steps (if any) show "skipped_dry_run".
```

## Regression check — omitting `dryRun` is unchanged

Re-run Parts A-D with `dryRun` omitted entirely (or `false`): Part A/B behave
exactly as `POST /api/sequences` does today (create-then-fail / create-then-
persist), and Part C fails with the existing "no session available" error
instead of succeeding with `skipped_dry_run` steps.
