# Research: Trace Noise Cleanup

The Technical Context has no open items. This file records the decisions and the audit method.

## R-001: Where the operator sees the traces

- **Decision**: The cleanup applies to the console output of the service at the default level (Warning).
- **Rationale**: `GameBotServiceSetup.ConfigureLogging` adds only the simple console provider. The installed service writes no trace file (`stdoutLogEnabled="false"`). `LoggingPolicyGate` uses Warning as the default level, and the logging policy seed is Warning too. Thus only messages at Warning level or higher show in a healthy run.
- **Alternatives considered**: Audit Information and Debug messages too. Rejected, because they do not show at the default level (spec, Assumptions).

## R-002: Audit method

- **Decision**: Read all `[LoggerMessage]` definitions at Warning level or higher, and find the ones that a healthy run writes regularly.
- **Rationale**: The live console of the service is not available to read. The code has no direct `LogWarning` or `LogError` calls, so the `[LoggerMessage]` definitions are the full set.
- **Result**: Two definitions come regularly in healthy runs: "Slow image match" (no event id) and `TapRetryExhausted` (6012). All other Warning definitions tell of a fault. The audit also found three definitions with no caller: 6002, 6006, and 6007.

## R-003: Remove or lower the "Slow image match" message

- **Decision**: Remove the message and the `Stopwatch` that measures the duration for it.
- **Rationale**: The 50 ms limit is lower than the usual duration of a full-frame template match, so the message comes on almost each evaluation. The user confirms that it was never useful. A Debug message is also of no use, because the duration is always "slow" by this limit. Other signals (step time limit, sequence watchdog, queue liveness check) show the effect of a real slowdown.
- **Alternatives considered**:
  - Raise the limit (for example, to 500 ms). Rejected, because no operator acts on the message, and the user asks for removal.
  - Lower it to Debug. Rejected, because it then fills the Debug output when an operator turns on Debug to find a different problem.

## R-004: Level of `TapRetryExhausted` (6012)

- **Decision**: Change the level from Warning to Debug. Keep the event id, the text, and the parameters.
- **Rationale**: Spec 037 (R-006) chose Warning for "operational visibility". Since then, the execution log records the step result `skipped_detection_failed` with the reason `detection_failed_after_N_retries`. Thus the operator sees the result in the execution log, where it has the sequence and step context. Sequences that tap a dialog only when it shows make this result normal and frequent. The per-cycle messages (6009, 6011) are already at Debug, so 6012 at Debug keeps the full retry story together at one level.
- **Alternatives considered**: Information. Rejected, because the message then shows as soon as an operator sets Information to look at queue events, and it is still noise there.

## R-005: Dead log definitions

- **Decision**: Remove `TriggerBypassed` (6002), `SessionCacheHit` (6006), and `SessionCacheMiss` (6007) from the `Log` class in `CommandExecutor.cs`.
- **Rationale**: No code calls them (a search for `Log.<name>(` gives 0 results). Constitution Principle I forbids dead code. The event ids stay free and no other definition takes them.
- **Alternatives considered**: Keep them for possible later use. Rejected, because a later feature can add a definition when it needs one.

## R-006: Test method

- **Decision**:
  - For FR-001: give `ImageMatchEvaluator` a test logger that records each entry. Give it also a screen source that waits 60 ms before it returns the frame. Make sure that the logger gets no entry with the text "Slow image match" and no Warning entry.
  - For FR-003: give `CommandExecutor` a test logger that records each entry, in a PrimitiveTap test where the target never shows (`TapRetryCount = 1`, short wait). Make sure that event 6012 is at Debug level and that no Warning entry comes.
- **Rationale**: The tests use the same test-logger pattern as `CapturingLogger` in `ImageMatchEvaluatorAlternatesTests` and `ReferenceSetTemplateMatcherTests.ListLogger`. The test logger for the 6012 test must record the `EventId`. The 60 ms wait keeps the test fast (less than 1 s).
- **Alternatives considered**: Test the source-generated attribute with reflection. Rejected, because a behavior test is more direct.
