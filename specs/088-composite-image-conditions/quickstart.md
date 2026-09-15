# Quickstart: Guarding a step on two images at once

**Feature**: 088-composite-image-conditions | **Date**: 2026-09-15

The problem this solves: a button that looks identical in two different dialogs. A guard built on that button alone fires on both. Combine it with something unique to the dialog you actually want.

Assumes the service is running on its configured port (`8080` by default in this project's deployments).

## 1. Capture the distinguishing template

You need a second reference image that appears in one dialog and not the other — usually the dialog's title text. Capture and crop it the usual way, then confirm it discriminates:

```bash
curl -s -X POST http://localhost:8080/api/images/detect-all -H "Content-Type: application/json" -d "{\"captureId\":\"<captureId>\"}"
```

Run it once with the wanted dialog on screen and once with the look-alike. The new template must score high on the first and low on the second. If it scores high on both, it is not a distinguishing signal — find another region.

## 2. Write the guard

Wrap the ambiguous button and the distinguishing title in an `all`:

```jsonc
"condition": {
  "type": "all",
  "children": [
    { "type": "imageVisible", "imageId": "pns-disconnect-confirm", "minSimilarity": 0.85 },
    { "type": "imageVisible", "imageId": "pns-disconnect-title",   "minSimilarity": 0.85 }
  ]
}
```

Or, when the wanted dialog has no unique anchor of its own but the unwanted one does, exclude the look-alike instead:

```jsonc
"condition": {
  "type": "all",
  "children": [
    { "type": "imageVisible", "imageId": "pns-disconnect-confirm", "minSimilarity": 0.85 },
    { "type": "imageVisible", "imageId": "pns-gas-dialog-title",   "minSimilarity": 0.85, "negate": true }
  ]
}
```

Prefer the exclusion form when the safety risk is asymmetric — here, tapping the gas dialog is the harmful outcome, so gating explicitly on "the gas dialog is not up" fails safe even if the disconnect dialog's own art changes.

Put the cheapest or most selective child first: `all` stops at the first false child.

## 3. Save it

```bash
curl -s -X PUT http://localhost:8080/api/sequences/<id> -H "Content-Type: application/json" -d @sequence.json
```

A malformed composite comes back as a 400 with a path-rooted message naming the offending child, for example `Step 'dismiss-disconnect' condition at $.children[1]: imageVisible condition requires imageId.` Fix and re-save; nothing is stored until it validates.

## 4. Prove it on the look-alike

This is the step worth not skipping, and it needs a **real** run, not a dry run. `dryRun: true` never reads the live screen, so every image condition evaluates false and the guarded step reports `Skipped` whatever is on screen — which looks exactly like the guard working.

An image condition also only sees the screen when the run has a capture session behind it: a session created with `POST /api/sessions/start`, not a bare `POST /api/sessions`. Without one, `imageVisible` is always false and you will again be reading a false pass.

So: bring up the **wrong** dialog, run the sequence for real, and read the execution log for that step.

```bash
curl -s -X POST http://localhost:8080/api/sequences/<id>/execute -H "Content-Type: application/json" -d "{\"sessionId\":\"<capture-session-id>\"}"
```

The guarded step must report `Skipped`, with `conditionType` showing the rule (`all`) and the message naming the child that settled it — that is the line proving the *second* signal is what stopped it, not a general failure to see anything. Then bring up the right dialog and confirm the same step runs.

A guard that has only been seen passing has not been tested.

## Limits

- 1 to 16 children per composite; an empty `children` list is rejected at save time.
- Nesting depth at most 4 — a composite of composites of composites of leaves.
- `any` and `none` work the same way in every position: step guards, `if` branches, loop conditions and break conditions.
- Children of one composite are judged against the same screen observation, so a two-image guard costs no extra capture.
