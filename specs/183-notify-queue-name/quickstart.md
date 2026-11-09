# Quickstart: Show the queue name in notify messages

## Check the change by hand

1. Start the service with at least one enabled notification target (for example, a test Telegram target).
2. Make a sequence with one `notify` step. Set `message` to `Daily reward ready`. Do not set `url`.
3. Make two queues, "Farm A" and "Farm B". Add the sequence to both.
4. Start both queues.
5. Check the target. You MUST see `Farm A : Daily reward ready` and `Farm B : Daily reward ready`.
6. Run the sequence by hand, outside a queue. You MUST see `Daily reward ready` with no label.
7. Rename "Farm A" to "Farm C" and run its queue again. You MUST see `Farm C : Daily reward ready`.

## Run the tests

```powershell
& dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Notif"
```

Expected: all tests pass. The new tests cover: a queue with a name, a blank name, a missing queue,
a failed queue read, a run outside a queue, and a message with many lines.
