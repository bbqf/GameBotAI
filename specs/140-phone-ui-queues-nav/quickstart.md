# Quickstart: Phone UI Queue Activity and Navigation

## Run the checks

1. Open a terminal in `C:\src\GameBot\src\web-ui`.
2. Run `npm test` (Jest). All tests must pass.
3. Run `npm run build` (`vite build`). The build must pass.

## Check by hand (phone size)

1. Start the service and open the web UI. In the browser tools, set the screen to 360 x 640.
2. Look at the tab bar. Execution is not there.
3. Open `/?area=execution`. The Queues area shows. Press back. The UI does not return to Execution.
4. Open a running queue. Check the "Now" text, the last outcome, and the schedule list in time order. Check that disabled entries are last and marked "disabled".
5. Wait 5 s. Check that the data refreshes.
6. Open a stopped queue. Check that no entry shows a next time.
7. Navigation path: Queues, open one queue, open Notifications. Press back 3 times. The UI shows the queue, then the Queues list, then the first screen. A fourth back leaves the application.
8. Open a link with `?area=queues&queue=<id>` as the first page. Tap the on-screen back control. The queue list shows. Press browser back. The browser leaves the application.
9. Open a link with `queue=unknown`. "Queue not found" shows with a button to the list.
10. Stop the service. The "connection lost" banner shows and the controls are disabled. Start it again. The state reloads.

## Check on desktop size

Open the UI at 1280 px wide. The Execution area is in the navigation and works as before.
