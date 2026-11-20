# Implementation Plan: Do not read a stale capture frame in a step (B-043)

**Branch**: `197-fix-stale-capture-frame` | **Date**: 2026-11-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/197-fix-stale-capture-frame/spec.md` (GitHub issue #407)

## Summary

After a capture time-out, the capture loop of a device starts no new capture for up to 60 s (the device is `Suspect`, feature 121). The frame in memory does not change, and the steps of a run read it with no age check. The screenshot endpoint gives a new `X-Capture-Id` for each read of the same frame. The fix gives each frame in memory a frame id and a stale-repeat flag (`CachedFrame`), and adds the pure rule `FrameFreshness` (age > `max(FrameMaxAgeMs, 2 × CaptureIntervalMs)`, with a `FrameMaxAgeMs` default of 5000 ms, or stale repeat). An ambient `FrameReadScope`, pushed for each sequence run with a session, lets the three frame decode places refuse a stale frame and record the frame data. A `WaitForImage` pass on a stale frame then has no frame and does not end the wait; the step details give the capture id, the frame age, the best score, and the pass counts. An `imageVisible` or `ocrText` read waits up to `FrameMaxAgeMs` for a fresh frame, else the step fails with `stale_frame`. The screenshot endpoint uses the frame id as `X-Capture-Id` and marks a stale frame. A stale repeat does not count as a completed capture for `capture_stalled`.

## Technical Context

**Language/Version**: C# (`LangVersion` preview) / .NET 9 (domain, emulator, service); no web UI change
**Primary Dependencies**: ASP.NET Core minimal APIs (no change), System.Drawing, OpenCvSharp (no change), `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) in tests
**Storage**: N/A (frames are in memory only; no stored data changes)
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`)
**Target Platform**: Windows service host with Android emulators
**Project Type**: web service (backend) with a web UI (not changed)
**Performance Goals**: No measurable change on a fresh frame: one subtraction and one compare for each read (research R-010). A condition read waits only on a stale frame, at most `FrameMaxAgeMs`.
**Constraints**: No change of the `WaitForImage` outcome rule (FR-013). No change of `IScreenSource`, `ISessionFrameSource`, `IDeviceLivenessTracker`, or `ISequenceExecutionService` signatures. Triggers and `POST /api/images/detect` keep their behaviour (no scope). The current tests keep their expected results.
**Scale/Scope**: One capture loop for each session (1 to 10 sessions). A `WaitForImage` step makes 1 to about 200 passes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not change the web UI. Phone support level: not applicable.

| Gate | Status | Note |
|---|---|---|
| I. Code quality | PASS | New small types (`FrameFreshness`, `FrameReadInfo`, `FrameReadScope`) and small changes in the capture loop, the readers, the runner, and the log map code. No new dependency. Doc comments on new public members. Method names in CamelCase. |
| II. Testing | PASS | Bug fix: tests that fail before the fix: a `WaitForImage` step ends with `image_detected` on a stale frame that shows the image; a negated `imageVisible` is true on a stale frame; two screenshot calls with the same frame give two ids. Unit tests for `FrameFreshness`, `FrameReadScope`, the stale-repeat rule of the loop, the runner counts, the condition wait, the OCR line, the liveness rule. Integration tests for the screenshot headers and a sequence run. |
| III. UX consistency | PASS | New log keys follow the current names (camelCase keys, snake_case outcome codes, kebab-case OCR codes). The failure message names the cause and the limit. API schema text updated. |
| IV. Performance | PASS | Declared in Technical Context (research R-010). The perf note goes into the PR. |
| V. Living documentation | PASS | Update `docs/architecture.md` ("Last reviewed"), `CHANGELOG.md`. Spec 106 `Status` gets "iterated by 197" (its header and stall rules change). `specs/STATUS.md` gets row 197 and the changed row 106. |
| VI. STE | PASS | All artifacts are in STE. Code comments and messages are in STE. |
| Version bump | PASS (phase 2) | Bump `patch` in `installer/versioning/version.override.json` before the push (pipeline step 10). |

Post-design re-check: PASS. The design adds no project, no stored data, and no new public interface member.

## Project Structure

### Documentation (this feature)

```text
specs/197-fix-stale-capture-frame/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── frame-freshness.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Sessions/CachedFrame.cs                     # + FrameId, StaleRepeat, StaleRepeatSince
├── GameBot.Domain/Sessions/FrameFreshness.cs                  # new: AgeMs, IsStale, Describe
├── GameBot.Domain/Sessions/FrameReadInfo.cs                   # new record
├── GameBot.Domain/Sessions/FrameReadScope.cs                  # new ambient scope: Admit, EnsureFreshAsync, LastRead
├── GameBot.Domain/Sessions/DeviceLivenessOptions.cs           # + FrameMaxAgeMs, MinFrameMaxAgeMs
├── GameBot.Domain/Vision/ImageDetectionPassScope.cs           # ImageDetectionPass + Frame
├── GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs  # pass gets FrameReadScope.Current?.LastRead
├── GameBot.Domain/Services/SequenceRunner.cs                  # pass counts, stale counts, new detail fields
├── GameBot.Domain/Services/SequenceStepConditionEvaluator.cs  # EnsureFreshAsync before image/ocr leaf, imageVisible log line, + StaleFrame in ConditionEvaluationFailureKind
├── GameBot.Domain/Services/OcrTextEvaluationScope.cs          # + stale-frame code, capture data on read and record
├── GameBot.Domain/Logging/ExecutionLogModels.cs               # WaitForImageDetailAttributes + 6 fields
├── GameBot.Emulator/Session/BackgroundScreenCaptureService.cs # stale-repeat rule, frame id, no RecordCapture on repeat
├── GameBot.Emulator/Session/SessionScopedScreenSource.cs      # DecodeCachedFrame calls FrameReadScope.Admit
├── GameBot.Service/Services/SequenceExecution/BackgroundCaptureSessionFrameSource.cs  # Capture calls Admit
├── GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs  # push FrameReadScope; log new detail keys
├── GameBot.Service/Services/Ocr/OcrTextConditionReader.cs     # capture data in the read result
├── GameBot.Service/Services/CaptureSessionStore.cs            # + GetOrAdd
├── GameBot.Service/Endpoints/EmulatorImageEndpoints.cs        # frame id as X-Capture-Id, stale flag
├── GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs  # map the new keys
├── GameBot.Service/Models/ExecutionLogs.cs                    # DTO + 6 fields
├── GameBot.Service/Endpoints/ExecutionLogsEndpoints.cs        # map the new fields
└── GameBot.Service/Swagger/DeviceLivenessSchemaFilter.cs      # schema text: watch during a step, stale repeat

