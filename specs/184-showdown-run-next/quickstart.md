# Quickstart: Run Next for Unscheduled Tasks

## Check the feature by hand

1. Create a template with three entries: A (once per run), B (once per run), C (timer, relative `00:00:05`).
   Link the template to a queue with cycle execution off.
2. Start the queue. Wait until A, B, and C finish. The run stays alive only if something is pending, so
   add a fourth entry D with a timer 10 minutes away to keep it alive.
3. Open Queues, then Edit the running queue. Entry A shows the state `idle`.
4. Press **Run next A**. Check that A runs once, and that B, C, and D do not change.
5. Press **Run next A** two times fast. Check that A runs once.
6. Press **Pause**. Press **Run next B**. Check that B shows "Queued to run next". Press **Resume**. Check that B runs.
7. Check that **Run next D** is off, with the tooltip "Scheduled. Use the timer to change it."
8. Stop the queue. Check that all **Run next** buttons are off.

## Phone check (375 px)

1. Open the queue detail of the running queue on a phone, or use a 375 px browser window.
2. Check that each idle row has a **Run next** button of 44 x 44 px, and that the page has no sideways scroll.
3. Tap the button. Check that the row shows "Queued to run next", and that it shows "Running" a moment later.
4. Turn Wi-Fi off. Check that the button is off and the banner "Connection lost" shows.

## Editor layout check (1024 px)

1. Open the template editor with 10 entries.
2. Check that each row uses 1 line, that each action is an icon, and that each icon has a tooltip.

## Run the tests

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~RunNext"
npm --prefix "C:\src\GameBot\src\web-ui" run test -- RunNext SchedulingSequenceCard PhoneQueuesView
```
