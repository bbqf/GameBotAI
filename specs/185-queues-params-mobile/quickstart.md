# Quickstart: Check the Feature by Hand

Use a queue that has a linked template with these entries:
- one Once Per Run entry,
- one Timer entry (time of day),
- one Timer entry (relative offset),
- one entry whose sequence declares a number parameter and an image parameter.

## Big screen

1. Start the web UI. Open **Queues**. Open a stopped queue.
2. Look at the cards. Check that the main row of each card has no schedule select and no timer input.
3. Check that each card shows a one-line schedule summary, for example "Daily at 14:30".
4. Select **Entry settings** on the Timer card. Check that the schedule type, the timer mode, and the time show.
5. Change the schedule type to **Once Per Run**. Check that the card moves and its panel stays open.
6. Open the panel of the entry with parameters. Check that the Schedule part shows first and the Parameters part second.
7. Open the panel of an entry with no parameters. Check that no empty Parameters part shows. Check that **Add a value** shows.
8. Drag an open card to another scheduling area. Check that its panel stays open.
9. Save the template. Open the queue again. Check that the schedule and the values stay.
10. Start the queue. Check that the editor of the running queue shows the same cards and panel.

## Phone (or a 375 px window)

1. Open the UI on the phone. Open **Queues**, then the queue.
2. Tap the row of the entry with parameters. Check that the entry page opens and the address holds `entry=`.
3. Check that the schedule shows as text and has no control.
4. Change the number value. Select **Save**. Check that **Saved.** shows. Time the steps from the queue detail (the limit is 30 seconds).
5. Select **Back**. Check that the queue detail shows at the same place in the list.
6. Open the queue on the big screen. Check that the entry shows the new value.
7. Change a value and select **Back** with no save. Check that the dialog asks to keep or discard.
8. Use the phone back button in the same state. Check that the same dialog shows and the page stays.
9. Start the queue, change a value, and save. Check that the notice "1 change waits in the template." shows on the detail.
10. Turn Wi-Fi off. Check that the banner shows and **Save** is off. Turn Wi-Fi on. Check that the app sends no save.
11. Reload the page on the entry page. Check that the same page shows.
12. Turn the phone to landscape. Check that no control is cut off.
13. Open a queue with no linked template. Check that the schedule rows are not tappable.
14. Make the sequence request fail (stop the service for a moment, then open the entry page). Check that the page shows "The parameters did not load." and **Save** is off.

## Automated checks

```bash
npm --prefix C:\src\GameBot\src\web-ui run test
npm --prefix C:\src\GameBot\src\web-ui run lint
npx --prefix C:\src\GameBot\src\web-ui playwright test editor-row-height phone-entry-page
```
