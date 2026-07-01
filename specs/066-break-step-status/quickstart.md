# Quickstart / Manual Verification: Break Step Success/Failure Statuses

Prerequisites: backend service running and the web UI served, with an emulator/session available
(or use the unit tests below for a no-device check).

## Automated check (fastest, no device)

```powershell
# Backend — break/loop behavior + mapping
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~SequenceRunnerLoopTests|FullyQualifiedName~MapStepStatus"

# web-ui — real green gate (lint/tsc have pre-existing failures; do NOT gate on them)
npm --prefix "C:\src\GameBot\src\web-ui" run build
npm --prefix "C:\src\GameBot\src\web-ui" test
```

Expected: break tests show fired→success, condition-false→`no_break` (not `Skipped`),
condition-error→`no_break` + loop continues + run `Succeeded`; the rewritten error test no longer
asserts a failed loop. web-ui tests render a distinct "No break" badge.

## Manual UI walkthrough

1. **Author a loop with a conditional break** in the sequence editor: a count loop with a body
   step followed by a Break step whose condition is `imageVisible(imageId=...)` for an image that
   is currently NOT on screen.
2. **Run the sequence** and open its entry in **Execution Logs**.
3. **Expand the loop.** For each iteration where the image was absent, the break step row shows a
   distinct neutral **"No break"** badge — **not** a red "Failed", and **not** "Skipped".
   - Verify the body step still ran every iteration and the loop ran to its full count
     (FR-006).
   - Verify the loop row and the overall run row are **Succeeded** (green), not failed
     (FR-004/FR-005).
4. **Make the break fire**: arrange for the image to be visible so the condition becomes true (or
   use an unconditional "Always break"). Re-run.
   - The break step row shows a **success**; the loop ends at that iteration (break fired).
5. **Break condition error path** (FR-002a): configure a break condition that cannot be evaluated
   (e.g. a missing/invalid image reference). Re-run.
   - The break step row shows **"No break"** (with the error detail in its message), the loop
     continues, and the run is **Succeeded** — it does **not** abort the sequence.
6. **Loop-level `breakOn`** (FR-010): for a while-style loop using a `breakOn` condition, confirm
   that a `breakOn` which never becomes true lets the loop run normally and the run stays
   Succeeded, and that a `breakOn` whose condition errors does not abort the run.

## What to confirm against the spec

- SC-001: fired→success, non-firing→"No break"; no `Skipped` for break steps anywhere.
- SC-002: loop + run report success when the break never fires.
- SC-003: same number of iterations / same actions as before the change.
- SC-004: run failure counts/alerts show zero contribution from non-firing breaks.
