# Implementation Plan: Start the Emulator From a Backend-Only, Session-Less State

**Branch**: `074-sessionless-emulator-start` | **Date**: 2026-07-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/074-sessionless-emulator-start/spec.md`

## Summary

An execution queue creates its ADB-bound device session **up front**, before it runs any sequence
(`QueueExecutionService.RunAsync` step 2: `_sessions.CreateSession(queue.EmulatorSerial)`). When the
emulator is closed, `CreateSession` → `ResolveOrValidateDeviceSerial` throws `no_adb_devices`, the run
is marked `Failure` ("emulator could not be reached"), and the queue never reaches the sequences that
would start the emulator. That is the concrete bootstrap gap: from a backend-only, session-less cold
state, a scheduled queue cannot self-start.

This feature adds a **pre-session emulator cold-start** to the queue run: when a queue is configured
with an optional emulator-instance identifier (name or index), `RunAsync` invokes the existing
feature-070 `IEnsureEmulatorRunningActionHandler` **before** `CreateSession`, using the queue's
existing `EmulatorSerial` for the responsiveness probe. A healthy/started/restarted or neutral
unsupported outcome proceeds to create the session and run exactly as today; a genuine emulator failure
(recovery timeout / instance-not-found) fails the run with a clear reason **without** creating a
session. Queues with the fields unset behave byte-for-byte as today.

Technical approach: reuse feature 070 wholesale (handler, `EnsureEmulatorRunningArgs`, outcome
semantics, timeouts) — no new emulator machinery. Add two optional persisted fields to `ExecutionQueue`
(`EmulatorInstanceName`, `EmulatorInstanceIndex`) and thread them through the queue-config path
(domain → repository JSON → API contracts → endpoints → web-ui), mirroring exactly how feature 073
added `PauseWhenIdle`/`IdleThresholdSeconds`. Inject the handler into `QueueExecutionService` as an
optional nullable constructor parameter (like the existing `IEnsureGameRunningActionHandler`), so DI
wires the real handler and existing test constructions keep compiling.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service, GameBot.Domain); TypeScript (web-ui React)
**Primary Dependencies**: ASP.NET minimal APIs, System.Text.Json; React + Jest/RTL; feature-070
emulator control (`LdConsoleEmulatorControl`, `AdbEmulatorDeviceProbe`) reused unchanged
**Storage**: JSON file persistence for queue config (`FileQueueRepository` serializes the whole
`ExecutionQueue`); runtime run state is in-memory (`QueueRunHandle`), never persisted
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`); Jest + React Testing Library
(web-ui); `dotnet test`; web-ui green gate = `vite build` + `jest`
**Target Platform**: Windows host driving an Android/LDPlayer emulator via ADB
**Project Type**: Web application — .NET backend service + React web-ui
**Performance Goals**: The pre-session ensure runs once per queue start, before any session; it adds the
feature-070 probe cost (≤10 s probe, ≤120 s boot wait) only on a cold/hung emulator and an
already-healthy instance returns after a single fast probe. No hot-path/steady-state change; the run
loop cadence is untouched.
**Constraints**: The ensure MUST run before `CreateSession`; a genuine failure MUST NOT create a
session (FR-002/FR-007/FR-013); a stop request during the ensure must abort promptly (the handler is
`ct`-aware); unset fields MUST be a total no-op (FR-014). Reuse feature-070 config — no new tuning
settings (FR-011/SC-008). CamelCase method names only.
**Scale/Scope**: One production machine, one emulator per queue; ~a dozen live daily queues that would
opt in. Touches one domain entity, three queue contracts, one endpoint mapper, one runtime method, and
the web-ui queue config form.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS (planned). Change is cohesive and localized: two domain fields,
  standard contract/endpoint plumbing (copy of the 073 pattern), and one new private helper +
  call-site in `RunAsync`. The helper stays under ~50 LOC. No dead code; no new dependencies. Public
  members get XML docs. **CamelCase method names only — no underscores.**
