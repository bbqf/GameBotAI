# Quickstart: Verifying Loop Exit Reason & Nested `stepRef` Resolution

Run against a local `GameBot.Service` instance (`GAMEBOT_USE_ADB=false` is fine —
both changes are pure sequence-authoring/execution-engine behavior, no device I/O
involved).

## Part A — a Loop's result reveals why it stopped

Create a sequence with a `Loop(maxIterations: 3)` whose body has a conditional
`Break` that fires once a counter parameter reaches a threshold, run it twice (once
with input that makes the break fire, once that exhausts the loop), then inspect
each run's result:

```bash
curl -s http://localhost:8080/api/sequences/{id}/executions/{executionId}
# Break-fired run — expect the Loop step's entry to include:
#   "exitReason": { "brokeVia": "<break-step-id>", "exhaustedMaxIterations": false }
# Exhausted run — expect:
#   "exitReason": { "brokeVia": null, "exhaustedMaxIterations": true }
```

## Part B — a condition can reference a nested `Break` from anywhere in the sequence

Author a sequence where a `Break` step lives inside one `Loop` body, and a separate
top-level step's own `condition` gate (not its `if.condition` — see
[contracts/loop-exit-reason-and-stepref-scope.md](contracts/loop-exit-reason-and-stepref-scope.md)
for why that distinction matters) — not a sibling of that `Break` — checks whether
it fired:

```bash
curl -s -X POST http://localhost:8080/api/sequences \
  -H "Content-Type: application/json" \
  -d '{
        "name": "quickstart-fr001-partb",
        "steps": [
          { "stepId": "loop-1", "loop": { "loopType": "count", "count": 3 },
            "body": [
              { "stepId": "cluster-not-found-break", "stepType": "Break",
                "breakCondition": { "type": "imageVisible", "imageId": "no-cluster" } }
            ] },
          { "stepId": "gate",
            "condition": { "type": "commandOutcome", "stepRef": "cluster-not-found-break", "expectedState": "break" },
            "primitiveAction": { "type": "Command", "payload": { "commandId": "fail-c4" } } }
        ]
      }'
# Before this feature: 400, errors mentions "gate" references unknown prior step
#   "cluster-not-found-break"
# After this feature: 201 Created — the reference resolves
```

```bash
curl -s http://localhost:8080/api/sequences/{id}/executions/{executionId}
# Break fired this run   -> gate's condition evaluates true  -> gate's action runs
# Break did not fire     -> gate's condition evaluates false -> gate is skipped
```

## Regression check — ordering is still enforced

The same request with `stepRef` pointed at a step that appears **after** `gate` in
the sequence must still be rejected:

```bash
curl -s -X POST http://localhost:8080/api/sequences \
  -H "Content-Type: application/json" \
  -d '{ "name": "quickstart-fr001-ordering", "steps": [
        { "stepId": "gate",
          "condition": { "type": "commandOutcome", "stepRef": "later-step", "expectedState": "success" },
          "primitiveAction": { "type": "Command", "payload": { "commandId": "noop" } } },
        { "stepId": "later-step", "primitiveAction": { "type": "Command", "payload": { "commandId": "noop" } } }
      ] }'
# Expect: 400, errors mentions "gate" ... stepRef 'later-step' must reference a prior step
```
