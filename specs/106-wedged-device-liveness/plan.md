# Implementation Plan: Make a wedged emulator visible to the API (B-019)

**Branch**: `106-wedged-device-liveness` | **Date**: 2026-09-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/106-wedged-device-liveness/spec.md`
**Issue**: [#220](https://github.com/bbqf/GameBotAI/issues/220) (B-019). The PR closes it.

## Summary

A wedged device keeps its ADB transport, but it renders no new frame and applies no input. Today the API reports such a device as good. This feature adds a device liveness state for each session: `live`, `not_live` or `unknown`, with a reason for `not_live`.

The approach has five parts:

1. **Data.** A singleton `DeviceLivenessTracker` keeps data for each session. The data is the time of the last completed capture and the time of the last frame change (byte compare in the capture loop). It also has the time of the first input after that change, and the start time and outcome of the last input.
2. **Rules.** A pure `DeviceLivenessEvaluator` computes the state from this data, the options and the current time. It has no I/O, so unit tests need no device.
3. **Time limits.** `AdbClient` kills the `adb` process when its token is cancelled. The input path, the direct capture, the capture loop and the transport check get time limits from the new options section `Service:DeviceLiveness`.
4. **API.** Session health gets a `liveness` block. Screenshot and snapshot get three staleness headers and a `504 capture_timeout`. The inputs endpoint gets `504 device_timeout` and `503 device_not_live`.
5. **Queues.** A gate before each firing group holds a due firing when the device is not live for a hard reason. The held firing stays due and runs after a recovery. The gate writes at most one failed entry for each sequence and fault episode. A periodic watch records one failed entry and one failed cycle for each fault episode. Queue health gets a `deviceLiveness` block with a `gatedFirings` counter. The queue never stops or recovers the device by itself.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (`net9.0`), Windows host for the ADB code (`[SupportedOSPlatform("windows")]`)
**Primary Dependencies**: ASP.NET Core Minimal APIs, `Microsoft.Extensions.Options`, `System.Diagnostics.Process` (ADB), Swashbuckle (schema filters and the examples operation filter), `TimeProvider` / `FakeTimeProvider`, xUnit + FluentAssertions
**Storage**: None new. Liveness data is in memory only. The new execution-log entries use the current execution-log store.
**Testing**: `dotnet test GameBot.sln` (unit, integration, contract). Web UI gate: `vite build` + `jest` (no UI change is planned).
**Target Platform**: Windows service host, localhost REST API on port 8080
**Project Type**: Web service with a shared domain library (`GameBot.Domain`), an emulator library (`GameBot.Emulator`) and a service host (`GameBot.Service`)
**Performance Goals**:
- Capture loop: one byte compare of two PNGs for each capture (research R-003). Below 1 ms for a 3 MB PNG, at the default interval of 500 ms. Task "perf note" measures it.
- `POST /api/sessions/{id}/inputs` on a live device: one data-only evaluation (one lock, a few comparisons), below 0.1 ms. No change to the response time that a client can see (US3 scenario 3).
- Health call: below 50 ms when capture data exists (transport check only). Worst case `TransportCheckTimeoutMs + CaptureTimeoutMs` (15 s with the defaults).
- Queue gate: one data-only evaluation for each firing. The periodic watch does one evaluation every 30 s for each queue that runs.
**Constraints**:
- Bounds (SC-003, SC-004): inputs at most `InputTimeoutMs + 2 s` for each request; screenshot and snapshot at most `CaptureTimeoutMs + 2 s`.
- `ISessionManager` does not change (27 test fakes). `InputActionResult` gets one optional positional parameter at the end.
- No change to the step results of sequences (clarification 5). `SendInputsAsync` gets no time limit.
- Keep `Program.cs` and the large methods thin (build-time analyzer cost). New logic goes into new small classes and small private helpers.
- Contract tests cannot override `Service:Sessions:*`. The new options use a new section, and the tests use `PostConfigure` or direct construction (research R-013).
- Current response fields, error codes and status codes stay the same for the current cases (SC-007).
- No web UI change (spec Assumptions; research R-016).
**Scale/Scope**: About 13 new source files, about 16 changed source files, about 19 new or extended test files, `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation progression is blocked. This block stays until the failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI). These artifacts are research, data model, contracts, quickstart, tasks, code comments and messages for users.

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. Each rule has one home. `DeviceLivenessEvaluator` holds the state rules. `DeviceLivenessTracker` holds the data. `SessionLivenessService` holds the bounded probes. `QueueLivenessEpisode` holds the episode rules. `QueueLivenessWatch` holds the periodic check. The run body gets one small local function (`FireGroupAsync`) that calls the gate helper one time for each firing group. The endpoints get small helper methods for the new responses. New public members get XML docs. No new package. Method names in CamelCase only. |
| **II. Testing Standards** | PASS. Unit tests cover each row of the evaluator table, the tracker transitions, the episode rules and the options normalization, all with `FakeTimeProvider`. Windows unit tests prove that `ExecAsync` and `GetScreenshotPngAsync` kill a hung process on cancel (a uniquely named copy of `ping.exe` as the fake `adb` path). Unit tests cover the capture loop compare with a fake `IAdbScreenCaptureProvider` (same bytes, changed bytes, and a provider that hangs). Unit tests also cover the input time-out in `SessionManager`, and the queue gate and watch (fake session manager, fake log, fake time). Contract tests cover the health block, the headers and the 503/504 rules. They use a fake `ISessionManager` and a fake `ISessionTransportCheck` in `ConfigureTestServices`. Contract tests also cover the CORS exposed headers and the OpenAPI schemas. Each bug scenario of the spec has a test that fails before the change. |
| **III. UX Consistency** | PASS. New error codes use the current error forms of each endpoint. Messages name the limit and give a remediation hint. New fields and headers are additive. Durations are milliseconds, as `watchdogTimeoutMs` and `delayMs`. Reason words are snake case, as `capture_timeout` and `not_running`. |
| **IV. Performance** | PASS. Goals are in Technical Context. The hot path (capture loop) gets one memory compare. The inputs path gets one in-memory evaluation. A perf note goes in the PR. |
| **V. Living Documentation** | PASS. `docs/architecture.md` gets the liveness model, the new configuration section, the new API fields, headers and errors, and the queue gate and watch. It also gets a new "Last reviewed" date. `spec.md` Status goes to Implemented at the end, and `specs/STATUS.md` gets row 106. `CHANGELOG.md` gets `Added` and `Changed` entries (new status codes of the inputs endpoint). No earlier spec is superseded. |
| **VI. Simplified Technical English** | PASS. This plan, research, data model, contracts and quickstart use STE. Tasks, code comments, API descriptions and error messages MUST also use STE. |

