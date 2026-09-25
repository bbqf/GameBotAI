---

description: "Task list for feature 106: make a wedged emulator visible to the API (B-019)"
---

# Tasks: Make a wedged emulator visible to the API (B-019)

**Input**: Design documents from `specs/106-wedged-device-liveness/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Issue**: #220 (B-019). The PR closes it.

**Tests**: Tests are necessary. Constitution Principle II and the plan Test Plan require them. Each bug scenario of the spec has a test that fails before the change. Write each test task before the implementation task of the same story. Make sure that the test fails first.

**Organization**: The tasks are in groups, one group for each user story. Each story can be tested independently.

**Quality gate**: `dotnet build "C:\src\GameBot\GameBot.sln"` and `dotnet test "C:\src\GameBot\GameBot.sln"`. The web UI does not change (research R-016). A task can change a file under `src/web-ui`. Then the web UI gate is `vite build` and `jest`. Lint and `tsc --noEmit` have failures from before this feature.

**Language**: All new text MUST use ASD-STE100 Simplified Technical English (Constitution Principle VI). This includes code comments, XML docs, API descriptions, error messages, CHANGELOG entries and docs. Code, identifiers and paths stay as they are.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel with other [P] tasks. The tasks change different files and do not depend on a task that is not complete.
- **[Story]**: The user story of the task (US1, US2, US3, US4).
- Each task gives the exact file path.

## Path Conventions

- Source: `src/GameBot.Domain`, `src/GameBot.Emulator`, `src/GameBot.Service`
- Tests: `tests/unit` (assembly `GameBot.UnitTests`), `tests/contract` (assembly `GameBot.ContractTests`)
- Unit tests use the current `tests/unit/Queues/FakeTimeProvider.cs` (or a copy in the same namespace pattern) and fakes. They need no device.
- Contract tests change the liveness options with `services.PostConfigure<DeviceLivenessOptions>(...)` in `ConfigureTestServices` (research R-013). They do not use configuration values.
- Contract tests that start a queue set their own data folder.

## Decision: the ADB seam in `SessionManager`

`SessionManager` creates `AdbClient` directly, so today no unit test can simulate a hung tap. The final shape of the seam is:

1. A new **internal** interface `IAdbSessionClient` in `src/GameBot.Emulator/Adb/IAdbSessionClient.cs`. It has the four calls that `SessionManager` makes for a bound device: `TapAsync`, `SwipeAsync`, `KeyEventAsync` and `GetScreenshotPngAsync`. The signatures are the same as the current `AdbClient` methods. `AdbClient` implements it with no signature change, and `AdbClient` stays public.
2. A new private field `Func<string, IAdbSessionClient> _clientFactory` in `SessionManager`. The public constructor sets it to `serial => new AdbClient(_adbLogger).WithSerial(serial)`. One private helper `CreateDeviceClient(string serial)` replaces each `new AdbClient(_adbLogger).WithSerial(...)` call. These calls are in `SendInputsAsync`, `SendInputsWithResultsAsync` and `GetSnapshotAsync`. `ResolveOrValidateDeviceSerial` does not change.
3. A new **internal** test constructor `SessionManager(IOptions<SessionOptions>, ILogger<SessionManager>, ILogger<AdbClient>, AppConfig?, IDeviceLivenessTracker?, IOptions<DeviceLivenessOptions>?, Func<string, IAdbSessionClient> clientFactory)`. It sets the ADB-input flag to `true` for the input and snapshot paths. It does not read `GAMEBOT_USE_ADB` for them. Session creation stays in stub mode (no `adb devices` call). The test creates a session and then sets `session.DeviceSerial` to a fake serial.
4. `[assembly: InternalsVisibleTo("GameBot.UnitTests")]` in the emulator project. The DI container sees only the public constructor, so production does not change.

This is the smallest seam that lets unit tests simulate a hung tap, a failed tap and a hung screenshot with no `adb` executable.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the projects and record the baseline.

- [ ] T001 Add `<ItemGroup><InternalsVisibleTo Include="GameBot.UnitTests" /></ItemGroup>` to `src/GameBot.Emulator/GameBot.Emulator.csproj`. Make sure that the unit test assembly name is `GameBot.UnitTests` (see `tests/unit/*.csproj`).
- [ ] T002 Run `dotnet build "C:\src\GameBot\GameBot.sln"` and `dotnet test "C:\src\GameBot\GameBot.sln"` on the branch before a code change. Write down the tests that fail before the change (if any). Later gates compare with this baseline.

---

## Phase 2: Foundational (Prerequisites for all stories)

**Purpose**: The liveness model, the ADB time limits, the data writers and the liveness service. All user stories need them.

**CRITICAL**: Do not start a user story before this phase is complete.

### Liveness model (FR-001 to FR-006, FR-019)

- [ ] T003 [P] Create `DeviceLivenessOptions` in `src/GameBot.Domain/Sessions/DeviceLivenessOptions.cs`:
  - Add `SectionName = "Service:DeviceLiveness"`.
  - Add the seven properties with the defaults and minimums of data-model section 1.
  - Add `Normalized()`. It returns a copy with each value set to at least its minimum. It never throws.
  - Add XML docs in STE.
- [ ] T004 [P] Create the constants in `src/GameBot.Domain/Sessions/DeviceLivenessStates.cs`:
  - Static class `DeviceLivenessStates`: `Live = "live"`, `NotLive = "not_live"`, `Unknown = "unknown"`.
  - Static class `DeviceLivenessReasons`: `CaptureStalled`, `InputTimeout`, `NoChangeAfterInput`, `TransportNotReady`, with snake-case values.
  - In `DeviceLivenessReasons`, add the set `Hard` with `capture_stalled`, `input_timeout` and `transport_not_ready` (research R-011).
  - Enum `InputOutcome`: `Pending`, `Completed`, `TimedOut`, `Failed`, `Cancelled`.
  - Helper `InputOutcomes.ToWire(InputOutcome)`. It returns `pending`, `completed`, `timed_out`, `failed` or `cancelled`.
- [ ] T005 [P] Create the record `DeviceLivenessSample` in `src/GameBot.Domain/Sessions/DeviceLivenessSample.cs`. Use the fields of data-model section 3 (with `FirstInputAfterChangeAt`).
- [ ] T006 [P] Create the record `DeviceLivenessReport` in `src/GameBot.Domain/Sessions/DeviceLivenessReport.cs`. Use the fields of data-model section 4 (`State`, `Reason`, `FrameAgeMs`, `UnchangedMs`, `Stale`, `LastInputAt`, `LastInputOutcome`, `NeedsProbe`).
- [ ] T007 Create the static pure class `DeviceLivenessEvaluator` in `src/GameBot.Domain/Sessions/DeviceLivenessEvaluator.cs` (depends on T003 to T006):
  - Its method is `Evaluate(DeviceLivenessSample sample, DeviceLivenessOptions options, DateTimeOffset now)`.
  - Apply the seven rules of data-model section 4 in their order. The first match wins.
  - Compute `FrameAgeMs`, `UnchangedMs` and `Stale` (FR-006) for all rules.
  - Rule 5 and `Stale` apply only while `CaptureLoopRunning` is `true`.
  - The class has no I/O and no clock.
- [ ] T008 Create the tracker (depends on T004, T005):
  - Create `IDeviceLivenessTracker` in `src/GameBot.Domain/Sessions/IDeviceLivenessTracker.cs`.
  - Create `DeviceLivenessTracker` in `src/GameBot.Domain/Sessions/DeviceLivenessTracker.cs` with the constructor `DeviceLivenessTracker(TimeProvider? timeProvider = null)`.
  - Keep one internal record for each session ID behind one lock.
  - Add the members of data-model section 5: `LoopStarted`, `LoopStopped`, `RecordCapture`, `RecordInputStarted`, `RecordInputCompleted`, `Remove`, `Sample`, `HasCaptureData` and `Now`.
  - Apply the transitions of data-model section 2. Set and clear `FirstInputAfterChangeAt` as section 2 tells.
  - Only `LoopStarted` and `RecordInputStarted` create a record. The other write methods do nothing for an unknown session ID.
  - `RecordCapture` does nothing for a stopped loop.
  - `HasCaptureData` returns `true` when a record exists and `LoopStartedAt` has a value. The snapshot headers use it.
- [ ] T009 [P] Write `DeviceLivenessOptionsTests` in `tests/unit/Sessions/DeviceLivenessOptionsTests.cs`. Cases:
  - The defaults equal the spec Assumptions.
  - The options clamp values below the minimum (0 and negative values).
  - `Normalized()` does not change the source object.
- [ ] T010 [P] Write `DeviceLivenessEvaluatorTests` in `tests/unit/Sessions/DeviceLivenessEvaluatorTests.cs`. Write one test for each rule of the table. Add tests for these cases:
  - Rule order: transport before input, input before stall, stall before no-change.
  - An input with the outcome `Pending` for longer than `InputTimeoutMs` gives `input_timeout`.
  - A frame change after a timed-out input clears `input_timeout`. A later completed input also clears it.
  - A `cancelled` or `failed` outcome does not give `input_timeout`.
  - Stall uses `LoopStartedAt` before the first capture.
  - Rule 5 counts from `FirstInputAfterChangeAt`. The screen is static for longer than the stale limit, and one tap arrives now. The result is `live` with `stale: true`.
  - The same sample after `StaleLimitMs` more, with no frame change, gives `not_live` `no_change_after_input`.
  - One inert tap within the stale limit gives `live`.
  - The capture loop stopped, the frame data is old, and a new input arrives. The result is `unknown` with `NeedsProbe: true` and `stale: false`.
  - A static screen with no input gives `live` and `stale: true` (SC-006).
  - Frames that change give `live` and `stale: false`.
  - No device gives `unknown`. No loop and no data give `unknown` with `NeedsProbe: true`.
- [ ] T011 [P] Write `DeviceLivenessTrackerTests` in `tests/unit/Sessions/DeviceLivenessTrackerTests.cs` with `FakeTimeProvider`. Cases:
  - `LoopStarted` clears the earlier data (edge case "Capture loop restarted").
  - `LoopStopped` keeps the data.
  - `RecordCapture` with a change and with no change.
  - The first input after a change sets `FirstInputAfterChangeAt`. A second input does not move it.
  - A frame change clears `FirstInputAfterChangeAt`.
  - An input goes from `Pending` to `Completed`.
  - `Remove` deletes the record.
  - `RecordCapture`, `RecordInputCompleted` and `LoopStopped` after `Remove` do not make the record again.
  - `RecordCapture` after `LoopStopped` changes nothing.
  - `HasCaptureData` is `false` for an unknown session. It is `true` after `LoopStarted`, also after `LoopStopped`.
  - Two sessions on one device stay separate.
  - An unknown session ID gives an empty sample.
  - A parallel smoke test with many writers does not throw.

### ADB time limits (FR-009, FR-011, FR-012)

- [ ] T012 Change `ExecAsync` and `GetScreenshotPngAsync` in `src/GameBot.Emulator/Adb/AdbClient.cs` (research R-004):
  - After `proc.Start()`, register the kill on the token before the first read: `using var reg = ct.Register(() => TryKill(proc));`.
  - Add the private static helper `TryKill(Process proc)`. It calls `proc.Kill(entireProcessTree: true)` when the process did not exit.
  - `TryKill` ignores `InvalidOperationException` and `Win32Exception`.
  - Put the reads and the wait in a `try` block. In `GetScreenshotPngAsync`, these are `CopyToAsync` and `WaitForExitAsync`. In `ExecAsync`, this is `WaitForExitAsync`.
  - In `catch`, when `ct.IsCancellationRequested` is `true`, throw `OperationCanceledException(ct)`. After the kill, the read can fail with an `IOException`.
  - Keep all signatures.
- [ ] T013 [P] Write `AdbClientCancellationTests` in `tests/unit/Emulator/AdbClientCancellationTests.cs` (`[SupportedOSPlatform("windows")]`):
  - Setup: copy `%SystemRoot%\System32\ping.exe` to a new temporary folder as `fake-adb-<guid>.exe`.
  - Build `new AdbClient(<path of the copy>)`.
  - Call `ExecAsync("-n 30 127.0.0.1", token)` with a token that cancels after 200 ms.
  - Make sure that the call throws `OperationCanceledException` in less than 2 s.
  - Make sure that no process with the name `fake-adb-<guid>` is alive. Use `Process.GetProcessesByName`, and poll for up to 2 s.
  - The unique name makes sure that other `ping` processes cannot make the test fail.
  - Delete the folder in `Dispose`.
- [ ] T014 Add a Windows test for `GetScreenshotPngAsync` in `tests/unit/Emulator/AdbClientCancellationTests.cs` (the file of T013, so after T013):
  - `AdbClient` always adds the arguments `exec-out screencap -p`, so the fake is a batch file.
  - Write `fake-adb-<guid>.cmd` in the temporary folder of T013 with one line: `@"<folder>\fake-adb-<guid>.exe" -n 30 127.0.0.1 >nul`.
  - The batch file stays silent and keeps the output pipe open for 30 s.
  - Build `new AdbClient(<path of the .cmd file>)`. Call `GetScreenshotPngAsync(token)` with a token that cancels after 200 ms.
  - Make sure that the call throws `OperationCanceledException` in less than 2 s after the cancel.
  - Make sure that no process with the name `fake-adb-<guid>` is alive after 2 s.
  - Before T012, this test does not return for 30 s, so it fails first.

### ADB seam and input records in `SessionManager` (FR-001, FR-014)

- [ ] T015 Add the optional last positional parameter `bool TimedOut = false` to `InputActionResult` in `src/GameBot.Emulator/Session/ISessionManager.cs` (data-model section 6). The current three-argument calls must still compile.
- [ ] T016 Create the internal interface `IAdbSessionClient` in `src/GameBot.Emulator/Adb/IAdbSessionClient.cs`. Give it `TapAsync`, `SwipeAsync`, `KeyEventAsync` and `GetScreenshotPngAsync`, with the signatures of `AdbClient`. Make `AdbClient` in `src/GameBot.Emulator/Adb/AdbClient.cs` implement it (see "Decision: the ADB seam").
- [ ] T017 Change `src/GameBot.Emulator/Session/SessionManager.cs` (depends on T008, T015, T016):
  - Add the optional parameters `IDeviceLivenessTracker? liveness = null` and `IOptions<DeviceLivenessOptions>? livenessOptions = null` at the end of the public constructor.
  - Add the internal test constructor with `Func<string, IAdbSessionClient> clientFactory`.
  - Add the helper `CreateDeviceClient(string serial)`. Use it in `SendInputsAsync`, `SendInputsWithResultsAsync` and `GetSnapshotAsync`.
  - Change the `adb` parameter type of `DispatchOneInputAsync` to `IAdbSessionClient?`.
  - Add one small private helper for the input records (research R-010).
  - The helper calls `RecordInputStarted` before the first attempt of each ADB action.
  - After the action, the helper calls `RecordInputCompleted` with `Completed`, `Failed` or `TimedOut`.
  - The caller token can stop the action. Then the helper records `TimedOut` if the action ran for longer than `InputTimeoutMs`, and `Cancelled` if not.
  - Measure the time with `tracker.Now`, so that tests with `FakeTimeProvider` work. Thus no cancel leaves the outcome `Pending`.
  - Use the helper for tap, swipe and key in both input paths. Only ADB mode records data. Stub mode records nothing.
  - Call `tracker.Remove(id)` in `StopSession`, and in `CleanupIdleSessions` when it evicts a session.
  - `SendInputsAsync` gets no time limit (clarification 5).
- [ ] T018 [P] Write `SessionManagerInputLivenessTests` in `tests/unit/Emulator/SessionManagerInputLivenessTests.cs`. Use the internal test constructor, a fake `IAdbSessionClient` and a real `DeviceLivenessTracker` on `FakeTimeProvider`. Cases:
  - A completed tap records `completed`.
  - A tap that returns a non-zero exit code on all attempts records `failed`.
  - `SendInputsAsync` (the sequence path) records the input data too. Its return value does not change.
  - A hung tap in `SendInputsAsync` whose caller token is cancelled before `InputTimeoutMs` records `cancelled`.
  - The same tap cancelled after `InputTimeoutMs` records `timed_out`.
  - Stub mode (public constructor with `GAMEBOT_USE_ADB=false`) records nothing.
  - `StopSession` removes the record.

### Capture loop (FR-001, FR-003, FR-006)

- [ ] T019 Change `src/GameBot.Emulator/Session/BackgroundScreenCaptureService.cs` (depends on T008):
  - Add the optional constructor parameters `IDeviceLivenessTracker? tracker = null` and `DeviceLivenessOptions? livenessOptions = null`. Pass them to each `SessionCaptureLoop`.
  - `StartCapture` calls `tracker.LoopStarted`. `StopCapture` and `StopAll` call `tracker.LoopStopped`.
  - In `SessionCaptureLoop.RunLoopAsync`, give each capture a linked token with `CancelAfter(CaptureTimeoutMs)` (research R-009).
  - A time-out of this token (not the loop token) is a failed capture (Debug log). The loop continues.
  - After a completed capture, compare the new bytes with the bytes of the current frame: `previous.PngBytes.AsSpan().SequenceEqual(png)`.
  - The first frame is a change. Then call `tracker.RecordCapture(sessionId, changed)` (research R-003).
- [ ] T020 [P] Extend `tests/unit/BackgroundScreenCaptureServiceTests.cs`. Use a fake `IAdbScreenCaptureProvider`. Cases:
  - The same bytes two times give `changed: false` on the second capture.
  - New bytes give `changed: true`.
  - A provider that hangs until its token is cancelled does not stop the loop.
  - After `CaptureTimeoutMs`, the next capture runs. The tracker records no capture for the hung one.
  - `StartCapture` and `StopCapture` call `LoopStarted` and `LoopStopped`.

### Liveness service, probes and service registration (FR-007 to FR-010)

- [ ] T021 [P] Create `ISessionTransportCheck` in `src/GameBot.Service/Services/Liveness/ISessionTransportCheck.cs` and `AdbSessionTransportCheck` in `src/GameBot.Service/Services/Liveness/AdbSessionTransportCheck.cs`:
  - `CheckAsync(string deviceSerial, CancellationToken ct)` runs `adb get-state` with the caller token.
  - It returns `SessionTransportCheckResult(Ok, Stdout, Stderr, Error)` (data-model section 7).
  - Move the current `adb get-state` logic of `GetSessionHealth` here. Keep the same `ok`, `stdout`, `stderr` and `error` values.
- [ ] T022 [P] Create `ISessionDirectCapture` in `src/GameBot.Service/Services/Liveness/ISessionDirectCapture.cs` and `AdbSessionDirectCapture` in `src/GameBot.Service/Services/Liveness/AdbSessionDirectCapture.cs`:
  - `TryCaptureAsync(string deviceSerial, CancellationToken ct)` returns `true` only for a PNG with at least one byte.
  - An exception (not the caller cancel) or an empty PNG gives `false`.
  - Do not use `SessionManager.GetSnapshotAsync` (research R-007).
- [ ] T023 Create `ISessionLivenessService` in `src/GameBot.Service/Services/Liveness/ISessionLivenessService.cs` and `SessionLivenessService` in `src/GameBot.Service/Services/Liveness/SessionLivenessService.cs` (depends on T007, T008, T021, T022):
  - `Options`: the normalized options.
  - `Evaluate(EmulatorSession session)`: data only, no I/O.
  - `ProbeAsync(EmulatorSession session, CancellationToken ct)` returns `SessionLivenessProbeResult(Adb, Liveness)`.
  - `ProbeAsync` step 1: the transport check with a linked token and `CancelAfter(TransportCheckTimeoutMs)`.
  - A time-out gives `Ok = false` and `Error = "adb get-state did not answer in <N> ms"`.
  - `ProbeAsync` step 2: the evaluation with `TransportReady`.
  - `ProbeAsync` step 3: when `NeedsProbe` (rule 7), one direct capture with `CancelAfter(CaptureTimeoutMs)`.
  - Success gives `live` with `FrameAgeMs = 0`. Failure or time-out gives `not_live`, `capture_stalled`.
  - A session with no device serial gives `unknown` and no `adb` call.
- [ ] T024 [P] Create `CaptureHeaders` in `src/GameBot.Service/Services/Liveness/CaptureHeaders.cs`:
  - Constants `CaptureId`, `AgeMs`, `UnchangedMs`, `Stale`.
  - The array `ExposedHeaders` with the four names.
  - `Apply(HttpResponse response, long ageMs, long unchangedMs, bool stale)`. It writes the three new headers with invariant culture and `true`/`false` in lower case.
  - A pure helper `FromReport(DeviceLivenessReport report, bool directCapture)`. It gives the values of contract `screenshot-snapshot.md`.
  - For a direct capture, the age is `0`. With no loop data, the unchanged time is `0` and stale is `false`.
- [ ] T025 Change `src/GameBot.Service/GameBotServiceSetup.cs` (depends on T017, T019, T023, T024):
  - Add `builder.Services.Configure<DeviceLivenessOptions>(builder.Configuration.GetSection(DeviceLivenessOptions.SectionName))`.
  - Register `IDeviceLivenessTracker` as a singleton with the registered `TimeProvider`. Register it always, also in stub mode.
  - Register `ISessionTransportCheck`, `ISessionDirectCapture` and `ISessionLivenessService` as singletons.
  - In `RegisterWindowsScreenCapture`, pass the tracker and the normalized options to the `BackgroundScreenCaptureService` constructor.
  - At both CORS policies, replace `WithExposedHeaders("X-Capture-Id")` with `WithExposedHeaders(CaptureHeaders.ExposedHeaders)`.
  - Keep `Program.cs` thin (build-time analyzers).
- [ ] T026 Run `dotnet build "C:\src\GameBot\GameBot.sln"`. Then run `dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Liveness|FullyQualifiedName~BackgroundScreenCapture|FullyQualifiedName~SessionManager|FullyQualifiedName~AdbClient"`. Fix all failures before Phase 3.

**Checkpoint**: The model, the data writers and the liveness service work. The user stories can start.

---

## Phase 3: User Story 1 - Session health tells when the device is not live (Priority: P1) MVP

**Goal**: `GET /api/sessions/{id}/health` returns a `liveness` block. The block tells if the device is live, and why not. The call returns in bounded time.

**Independent Test**: Use a fake transport check and a fake direct capture (or fake capture data in the tracker). Call the health endpoint. Make sure that it reports `not_live` with a reason. Then give frames that change, and make sure that it reports `live`.

### Tests for User Story 1 (write first, make sure they fail)

- [ ] T027 [P] [US1] Write `SessionLivenessServiceTests` in `tests/unit/Liveness/SessionLivenessServiceTests.cs`. Use fake `ISessionTransportCheck` and `ISessionDirectCapture` objects. Cases:
  - A transport check that hangs gives `not_live` `transport_not_ready`. The call returns in less than `TransportCheckTimeoutMs + 1 s`.
  - A transport result `Ok = false` gives `transport_not_ready`.
  - No capture data and a direct capture that succeeds give `live` with `frameAgeMs: 0`.
  - A direct capture that fails or hangs gives `not_live` `capture_stalled` in bounded time (US1 scenario 6).
  - Data in the tracker gives no direct capture.
  - A stub session gives `unknown` and no call to the fakes.
- [ ] T028 [P] [US1] Write `SessionHealthLivenessContractTests` in `tests/contract/Sessions/SessionHealthLivenessContractTests.cs`. Cases:
  - A stub session gives `liveness.state: "unknown"` with all seven fields.
  - The fields `id`, `mode`, `deviceSerial` and `adb` do not change.
  - Use a fake `ISessionManager` session with a device serial and a fake `ISessionTransportCheck` that fails. The result is `adb.ok: false` and `liveness` `not_live` / `transport_not_ready`.
  - The same session with a fake tracker state of "no change after input" gives `no_change_after_input` (US1 scenario 3).
  - `404` does not change.
- [ ] T029 [P] [US1] Create `tests/contract/DeviceLivenessOpenApiTests.cs` with tests for the session health schema:
  - `SessionHealthSchema` has the property `liveness`.
  - `SessionLivenessSchema` has the seven fields.
  - `state`, `reason` and `lastInputOutcome` have the enum values of contract `session-health.md` and a description.

### Implementation for User Story 1

- [ ] T030 [US1] Change `GetSessionHealth` in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs` (depends on T023):
  - Call `ISessionLivenessService.ProbeAsync` with the request token.
  - Keep the current `adb` block values and form.
  - Add the `liveness` block (contract `session-health.md`). A small static helper `BuildLivenessBlock(DeviceLivenessReport)` builds it.
  - Write `lastInputAt` as an ISO date-time and `lastInputOutcome` as the wire string.
- [ ] T031 [US1] Change `src/GameBot.Service/Swagger/SwaggerConfig.cs` (FR-020):
  - Add the class `SessionLivenessSchema` and the property `Liveness` on `SessionHealthSchema`.
  - Add a `liveness` block to the health example.
- [ ] T032 [US1] Create `DeviceLivenessSchemaFilter` (`ISchemaFilter`) in `src/GameBot.Service/Swagger/DeviceLivenessSchemaFilter.cs`. Give it STE descriptions and `enum` values for `SessionLivenessSchema`. Register it next to `QueueHealthSchemaFilter` in `src/GameBot.Service/GameBotServiceSetup.cs`.
- [ ] T033 [US1] Run the US1 tests (T027 to T029) and the current session tests (`--filter "FullyQualifiedName~Session"`). All must pass.

**Checkpoint**: The session health reports the device liveness. This is the MVP.

---

## Phase 4: User Story 2 - Screenshot and snapshot responses tell when the frame is stale (Priority: P1)

**Goal**: The screenshot and snapshot responses have three staleness headers. A direct capture that hangs returns `504 capture_timeout` in bounded time.

**Independent Test**: Put a frame with an old time stamp in the capture data. Alternatively, keep the same bytes for longer than the stale limit. Get the screenshot and make sure that the headers are correct. Make the direct capture hang, and make sure that the request returns `504` in bounded time.

### Tests for User Story 2 (write first, make sure they fail)

- [ ] T034 [P] [US2] Write `CaptureHeadersTests` in `tests/unit/Liveness/CaptureHeadersTests.cs`. Cases:
  - `FromReport` for a cached frame gives the report age, the unchanged time and the stale flag.
  - `FromReport` for a direct capture gives age `0`.
  - `FromReport` with no loop data gives unchanged `0` and stale `false`.
  - `Apply` writes invariant-culture integers and lower-case `true`/`false`.
- [ ] T035 [P] [US2] Write `SessionManagerSnapshotTimeoutTests` in `tests/unit/Emulator/SessionManagerSnapshotTimeoutTests.cs` with the internal test constructor:
  - Use a fake `IAdbSessionClient.GetScreenshotPngAsync` that hangs until its token is cancelled.
  - Cancel the caller token. `GetSnapshotAsync` must throw `OperationCanceledException`.
  - It must not return the 1x1 stub PNG (research R-008).
- [ ] T036 [P] [US2] Write `CaptureHeadersContractTests` in `tests/contract/Sessions/CaptureHeadersContractTests.cs`. Cases:
  - A direct capture on the screenshot endpoint has `X-Capture-Id`, `X-Capture-Age-Ms: 0`, `X-Capture-Unchanged-Ms` and `X-Capture-Stale: false`.
  - Register a `BackgroundScreenCaptureService` with a fake provider factory in `ConfigureTestServices`.
  - A cached frame that did not change for longer than a small `StaleLimitMs` gives `X-Capture-Stale: true` (US2 scenario 2).
  - A fake `ISessionManager.GetSnapshotAsync` that hangs gives `504` on the screenshot endpoint, with `error: "capture_timeout"`.
  - The same fake gives `504` on the snapshot endpoint, with `error.code: "capture_timeout"`.
  - Both calls return in less than `CaptureTimeoutMs + 2 s` (SC-004). Use a small `CaptureTimeoutMs`.
  - A CORS request has `Access-Control-Expose-Headers` with the four names.
  - `404`, `409` and `503 emulator_unavailable` do not change.
- [ ] T037 [US2] Extend `tests/contract/DeviceLivenessOpenApiTests.cs`. Make sure that `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot` document the three headers and a `504` response.

### Implementation for User Story 2

- [ ] T038 [US2] Change the screenshot endpoint in `src/GameBot.Service/Endpoints/EmulatorImageEndpoints.cs` (depends on T023, T024):
  - On the cached-frame path, add the headers from `ISessionLivenessService.Evaluate(session)` and `CaptureHeaders`.
  - On the direct path, call `GetSnapshotAsync` with a linked token of the request token and `CancelAfter(CaptureTimeoutMs)`.
  - Add the headers after a direct capture that succeeds.
  - Map a time-out of the limit (not the request token) to `504` `{ "error": "capture_timeout", "message": ... }`.
  - Keep `emulator_unavailable` for other failures.
  - Put the new logic in small private helpers.
- [ ] T039 [US2] Change `GetSnapshot` in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`:
  - Use a linked token with `CancelAfter(CaptureTimeoutMs)`.
  - A time-out of the limit gives `504` `{ "error": { "code": "capture_timeout", "message": ..., "hint": ... } }`.
  - Add the headers only when `tracker.HasCaptureData(session.Id)` is `true` (FR-010).
  - A cancel by the client stays as it is now.
- [ ] T040 [US2] Change `src/GameBot.Service/Swagger/SwaggerConfig.cs` (FR-020). Document the headers `X-Capture-Age-Ms`, `X-Capture-Unchanged-Ms` and `X-Capture-Stale`. Document the `504 capture_timeout` responses of the screenshot and snapshot operations.
- [ ] T041 [US2] Run the US2 tests (T034 to T037) and the current screenshot and image tests (`--filter "FullyQualifiedName~Screenshot|FullyQualifiedName~EmulatorImage|FullyQualifiedName~Snapshot"`). All must pass.

**Checkpoint**: Screenshots and snapshots tell staleness and never hang.

---

## Phase 5: User Story 3 - Session inputs have a time limit and do not claim a false success (Priority: P1)

**Goal**: `POST /api/sessions/{id}/inputs` returns in bounded time. A hung action gives `504 device_timeout`. A device that is not live gives `503 device_not_live` and no `dispatched: true`.

**Independent Test**: Use a fake ADB client whose tap never returns. Send one tap. Make sure that the call returns after about the input time limit with a time-out failure. Then make the device not live, and send a tap that the fake accepts. Make sure that the result is not `dispatched: true`.

### Tests for User Story 3 (write first, make sure they fail)

- [ ] T042 [P] [US3] Write `SessionManagerInputTimeoutTests` in `tests/unit/Emulator/SessionManagerInputTimeoutTests.cs`. Use the internal test constructor and a small `InputTimeoutMs` (for example 300). Cases:
  - A tap that hangs until its token is cancelled gives `InputActionResult(0, false, "tap: device did not answer in 300 ms", TimedOut: true)`.
  - The fake gets no call for the actions after it. The fake counts the calls.
  - The call returns in less than the limit plus 2 s.
  - The tracker outcome is `timed_out`.
  - A cancel of the caller token before the limit does not give `TimedOut` in the result.
  - The tracker outcome is then `cancelled`, not `pending`.
  - The ADB retries of one action share the one limit.
- [ ] T043 [P] [US3] Write `SessionInputsLivenessContractTests` in `tests/contract/Sessions/SessionInputsLivenessContractTests.cs`. Use a fake `ISessionManager` and a fake `ISessionLivenessService`. Cases:
  - No actions give `400 invalid_request`. The fake gets no dispatch call (SC-007).
  - A session that does not exist gives `409 not_running`.
  - A result with `TimedOut: true` gives `504`, `error.code: "device_timeout"` and the `results` array.
  - In the `504` case, the actions before the timed-out action keep `dispatched: true`, also with a `not_live` report (FR-013).
  - Dispatched results and a `not_live` report give `503`, `error.code: "device_not_live"` and `error.reason`.
  - In the `503` case, all results are `dispatched: false` with `failureReason: "device_not_live: <reason>"`.
  - A live report gives `202` with the current body.
  - No dispatched action gives `400 invalid_input_actions`.
- [ ] T044 [US3] Extend `tests/contract/DeviceLivenessOpenApiTests.cs`. Make sure that `POST /api/sessions/{id}/inputs` documents the `503` and `504` responses with examples.

### Implementation for User Story 3

- [ ] T045 [US3] Change `SendInputsWithResultsAsync` in `src/GameBot.Emulator/Session/SessionManager.cs` (depends on T017):
  - For each action, create a linked token with `CancelAfter(InputTimeoutMs)`. Pass it to `DispatchOneInputAsync`.
  - Catch `OperationCanceledException` when the action token fired and the caller token did not.
  - Then record the outcome `TimedOut`.
  - Add `InputActionResult(index, false, "<type>: device did not answer in <N> ms", TimedOut: true)`, and stop the loop (research R-005).
  - The delay between actions keeps the caller token.
- [ ] T046 [US3] Change the inputs endpoint in `src/GameBot.Service/Endpoints/SessionsEndpoints.cs` (depends on T023, T045):
  - Keep the current `400 invalid_request` check for a request with no actions. It comes before the dispatch.
  - After the dispatch, get the data-only report with `ISessionLivenessService.Evaluate`.
  - Apply the rule order of contract `session-inputs.md` in a static helper `MapDispatchResult(dispatch, report, sessionId)`.
  - The order after the dispatch is: `409`, `504 device_timeout`, `503 device_not_live`, `400 invalid_input_actions`, `202`.
  - For `504`, keep the `dispatched` value of the actions before the timed-out action (FR-013).
  - For `503`, rewrite the results to `dispatched: false`.
  - Use the current session error form with `message` and `hint` in STE.
- [ ] T047 [US3] Change `src/GameBot.Service/Swagger/SwaggerConfig.cs` (FR-020). Add the `503 device_not_live` and `504 device_timeout` response examples for `POST /api/sessions/{id}/inputs`.
- [ ] T048 [US3] Run the US3 tests (T042 to T044) and the current input tests (`--filter "FullyQualifiedName~Input"`). All must pass.

**Checkpoint**: Inputs are bounded and tell the truth about a wedged device.

---

## Phase 6: User Story 4 - A queue bound to a wedged device shows the fault (Priority: P2)

**Goal**: The queue health shows the device liveness. A due firing on a device that is not live for a hard reason is held. It stays due and runs after a recovery. The gate writes at most one failed entry for each sequence and episode. The watch records one failed entry and one failed cycle for each episode. The queue never stops or recovers the device by itself. Only a failure policy that the operator configured can pause or stop it (FR-018).

**Independent Test**: Start a queue on a session with a fake liveness service that goes `not_live`. Make sure that `health.deviceLiveness` shows `not_live`. Make sure that a failed entry with `device_not_live` is in the execution logs. Make sure that the consecutive-failure count increases.

### Tests for User Story 4 (write first, make sure they fail)

- [ ] T049 [P] [US4] Write `QueueLivenessEpisodeTests` in `tests/unit/Queues/QueueLivenessEpisodeTests.cs`. Cases:
  - `not_live` opens an episode with `NotLiveSince = now`. This applies to each reason.
  - A second `not_live` keeps the start time.
  - `live` and `unknown` close the episode and clear all episode data.
  - `RecordGatedFiring` returns `true` for the first held firing of a sequence, and `false` for later ones.
  - `RecordGatedFiring` returns `true` again for a different sequence.
  - `GatedFirings` counts all held firings of the episode.
  - `TryClaimFaultCycle` returns `false` before the grace period, and `true` one time after it.
  - `TryClaimFaultCycle` returns `true` also after gate entries in the episode.
  - A new episode after a close can record gate entries and a fault cycle again.
  - `Snapshot` gives a copy with `GatedFirings`.
- [ ] T050 [P] [US4] Write `QueueCycleLedgerFaultCycleTests` in `tests/unit/Queues/QueueCycleLedgerFaultCycleTests.cs`. Cases:
  - `RecordFaultCycle` seals one failed cycle with no entries.
  - It increments `ConsecutiveFailedCycles`.
  - It does not change the open cycle.
- [ ] T051 [P] [US4] Write `QueueLivenessGateTests` in `tests/unit/Queues/QueueLivenessGateTests.cs`. Use a fake session manager, a fake `ISessionLivenessService`, the fake `RecordingExecutionLog` and `FakeTimeProvider`.
  - First, move `RecordingExecutionLog` out of `tests/unit/Queues/QueueExecutionServiceTests.cs` (today a private nested class at line 234). Put it in its own file `tests/unit/Queues/RecordingExecutionLog.cs` as an `internal sealed class` in the same namespace.
  - `QueueExecutionServiceTests`, `QueueLivenessGateTests` and `QueueLivenessWatchTests` all use this shared fake. Do not change its current behavior.
  - Cases:
  - A hard reason holds the firing. The sequence does not run.
  - The first held firing of a sequence writes one failed `sequence` entry with summary `device_not_live: <reason>` under the queue run root.
  - The entry root is `handle.RootExecutionId` (the current root segment), and the entry has depth 1.
  - That firing also adds one `failure` run to the sequence statistics.
  - A second held firing of the same sequence in the same episode writes no entry and no statistics run. `GatedFirings` is 2.
  - The loop waits `QueueCheckIntervalMs` after a held firing. It does not complete a cycle.
  - One gate for each firing group: use a template with EveryStep and BeforeEachRun entries and one due timer.
  - On a `not_live` device, this template gives one failed entry (for the timer sequence). No guard sequence runs, and the log has no guard entry.
  - A standalone EveryStep pass (no once-per-run entries) gives one failed entry for the first EveryStep sequence.
  - A held time-of-day timer stays due. `MarkTimeOfDayFired` is not called, and no daily retry is armed.
  - A held daily retry keeps its attempt number. After a recovery, it runs with the same attempt number.
  - A held self-reschedule Timer firing is in the register again with its original `FireAt` (`SnapshotPendingTimerFirings`).
  - A held self-reschedule next-cycle-start firing does not fire again in the same drain. It is in its register again.
  - A held self-reschedule once-per-run firing is in `PendingOncePerRun` again.
  - A held template once-per-run pass continues with the first entry that did not run. The entries that ran do not run again.
  - Two sequences are due at the same time (for example two time-of-day timers). Each sequence gets one failed entry. The second sequence is held in hold-only mode: no new evaluation, and it does not run.
  - After a recovery, both sequences run one time.
  - A template with only AtQueueStart entries and a device that is not live: the run does not end.
  - The held at-start entry and the at-start entries after it are in `PendingNextCycleStart`.
  - After a recovery, these entries run in template order, one time each.
  - A held live schedule is not put back over a newer schedule that the API wrote for the same sequence during the hold.
  - A queue with `cycleExecution: false` does not end while it holds a firing.
  - Bounded count: the device is not live for N x `QueueCheckIntervalMs` (for example N = 20).
  - For the bounded count, use a self-reschedule Timer chain and a once-per-run entry. Set `cycleExecution: true`.
  - The log then has at most one entry for each sequence, plus one watch entry.
  - After a recovery, the held firings run. The self-reschedule chain continues at its original cadence, not every 30 s.
  - The reason `no_change_after_input` does not hold. The sequence and its guard sequences run.
  - `live` and `unknown` run the sequence and its guard sequences as before.
- [ ] T052 [US4] Write `QueueLivenessWatchTests` in `tests/unit/Queues/QueueLivenessWatchTests.cs` (after T051, because it uses the shared `RecordingExecutionLog`). Cases:
  - Not live for longer than the grace period gives one `queue` log entry.
  - It also gives one fault cycle and one call to the failure policy.
  - Still not live on the next checks gives no second entry and no second fault cycle.
  - Live and then not live again gives a new entry and a new fault cycle.
  - Gate entries in the episode do not stop the watch entry or the fault cycle.
  - The reason `no_change_after_input` also gives one entry and one fault cycle after the grace period.
  - A queue with `cycleExecution: false` and a device that is not live: `ConsecutiveFailedCycles` increases by one for each episode.
  - The same queue with held firings in the episode: the count also increases by one.
  - An exception in the evaluation is logged, and the watch continues.
  - The watch never stops the run directly or calls a recovery (FR-018). With no policy, the run stays active after the fault cycle.
  - A stop policy acts through the evaluator, not through the watch. Use a queue with a `notifyAndStop` policy and a threshold of 1.
  - After the fault cycle, the evaluator marks the stop as a policy stop (`MarkStopRequestedByPolicy`) and cancels the run. The watch code calls no stop and no cancel.
  - A cancel stops the watch.
  - Also add a case to `tests/unit/Queues/QueueFailurePolicyEvaluatorTests.cs`: two concurrent `OnCycleCompleted` calls on a failed cycle past the threshold give one notification (one `Act`).
- [ ] T053 [P] [US4] Write `QueueDeviceLivenessContractTests` in `tests/contract/Queues/QueueDeviceLivenessContractTests.cs` (own data folder). Cases:
  - A queue that runs on a stub session has `health.deviceLiveness` with `state: "unknown"` and `gatedFirings: 0`.
  - Use a fake `ISessionLivenessService` that returns `not_live` with a hard reason. Set small `QueueGracePeriodMs` and `QueueCheckIntervalMs` values with `PostConfigure`.
  - `health.deviceLiveness` has `state`, `reason`, `notLiveSince`, `stale`, `frameAgeMs`, `unchangedMs` and `gatedFirings`.
  - A failed execution-log entry with `device_not_live` appears.
  - `health.consecutiveFailedCycles` increases after the grace period.
  - The queue status stays `Running`.
- [ ] T054 [US4] Extend `tests/contract/DeviceLivenessOpenApiTests.cs`:
  - `QueueHealthResponse` has `deviceLiveness`.
  - `QueueDeviceLivenessResponse` has the seven fields, the `state` and `reason` enums, and descriptions.

### Implementation for User Story 4

- [ ] T055 [P] [US4] Create `QueueLivenessEpisode` in `src/GameBot.Service/Services/QueueExecution/QueueLivenessEpisode.cs` (data-model section 8):
  - Use one lock.
  - Add `Observe`, `RecordGatedFiring`, `TryClaimFaultCycle` and `Snapshot`.
  - Keep the two records separate: `GateLoggedSequences` for the gate, and `FaultCycleRecorded` for the watch.
  - In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, add the property `Liveness` (one new instance for each run).
  - Also add the method `RearmTimerFiring(SelfRescheduleEntry entry)`. It puts back a held Timer firing with its original `FireAt`.
  - `RearmTimerFiring` uses `_timerLock`. It adds the entry only when the register has no Timer firing for that sequence (research R-011).
  - Also add the method `bool TryMarkPolicyTripped()` to `QueueRunHandle`. Under `_policyLock`, it sets the flag and returns `true` only when the flag was `false` (research R-012).
  - In `src/GameBot.Service/Services/QueueExecution/QueueFailurePolicyEvaluator.cs`, replace the read of `handle.PolicyTripped` and the call to `MarkPolicyTripped` (lines 67 to 69) with `if (!handle.TryMarkPolicyTripped()) return;`. Thus the watch and the run loop cannot both act for one episode.
- [ ] T056 [P] [US4] Add `RecordFaultCycle(DateTimeOffset startedAt, DateTimeOffset now)` to `src/GameBot.Service/Services/QueueExecution/QueueCycleLedger.cs` (data-model section 9). Use the current lock.
- [ ] T057 [US4] Add the execution-log method for the fault episode (research R-014):
  - Add `Task LogQueueDeviceFaultAsync(string rootExecutionId, string queueId, string queueName, string reason, CancellationToken ct = default)` to `IExecutionLogService`. This `internal interface` is in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (line 76). No separate interface file exists.
  - Implement it in `ExecutionLogService` in the same file.
  - The entry has `ExecutionType = "queue"`, `FinalStatus = "failure"` and the queue `ObjectRef`.
  - The entry is a depth-1 child of the run root, with `Summary = "device_not_live: <reason>"`.
  - Update the fake `FakeExecutionLog` in `tests/unit/Queues/QueueMonitorServiceTests.cs`.
  - Update the shared fake `RecordingExecutionLog` in `tests/unit/Queues/RecordingExecutionLog.cs` (T051 moved it there from `QueueExecutionServiceTests.cs`), so that it records the call.
- [ ] T058 [US4] Change `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (research R-011; depends on T055):
  - Add the optional last constructor parameter `ISessionLivenessService? liveness = null`.
  - Add the private helper `TryGateOnLivenessAsync` (about 25 lines). It evaluates and calls `handle.Liveness.Observe`.
  - The helper holds only for `not_live` with a reason in `DeviceLivenessReasons.Hard`. For all other reports, it returns `false`.
  - On a hold, the helper calls `handle.Liveness.RecordGatedFiring(sequenceId)`.
  - When that call returns `true`, the helper logs the failed sequence entry with the normal firing context.
  - The context uses `handle.RootExecutionId ?? rootId` as parent and root, depth 1, and `SequenceIndex = ++index`.
  - The `++index` is safe: a hold sets `held`, and the held path skips the `index != indexAtIterationStart` check (T059).
  - When that call returns `true`, the helper also records a `Failure` run in the statistics. The start and end are the gate time.
  - On a hold, the helper returns `true`. It does not wait.
  - Add a hold-only mode to the helper. In this mode, it does not evaluate. It uses the report of the first hold of the iteration, and does the hold steps above.
  - Do not call the gate in `RunOneSequenceAsync`. The gate acts one time for each firing group.
  - Add the local function `FireGroupAsync` in the run body. It calls `EnsureSessionBound` and the gate. When `held` is already `true`, it calls the gate in hold-only mode.
  - When the gate returns `true`, `FireGroupAsync` sets `held` and returns `null` (held). It does not run the guard passes or the main sequence.
  - When the gate returns `false`, `FireGroupAsync` increments `index` and runs the BeforeEachRun pass (for the kinds that have one).
  - It then runs `RunOneSequenceAsync` and the EveryStep pass, as now, and returns the result.
  - Use `FireGroupAsync` at all nine main firing sites. For a firing that ran, keep the counters and marks of each site.
  - Gate the standalone EveryStep pass (no once-per-run entries) one time. Use its first sequence as the main sequence.
- [ ] T059 [US4] Add the hold rules to the firing sites in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` (research R-011; depends on T058):
  - For a held firing, do not change `executed` or `failed`.
  - For a held firing, do not call `RecordEntry`, `MarkTimeOfDayFired`, `ArmOrClearDailyRetry`, `MarkRelativeFired` or `MarkOncePerRunCompleted`.
  - Thus a held timer stays due, and a held daily retry keeps its attempt number.
  - After the first hold, continue through the other firing sites of the iteration. `FireGroupAsync` holds each other due firing in hold-only mode (`RecordGatedFiring`, no run). Each site applies its put-back rule. Thus each due sequence gets its one entry in the episode (FR-016).
  - At-queue-start pass (lines 414 to 421, before the loop): on a hold, stop the pass. Hold the at-start entries after the held entry in hold-only mode.
  - Move the held entry and these entries into `PendingNextCycleStart`, in template order. Each one is a new `SelfRescheduleEntry("at-queue-start:<template index>", sequenceId, SelfRescheduleOption.AtQueueStart, null, EntryScope(startEntry))`.
  - Thus `handle.HasPendingSelfRescheduleWork` is `true`, the run enters the loop (line 451), and the next-cycle-start rule applies. An AtQueueStart-only template does not end the run.
  - The a0 site passes `null` as the self-reschedule origin action ID for an entry whose `Id` starts with `at-queue-start:`. Such an entry did not come from a self-reschedule action.
  - Put back a held self-reschedule Timer firing with `handle.RearmTimerFiring(entry)`. Keep its original `FireAt`.
  - Also hold the other drained Timer firings in hold-only mode, and put each one back with `RearmTimerFiring`.
  - Put back a held live schedule with `handle.PendingLiveSchedules.TryAdd(sequenceId, originalDueTime)`. When the API wrote a newer schedule during the hold, `TryAdd` does nothing and the newer schedule wins.
  - Next-cycle-start drain: on a hold, dequeue the remaining entries into a list and hold each one in hold-only mode. Enqueue the held entry and the list again, in order, and stop the drain.
  - Enqueue a held once-per-run self-reschedule firing again in `PendingOncePerRun`. Hold the other snapshot entries in hold-only mode, and enqueue them again too.
  - The `ConcurrentQueue` registers can get a different order after a hold, because other writers can enqueue during the hold. Accept this (research R-011).
  - Template once-per-run pass: on a hold, call `handle.Cycles.ClearCurrentEntryIndex()`, because `SetCurrentEntryIndex` came before the gate. Hold the other entries of the pass in hold-only mode. Do not mark them.
  - Then set the local flag `oncePerRunPassHeld`.
  - Add the query `public bool OncePerRunCompletedThisCycle(int index)` to `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`. It reads `_oncePerRunDoneThisCycle` under `_gate`.
  - When `oncePerRunPassHeld` is `true`, the next pass does not call `BeginCycle`. It skips each entry for which `OncePerRunCompletedThisCycle(entryIndex)` is `true`.
  - When `held` is `true`, do not complete the cycle. Do not call `MarkOncePerRunPassDone`, `CompleteOpen` or `OnCycleCompleted`.
  - When `held` is `true`, wait `QueueCheckIntervalMs` with `TimeProvider` and the stop token. Then start the next iteration.
  - When `held` is `true`, skip the idle pause. Also skip the `break` check for a queue with `cycleExecution: false`.
- [ ] T060 [US4] Create `QueueLivenessWatch` in `src/GameBot.Service/Services/QueueExecution/QueueLivenessWatch.cs` (research R-012; depends on T055, T056, T057):
  - Use a loop with a `TimeProvider` delay of `QueueCheckIntervalMs`.
  - Evaluate the current `handle.SessionId`, and call `Observe`.
  - When `TryClaimFaultCycle(now, QueueGracePeriodMs)` is `true`, call `LogQueueDeviceFaultAsync` with `CancellationToken.None`.
  - The root ID of this entry is `handle.RootExecutionId ?? rootId`, read at the time of the call (the current root segment).
  - Then call `handle.Cycles.RecordFaultCycle` and `QueueFailurePolicyEvaluator.OnCycleCompleted`.
  - Do these steps also when the gate wrote entries in the episode.
  - Log each exception at Warning level and continue.
  - Never stop the run directly. A stop policy that the operator configured acts through the evaluator (FR-018).
- [ ] T061 [US4] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, start the `QueueLivenessWatch` task after the run binds its session (depends on T060):
  - Start it only when the liveness service is not null.
  - In the `finally` block of the run, cancel the task and await it before the session stops.
- [ ] T062 [P] [US4] Create `QueueDeviceLivenessResponse` in `src/GameBot.Service/Contracts/Queues/QueueDeviceLivenessResponse.cs` (data-model section 10, with `GatedFirings`). Add the property `DeviceLiveness` to `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs`.
- [ ] T063 [US4] Change `ProjectHealth` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (depends on T055, T062):
  - Take `ISessionLivenessService?`.
  - When the handle has a session, evaluate it and call `handle.Liveness.Observe`.
  - Fill `DeviceLiveness` from the snapshot, with `gatedFirings`. Write `notLiveSince` in service-local time.
  - `DeviceLiveness` is null when no session is bound yet.
- [ ] T064 [US4] Extend `src/GameBot.Service/Swagger/DeviceLivenessSchemaFilter.cs` with descriptions and enums for `QueueDeviceLivenessResponse`. Add the description of `deviceLiveness` to `src/GameBot.Service/Swagger/QueueHealthSchemaFilter.cs` (FR-020).
- [ ] T065 [US4] Run the US4 tests (T049 to T054) and all current queue tests (`--filter "FullyQualifiedName~Queue"`). All must pass. Do not change `src/GameBot.Service/Hosted/QueueDeviceWatchdogService.cs` (research R-017).

**Checkpoint**: All four user stories work and can be tested independently.

---

## Phase 7: Polish and Shared Concerns

**Purpose**: Documentation, the full quality gate and the performance note.

- [ ] T066 [P] Update `docs/architecture.md` in STE:
  - Add the liveness model (tracker, evaluator, rules).
  - Add the configuration section `Service:DeviceLiveness` with the defaults.
  - Add the new health block and the capture headers.
  - Add the `503`/`504` errors of the inputs endpoint and the `504 capture_timeout`.
  - Add the queue gate: one gate for each firing group and the hold for hard reasons.
  - Add the put-back rules (with the at-queue-start move), the hold-only walk and the log cap of the gate.
  - Add the known limit: a time-of-day firing held past midnight is lost for that day.
  - Add the watch: one entry and one fault cycle for each episode.
  - Add the kill of `adb` processes on cancel and the detection limit of an idle pause.
  - Set a new "Last reviewed" date.
- [ ] T067 [P] Add entries to `CHANGELOG.md` under the unreleased section, in STE. Refer to issue #220.
  - `Added`: the session health `liveness` block and the three capture headers.
  - `Added`: queue `health.deviceLiveness` with `gatedFirings`, the hold of firings, and the fault-episode entries.
  - `Added`: the configuration section `Service:DeviceLiveness`.
  - `Changed`: `POST /api/sessions/{id}/inputs` can now return `503 device_not_live` and `504 device_timeout`.
  - `Changed`: screenshot and snapshot can return `504 capture_timeout`.
  - `Changed`: the service kills an `adb` process when its call times out.
- [ ] T068 [P] Add row 106 to `specs/STATUS.md`. Set **Status** to `Implemented` in `specs/106-wedged-device-liveness/spec.md`.
- [ ] T069 Update the API contract snapshot in `tests/contract/ApiContractSnapshots/` only if a current snapshot test fails because of the new responses. Do not change other snapshot content.
- [ ] T070 Measure the byte compare of the capture loop:
  - Compare two 3 MB byte arrays with `SequenceEqual`.
  - Use a short measurement in a scratch unit test or a benchmark run. Do not commit it.
  - Write the result in the perf note for the PR description. The goal is below 1 ms.
- [ ] T071 Run the full quality gate:
  - Run `dotnet build "C:\src\GameBot\GameBot.sln"` and `dotnet test "C:\src\GameBot\GameBot.sln"`.
  - All tests must pass, or fail only as in the T002 baseline.
  - A flaky test from the known list can fail (for example `MaskedTemplateMatchTests` or `QueueTemplateLink`). Then run it again.
  - If a file under `src/web-ui` changed, also run `vite build` and `jest` in `src/web-ui`.
- [ ] T072 Do the steps of `specs/106-wedged-device-liveness/quickstart.md` section 7:
  - Its filter selects all test classes of this feature.
  - The filter names are `Liveness`, `AdbClientCancellation`, `BackgroundScreenCapture`, `SessionManagerInputTimeout`, `SessionManagerSnapshotTimeout`, `CaptureHeaders` and `QueueCycleLedgerFaultCycle`.
  - Then read the quickstart again. Make sure that each field, header and error name agrees with the code.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. Blocks all user stories.
- **US1 (Phase 3)**, **US2 (Phase 4)**, **US3 (Phase 5)**: Each depends only on Phase 2.
- **US4 (Phase 6)**: Depends only on Phase 2 (it uses `ISessionLivenessService.Evaluate`). It does not need US1 to US3. But in production, the gate is only useful with the data writers of Phase 2.
- **Polish (Phase 7)**: Depends on all user stories.

### Shared files (do these tasks in sequence, not in parallel)

- `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`: T030 (US1), T039 (US2), T046 (US3).
- `src/GameBot.Service/Swagger/SwaggerConfig.cs`: T031 (US1), T040 (US2), T047 (US3).
- `tests/contract/DeviceLivenessOpenApiTests.cs`: T029 (US1) creates it. T037, T044 and T054 extend it.
- `src/GameBot.Emulator/Session/SessionManager.cs`: T017 (Phase 2), T045 (US3).
- `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: T058, T059, T061.
- `src/GameBot.Service/GameBotServiceSetup.cs`: T025, T032.
- `tests/unit/Emulator/AdbClientCancellationTests.cs`: T013 creates it. T014 extends it.
- `tests/unit/Queues/RecordingExecutionLog.cs`: T051 creates it (moved from `QueueExecutionServiceTests.cs`). T052 uses it. T057 extends it.

### Within Each User Story

- Write the tests first, and make sure that they fail.
- Do the models and records before the services. Do the services before the endpoints. Do the endpoints before the OpenAPI documentation.
- Run the story checkpoint task before the next story.

---

## Parallel Examples

### Phase 2

```text
Task: "T003 Create DeviceLivenessOptions in src/GameBot.Domain/Sessions/DeviceLivenessOptions.cs"
Task: "T004 Create constants in src/GameBot.Domain/Sessions/DeviceLivenessStates.cs"
Task: "T005 Create DeviceLivenessSample in src/GameBot.Domain/Sessions/DeviceLivenessSample.cs"
Task: "T006 Create DeviceLivenessReport in src/GameBot.Domain/Sessions/DeviceLivenessReport.cs"
# After T007 and T008:
Task: "T009 DeviceLivenessOptionsTests", "T010 DeviceLivenessEvaluatorTests", "T011 DeviceLivenessTrackerTests", "T013 AdbClientCancellationTests"
Task: "T021 AdbSessionTransportCheck", "T022 AdbSessionDirectCapture", "T024 CaptureHeaders"
```

### User Story 1

```text
Task: "T027 SessionLivenessServiceTests in tests/unit/Liveness/SessionLivenessServiceTests.cs"
Task: "T028 SessionHealthLivenessContractTests in tests/contract/Sessions/SessionHealthLivenessContractTests.cs"
Task: "T029 DeviceLivenessOpenApiTests in tests/contract/DeviceLivenessOpenApiTests.cs"
```

### User Story 2

```text
Task: "T034 CaptureHeadersTests in tests/unit/Liveness/CaptureHeadersTests.cs"
Task: "T035 SessionManagerSnapshotTimeoutTests in tests/unit/Emulator/SessionManagerSnapshotTimeoutTests.cs"
Task: "T036 CaptureHeadersContractTests in tests/contract/Sessions/CaptureHeadersContractTests.cs"
```

### User Story 3

```text
Task: "T042 SessionManagerInputTimeoutTests in tests/unit/Emulator/SessionManagerInputTimeoutTests.cs"
Task: "T043 SessionInputsLivenessContractTests in tests/contract/Sessions/SessionInputsLivenessContractTests.cs"
```

### User Story 4

```text
Task: "T049 QueueLivenessEpisodeTests", "T050 QueueCycleLedgerFaultCycleTests", "T051 QueueLivenessGateTests", "T053 QueueDeviceLivenessContractTests"
# After T051 (shared RecordingExecutionLog):
Task: "T052 QueueLivenessWatchTests"
Task: "T055 QueueLivenessEpisode", "T056 QueueCycleLedger.RecordFaultCycle", "T062 QueueDeviceLivenessResponse"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Do Phase 1 and Phase 2.
2. Do Phase 3 (US1).
3. Stop and validate. The session health must report `not_live` for a wedged device and `live` for a live device.

### Incremental Delivery

1. Setup and Foundational: the data and the rules are ready.
2. US1: the health call shows the fault (MVP).
3. US2: screenshots tell staleness and never hang.
4. US3: inputs are bounded and do not claim a false success.
5. US4: queues show the fault and record failures for unattended operation.
6. Polish: documentation, CHANGELOG, full gate.

Each story adds value and does not break the stories before it.

---

## Notes

- [P] tasks change different files and do not depend on a task that is not complete.
- Do not change `ISessionManager` members (27 test fakes). Only `InputActionResult` gets one optional parameter.
- All new constructor parameters are optional and at the end.
- Do not add a time limit to `SendInputsAsync`. Do not change the step results of sequences (clarification 5).
- Do not commit in this step. The pipeline commits after the implementation.
