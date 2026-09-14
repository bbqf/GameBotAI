# Quickstart: Queue Cycle Observability

**Feature**: 086-queue-cycle-observability | **Date**: 2026-09-14

How to verify the feature by hand once it is built. The service listens on port **8080**.

## 1. Start a cycling queue

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/queues/<queueId>/start"
```

## 2. Watch it stay honest

```powershell
(Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>").health | Format-List
```

Expect `cyclesCompleted` to be 0 with all three `lastCycle*` fields null until the first cycle
completes, then to advance on every cycle. `currentSequenceId` changes as the run moves through the
roster; `currentEntryIndex` tracks its position and is null between roster entries.

Re-read it a minute later. **If `cyclesCompleted` has not moved, the queue is not doing work** — that
is the entire point of the feature, and the question that could not previously be asked.

## 3. Read the recent cycles while it runs

```powershell
Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>/cycles?limit=5" | ConvertTo-Json -Depth 5
```

Expect five cycles, newest first, each with its per-entry outcomes. The queue keeps running
throughout — nothing here stops or pauses it.

## 4. Reproduce the failure condition

Put the device into a state where every sequence fails (the original outage was a blocking
network-loss modal; covering the game window or stopping the emulator will do).

```powershell
(Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>").health |
  Select-Object cyclesCompleted, lastCycleStatus, consecutiveFailedCycles
```

Expect `status` on the queue to still read `Running` — that is correct, the loop *is* running — while
`lastCycleStatus` reads `failure` and `consecutiveFailedCycles` climbs with each cycle. Those two
values are the distinction the platform previously could not make.

Clear the condition and confirm `consecutiveFailedCycles` resets to 0 after the next successful cycle.

## 5. Confirm the boundaries

```powershell
# Not running: health is null, cycles are empty — not an error
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/queues/<queueId>/stop"
(Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>").health   # -> null
Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>/cycles"     # -> running:false, cycles:[]

# Unknown queue: 404
Invoke-RestMethod -Uri "http://localhost:8080/api/queues/does-not-exist/cycles"

# Limit is clamped, never rejected
Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>/cycles?limit=9999"  # -> at most 50
Invoke-RestMethod -Uri "http://localhost:8080/api/queues/<queueId>/cycles?limit=0"     # -> 1
```

Restarting the queue resets `cyclesCompleted` to 0 and empties the cycle list: all values describe the
current run only.

## 6. Confirm nothing else moved

```powershell
Invoke-RestMethod -Uri "http://localhost:8080/api/executionlogs?limit=5" | ConvertTo-Json -Depth 4
```

The run-level records — one at start, one at finish, with their existing summary text — must look
exactly as they did before this feature. Nothing in this feature writes to the execution log.

## Automated verification

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj"
```
