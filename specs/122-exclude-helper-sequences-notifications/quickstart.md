# Quickstart: Exclude Helper Sequences From Success Notifications

Use this guide to check the feature by hand after implementation. The service listens on port 8080.

## 1. Set up

1. Start the service and the web UI.
2. Add one notification target. Use a fake or test target.
3. Create a queue with level "Success+Failure".
4. Put two sequences in the queue: `Helper` and `Task`. Make both succeed.

## 2. Turn the option on

1. Open the Sequences page.
2. Open `Helper`.
3. In "Basics", select "Exclude from success notifications". Read the help text.
4. Save.
5. Open the sequence list. Check that `Helper` has the badge "No success notifications" and `Task` has none.

API check:

```text
GET  /api/sequences/{helperId}   -> "excludeFromSuccessNotifications": true
```

Check that `version` and `updatedAt` of `Helper` changed after the save (FR-012).

## 2a. Check SC-001 (manual, timed)

Use a person who does not know the feature, or reset your own knowledge of where the option is.
Use no help text outside the UI.

1. Start a timer. Open the Sequences page of the web UI.
2. Find the option "Exclude from success notifications" for a sequence. Turn it on and save.
3. Stop the timer when the list shows the badge "No success notifications".
4. Pass: the time is under 1 minute. Fail: the time is 1 minute or more. If it fails, write the cause (for example, the option is hard to find) and report it.

## 3. Check the success rule

1. Start the queue. Let both sequences run.
2. Check the target. It gets `<queue> : Task : success`. It gets no message for `Helper`.

## 4. Check that failures still arrive

1. Make `Helper` fail (for example, use an unresolvable sequence reference step).
2. Check the target. It gets one "failure" message.
3. Repair `Helper` and let it succeed.
4. Check the target. It gets one "recovered" message. It gets no "success" message.

## 5. Check the save rules

1. Send a PUT for `Helper` with no `excludeFromSuccessNotifications` member.
2. Read `Helper`. The value is still `true`.
3. Send a PUT with `"excludeFromSuccessNotifications": "yes"`. The answer is 400.
4. Turn the option off in the UI and save. The next success sends a message again.
5. Clone check: send a GET for `Helper`. Send a POST with the same data and a new name. Read the new sequence. The value is `true`.
6. Cancel check: with the option on, cancel `Helper` while it runs. Then stop the queue while it runs. Each case sends one "cancelled" message.
7. Level check: set the queue level to "Failure". Then set it to "None". `Helper` and `Task` send no "success" message. This is the same as before.

## 6. Run the automated checks

```text
dotnet test (unit, contract and integration tests for notifications and sequences)
npm run build and npx jest in src/web-ui
```

The real green gate for the UI is `vite build` and `jest`.
