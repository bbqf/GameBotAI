# Quickstart: Verifying the Condition-Model Retest

**Feature**: 103-condition-model-retest-docs | **Date**: 2026-09-17

How to reproduce each finding by hand. The automated suite is the actual evidence
(spec FR-007); this file exists so a reader can confirm a claim without reading test
code.

Base URL below assumes the service on its usual port. Replace `SEQ` ids freely — use
distinct ids per attempt, since sequences persist.

```bash
BASE=http://localhost:8080
```

---

## 1. Nested reference, written directly — should be ACCEPTED (confirmed gone)

A guard referencing a `Break` nested inside an earlier loop's body.

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/sequences" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "qs-direct-nested",
    "steps": [
      { "stepId": "loop1", "stepType": "Loop",
        "loop": { "loopType": "count", "count": 3 },
        "body": [
          { "stepId": "probe", "stepType": "Action",
            "primitiveAction": { "type": "Command", "payload": { "commandId": "c1" } } },
          { "stepId": "nested-break", "stepType": "Break",
            "breakCondition": { "type": "imageVisible", "imageId": "done-marker" } }
        ] },
      { "stepId": "gate", "stepType": "Action",
        "condition": { "type": "commandOutcome", "stepRef": "nested-break", "expectedState": "break" },
        "primitiveAction": { "type": "Command", "payload": { "commandId": "c2" } } }
    ]
  }'
# Expect: 201
# Before feature 081 this was 400 "references unknown prior step".
```

Both halves of the original ceiling are visible here at once: the reference reaches
*into* a loop body, and `expectedState` is `break`.

---

## 2. Nested reference inside a composite, dangling — should be REJECTED (the gap)

Identical except the reference is wrapped in an `all`, and names a step that does not
exist.

```bash
curl -s -X POST "$BASE/api/sequences" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "qs-composite-dangling",
    "steps": [
      { "stepId": "gate", "stepType": "Action",
        "condition": { "type": "all", "children": [
          { "type": "imageVisible", "imageId": "confirm-btn" },
          { "type": "commandOutcome", "stepRef": "no-such-step", "expectedState": "success" }
        ] },
        "primitiveAction": { "type": "Command", "payload": { "commandId": "c1" } } }
    ]
  }'
# BEFORE this feature: 201 Created — the dangling reference is accepted, then the
#   run fails with: commandOutcome reference 'no-such-step' is not available.
# AFTER this feature: 400 with
#   "Step 'gate' condition at $.children[1]: commandOutcome references unknown prior step 'no-such-step'."
```

Swap `"no-such-step"` for a step declared *later* in the same sequence to see the
ordering rule instead:

```text
"Step 'gate' condition at $.children[1]: commandOutcome stepRef 'later-step' must reference a prior step."
```

Nest one composite inside another to confirm the path renders as
`$.children[2].children[0]`.

---

## 3. Nested reference inside a composite, valid — should stay ACCEPTED

Sanity check that C-1 widens *who is checked*, not *what passes*: take example 1 and
wrap its condition in an `all` beside an `imageVisible`. Expect `201` before and
after. If this returns `400`, the fix over-reached.

---

## 4. Loop exit reason on a direct run — already readable (confirmed gone)

```bash
curl -s -X POST "$BASE/api/sequences/<SEQ>/execute" \
  -H 'Content-Type: application/json' -d '{ "sessionId": "<SESSION>" }' \
  | jq '.steps[] | select(.loopIterations != null)
        | { stepId: .commandId, status, iterations: (.loopIterations | length), exitReason }'
```

```json
{ "stepId": "loop1", "status": "Succeeded", "iterations": 2,
  "exitReason": { "brokeVia": "nested-break", "exhaustedMaxIterations": false } }
```

The three exits to try:

| Arrange | Expect |
|---|---|
| A `Break` whose condition is met on iteration 2 | `brokeVia: "<break stepId>"`, `exhaustedMaxIterations: false` |
| A count loop whose break never fires | `brokeVia: null`, `exhaustedMaxIterations: true` |
| A `while` loop whose condition goes false before the ceiling | `brokeVia: null`, `exhaustedMaxIterations: false` |

A `Break` nested inside an `If` inside the body must report the `Break`'s own
`stepId`, not the `If`'s.

---

## 5. Loop exit reason in the persisted log — the gap

Run the sequence through a queue (or any path that leaves only a log), then read the
loop step's entry:

```bash
curl -s "$BASE/api/execution-logs?limit=1" | jq -r '.items[0].executionId'
curl -s "$BASE/api/execution-logs/<EXEC_ID>" \
  | jq '.details[] | select(.attributes.stepType == "loop") | .attributes
        | { stepId, iterations, status, brokeVia, exhaustedMaxIterations }'
```

```json
// BEFORE: brokeVia and exhaustedMaxIterations are absent — the exit reason is gone,
// inferable only from the prose in "message".
{ "stepId": "loop1", "iterations": 2, "status": "Succeeded",
  "brokeVia": null, "exhaustedMaxIterations": null }

// AFTER:
{ "stepId": "loop1", "iterations": 2, "status": "Succeeded",
  "brokeVia": "nested-break", "exhaustedMaxIterations": false }
```

This is the surface that matters for a queue-driven run, which has no response to
read.

---

## 6. The web authoring UI — the original ceiling, intact

No curl for this one; it is client-side. In the sequence editor:

1. Build a sequence with a `Break` inside a loop body, then add a later step whose
   condition references that `Break` by id. **Before**: saving is blocked with
   `references unknown prior step` — the UI's own message, not the service's.
   **After**: it saves.
2. Set that condition's `expectedState` to `break`. **Before**: blocked with
   `must be one of success|failed|skipped`. **After**: accepted.

Both objections come from `validatePerStepConditions`, which runs before the request
is sent — so the service never sees the attempt and the API-level fix from feature 081
is invisible to a UI author.

**Not fixable here**: there is no way to author an `all`/`any`/`none` condition in the
UI at all. Recorded as a remaining limitation (spec FR-016a).

---

## 7. Boundary that must NOT change

```bash
# A LEAF commandOutcome directly in a while-loop condition is deliberately unvalidated
# (feature 088 decision D-006). A dangling reference here is accepted, before and after.
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/sequences" \
  -H 'Content-Type: application/json' \
  -d '{ "name": "qs-d006-boundary", "steps": [
        { "stepId": "loop1", "stepType": "Loop",
          "loop": { "loopType": "while",
                    "condition": { "type": "commandOutcome", "stepRef": "nope", "expectedState": "success" },
                    "maxIterations": 3 },
          "body": [ { "stepId": "b1", "stepType": "Action",
                      "primitiveAction": { "type": "Command", "payload": { "commandId": "c1" } } } ] } ] }'
# Expect 201 both before and after. Wrapping the same reference in an "all" here
# DOES get checked, because composites are validated in this slot and leaves are not.
```

If this starts returning `400`, the feature has crossed a boundary it was required to
leave alone (spec FR-003a).

---

## Running the evidence

```bash
dotnet test GameBot.sln
```

```bash
cd src/web-ui; npx jest src/lib/__tests__/validation.spec.ts
```

The web-ui green gate is `vite build` + `jest`; `lint` and `tsc --noEmit` carry
pre-existing failures unrelated to this feature.
