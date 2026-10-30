# Live template changes

Last reviewed: 2026-10-30 (feature 171)

Use this guide to change a queue that runs, with no stop of the run. You pause the queue between two sequences. You
apply the changes of its template. Then you resume the same run. No schedule changes, unless you select a schedule change.

## Terms

- **Pause**: the queue lets the sequence that runs finish. Then it starts no new sequence until you resume. The run stays
  active. The queue keeps its cycle position and all its schedules. The state shows as "Pausing (waiting for the running
  sequence)" and then as "Paused (between sequences)".
- **Resume**: the queue goes on with the same run. A sequence that came due during the pause runs at once, with its
  original due time.
- **Firing**: a scheduled sequence and its helper sequences (the "Before each run" and "After every step" sequences). The
  pause waits for the whole firing to end. The screen calls it a "sequence".
- **Entry ID**: the stable name of one entry of a template. The system creates it one time. It does not change when you edit,
  move, or disable the entry. Two entries of one sequence have two IDs.
- **Pause and Pause when idle**: these are two things. "Pause when idle" is a setting of the queue. It backs the game out to
  the home screen during a long gap between two sequences, and it ends by itself. The pause of this guide is a hold that you
  start and you end. While the queue is paused, the system does not change the game state. If you pause the queue during
  a "Pause when idle" hold, the hold ends early and the game stays backed out. The resume brings the game to the front.

## Change a running queue

1. Open the Queues page. Find the queue that runs.
2. Select **Pause**. Wait for "Paused (between sequences)". A long sequence can take some time to end.
3. Edit the template of the queue and save it. You can do this before step 2 also.
4. Select **Review changes**. The list shows each difference between the running queue and the template.
5. Clear the changes that you do not want. A schedule change is not selected at first. Select it only if you want it.
6. Select **Apply**. All selected changes go in, or none of them.
7. Select **Resume**. The queue goes on with the same run.

The pause has no time limit. A paused queue waits until you resume or stop it. The page shows how long the queue is paused.

## What you can change

| Change | What the apply does |
|--------|---------------------|
| Add an entry | The entry joins the run with the schedule that the template states. |
| Remove an entry | The entry never runs again in this run. Its timer state goes away. |
| Disable an entry | The entry does not run. Its timer state stays. |
| Enable an entry | The entry runs by its schedule. A timer that is due runs as late, with its original due time. |
| Parameters | The next firing of the entry uses the new values, or the new sequence. |
| Schedule | The entry gets the new schedule, and its timer state starts again. Select it on purpose. |
| Order | The entries take the order of the template. No timer changes. |

## Schedules stay the same

- A pause, an apply, and a resume do not reset, move, or skip a timer.
- An entry that you did not change keeps its next due time. This holds also when you add, remove, or move other entries.
- An entry that is due while the queue is paused runs when you resume, with its original due time.
- A time-of-day entry that fired today does not fire again today. A relative entry that fired does not fire again in this run.
- An entry that ran in the present cycle does not run again in that cycle after the apply.

## Limits

- You can apply only to a queue that is paused, and only from its linked template.
- An "At queue start" entry that you add during a run does not run in this run. That schedule means "once, at the start".
- In a run that does not cycle, the "Once per run" pass runs one time. An entry that you add after that pass does not run
  in this run.
- A service restart ends the run and the pause. The queue is stopped. If **Resume on service start** is on, it starts a fresh run
  that is not paused.
- The time limit of a sequence still applies. If the sequence that runs reaches its limit, the limit ends it. Then the
  pause starts. A paused queue has no running sequence, so no time limit ends a paused run.
- The liveness watch and the device recovery keep running while the queue is paused.

## Upgrade an old template

A template that an older release saved has no entry IDs. It works as before. The page marks it **Needs upgrade**. You cannot
apply changes to a running queue from it until you upgrade it. The upgrade gives an ID to each entry. It changes nothing else.

Steps:

1. Open the queue and look at the template. A template that needs an upgrade shows **Needs upgrade**. You also see it in the
   review of a running queue.
2. Select **Upgrade template**. Read the preview. It tells how many entries get an ID.
3. If a running queue uses the template, the preview lists the running entries that get the same IDs. Their schedules do not
   change. If the preview lists entries that it cannot match, stop the queue or reload the template. Then upgrade again.
4. Select **Confirm upgrade**. The result tells how many entries got an ID.
5. Check the result. Select **Upgrade template** again. The result must tell "0 entries changed". The mark is gone.

Do the upgrade before you edit the template, when a queue runs. The preview compares the running entries with the template as
it is now.

The API form of the same steps:

```bash
curl -X POST "http://localhost:5000/api/queue-templates/<id>/upgrade?dryRun=true"
curl -X POST "http://localhost:5000/api/queue-templates/<id>/upgrade?confirm=true"
```

## API summary

```bash
curl -X POST "http://localhost:5000/api/queues/<id>/pause"
curl "http://localhost:5000/api/queues/<id>/template-diff"
curl -X POST "http://localhost:5000/api/queues/<id>/apply-template" -H "Content-Type: application/json" -d "{\"baseToken\":\"<token>\",\"selectedKeys\":[\"<key>\"]}"
curl -X POST "http://localhost:5000/api/queues/<id>/resume"
```

`GET /api/queues/<id>` shows `health.operatorPause` (`none`, `pausing`, or `paused`). When a failure-policy pause and the operator pause are
both in force, `pauseKind` is `failurePolicy`, and the first `resume` ends the operator pause. A second `resume` ends the other.

## If something is refused

| Code | Meaning | What to do |
|------|---------|------------|
| `not_running` | The queue does not run. | Start the queue first. |
| `not_paused` | The queue is not paused. | Pause the queue first. |
| `needs_upgrade` | The template has entries with no ID. | Upgrade the template first. |
| `template_changed` | The template changed after you read the differences. | Review the changes again. |
| `apply_in_progress` | Another apply runs for this queue. | Wait for it to end. |
| `invalid_request` | A part of the change is not valid, for example a sequence that no longer exists. The queue is unchanged. | Fix the template, save it, and review again. |
| `confirm_required` | A running queue uses the template. | Read the preview, then send `confirm=true`. |
| `unmatched_entries` | The upgrade cannot match some running entries. | Stop the queue or reload the template. |

## Phone

The pause, review, apply, resume, and upgrade controls work on a phone, in one column, with no sideways scroll. Each control
is at least 44 px high. You cannot edit a template on a phone. Edit it on a larger screen, then use the phone to pause, review,
apply, and resume.

## Records

Each pause request, pause, cancel, resume, and apply writes a line in the service log. The apply line lists the applied
differences. Find them by the event IDs 1103, 1104, 1134, 1135, and 1136.
