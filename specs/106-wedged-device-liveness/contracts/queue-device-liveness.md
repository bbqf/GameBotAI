# Contract: queue device liveness (FR-015 to FR-018)

## `GET /api/queues/{id}`: new `health.deviceLiveness` block

The `health` block is present only while the queue runs (no change). It gets a new property `deviceLiveness`. It is null before the run binds a session.

```json
{
  "health": {
    "runStartedAt": "2026-09-25T02:00:03+02:00",
    "cyclesCompleted": 0,
    "consecutiveFailedCycles": 1,
    "paused": false,
    "pauseKind": null,
    "deviceLiveness": {
      "state": "not_live",
      "reason": "capture_stalled",
      "notLiveSince": "2026-09-25T03:12:40+02:00",
      "stale": true,
      "frameAgeMs": 95012,
      "unchangedMs": 95012,
      "gatedFirings": 4
    }
  }
}
```

| Field | Type | Values |
|---|---|---|
| `state` | string | `live`, `not_live`, `unknown` |
| `reason` | string or null | as in the session health. Null when not `not_live`. |
| `notLiveSince` | date-time or null | Service-local time at which the queue first observed the device as not live in this fault episode. Null when not `not_live`. |
| `stale` | boolean | The stale flag. |
| `frameAgeMs` | integer or null | Frame age. |
| `unchangedMs` | integer or null | Time for which the frame did not change. |
| `gatedFirings` | integer | The number of held firings in the current fault episode. `0` when no episode is open. |

The read computes the state from the current data. It does not wait for the next periodic check.

A fault episode starts when the queue first observes the state `not_live`. It ends when the queue observes `live` or `unknown`. A new `not_live` state then starts a new episode.

## Gate before each firing (FR-016)

The gate acts one time for each firing group. A firing group is one due main firing (any schedule type), the BeforeEachRun pass before it, and the EveryStep pass after it. A standalone EveryStep pass (no once-per-run entries) is also a firing group, and its first sequence is the main sequence. Before the group starts, the queue evaluates the device liveness of its session.

### Hard reasons: the queue holds the firing

The state is `not_live` with the reason `capture_stalled`, `input_timeout` or `transport_not_ready`. Then these rules apply:

1. The queue does not run the main sequence. It also does not run the BeforeEachRun and EveryStep guard sequences of the group.
2. The firing stays due. The queue does not fail it.
   - A timer firing (time-of-day, relative, live schedule or self-reschedule timer) keeps its original due time.
   - A daily retry keeps its attempt number. A held firing does not use up a daily retry attempt.
   - Once-per-run entries and next-cycle-start entries stay queued. A held once-per-run pass continues later with the first entry that did not run.
   - A held at-queue-start entry and the at-queue-start entries after it move to the next-cycle-start register. The run does not end, and these entries run after a recovery.
   - A held live schedule does not replace a newer schedule that an API call wrote for the same sequence during the hold.
   - EveryStep injections stay registered.
   - The order of the queued entries can change after a hold. Each entry still fires one time.
   - A time-of-day firing held past midnight is lost for that day (known limit).
3. The queue does not complete the cycle. It waits `QueueCheckIntervalMs` and then checks again. A queue that does not cycle does not end while it holds a firing.
4. When the device is live again, the held firings run. Self-reschedule chains continue at their original cadence.
5. The first held firing of each sequence in the fault episode writes one `sequence` entry to the execution log under the queue run: `finalStatus: "failure"`, `summary: "device_not_live: <reason>"`. It also adds one `failure` run to the `sequenceStats` of that sequence.
6. Later held firings of the same sequence in the same episode write no log entry and no statistics run. Each held firing adds one to `gatedFirings`.
7. After the first hold in one pass of the run loop, the queue holds all other firings that are due in that pass, with no new evaluation. Rules 1 to 6 apply to each of them. Thus each due sequence gets its log entry.

### Other states: the firing runs

The state is `not_live` with the reason `no_change_after_input`, or the state is `live` or `unknown`. Then the firing group runs as before. The inputs of the sequence can change the screen and clear `no_change_after_input`. The guard sequences get no gate of their own.

## Periodic check (FR-017)

Every `QueueCheckIntervalMs`, a queue that runs evaluates its device liveness. The device can stay `not_live` (each reason) for longer than `QueueGracePeriodMs`. Then the queue does these steps one time for the episode:

1. The execution log gets one `queue` entry under the queue run: `finalStatus: "failure"`, `summary: "device_not_live: <reason>"`, with no sequence.
2. The cycle ledger gets one failed cycle with no entries. `health.consecutiveFailedCycles` increases by one. This also applies to a queue with `cycleExecution: false`.
3. The failure policy of the queue (when configured) evaluates the new cycle. It can notify, pause or stop as today. The policy acts one time for each episode, also when the run loop evaluates at the same time.

The queue does these steps also when held firings wrote entries in the episode. The two records are separate.

Limit for one episode: at most one `sequence` entry for each sequence, plus one `queue` entry. The length of the episode does not change this limit.

## No recovery (FR-018)

The new code never stops the queue, never restarts the run, and never reboots the device. The current transport watchdog (`QueueDeviceWatchdogService`) does not change.

A failure policy that the operator configured is not an action of the queue "by itself". The fault cycle can trip this policy. Then the policy can stop the queue (for example `notifyAndStop`), as for each other failed cycle. The failure policy evaluator does this stop, not the new code.
