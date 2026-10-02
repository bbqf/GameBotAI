# Quickstart: Step-Through Sequence Execution

## For the author

1. Open a saved sequence in the Sequences page. Save it if the form has unsaved edits.
2. Click **Step through**. The button is disabled with a reason text for a new or edited sequence.
3. Select a game session. Click **Start**.
4. Click **Run next step**. Watch the emulator. Read the status in the history list.
5. To run another step, select it in the step list. Then click **Run next step**.
6. If a queue owns the session, click **Pause queue**. The queue resumes when you close the view.
7. Click **Restart** to clear the history and go back to the first step.

## For the developer

Start the service and the web UI:

```powershell
& "C:\src\GameBot\.specify\scripts\powershell\check-prerequisites.ps1" -Json
```

Run the backend tests for this feature:

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~StepThrough"
```

Run the web UI tests and the build gate:

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" run test -- StepThrough
npm --prefix "C:\src\GameBot\src\web-ui" run build
```

Try the API with the local service (port 8080):

```powershell
$body = '{"sequenceId":"<id>","gameSessionId":"<session>"}'
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/step-through" -ContentType "application/json" -Body $body
```

## Manual check list

| Step | Expected result |
|------|-----------------|
| Start on a 3-step sequence, run 3 times | Marker moves. After step 3 the view shows "complete". |
| Select step 3, run it, select step 1, run it | History has 2 entries. Marker follows the manual choice. |
| Edit the sequence in another tab, save, click **Run next step** | `sequence_changed` message. |
| Close the browser tab during a step | Step cancels after at most 90 seconds. Paused queue resumes. |
| Step a `reschedule-self` action | History shows "would reschedule at ...". The queue schedule does not change. |
