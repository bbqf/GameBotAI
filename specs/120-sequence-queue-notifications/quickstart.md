# Quickstart: Queue Sequence Notifications

## Set up a Telegram target (UI)

1. Open the GameBot UI. Select "Notifications" in the navigation.
2. Read the guide. Open Telegram. Talk to `@BotFather`. Send `/newbot`. Follow the steps.
3. Copy the bot token that BotFather gives you.
4. Open a chat with your new bot and send it one message. You can also add the bot to a group.
5. Find the chat ID. Open `https://api.telegram.org/bot<token>/getUpdates` in a browser. Find
   `"chat":{"id": ...}` in the answer. Copy the number.
6. In the UI, select "Add target". Choose "Telegram". Enter a name, the bot token, and the chat ID.
   Save.
   Or select "Save and test". It saves the target and then sends the test message.
7. Select "Send test message". Check that the message arrives on your phone.
   The pass criterion is a message on the phone and "ok" in the UI.
8. In the queue table, set the level of a queue to "Failure" or "Success+Failure".

## Set up without the UI (API)

```powershell
$h = @{ Authorization = "Bearer <token>" }
$body = '{"type":"telegram","name":"My phone","enabled":true,"settings":{"chatId":"-1001234567890"},"secrets":{"botToken":"<bot token>"}}'
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/notifications/targets" -Headers $h -ContentType "application/json" -Body $body
Invoke-RestMethod -Method Put -Uri "http://localhost:8080/api/queues/<queue id>/notification-level" -Headers $h -ContentType "application/json" -Body '{"level":"failure"}'
```

You can also edit `data/notifications/targets.json` by hand. The service reads the change without a
restart. Do not edit the queue file by hand. Set the queue level with the UI or the API.

## Check the behavior

| Step | Expected result |
|------|-----------------|
| Queue level "Failure". A queue sequence fails. | One message: `Farm-1 : <sequence> : 🔴 failure`. |
| The same sequence fails 9 more times. | No new message. |
| The sequence then succeeds. | One message: `... : 🟢 recovered`. |
| Queue level "Success+Failure". A sequence succeeds. | One message with 🟢 success. |
| Queue level "None". Any result. | No message. |
| You run a sequence by hand. | No message. |
| The target is wrong. | Queue runs as usual. The service log has the send failure. |
| You stop the queue while a sequence. | One message with 🟡 cancelled (levels other than "None"). |
| Level "Success+Failure". The sequence fails, then succeeds twice. | Messages: 🔴 failure, 🟢 recovered, 🟢 success. |
| Level "Failure" with an open streak. You set the level to "None". | No message. The streak is closed at once. |
| Then you set "Failure" and a sequence fails. | One "failure" message. |
| Three targets hang and one target works. A sequence fails. | The healthy target gets the message at once. The stuck targets time out at 30 s with a log line. |
| You edit a target in the list and save with an empty token field. | The stored token stays. |
| You delete a target in the list. | The target is gone. It gets no more messages. |
| You edit the queue name in the UI and save. | The level is unchanged. |
| You duplicate the queue. | The new queue has level "None". |
| You edit `targets.json` by hand while the service runs. | The next failure goes to the new target. No restart. |
| The last step of the work. | The Status line of the spec and the row in `specs/STATUS.md` say "Implemented". |

## Time check (SC-001, SC-002)

1. SC-001: Give the on-screen guide to a person who has not seen the feature. Start a timer. Stop it
   when the test message arrives. Pass: under 10 minutes. Write the time in the PR description.
2. SC-002: Fail a sequence in a queue at level "Failure". Measure the time from the failure to the
   message on the phone. Repeat 5 times. Pass: each time is under 30 seconds.
   The automated test (V-24) repeats this 20 times with a fake target.
3. No real bot or phone: run steps 1 and 2 against a fake target. Write this in the PR description.
   These checks do not block the final Status update.

## Run the tests

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Notification"
& "C:\Program Files\nodejs\npm.cmd" --prefix "C:\src\GameBot\src\web-ui" test -- notifications
& "C:\Program Files\nodejs\npm.cmd" --prefix "C:\src\GameBot\src\web-ui" run build
```

The web-ui quality gate is `vite build` and `jest`. Lint and `tsc --noEmit` have old failures that
are not part of this feature.
