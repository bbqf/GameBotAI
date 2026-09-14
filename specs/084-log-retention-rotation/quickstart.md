# Quickstart: Execution Log Retention Default & Long-Run Rotation

## Verify the new retention default

1. On a machine/profile with no existing `data/config/execution-log-policy.json`, start GameBot.
2. `GET /api/execution-logs/retention` → `retentionDays` is `7`.
3. If a policy file already exists from before this change, its saved `retentionDays` is untouched — the new default only applies where nothing was ever saved.

## Exercise rotation (test-time, without waiting 24h)

Rotation is time-based (`DateTimeOffset.UtcNow` vs. the active segment root entry's `TimestampUtc`), so tests use an injected/fake time provider (the codebase already uses `ITimeProvider`/similar in `QueueExecutionService`, see `_timeProvider.GetLocalNow()` usage) to simulate elapsed time between firings rather than a real 24-hour wait.

1. Start a queue with a template that fires a trivial sequence frequently (e.g. every-step or a short timer).
2. Let 2-3 firings complete at the simulated "current" time.
3. Advance the fake clock by >24h.
4. Trigger the next scheduled firing.
5. Assert:
   - The old root entry (`GET /api/execution-logs/{oldRootId}`) has `finalStatus` reflecting closure and `rotatedToExecutionId` set to a new id.
   - The new root entry (`GET /api/execution-logs/{newRootId}`) has `rotatedFromExecutionId == oldRootId` and its summary states it is a continuation.
   - The firing that triggered rotation is nested under the **new** root only (its `hierarchy.rootExecutionId == newRootId`), never the old one.
   - No sequence entry has `hierarchy.rootExecutionId` values from both segments (i.e., nothing straddles the cut).
6. Repeat the clock-advance step twice more (simulate a 3-day run) and assert a 3-segment chain, each linked to its neighbor(s).
7. Stop the queue before 24h elapse in a separate run and assert no rotation occurred (`rotatedToExecutionId`/`rotatedFromExecutionId` both `null` throughout).

## Manual UI check

1. Run a queue long enough (or use the same fake-clock test harness in an integration/UI test) to produce a rotated chain.
2. Open the Execution Logs page, open the closed-out segment's detail view → see a "Continues in newer run segment" link; click it → lands on the new segment.
3. Open the new segment's detail view → see a "Continued from earlier run segment" link back.
