# Research: Queue sessions survive idle gaps (#217)

## R-001 — Where does "emulator connection lost mid-run" come from?

`QueueExecutionService.RunAsync` catches `QueueConnectionLostException` and sets
`failureReason = "emulator connection lost mid-run ('<serial>')"`. The exception is thrown only by
the guard `if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();`,
which appears before every firing (at-start, every-step, before-each-run, time-of-day, daily retry,
relative, live, self-reschedule timer/next-cycle/once-per-run, once-per-run). Nothing talks to ADB
at that point: the "connection" is purely the presence of the session object in `SessionManager`.

## R-002 — Why does the session disappear after ~30 idle minutes?

`SessionManager.CleanupIdleSessions()` removes every session with
`now - LastActivity > IdleTimeout`; `IdleTimeoutSeconds` defaults to 1800. It runs at the top of
`CreateSession`, `GetSession`, `ListSessions` and `GetSnapshotAsync`. `LastActivity` is refreshed
only by `GetSession`, `SendInputsAsync`, `SendInputsWithResultsAsync` and `GetSnapshotAsync` on
that session. A queue waiting between firings (bare poll or idle-pause hold) calls none of them;
the idle-pause hold sends one HOME key at the start of the gap and nothing after. The background
capture loop runs against the device serial, not through `GetSnapshotAsync(sessionId)`, so it does
not refresh the session either.

This matches every observation in the issue: failure after 30–40 min regardless of `pauseWhenIdle`;
ADB unaffected (no ADB involved); a 2-minute idle works; the screenshot endpoint 404s
`session_not_found` from the first sweep after minute 30 because `FindSessionBySerial` finds no
session for the serial; the execution log has no node for the wake-up because the first thing the
wake-up does is the before-each-run pass, whose first statement is the guard.

**Decision**: Fix the lifetime rule, not the symptom.
**Alternatives considered**:
- *Touch the session from the idle loops* — works, but leaves any future waiting path exposed, and
  touching inside a 250 ms poll is noise. Rejected as the primary fix.
- *Raise the default timeout* — only moves the cliff; 8-hourly schedules exist. Rejected (and a
  spec non-goal).
- *Exempt by game label `queue:*`* — label is free text any API caller can set. Rejected (Clarify Q1).

## R-003 — Where to put the ownership marker

`ISessionManager` has ~28 implementations in the test suite; adding a parameter or member forces
edits to all of them. `SessionManager` stores the `EmulatorSession` object returned by
`CreateSession` by reference, so a settable property on `EmulatorSession` set by the queue right
after creation is visible to the sweep without an interface change. The window between creation and
assignment is a few instructions with `LastActivity` freshly set, so the sweep cannot evict it.

**Decision**: `EmulatorSession.OwnerQueueId` (`string?`, null = ad-hoc). Carrying the id rather than
a bool costs nothing and makes a session list self-explanatory in a debugger or future DTO.

## R-004 — Re-bind mechanics

`CreateSession(label, serial)` already validates the serial against `adb devices -l` (throws
`KeyNotFoundException` when the serial is absent, `InvalidOperationException("no_adb_devices")`
when there are none, `InvalidOperationException` on capacity). That is exactly the "genuinely
gone" test the spec needs, so the re-bind reuses it unchanged. Capture is keyed by session id, so
the old capture loop is stopped and a new one started for the new id. `sessionId` is a local
captured by the run's local functions, so reassigning it updates every later firing and the
`finally` teardown.

## R-005 — Existing tests affected

`QueueExecutionServiceTests.ConnectionLostMidRunFailsTheRun` (T031) and
`QueueExecutionServiceBeforeEachRunTests.ConnectionLostBeforeBeforeEachRunFailsTheRun` (T008(g))
simulate loss with `Sessions.Connected = false`. The fake's `CreateSession` sets `Connected = true`,
so after this change they would re-bind and pass through. They are re-expressed by also setting
`CreateThrows` to a `KeyNotFoundException`, which is the real "device gone" signal; their assertions
stay as they are. `tests/integration/ResourceLimitsTests.IdleSessionIsEvictedAfterTimeout` covers
ad-hoc eviction and must keep passing unchanged (FR-002).
