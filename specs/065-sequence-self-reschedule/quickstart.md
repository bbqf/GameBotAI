# Quickstart: Sequence Self-Rescheduling

Manual verification of the four user stories. Assumes the GameBot service is running with the built
web UI, an emulator reachable, and at least one queue linked to a template containing a sequence.

## Prerequisites
- A sequence `S` (e.g. "Daily Reward") that you can edit in the **Sequences** page.
- A queue `Q` bound to an emulator, linked to a template whose entries include `S` as a once-per-run
  entry.

---

## US1 — Author a self-reschedule action gated by an IF condition (P1)

1. Open **Sequences → S** in the editor.
2. Add an **IF** branch with a condition you can force true (e.g. an image you can show on screen, or
   a command-outcome condition).
3. Inside the true branch, add a step → action type **Reschedule this sequence**.
4. Choose option **Once Per Run**. Save.
5. Re-open `S`; confirm the reschedule action is still present inside the IF branch (round-trip).
6. Start `Q` with the condition forced **true**: confirm in **Execution Logs** that `S` fires a
   **second time** during the same run as a direct result of the action.
7. Stop `Q`, force the condition **false**, run again: confirm `S` fires only once (no extra firing).

**Expected**: action visible/configurable under the IF branch (AS-3); fires again when true (AS-1);
no extra firing when false (AS-2); the remaining steps of `S` still run after the action (AS-4).

---

## US2 — Choose any schedule option when rescheduling (P1)

Repeat the authoring step, changing the option each time, and observe the timing in Execution Logs:

| Option configured | Expected firing |
|-------------------|-----------------|
| **Once Per Run** | `S` fires again within the current run, as an extra normal step |
| **Timer → relative offset** `00:10:00` | `S` fires once ~10 min after the action executed, at the first iteration boundary at/after that instant |
| **Timer → time-of-day** `HH:MM` | `S` fires once at the first boundary at/after that wall-clock time |
| **After Every Step** | `S` fires after each subsequent normal step for the rest of the run — and the reschedule action does **not** spawn an endless chain |
| **At Queue Start** (cycling `Q`) | `S` fires at the start of the next cycle |
| **At Queue Start** (non-cycling `Q`) | `S` fires at the next iteration boundary (fallback) |

Also confirm: when **Timer** is selected the time-of-day / relative-offset inputs appear and match the
queue-template editor's timer inputs (AS-6).

---

## US3 — No-op with success when not started from a queue (P1)

1. Keep the self-reschedule action in `S` (any option).
2. From **Sequences → S**, run `S` **standalone** (execute from the authoring UI), not via a queue.
3. Open **Execution Logs** for that standalone run.

**Expected**: the action appears with a **success** status and a note that there was **no originating
queue, so no reschedule was performed** (AS-2); nothing is scheduled anywhere (AS-1); the remaining
steps of `S` run and the overall outcome is unaffected (AS-3).

---

## US4 — See the reschedule reflected in the execution logs (P2)

1. Run `Q` so the action fires (e.g. Timer relative `00:01:00`).
2. Let the rescheduled firing occur, then open **Execution Logs** for the run.

**Expected**:
- The **action entry** records the chosen option, the resolved timing (e.g. the target instant for a
  timer), and that the schedule applies to the current run only (AS-1).
- The **rescheduled firing** appears as an executed sequence in the run, consistent with other
  live/scheduled firings, attributable to `S` (AS-2).
- If you stop the run before a timer becomes due, the action entry shows the reschedule was **accepted
  but did not fire within the run**, and the run is **not** marked failed (AS-3).
- For the US3 standalone case, the entry clearly reads **"no-op, not started from a queue"** vs.
  "scheduled" (AS-4).

---

## Regression checks
- A self-reschedule never appears in `Q`'s saved template after the run ends (SC-005) — re-open the
  template editor and confirm no extra entry was added.
- Existing template/live scheduling (features 053/059/060) behaves unchanged.

## Automated gates
- Backend: `dotnet test` (unit/contract/integration green; coverage ≥80% line / ≥70% branch for
  touched areas).
- Web UI: `vite build` **and** `jest` both green (the real web-ui gate).
