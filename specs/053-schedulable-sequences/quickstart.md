# Quickstart: Queue Sequence Scheduling

**Feature**: 053-schedulable-sequences

## End-to-end verification

### Prerequisites

- GameBot service running locally
- At least two sequences created (e.g., "Collect Resources", "Check Health")
- A queue configured with a bound emulator and a linked template

---

### 1. Save a template with all three schedule types (API)

```http
POST /api/queue-templates
Content-Type: application/json

{
  "name": "Scheduling Test",
  "overwrite": true,
  "entries": [
    { "sequenceId": "<once-per-run-sequence-id>" },
    { "sequenceId": "<every-step-sequence-id>", "scheduleType": "EveryStep" },
    { "sequenceId": "<timer-sequence-id>", "scheduleType": "Timer", "timerTimeOfDay": "HH:MM" }
  ]
}
```

Replace `HH:MM` with a time 1-2 minutes from now (server local time) for quick testing, or a past time for immediate firing.

**Expected**: 201 Created; response entries include `scheduleType` and `timerTimeOfDay` fields.

---

### 2. Verify retrieval round-trips schedule types

```http
GET /api/queue-templates/<id>
```

**Expected**: `entries[0].scheduleType == "OncePerRun"`, `entries[1].scheduleType == "EveryStep"`, `entries[2].scheduleType == "Timer"` and `entries[2].timerTimeOfDay == "HH:MM"`.

---

### 3. Load the template into a queue and start execution

In the UI:
1. Open the queue editor.
2. Load "Scheduling Test" template.
3. Verify that each entry displays its schedule type badge/label.
4. Start the queue.

**Expected during run (check execution log)**:
- If timer time has passed: timer sequence fires first, before the once-per-run sequence.
- Once-per-run sequence executes.
- Every-step sequence executes immediately after the once-per-run sequence.
- Execution log shows all three sequence executions nested under the queue run entry.

---

### 4. Verify every-step does not count toward completion

With a template of 2 once-per-run and 1 every-step:
- Start a non-cyclic queue.
- Observe: run completes after both once-per-run sequences finish (every-step runs twice but doesn't extend the run).
- Execution log stop reason: "completed full run".

---

### 5. Verify timer does not re-fire in the same calendar day within one run

With a timer set to a past time:
- Start a cyclic queue.
- Observe: timer fires on the first iteration, skipped on all subsequent iterations of the same calendar day.
- On the next calendar day (if testing long-running queue): timer fires again at the start of the first iteration after the scheduled time.

---

### 6. Verify backward compatibility

Load a template that was saved before this feature (no `scheduleType` in its JSON file).
- Expected: all entries behave as "OncePerRun" — existing queue behavior is unchanged.

---

## UI checklist

- [ ] Schedule type selector visible per entry in queue entry list
- [ ] Timer time input appears only when "Timer" is selected
- [ ] Badge/label distinguishes "EveryStep" and "Timer" entries at a glance
- [ ] Template save includes schedule types (verify via GET after save)
- [ ] Template load restores schedule types into queue editor