tests/
├── unit/Sessions/FrameFreshnessTests.cs                       # new
├── unit/Sessions/FrameReadScopeTests.cs                       # new
├── unit/BackgroundScreenCaptureServiceTests.cs                # + stale-repeat and frame id tests
├── unit/Sessions/DeviceLivenessOptionsTests.cs                # + FrameMaxAgeMs default and minimum
├── unit/Queues/QueueLivenessWatchTests.cs                     # + capture_stalled seen while a step runs
├── unit/Services/CaptureSessionStoreTests.cs                  # new: GetOrAdd
├── unit/Sequences/SequenceRunnerWaitForImageTests.cs          # + stale pass, counts, detail fields
├── unit/Sequences/OcrTextConditionEvaluatorTests.cs           # + stale-frame, capture data in line
├── unit/Sequences/ImageVisibleStaleFrameTests.cs              # new: wait, fail, negate, log line
├── unit/Services/Ocr/OcrTextConditionReaderTests.cs           # + capture data
├── unit/Liveness/CaptureHeadersTests.cs                       # + stale by frame
├── integration/EmulatorScreenshotCaptureTests.cs              # + same id, stale header, crop with frame id
└── integration/Sequences/WaitForImageStaleFrameRunTests.cs    # new: run with stalled then fresh frames
```

**Structure Decision**: The current projects `GameBot.Domain`, `GameBot.Emulator`, and `GameBot.Service`, and the test projects `tests/unit`, `tests/integration`, and `tests/contract`. The rule and the scope are in the domain, because `SequenceRunner`, `ImageMatchEvaluator`, and the condition evaluator are in the domain. The capture loop is in the emulator project.

## Design notes

1. **Frame id and stale repeat (FR-001, FR-002, FR-011)**: in `SessionCaptureLoop.RunLoopAsync`, see data-model section 1. The loop gets `FrameMaxAgeMs` from the normalized `DeviceLivenessOptions` (the constructor already takes them). On a stale repeat the loop sets the flag with `previous with { ... }` and must not dispose the shared `Bitmap`.
2. **Admit (FR-003, FR-004)**: `SessionScopedScreenSource.DecodeCachedFrame` and `BackgroundCaptureSessionFrameSource.Capture` call `FrameReadScope.Admit(frame)` and return null when it is false. With no scope there is no change.
3. **Scope push**: `SequenceExecutionService.ExecuteCoreAsync` pushes `FrameReadScope` after the device context, when the run is not a dry-run, a session id is known, and `BackgroundScreenCaptureService` is available. `MaxAgeMs` is `FrameFreshness.EffectiveLimitMs(options.FrameMaxAgeMs, appConfig.CaptureIntervalMs)`. The wait delegate reads `GetCachedFrame(sessionId)` every `CaptureIntervalMs` up to `MaxAgeMs`. The service gets `IOptions<DeviceLivenessOptions>?` and `AppConfig?` as optional constructor parameters (research R-010a; with no `AppConfig`, 500 ms).
3a. **Effective limit everywhere**: the capture loop (repeat window, with its own `_intervalMs`), `FrameReadScope`, and the screenshot endpoint (with `AppConfig.CaptureIntervalMs`) all use `FrameFreshness.EffectiveLimitMs`.
4. **WaitForImage (FR-004, FR-005, FR-007)**: both poll loops count stale passes from `ImageDetectionPassScope.Current.LastPass.Frame`. The poll loops return the counts. `WaitForImageAsync` fills the six new fields on each exit path that polls. `LogWaitForImageTimeout` adds the stale count.
5. **Conditions (FR-006, FR-008)**: research R-006 and R-007.
6. **Screenshot (FR-009, FR-010)**: research R-008. `ApplyCachedFrameHeaders` sets `Stale = fromReport.Stale || FrameFreshness.IsStale(frame, now, effectiveLimitMs)`.
6a. **Stall rule (FR-011)**: a stale repeat skips `RecordCapture`. With the default values the repeat window (5 s) ends before the stall limit (60 s), so the rule acts only when `FrameMaxAgeMs` is equal to or more than `CaptureStallLimitMs`. The test of US4 sets this value.
7. **Liveness text (FR-012)**: `DeviceLivenessSchemaFilter` text and `docs/architecture.md`.

## Complexity Tracking

No violations.
