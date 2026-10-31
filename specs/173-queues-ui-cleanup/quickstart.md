# Quickstart: Check the Queues page cleanup (173)

Use these steps after the build. Use two queues on two emulators, or one queue with two entries.

## Big screen

1. Open Queues on a wide window. Check the columns: Name, Cycle, Status, Activity, Actions. There is no Emulator column.
2. Check that each action is an icon. Move the pointer over each icon. Check the tooltip.
3. Start a queue. Check that the row shows Pause, Stop, Edit, Duplicate, and a disabled Delete. Check that there is no Schedule button.
4. Check the Activity text: "Running: <sequence>" or "Next run at <time>".
5. Press Pause. Check that the Activity text shows "Pausing" and then "Paused".
6. Press Edit on the running queue. Check that the page shows the pause controls, the monitor, the settings, and the template editor.
7. Check that the emulator field is read-only. Change the queue name. Press Save. Check the note "Settings apply at the next start."
8. In the editor, turn one entry off. Press Save template. Check that the queue does not stop.
9. Check the notice "1 change waits" and the Review button. Pause the queue. Press Review. Check the one-sentence explanation. Apply. Resume.
10. Check that the notice is gone after the apply.

## Running queue with no template

1. Start a queue that has no linked template. Press Edit.
2. Check the entries are read-only and the message "Link a template to edit this queue while it runs." shows.
3. Link a template. Check that the editor opens.

## Phone

1. Open the UI on a phone, or resize the window to 390 px wide.
2. Check that each queue card shows the same Activity text as the big screen. Pause one queue. Check "Paused" on the card.
3. Open the running queue. Turn one entry off with its switch. Check that the notice "1 change waits" shows.
4. Press Review. Pause the queue. Apply. Resume.
5. Check that no control is smaller than 44 px and there is no sideways scroll.

## Automatic checks

- Run the web UI tests: `npm test` in `src/web-ui`.
- Run the service tests for the queue endpoints.
- Run the shard balance check when a test project changes (`scripts/ci/measure-shards.ps1`).
