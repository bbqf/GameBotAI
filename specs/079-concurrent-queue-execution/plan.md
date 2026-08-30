# Implementation Plan: Concurrent Queue Execution

**Branch**: `079-concurrent-queue-execution` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/079-concurrent-queue-execution/spec.md`

## Summary

Two queue runs today share three things they must not share: the screen they observe, the rule they
use to pick a device, and the physical device itself. The fix is three matching changes.

1. **Observation becomes session-scoped.** `BackgroundCaptureScreenSource` currently answers
   `GetLatestScreenshot()` with the frame of the *first* running session it finds. It is replaced by a
   `IScreenSourceFactory.ForSession(sessionId)` used everywhere a session id is already in hand, plus
   one ambient `IDeviceContextAccessor` (AsyncLocal) that the singleton `IScreenSource` consults so
   trigger evaluators and condition adapters -- which cannot take a session id without changing
   `ITriggerEvaluator` -- observe the device of the run whose execution flow they are on.
2. **Device resolution stops guessing.** The five `runningSessions.Count != 1` fallbacks are narrowed:
   when the caller supplies a session, it is used unconditionally; when it does not and exactly one
   session is active, today's behavior is kept; when it does not and several are active, the step
   fails with an actionable message instead of a bare "no session available".
3. **A device gets an exclusive owner.** A new in-memory `IDeviceClaimRegistry` (serial -> queue)
   is claimed atomically in `QueueExecutionService.StartAsync` and released in the run's `finally`.
   A second queue on a claimed serial is refused with a new `QueueStartOutcome.DeviceInUse`, surfaced
   as HTTP 409 `device_in_use` naming the serial and the holding queue.

Two supporting changes complete it: the concurrent-session ceiling rises from 3 to 8 with an
actionable capacity message, and the operator image tooling's arbitrary `PickSession` is replaced by an
explicit selector plus an ambiguity error.

Everything is behavior-preserving for a single running queue: with exactly one session active, every
resolution path lands on the same session it does today.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`), TypeScript 5 + React 18 (Vite) for `src/web-ui`
**Primary Dependencies**: ASP.NET Core Minimal APIs, `System.Text.Json`, OpenCvSharp (untouched),
xUnit + FluentAssertions, Jest + React Testing Library
**Storage**: JSON files on disk via the existing `File*Repository` classes. This feature adds **no**
persisted state: device claims and device contexts are in-memory and die with the process (FR-013).
**Testing**: `dotnet test` across `tests/unit`, `tests/integration`, `tests/contract`; `npm test`
(Jest) and `npm run build` (Vite) for `src/web-ui`
**Target Platform**: Windows 11 desktop service (`GameBot.Service`, port 8080) driving LDPlayer
emulators over ADB
**Project Type**: Web application -- .NET backend + React SPA in one repository
**Performance Goals**: claim acquisition/release O(1) and < 0.05 ms (one `ConcurrentDictionary`
operation); session-scoped frame lookup no slower than today's first-session scan; no measurable change
to queue-run wall-clock time, which is dominated by ADB round-trips of 10-100 ms
**Constraints**: the run loop writes run state on one thread while the monitor reads it on another, so
every new shared structure must be concurrency-safe; `AsyncLocal` context must survive the
`Task.Run` that launches a run and every `await` inside it; single-session behavior must be
byte-for-byte unchanged
**Scale/Scope**: one operator, one machine, up to 8 concurrent emulator sessions; ~20 sequences,
~60 commands, ~6 queue templates in the live authoring store

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design -- see bottom of section.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | How this feature satisfies it |
|---|---|---|
| **I. Code Quality Discipline** | PASS | The new types are small and single-purpose: `DeviceContext`, `IDeviceContextAccessor` + `AsyncLocalDeviceContextAccessor`, `IScreenSourceFactory` + `BackgroundCaptureScreenSourceFactory`, `SessionScopedScreenSource`, `IDeviceClaimRegistry` + `DeviceClaimRegistry`. Each is well under the ~50 LOC/method bar. Every new public/internal member gets XML docs with inputs, outputs and error modes. No new dependencies. The AsyncLocal accessor is the one piece of ambient state and is justified in research R2 (it replaces a *worse* global -- "the first running session"). Method names are CamelCase, no underscores. `Program.cs` is untouched, so the build-time taint analyzers stay happy. |
| **II. Testing Standards** | PASS | Unit tests for the claim registry (atomicity, release-on-every-exit, case/whitespace normalization), the AsyncLocal accessor (flow across `await` and `Task.Run`, nested push/pop), the session-scoped screen source (own session only, no-frame path), the narrowed session-resolution rule (1 session vs N sessions vs explicit) and the capacity message. Integration tests for two concurrent runs observing distinct screens, same-device refusal, claim release after completion/stop/failure, and non-interference on stop. Contract tests for the 409 `device_in_use` body and the monitor's new device field. Target >=80% line / >=70% branch on touched areas. |
| **III. UX Consistency** | PASS | The new refusal reuses the existing `{ error, message }` error envelope and 409 status already used by `already_running` and the 078 parameter refusal. Messages are actionable and name the device, the holding queue and the counts involved. All API changes are additive (one new error code, one new optional response field, one new optional query parameter), so no version bump. |
| **IV. Performance** | PASS | Budget declared above. The claim registry adds one dictionary operation per run start/end. Replacing the first-session `ListSessions()` scan with a keyed lookup is neutral-to-faster. A unit benchmark under `tests/unit/Performance/` holds the claim path under budget. |
| **V. Living Documentation** | PASS | This changes the capability set and the API surface, so `docs/architecture.md` MUST be updated with a refreshed "Last reviewed" date in the same PR, and `specs/STATUS.md` gets the 079 entry. Feature **051**'s FR-013 ("allow concurrent runs on the same emulator -- operator's responsibility, no guard") is **reversed** by FR-008: `specs/051-queue-execution-runtime/spec.md` must get a Status line noting that FR-013 is superseded by 079, and the matching XML doc on `IQueueExecutionService` must be corrected in code. FR-013a (no second run of the *same* queue) is unchanged. |

