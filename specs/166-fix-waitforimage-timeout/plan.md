# Implementation Plan: Fix WaitForImage timeout on an image that is on screen (B-038)

**Branch**: `166-fix-waitforimage-timeout` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/166-fix-waitforimage-timeout/spec.md` (GitHub issue #350)

## Summary

A `WaitForImage` step times out when its reference image is on screen. The root cause (research R-001) is in the session lifecycle, not in the matcher. `POST /api/sessions` makes a session, but it does not start the background capture loop of that session. The step reads its frame from that loop, so each detection pass gets no frame, and the step times out.

The fix has five parts:

1. `POST /api/sessions` starts the capture loop of a session that has a device. `DELETE /api/sessions/{id}` stops the loop (R-002).
2. A new `SessionRemoved` event of `SessionManager` stops the loop when the session manager removes a session for any reason, for example the idle sweep (R-002a, FR-013).
3. A strict deadline rule in `CommandExecutor` and `SequenceRunner`: no detection pass starts at or after the deadline. When the remaining time is not more than the poll interval, the last wait ends 50 ms before the deadline, and the step does one last pass (R-003, FR-010).
4. One debug log line on each timeout, with the frame state and the best score of the last pass (R-005, R-008, FR-012).
5. Service-level tests with a fixed frame through the real capture path, on all three paths, plus edge-case, deadline, and lifecycle tests (R-006, FR-009).

The API shape does not change. No new status, reason, field, or endpoint.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core minimal API, OpenCvSharp (template match), System.Drawing, Microsoft.Extensions.Logging. No new package.
**Storage**: N/A. No persisted data changes.
**Testing**: xUnit. Unit tests in `tests/unit/GameBot.UnitTests.csproj`. Integration tests with `WebApplicationFactory<Program>` in `tests/integration/GameBot.IntegrationTests.csproj`.
**Target Platform**: Windows service (the GameBot service). ADB mode on Windows. Stub mode (`GAMEBOT_USE_ADB=false`) in CI and in tests.
**Project Type**: Web service (backend only). No web UI change.
**Performance Goals**: A step with the image on screen completes at the first or second pass, less than `timeoutMs`. A step with no image completes in not more than `timeoutMs` plus one pass (tests: not more than `timeoutMs` + 500 ms, and less than 2 × `timeoutMs`). A session from `POST /api/sessions` has one capture loop at `CaptureIntervalMs`, the same cost as a session from `POST /api/sessions/start`.
**Constraints**: No API change. No change to `imageVisible`, to `POST /api/images/detect`, or to the image lookup. Deterministic tests (Principle II): the deadline tests assert on pass counts and on pass start offsets with a 30 ms tolerance, not on exact times.
**Scale/Scope**: About 8 source files change. About 6 test files are new or changed. One section of `docs/architecture.md` changes.

No item is NEEDS CLARIFICATION. The spec Clarifications section and research.md resolve all items.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: This feature does not change the web UI. No phone support level applies.

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | No new analyzer warnings. Methods under about 50 lines: put the poll schedule in `WaitForImageSchedule`, and put the timeout log in a separate method. CamelCase method names, no underscores. Public members get XML doc comments. Use `EventHandler<SessionRemovedEventArgs>` (CA1003). | PASS (planned) |
| II. Testing | A failing test before the fix: the found-case integration tests fail on the code before the fix (SC-005), because no loop starts. The deadline unit tests fail before the deadline fix (FR-010). Each lifecycle part has a test (FR-013). Deterministic deadline tests (U1). Coverage of 80% line and 70% branch for touched code. | PASS (planned) |
| III. UX Consistency | No API shape change. Log lines use the existing `LoggerMessage` pattern and STE text. | PASS |
| IV. Performance | Goals are in Technical Context. The PR description MUST contain the performance note of research R-007, written together with the version bump. | PASS (planned) |
| V. Living Documentation | Update `docs/architecture.md` (capture loop section: the session paths that start the loop, the removal rule, the `WaitForImage` deadline rule). Refresh "Last reviewed". At merge, set the spec `Status` and `specs/STATUS.md`. | PASS (planned) |
| VI. STE | All artifacts of this plan are in STE. Code comments and log messages in STE. | PASS |
| Version bump | Bump `installer/versioning/version.override.json` before the PR push, and mention it in the CHANGELOG text. | PASS (planned) |

**Post-design re-check**: PASS. The design adds one small interface (`ISessionRemovalNotifier`), one static helper (`SessionCaptureCleanup`), one pure static helper (`WaitForImageSchedule`), one pass result type (`DetectionPassResult`), and one ambient scope (`ImageDetectionPassScope`). Each one has a clear reason in research.md. No violation needs a Complexity Tracking entry.

## Design

### D-1: Capture loop on `POST /api/sessions` and `DELETE /api/sessions/{id}` (FR-013, R-002)

- File: `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`.
- `POST /api/sessions` (lines 19-51): after `CreateSession` and before the `201` result, get `BackgroundScreenCaptureService` with `ctx.RequestServices.GetService<...>()`. If it is not null and `sess.DeviceSerial` is not blank, call `StartCapture(sess.Id, sess.DeviceSerial)`. If `StartCapture` throws, write a warning log and still return `201`.
- `DELETE /api/sessions/{id}` (lines 134-138): call `StopCapture(id)` on the capture service (if registered) before `mgr.StopSession(id)`.
- Stub mode: the capture service is not registered, so nothing changes.

### D-2: Stop the loop on each removal of a session (FR-013, R-002a)

- New file `src/GameBot.Emulator/Session/ISessionRemovalNotifier.cs`: the interface with `event EventHandler<SessionRemovedEventArgs>? SessionRemoved`, and the `SessionRemovedEventArgs` class with `SessionId`.
- `src/GameBot.Emulator/Session/SessionManager.cs`: implement the interface. Raise the event after `TryRemove` succeeds in `StopSession` (line 176) and in `CleanupIdleSessions` (line 513). Raise it outside of any lock. Catch and log a handler exception, so that a removal never fails.
- New file `src/GameBot.Emulator/Session/SessionCaptureCleanup.cs`: `Attach(ISessionManager, BackgroundScreenCaptureService)`. When the session manager implements `ISessionRemovalNotifier`, subscribe a handler that calls `StopCapture(e.SessionId)`.
- `src/GameBot.Service/GameBotServiceSetup.cs` (lines 481-492): in the DI factory of `BackgroundScreenCaptureService`, call `SessionCaptureCleanup.Attach(sp.GetRequiredService<ISessionManager>(), service)` before the factory returns.

### D-3: Deadline rule (FR-010, R-003)

- New file `src/GameBot.Domain/Commands/Execution/WaitForImageSchedule.cs`: a pure static method that gives the next wait and a "last wait" flag from the remaining time and the poll interval. See data-model.md for the table.
- `src/GameBot.Service/Services/CommandExecutor.cs` (poll loop, lines 697-720) and `src/GameBot.Domain/Services/SequenceRunner.cs` (poll loop, lines 1065-1086): use `WaitForImageSchedule`. After each wait, read the clock. Start a pass only before the deadline. Stop after the last pass.
- Write the deadline unit tests first (see Test plan). Record in the task notes that they fail before the fix, or why they do not.

### D-4: Timeout log line (FR-012, R-005, R-008)

- `src/GameBot.Service/Services/ImageDetectionHelper.cs`: add `DetectionPassResult` (`Found`, `FrameAvailable`, `BestScore`, `ResolvedPoint`, `DetectionConfidence`) and a method that returns it. `TryDetect` keeps its signature and calls the new method.
- `CommandExecutor`: keep the last `DetectionPassResult` and the pass count. On timeout, write one debug line with the session id, the reference image id, the `confidence`, the pass count, the frame state, and the best score (three decimals, or `none`).
- New file `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`: an `AsyncLocal` scope, the same pattern as `OcrTextEvaluationScope`.
- `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`: write the last pass into `ImageDetectionPassScope.Current`, if a scope is present.
- `SequenceRunner.WaitForImageAsync`: push the scope for the wait. On timeout, write one debug line with the sequence id, the step key, the reference image id, the `confidence`, the pass count, the frame state (`true`, `false`, or `unknown`), and the best score.

### D-5: Documentation

- `docs/architecture.md`, capture loop section (about lines 968-1008): the three session-creation paths start the loop; each removal of a session stops the loop; the `WaitForImage` deadline rule; the timeout debug log line. Refresh "Last reviewed".
- PR description: the performance note of R-007 and the version bump.

## Test plan

All tests are deterministic and need no real emulator.

| Test | Level | File (new or changed) | Requirement |
|------|-------|-----------------------|-------------|
| Found case: step execute, force execute, inline sequence step, sequence command step | Integration | `tests/integration/WaitForImageCaptureLoopTests.cs` (new) | FR-001 to FR-005, FR-011, SC-001, SC-002, SC-005 |
| Timeout case on the three paths: elapsed time not more than `timeoutMs` + 500 ms and less than 2 × `timeoutMs` | Integration | same | FR-006, SC-003 |
| Step result agrees with `POST /api/images/detect` on an image frame and on a no-image frame | Integration | same | FR-003, SC-004 |
| Edge cases: image comes later, low `confidence` 0.5, stored alternate, no frame yet | Integration | same | FR-009 edge cases |
| `DELETE /api/sessions/{id}` stops the loop (no cached frame and no metrics after the call) | Integration | same or `tests/integration/Sessions/SessionCaptureLifecycleTests.cs` (new) | FR-013 |
| Stub mode starts no loop | Integration | `tests/integration/Sessions/SessionCaptureLifecycleTests.cs` (new) | FR-013 |
| Idle sweep stops the loop; `StopSession` raises `SessionRemoved` one time | Unit | `tests/unit/Sessions/SessionCaptureCleanupTests.cs` (new) | FR-013 |
| Deadline: 2 passes, no pass starts at or after the deadline (tolerance 30 ms) | Unit | `tests/unit/Commands/CommandExecutorWaitForImageTests.cs`, `tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs` (changed) | FR-010 |
| `WaitForImageSchedule.NextWait` with fixed values | Unit | `tests/unit/Commands/WaitForImageScheduleTests.cs` (new) | FR-010 |
| Timeout log line on each path (frame state and best score) | Unit | `CommandExecutorWaitForImageTests`, `SequenceRunnerWaitForImageTests`, `tests/unit/Vision/ImageDetectionPassScopeTests.cs` (new) | FR-012 |
| `completed_image_unavailable` and `delay_elapsed` do not change | Unit (existing) | existing tests | FR-007, FR-008 |

The integration test host keeps `GAMEBOT_USE_ADB=false`. It replaces `ISessionManager` with a fake that gives a `DeviceSerial`, registers a real `BackgroundScreenCaptureService` with a fake `IAdbScreenCaptureProvider` that returns a changeable PNG, and registers `BackgroundCaptureScreenSource` and `BackgroundCaptureScreenSourceFactory` (R-006).

## Project Structure

### Documentation (this feature)

```text
specs/166-fix-waitforimage-timeout/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── session-capture-lifecycle.md
│   └── waitforimage-step-outcome.md
└── tasks.md             # Phase 2 output (/speckit-tasks, not made by this command)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/Execution/WaitForImageSchedule.cs        # new: poll schedule (FR-010)
│   ├── Services/SequenceRunner.cs                         # deadline rule, scope, timeout log
│   ├── Triggers/Evaluators/ImageMatchEvaluator.cs         # write the last pass to the scope
│   └── Vision/ImageDetectionPassScope.cs                  # new: ambient scope (FR-012)
├── GameBot.Emulator/
│   └── Session/
│       ├── ISessionRemovalNotifier.cs                     # new: SessionRemoved event
│       ├── SessionCaptureCleanup.cs                       # new: StopCapture on SessionRemoved
│       └── SessionManager.cs                              # raise SessionRemoved
└── GameBot.Service/
    ├── Endpoints/SessionsEndpoints.cs                     # StartCapture on POST, StopCapture on DELETE
    ├── GameBotServiceSetup.cs                             # call SessionCaptureCleanup.Attach
    └── Services/
        ├── CommandExecutor.cs                             # deadline rule, timeout log
        └── ImageDetectionHelper.cs                        # DetectionPassResult

tests/
├── integration/
│   ├── WaitForImageCaptureLoopTests.cs                    # new
│   └── Sessions/SessionCaptureLifecycleTests.cs           # new
└── unit/
    ├── Commands/CommandExecutorWaitForImageTests.cs       # changed
    ├── Commands/WaitForImageScheduleTests.cs              # new
    ├── Sequences/SequenceRunnerWaitForImageTests.cs       # changed
    ├── Sessions/SessionCaptureCleanupTests.cs             # new
    └── Vision/ImageDetectionPassScopeTests.cs             # new

docs/architecture.md                                       # capture loop section
```

**Structure Decision**: Use the existing projects. The lifecycle code goes in `GameBot.Emulator/Session`, next to `SessionManager` and `BackgroundScreenCaptureService`. The poll schedule goes in `GameBot.Domain`, because both `CommandExecutor` (Service) and `SequenceRunner` (Domain) use it. The new scope goes in `GameBot.Domain/Vision`, next to the other vision types.

## Complexity Tracking

No constitution violation. This section is empty.