- **II. Testing Standards**: PASS (planned). Bug-repro-first: a unit test proving a cold queue with an
  instance identifier now starts the emulator before `CreateSession` (and that a genuine failure skips
  session creation) is written before the runtime change. Full behavior matrix (unset / already-healthy
  / started / restarted / recovery-timeout / instance-not-found / unsupported) with a fake handler;
  contract tests for the new fields' round-trip + negative-index rejection + back-compat default;
  repository back-compat test; web-ui tests for the config control + types. Targets the ≥80% line /
  ≥70% branch baseline on touched code.
- **III. UX Consistency**: PASS (planned). New fields surfaced consistently across the API
  (create/update/response) and web-ui, mirroring `cycleExecution`/`pauseWhenIdle`. The cold-start
  outcome is recorded in the queue execution log so operators can see what happened (FR-010). Failure
  message is actionable ("emulator instance '<x>' could not be started: <reason>").
- **IV. Performance**: PASS. One extra probe at queue start, only when opted in; already-healthy is a
  single fast probe; no steady-state or hot-path change. Perf note captured above.
- **V. Living Documentation**: PASS (planned). `docs/architecture.md` updated (queue config gains the
  emulator-instance fields; queue-start pre-session emulator ensure behavior; "Last reviewed"
  refreshed). `spec.md` Status set to Implemented on completion; `specs/STATUS.md` updated. Features
  070/071/073 referenced, not superseded (this complements them; no earlier spec misrepresents current
  behavior, so no Status edits needed elsewhere).

**Initial gate**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/074-sessionless-emulator-start/
├── plan.md              # This file
├── research.md          # Phase 0 output (design decisions)
├── data-model.md        # Phase 1 output (queue fields + outcome mapping)
├── quickstart.md        # Phase 1 output (enable + verify cold-start)
├── contracts/
│   └── queue-emulator-instance-config.md   # Phase 1 output (API contract for new fields + run behavior)
├── checklists/
│   └── requirements.md  # from /speckit-specify (+ clarify re-validation)
└── tasks.md             # /speckit-tasks output
```

### Source Code (repository root)

```text
src/GameBot.Domain/
└── Queues/
    └── ExecutionQueue.cs            # + EmulatorInstanceName (string?), EmulatorInstanceIndex (int?)
                                     #   (optional, null default → JSON back-compat via FileQueueRepository whole-object serialization)

src/GameBot.Service/
├── Services/QueueExecution/
│   └── QueueExecutionService.cs     # inject IEnsureEmulatorRunningActionHandler? (optional ctor param);
│                                    #   pre-session EnsureEmulatorBeforeSessionAsync helper called in RunAsync before CreateSession;
│                                    #   genuine failure → Failure reason, no session; log outcome
├── Contracts/Queues/
│   ├── CreateQueueRequest.cs        # + EmulatorInstanceName, EmulatorInstanceIndex
│   ├── UpdateQueueRequest.cs        # + EmulatorInstanceName, EmulatorInstanceIndex
│   └── QueueResponse.cs             # + EmulatorInstanceName, EmulatorInstanceIndex
└── Endpoints/QueuesEndpoints.cs     # map new fields on create/update/response; reject negative index

src/web-ui/src/
├── services/queues.ts               # types for new fields (Queue, Create/Update payloads)
└── pages/QueuesPage.tsx             # emulator-instance inputs in the queue config form

tests/
├── unit/Queues/QueueExecutionServiceTests.cs   # pre-session cold-start behavior matrix (fake handler)
├── contract/ (queues)                           # new fields round-trip + negative-index reject + back-compat default
├── unit/Queues/FileQueueRepositoryTests.cs      # back-compat: JSON without fields → null
└── (web-ui) QueuesPage.*                         # config control renders + posts new fields

docs/architecture.md                 # living-docs update: queue config fields + pre-session emulator ensure behavior
specs/STATUS.md                       # add 074
```

**Structure Decision**: Existing web-application layout (backend service + web-ui). The feature threads
two optional config fields through the established queue-config path (identical to `PauseWhenIdle`) and
adds a single pre-session ensure call in the run loop. No new projects, services, or persistence
stores. The emulator control itself is reused from feature 070 with zero changes.

## Complexity Tracking

Not required — Constitution Check passed with no violations.