No violations. **Complexity Tracking** is not necessary.

**Post-Phase-1 re-check**: unchanged. The design adds one singleton tracker and one service class. The queue watch is a small class with the life of the run. It follows the current pattern of the failure-policy evaluator (a separate class that acts on the run handle).

**Note on behavior change (Principle III, "stable" outputs)**: `POST /api/sessions/{id}/inputs` can now return `503` and `504`. Before, the same case returned `202` with a false `dispatched: true`, or did not return. This is the purpose of the fix (spec FR-012, FR-013). The CHANGELOG entry states it under `Changed`.

## Project Structure

### Documentation (this feature)

```text
specs/106-wedged-device-liveness/
├── spec.md
├── plan.md                              # this file
├── research.md                          # Phase 0
├── data-model.md                        # Phase 1
├── quickstart.md                        # Phase 1
├── contracts/
│   ├── session-health.md                # Phase 1
│   ├── screenshot-snapshot.md           # Phase 1
│   ├── session-inputs.md                # Phase 1
│   └── queue-device-liveness.md         # Phase 1
├── checklists/
└── tasks.md                             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Sessions/
├── DeviceLivenessOptions.cs             # NEW options, SectionName "Service:DeviceLiveness", Normalized()
├── DeviceLivenessStates.cs              # NEW constants: states, reasons, input outcomes (+ InputOutcome enum)
├── DeviceLivenessSample.cs              # NEW record (data-model 3)
├── DeviceLivenessReport.cs              # NEW record (data-model 4)
├── DeviceLivenessEvaluator.cs           # NEW static pure Evaluate (research R-002)
├── IDeviceLivenessTracker.cs            # NEW
└── DeviceLivenessTracker.cs             # NEW per-session records, TimeProvider

src/GameBot.Emulator/
├── GameBot.Emulator.csproj              # + InternalsVisibleTo GameBot.UnitTests
├── Adb/AdbClient.cs                     # ExecAsync + GetScreenshotPngAsync: kill registered on the token before the read (R-004); implements IAdbSessionClient
├── Adb/IAdbSessionClient.cs             # NEW internal seam: TapAsync, SwipeAsync, KeyEventAsync, GetScreenshotPngAsync
└── Session/
    ├── BackgroundScreenCaptureService.cs  # + optional tracker + options; loop: byte compare, RecordCapture, capture time limit (R-003, R-009)
    ├── ISessionManager.cs               # InputActionResult + TimedOut = false
    └── SessionManager.cs                # + optional tracker + options; internal test constructor with Func<string, IAdbSessionClient>; input start/end records; per-action limit in SendInputsWithResultsAsync; tracker.Remove on stop/evict (R-005, R-010)

src/GameBot.Service/
├── GameBotServiceSetup.cs               # register options, tracker, liveness service, transport check; pass tracker/options to capture service; CORS headers; schema filter
├── Services/Liveness/
│   ├── ISessionLivenessService.cs       # NEW
│   ├── SessionLivenessService.cs        # NEW Evaluate + ProbeAsync (R-007)
│   ├── ISessionTransportCheck.cs        # NEW
│   ├── AdbSessionTransportCheck.cs      # NEW bounded adb get-state
│   ├── ISessionDirectCapture.cs         # NEW
│   ├── AdbSessionDirectCapture.cs       # NEW probe capture (not GetSnapshotAsync, which returns a stub PNG on failure)
│   └── CaptureHeaders.cs                # NEW header names + Apply(response, report) helper
├── Endpoints/SessionsEndpoints.cs       # health: liveness block; inputs: 503/504 rules; snapshot: time limit, headers, 504
├── Endpoints/EmulatorImageEndpoints.cs  # headers on both paths; direct capture time limit; 504
├── Contracts/Queues/QueueHealthResponse.cs         # + DeviceLiveness
├── Contracts/Queues/QueueDeviceLivenessResponse.cs # NEW
├── Endpoints/QueuesEndpoints.cs         # ProjectHealth: + deviceLiveness (Observe + Snapshot)
├── Services/ExecutionLog/ExecutionLogService.cs    # interface IExecutionLogService (line 76) and ExecutionLogService: + LogQueueDeviceFaultAsync (R-014)
├── Services/QueueExecution/
│   ├── QueueLivenessEpisode.cs          # NEW (data-model 8)
│   ├── QueueLivenessWatch.cs            # NEW periodic check (R-012)
│   ├── QueueRunHandle.cs                # + Liveness episode; + RearmTimerFiring (put back a held timer firing with its original FireAt); + TryMarkPolicyTripped (atomic)
│   ├── QueueFailurePolicyEvaluator.cs   # use TryMarkPolicyTripped in place of the PolicyTripped read and MarkPolicyTripped
│   ├── QueueRunSchedule.cs              # + OncePerRunCompletedThisCycle(int) query
│   ├── QueueCycleLedger.cs              # + RecordFaultCycle
│   └── QueueExecutionService.cs         # + optional ISessionLivenessService; gate one time for each firing group (FireGroupAsync); hold on hard reasons; put-back rules; wait QueueCheckIntervalMs; start/stop the watch
└── Swagger/
    ├── DeviceLivenessSchemaFilter.cs    # NEW descriptions + enums
    └── SwaggerConfig.cs                 # SessionLivenessSchema, health example, 503/504 examples, capture headers

tests/
├── unit/Sessions/DeviceLivenessEvaluatorTests.cs        # NEW each table row, order of rules, stale flag, probe need, first-input rule 5, stopped loop
├── unit/Sessions/DeviceLivenessTrackerTests.cs          # NEW transitions, loop restart reset, remove, no record after remove, concurrency smoke
├── unit/Sessions/DeviceLivenessOptionsTests.cs          # NEW defaults, clamps
├── unit/Emulator/AdbClientCancellationTests.cs          # NEW (Windows) ExecAsync + GetScreenshotPngAsync: hung fake process is killed on cancel
├── unit/Emulator/SessionManagerInputLivenessTests.cs    # NEW input records, cancelled / timed_out on caller cancel (seam: IAdbSessionClient)
├── unit/Emulator/SessionManagerInputTimeoutTests.cs     # NEW per-action time-out stops the rest
├── unit/Emulator/SessionManagerSnapshotTimeoutTests.cs  # NEW cancel is not the stub PNG
├── unit/BackgroundScreenCaptureServiceTests.cs          # extend: same bytes → no change; new bytes → change; hung provider → loop continues after limit
├── unit/Liveness/SessionLivenessServiceTests.cs         # NEW probe: transport time-out, direct capture success/failure/time-out, stub → unknown
├── unit/Liveness/CaptureHeadersTests.cs                 # NEW header values, invariant culture
├── unit/Queues/QueueLivenessEpisodeTests.cs             # NEW open/close, one gate entry for each sequence, gatedFirings, fault cycle claim independent of gate entries
├── unit/Queues/QueueCycleLedgerFaultCycleTests.cs       # NEW fault cycle seals, count increments
├── unit/Queues/QueueLivenessGateTests.cs                # NEW hard reason → hold (no run, no guard run, stays due, attempt kept), one entry for each sequence and episode, bounded count, runs after recovery; no_change_after_input → runs
├── unit/Queues/QueueLivenessWatchTests.cs               # NEW grace, one entry and one fault cycle for each episode (also after gate entries), non-cycling count +1, policy call, no direct stop
├── unit/Queues/RecordingExecutionLog.cs                 # MOVED shared internal fake (was a private nested class of QueueExecutionServiceTests)
├── unit/Queues/QueueFailurePolicyEvaluatorTests.cs      # extend: two concurrent OnCycleCompleted calls give one notification
├── contract/Sessions/SessionHealthLivenessContractTests.cs   # NEW liveness block shape, stub unknown, transport fault → not_live
├── contract/Sessions/SessionInputsLivenessContractTests.cs   # NEW 504 device_timeout, 503 device_not_live, 202 unchanged, 400 unchanged
├── contract/Sessions/CaptureHeadersContractTests.cs          # NEW headers present, CORS exposes them, 504 capture_timeout
├── contract/Queues/QueueDeviceLivenessContractTests.cs       # NEW health.deviceLiveness, failed entry, status stays Running
└── contract/DeviceLivenessOpenApiTests.cs                    # NEW schemas, enums, headers, 503/504 responses

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: No new project. The pure model (options, sample, report, evaluator, tracker) goes in `GameBot.Domain/Sessions`, next to `CachedFrame` and `CaptureMetrics`, because both `GameBot.Emulator` and `GameBot.Service` use it. The probes that call `adb` go in a new folder `GameBot.Service/Services/Liveness`. The queue parts go next to the failure-policy evaluator in `Services/QueueExecution`.

## Implementation Approach

### A. Liveness model (FR-001 to FR-006, FR-019)

1. Add `DeviceLivenessOptions` with the defaults and minimums of data-model section 1. Register it: `builder.Services.Configure<DeviceLivenessOptions>(builder.Configuration.GetSection(DeviceLivenessOptions.SectionName))`.
2. Add the constants, `InputOutcome`, `DeviceLivenessSample`, `DeviceLivenessReport` and `DeviceLivenessEvaluator` (data-model sections 3 and 4, research R-002).
3. Add `IDeviceLivenessTracker` / `DeviceLivenessTracker` (data-model sections 2 and 5). Register it as a singleton with the registered `TimeProvider`. Register it always, also in stub mode.

### B. ADB time limits (FR-009, FR-011, FR-012)

1. `AdbClient.ExecAsync` and `GetScreenshotPngAsync`: after the process starts, register the kill on the token before the first read (`ct.Register(() => TryKill(proc))`). `TryKill` kills the process tree when it did not exit, and ignores `InvalidOperationException` and `Win32Exception`.
2. Put the reads and the wait in a `try` block. In `catch`, when the token is cancelled, throw `OperationCanceledException`. A kill in a `catch` block only is not sufficient: `CopyToAsync` on the synchronous screenshot pipe does not stop on cancel (research R-004).
3. Keep all current signatures.

### C. Capture loop (FR-001, FR-003, FR-006)

1. `BackgroundScreenCaptureService`: new optional constructor parameters `IDeviceLivenessTracker? tracker = null` and `DeviceLivenessOptions? livenessOptions = null`. Pass both to each `SessionCaptureLoop`. `StartCapture` calls `tracker.LoopStarted`. `StopCapture` and `StopAll` call `tracker.LoopStopped`.
2. `SessionCaptureLoop.RunLoopAsync`: give each capture a linked token with `CancelAfter(CaptureTimeoutMs)`. A time-out of this token (not the loop token) is a failed capture, and the loop continues. After a completed capture, compare the bytes with the current frame and call `tracker.RecordCapture(sessionId, changed)`.
3. `GameBotServiceSetup.RegisterWindowsScreenCapture`: pass the tracker and the normalized options to the constructor.

### D. Input path (FR-001, FR-004, FR-012, FR-014)

1. `SessionManager`: new optional constructor parameters `IDeviceLivenessTracker? liveness = null` and `IOptions<DeviceLivenessOptions>? livenessOptions = null`. The DI container fills them.
2. In the ADB branch of `SendInputsAsync` and in `DispatchOneInputAsync`, call `RecordInputStarted` before the first attempt of each action. Call `RecordInputCompleted` with `Completed`, `Failed` or `TimedOut` after it. When the caller cancels, record `TimedOut` if the action ran for longer than `InputTimeoutMs`, and `Cancelled` if not. Use one small private helper so that the three action kinds do not repeat the code.
3. Add the internal seam `IAdbSessionClient` and the internal test constructor (tasks.md, "Decision: the ADB seam").
4. `SendInputsWithResultsAsync`: for each action, create a linked token with `CancelAfter(InputTimeoutMs)` and pass it to `DispatchOneInputAsync`. Catch `OperationCanceledException` when the action token fired and the caller token did not. Then add `InputActionResult(index, false, "<type>: device did not answer in <N> ms", TimedOut: true)` and stop the loop.
5. `StopSession` and `CleanupIdleSessions` call `tracker.Remove`.
6. `SendInputsAsync` gets no time limit (clarification 5).

### E. Liveness service and session endpoints (FR-007 to FR-013)

1. Add `ISessionTransportCheck` / `AdbSessionTransportCheck` (bounded `adb get-state`), `ISessionDirectCapture` / `AdbSessionDirectCapture` (probe capture), and `ISessionLivenessService` / `SessionLivenessService` (data-model section 7, research R-007). Register them as singletons. Do not use `SessionManager.GetSnapshotAsync` for the probe: it returns a stub PNG when the capture fails.
2. Health endpoint: call `ProbeAsync`. Keep the current `adb` block values. Add `liveness` (contract `session-health.md`). Keep the endpoint body small: build the block in a static helper.
3. Inputs endpoint: apply the rule table of contract `session-inputs.md` in a static helper `MapDispatchResult(dispatch, report)`.
4. Snapshot endpoint: linked token with `CancelAfter(CaptureTimeoutMs)`. Map the time-out to `504 capture_timeout`. Add the headers when the tracker has data for the session.
5. Screenshot endpoint: add the headers on both paths. Pass a linked token with the capture limit to `GetSnapshotAsync`. Map the time-out to `504 capture_timeout`. Keep `emulator_unavailable` for other failures.
6. `CaptureHeaders`: one constant array of the four header names. `GameBotServiceSetup` uses it in `WithExposedHeaders`. One helper writes the three new headers with invariant culture.

### F. Queue gate and watch (FR-015 to FR-018)

1. `QueueLivenessEpisode` (data-model section 8) and `QueueRunHandle.Liveness`. The episode keeps two separate records: the gate log entries (one for each sequence) and the fault cycle.
2. `QueueCycleLedger.RecordFaultCycle` (data-model section 9).
3. `IExecutionLogService.LogQueueDeviceFaultAsync` and its implementation (research R-014). Both are in `Services/ExecutionLog/ExecutionLogService.cs`. Update the 2 test fakes. Move `RecordingExecutionLog` to the shared file `tests/unit/Queues/RecordingExecutionLog.cs`.
4. `QueueExecutionService`: new optional constructor parameter `ISessionLivenessService? liveness = null` at the end. Add the private helper `TryGateOnLivenessAsync` (research R-011). Call it one time for each firing group, from a local function `FireGroupAsync`, not in `RunOneSequenceAsync`. The gate holds a firing only for the hard reasons `capture_stalled`, `input_timeout` and `transport_not_ready`. A held firing does not run, and its BeforeEachRun and EveryStep guard passes do not run. The site does not update its counters and marks, so the firing stays due and keeps its daily retry attempt. The site puts back an entry that it removed from its register, with its original due time. The loop then waits `QueueCheckIntervalMs` and does not complete the cycle. The gate writes one failed entry and one statistics failure only for the first held firing of each sequence in the episode. The entry uses the root `handle.RootExecutionId ?? rootId` and the sequence index `++index`. After the first hold, the other due firings of the iteration go through the gate in hold-only mode: no new evaluation, no run, and the put-back rule of the site. A held at-queue-start entry and the at-start entries after it move to `PendingNextCycleStart`, so the run enters the loop and does not end. A held live schedule goes back with `TryAdd`, so a newer schedule wins.
5. `QueueLivenessWatch` (research R-012). Start it in the run after the session is bound. Cancel it and await it in the `finally` block, before the session stops. The watch never throws out: it logs each exception at Warning level and continues. The watch never stops the run. A stop policy that the operator configured acts through the evaluator (FR-018).
6. `QueueRunHandle.TryMarkPolicyTripped()`: an atomic check and mark under `_policyLock`. `QueueFailurePolicyEvaluator.OnCycleCompleted` uses it, so that two concurrent calls (the watch and the run loop) act one time.
7. `QueuesEndpoints.ProjectHealth`: take `ISessionLivenessService?`. When the handle has a session, evaluate, call `handle.Liveness.Observe`, and fill `DeviceLiveness` from the snapshot.
8. Do not change `QueueDeviceWatchdogService` (research R-017).

### G. OpenAPI (FR-020)

As in research R-015: `SessionLivenessSchema` and examples in `SwaggerConfig.cs`, the new `DeviceLivenessSchemaFilter`, the capture headers, and the `503`/`504` responses. Register the filter in `GameBotServiceSetup`.

### H. Documentation

- `docs/architecture.md`: liveness model, configuration section, API surface changes, queue gate and watch, "Last reviewed" date.
- `CHANGELOG.md`: `Added` (liveness block, headers, queue device liveness, configuration) and `Changed` (inputs endpoint `503`/`504`; ADB processes are killed on time-out).
- `specs/STATUS.md`: row 106.
- Spec Status at the end.

## Test Plan

| Area | Test | Requirement |
|---|---|---|
| Evaluator | - one test for each row of the rule table<br>- rule order (transport before input, input before stall, stall before no-change)<br>- a frame change after a timed-out input clears `input_timeout`<br>- a later completed input clears it<br>- `cancelled` does not give `input_timeout`<br>- a static screen for longer than the stale limit, then one tap now → `live`<br>- a stopped loop with old data and a new input → `unknown` with a probe<br>- a static screen with no input → `live` and `stale: true`<br>- frames that change → `live`, not stale<br>- stub → `unknown` | FR-002 to FR-006, SC-006, edge cases |
| Tracker | - loop start resets data<br>- loop stop keeps data<br>- capture with and without change<br>- `FirstInputAfterChangeAt` set and cleared<br>- input outcome `Pending` → `Completed`<br>- remove<br>- no record after remove<br>- two sessions on one device stay separate | FR-001, edge cases |
| Options | - defaults equal the spec<br>- the options clamp values below the minimum | FR-019 |
| ADB kill | - a uniquely named copy of `ping.exe` as the fake `adb` (and a batch file around it for `GetScreenshotPngAsync`)<br>- the process runs 30 s<br>- cancel after 200 ms<br>- the call returns in less than 2 s, and no process with the unique name is alive | FR-011, FR-012, SC-003, SC-004 |
| Capture loop | - same bytes two times → `changed` false<br>- new bytes → `changed` true<br>- a provider that hangs → the loop continues after `CaptureTimeoutMs` and records no capture | FR-003, FR-006 |
| Inputs time-out | - an action that hangs → `TimedOut` result, later actions not sent, return in less than limit + 2 s<br>- tracker outcome `timed_out` | FR-012, US3 scenario 1 |
| Liveness service | - transport time-out → `not_live transport_not_ready`, bounded<br>- no capture data + direct capture success → `live`<br>- failure or time-out → `not_live capture_stalled`, bounded | FR-008, FR-009, US1 scenario 6 |
| Queue episode | - open on `not_live`<br>- close on `live`<br>- one gate entry for each sequence<br>- `gatedFirings` counts all held firings<br>- fault cycle claim one time after grace, also after gate entries | FR-015, FR-017 |
| Queue gate | - hard reason → the sequence does not run, no guard run, the firing stays due, the daily retry attempt stays the same<br>- first held firing of a sequence → one failed log entry and one statistics failure<br>- later held firings of that sequence → nothing<br>- not live for N intervals → at most one entry for each sequence plus one watch entry<br>- held firings run after recovery at their original cadence<br>- two sequences due at the same time → one entry for each sequence (hold-only walk)<br>- an AtQueueStart-only template on a device that is not live → the run does not end, and the entries run after recovery<br>- `no_change_after_input`, `live` and `unknown` → runs | FR-016, SC-008, US4 scenarios 3, 5 and 6 |
| Queue watch | - not live longer than grace → one queue log entry, one fault cycle, policy evaluated, also after gate entries<br>- a queue with `cycleExecution: false` → the consecutive-failure count increases by one for each episode<br>- still not live → no second entry<br>- live then not live → a new entry<br>- never stops the run directly; a stop policy acts through the evaluator<br>- two concurrent `OnCycleCompleted` calls → one notification | FR-017, FR-018, US4 scenario 4 |
| Contract | - health `liveness` block: stub → `unknown`<br>- health `liveness` block: fake session with serial + fake transport fault → `not_live`<br>- inputs `504` keeps the `dispatched` value of the actions before the timed-out action<br>- inputs `504` and `503` bodies with a fake `ISessionManager`<br>- current `202`/`400 invalid_request`/`400 invalid_input_actions`/`409` unchanged<br>- screenshot headers<br>- snapshot `504`<br>- CORS `Access-Control-Expose-Headers` has the four names<br>- queue `health.deviceLiveness` (with `gatedFirings`) is present while the queue runs | FR-007, FR-010 to FR-013, FR-015, SC-007 |
| OpenAPI | - `SessionLivenessSchema` fields and enums<br>- `QueueDeviceLivenessResponse`<br>- header descriptions<br>- `503`/`504` on inputs<br>- `504` on screenshot and snapshot | FR-020 |
| Regression | - all current session, screenshot, inputs and queue tests pass | SC-007 |

Test isolation: unit tests use `FakeTimeProvider` and fakes. Contract tests that start a queue set their own data folder (memory "GameBot test-harness gotchas"). Contract tests change the options with `services.PostConfigure<DeviceLivenessOptions>(...)` in `ConfigureTestServices`, not with configuration values.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| A false `not_live` stops queue firings on a live device | The reason `no_change_after_input` does not hold firings. Thus the firings continue to send inputs that can change the screen and clear the state (no latch). Rule 5 also needs the stale limit (5 min) after the first input that followed the last frame change. The hard reasons (`capture_stalled`, `input_timeout`, `transport_not_ready`) are direct signs of a fault. The next evaluation after a recovery is `live` again, and the held firings run. |
| A queue writes too many log entries while the device is not live | A held firing stays due, and the loop waits `QueueCheckIntervalMs` before the next check. The gate writes at most one entry for each sequence and episode. The watch writes one entry for each episode (research R-011, SC-008). |
| A held firing uses up the daily retries or ends a self-reschedule chain | A held firing does not update the counters and marks of its site, and keeps its attempt number. The site puts back a removed entry with its original due time (research R-011). |
| The failure count does not increase for a queue that does not cycle | The watch records one fault cycle for each episode. This record is separate from the gate entries (research R-012). |
| During an idle pause, a wedged device with captures that complete is not detected | Recorded limit (spec Assumptions, SC-005). The watch detects it when the captures stop. Otherwise the next firing sends input, and rule 5 applies after the stale limit. |
| `SessionManager` creates `AdbClient` directly, so a unit test cannot fake a hung tap | Add an internal seam: the interface `IAdbSessionClient` and an optional `Func<string, IAdbSessionClient>` factory (default: `AdbClient`), visible to the unit test assembly with `InternalsVisibleTo`. An internal test constructor sets the ADB-mode switch and the factory (tasks.md, "Decision: the ADB seam"). |
| Kill on cancel changes the behavior of current callers of `AdbClient` | A cancelled call already threw. The only new effect is that the `adb` process ends. No caller depends on the process that stays alive. |
| The byte compare costs CPU on large screens | One vectorized compare for each capture, below 1 ms (perf note). |
| The watch task and the run loop write to the handle at the same time | `QueueLivenessEpisode` and `QueueCycleLedger` each use one lock. The watch writes the log with `CancellationToken.None` so that a stop does not leave half an entry. Both can call `QueueFailurePolicyEvaluator.OnCycleCompleted` at the same time. Today the evaluator reads `PolicyTripped` and then calls `MarkPolicyTripped` in two steps, so the policy can act two times. The new `QueueRunHandle.TryMarkPolicyTripped()` does the check and the mark in one step under `_policyLock`. A unit test makes sure that two concurrent calls give one notification. |
| A put-back races with an API write or a sequence that enqueues | A held live schedule goes back with `TryAdd`, so a newer schedule from the API wins. `RearmTimerFiring` adds a Timer firing only when the register has no Timer firing for that sequence. The `ConcurrentQueue` registers can get a different order after a hold. This is accepted, because each entry still fires one time (research R-011). |
| A held at-queue-start firing is lost, or an AtQueueStart-only run ends | The site moves the held entry and the at-start entries after it into `PendingNextCycleStart`. `HasPendingSelfRescheduleWork` then keeps the run in the loop, and the next-cycle-start put-back rule applies (research R-011). |
| A time-of-day firing held past midnight is lost for that day | Known limit (spec Edge Cases, Assumptions). No design change. |
| New constructor parameters break the test harnesses | All new parameters are optional (default null) and at the end of the constructors. `ISessionManager` does not change. Only the 2 `IExecutionLogService` fakes change. |
| The run method grows and the analyzers slow the build | The gate is a helper of about 25 lines. `FireGroupAsync` replaces the repeated block at the nine firing sites, so the run method does not grow much. The watch is a separate class. |

## Phase Status

- [x] Phase 0: research complete ([research.md](./research.md))
- [x] Phase 1: design complete ([data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md); agent context updated in `CLAUDE.md`)
- [x] Phase 2: tasks complete ([tasks.md](./tasks.md))