**Post-Phase-1 re-check**: PASS. The design adds no project, no dependency and no constitutional
exception. The Complexity Tracking table below is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/079-concurrent-queue-execution/
+-- spec.md               # Feature specification (with Clarifications)
+-- plan.md               # This file
+-- research.md           # Phase 0 -- R1..R8 technical decisions
+-- data-model.md         # Phase 1 -- new/changed types and their invariants
+-- quickstart.md         # Phase 1 -- how an operator runs several queues at once
+-- contracts/
|   +-- api.md            # Phase 1 -- additive REST contract changes
+-- checklists/
|   +-- requirements.md   # Spec quality checklist
+-- tasks.md              # Phase 2 -- created by /speckit-tasks, not by this command
```

### Source Code (repository root)

```text
src/GameBot.Domain/
+-- Sessions/DeviceContext.cs                  NEW: (SessionId, DeviceSerial) record
+-- Sessions/IDeviceContextAccessor.cs         NEW: ambient current-device contract
+-- Sessions/AsyncLocalDeviceContextAccessor.cs NEW: AsyncLocal impl, Push returns IDisposable
+-- Triggers/Evaluators/ScreenSourceAbstractions.cs CHANGED: + IScreenSourceFactory

src/GameBot.Emulator/
+-- Session/SessionScopedScreenSource.cs       NEW: frames for one fixed session id
+-- Session/BackgroundCaptureScreenSourceFactory.cs NEW: IScreenSourceFactory impl
+-- Session/BackgroundCaptureScreenSource.cs   CHANGED: ambient context -> single session ->
|                                                       null; the FirstOrDefault scan is removed
+-- Session/SessionOptions.cs                  CHANGED: MaxConcurrentSessions default 3 -> 8
+-- Session/SessionManager.cs                  CHANGED: capacity error names active/max

