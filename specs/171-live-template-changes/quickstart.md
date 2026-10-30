# Quickstart: Apply template changes to a running queue

Use this procedure to check the feature by hand. Use a test queue on a test emulator.

## 1. Upgrade an old template (one time)

1. Open the Queues page. A template from an old release shows "Needs upgrade".
2. Select **Upgrade template**. Read the preview. The preview lists each running entry and its match.
3. Select **Confirm**. The result says how many entries got an ID.
4. Select **Upgrade template** again. The result says 0 entries changed.

API form:

```bash
curl -X POST "http://localhost:5000/api/queue-templates/<id>/upgrade?dryRun=true"
curl -X POST "http://localhost:5000/api/queue-templates/<id>/upgrade?confirm=true"
```

## 2. Change a running queue

1. Start a queue that links to the template. Wait for the first sequence.
2. Edit the template. Disable one entry, change the values of a second entry, add a third entry, and remove a
   fourth entry. Save the template.
3. On the queue, select **Pause**. The state shows "Pausing (waiting for the running sequence)". After the
   running sequence ends, the state shows "Paused (between sequences)".
4. Select **Review changes**. Check the list. The schedule changes are not selected by default.
5. Select **Apply**. The list shows the applied changes.
6. Select **Resume**. The queue continues the same run.

API form:

```bash
curl -X POST "http://localhost:5000/api/queues/<id>/pause"
curl "http://localhost:5000/api/queues/<id>/template-diff"
curl -X POST "http://localhost:5000/api/queues/<id>/apply-template" -H "Content-Type: application/json" -d "{\"baseToken\":\"<token>\",\"selectedKeys\":[\"<key>\"]}"
curl -X POST "http://localhost:5000/api/queues/<id>/resume"
```

## 3. Checks

- The run ID and the cycle count do not restart.
- An entry with a schedule in progress has the same next due time before the pause and after the resume.
- An entry that was due during the pause runs at once after the resume.
- Apply while the queue runs: the answer is 409 `not_paused`.
- Edit the template again after the review, then apply: the answer is 409 `template_changed`.
- Restart the service while paused: the queue is Stopped (or starts a fresh run that is not paused if
  `ResumeOnServiceStart` is on).
- A phone browser: Pause, Review changes, Apply, and Resume are all usable with no sideways scroll.