src/GameBot.Service/
+-- Services/QueueExecution/IDeviceClaimRegistry.cs NEW: TryClaim/Release/TryGetHolder
+-- Services/QueueExecution/DeviceClaimRegistry.cs  NEW: ConcurrentDictionary impl
+-- Services/QueueExecution/IQueueExecutionService.cs CHANGED: + QueueStartOutcome.DeviceInUse,
|                                                              corrected same-emulator doc comment
+-- Services/QueueExecution/QueueExecutionService.cs CHANGED: claim on start, release in finally,
|                                                             push device context per firing,
|                                                             actionable capacity failure text
+-- Services/QueueExecution/QueueMonitorService.cs   CHANGED: project the run's device serial
+-- Services/QueueExecution/QueueMonitorSnapshot.cs  CHANGED: + DeviceSerial
+-- Services/SequenceExecution/SequenceExecutionService.cs CHANGED: push device context for the
|                                                                   sequence; narrowed resolution
+-- Services/CommandExecutor.cs                 CHANGED: session-scoped screen source for detection;
|                                                        narrowed resolution + actionable message
+-- Services/EnsureGameRunning/IGameReadinessProbe.cs CHANGED: + sessionId parameter
+-- Services/EnsureGameRunning/GameReadinessProbe.cs  CHANGED: resolve screen via the factory
+-- Services/EnsureGameRunning/EnsureGameRunningActionHandler.cs CHANGED: pass its sessionId through
+-- Contracts/Queues/QueueMonitorResponse.cs    CHANGED: + deviceSerial
+-- Endpoints/QueuesEndpoints.cs                CHANGED: map DeviceInUse -> 409 device_in_use
+-- Endpoints/QueuesEndpoints.Logging.cs        CHANGED: + refused-start application-log entry
+-- Endpoints/EmulatorImageEndpoints.cs         CHANGED: explicit selector + ambiguity error
+-- GameBotServiceSetup.cs                      CHANGED: register accessor, factory, claim registry

src/web-ui/src/
+-- components/queues/QueueMonitor.tsx          CHANGED: show the run's device
+-- pages/QueuesPage.tsx                        CHANGED: surface the device_in_use message on Start
+-- services/queues.ts                          CHANGED: deviceSerial on QueueMonitorDto
+-- services/images.ts                          CHANGED: always send the screenshot selector

tests/
+-- unit/Sessions/DeviceContextAccessorTests.cs           NEW
+-- unit/Sessions/SessionScopedScreenSourceTests.cs       NEW
+-- unit/Sessions/SessionCapacityMessageTests.cs          NEW
+-- unit/Queues/DeviceClaimRegistryTests.cs               NEW
+-- unit/Queues/QueueExecutionServiceDeviceClaimTests.cs  NEW
+-- unit/Sequences/SessionResolutionTests.cs              NEW
+-- unit/Performance/DeviceClaimBenchmarkTests.cs         NEW
+-- integration/Queues/ConcurrentQueueRunTests.cs         NEW
+-- integration/Sessions/ConcurrentScreenIsolationTests.cs NEW
+-- integration/Queues/ConcurrentRunStateIsolationTests.cs NEW
+-- contract/QueueConcurrencyContractTests.cs             NEW
+-- contract/EmulatorScreenshotSelectorContractTests.cs   NEW
+-- src/web-ui/src/pages/__tests__/QueuesPage.execution.spec.tsx CHANGED

docs/architecture.md                            CHANGED (living docs, Principle V)
specs/STATUS.md                                 CHANGED (add 079)
specs/051-queue-execution-runtime/spec.md        CHANGED (Status: FR-013 superseded by 079)
```

**Structure Decision**: The existing layout is kept unchanged. Device identity and the ambient
context are pure domain concepts, so they live in `GameBot.Domain/Sessions`. Anything that touches the
capture cache lives in `GameBot.Emulator/Session` next to the cache itself. The claim registry is a
queue-run lifecycle concern and sits beside `QueueRunRegistry` in
`GameBot.Service/Services/QueueExecution`, mirroring how feature 065 extracted that registry.

## Complexity Tracking

> No constitution violations. This table is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | | |
