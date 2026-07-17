# Implementation Plan: Connect-to-Game Optionally Ensures the Emulator Is Running

**Branch**: `071-connect-ensure-emulator` | **Date**: 2026-07-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/071-connect-ensure-emulator/spec.md`

## Summary

Extend the `connect-to-game` **sequence action** so that, when the author supplies an optional
LDPlayer instance identifier (`instanceName` or `instanceIndex`), the action first runs the existing
feature-070 emulator health-and-recover handler (`IEnsureEmulatorRunningActionHandler`) against that
instance + the connect's `adbSerial` **before** starting the session. A genuine emulator failure
(recovery timeout / instance-not-found) fails the connect step without attempting `StartSession`;
success or a neutral unsupported outcome proceeds to the existing `StartSession` + `ensure-game-running`
flow. With no instance identifier, the action behaves exactly as today (zero regression).

The change is small and reuses everything from feature 070 — no new emulator machinery, no new config,
no new env vars. It threads two optional fields through the connect-to-game action's carriers and adds
one pre-heal call in `DispatchConnectToGameAsync`.

## Technical Context

**Language/Version**: C# / .NET (net8.0) backend; TypeScript MCP server (web-ui not touched — see below)
**Primary Dependencies**: ASP.NET Minimal APIs + MVC; System.Text.Json; xUnit
**Storage**: File-based sequences (JSON); additive optional fields round-trip via the existing `SequenceActionPayload.Parameters` dictionary — no schema migration
**Testing**: xUnit (`tests/unit`, `tests/integration`); web-ui `jest` unchanged (no web-ui change)
**Target Platform**: Windows host driving LDPlayer via ADB + `ldconsole` (reused from 070); degrades to neutral no-op off-Windows
**Project Type**: Web service + MCP server (web-ui not in scope for this feature)
**Performance Goals**: When an instance id is present, the connect step adds the same bounded emulator health/boot-wait as feature 070 (only long when an actual (re)start is needed); with no instance id, zero added work
**Constraints**: CamelCase method names; functions ≈<50 LOC; keep `Program.cs` thin; ≥80% line / ≥70% branch coverage on touched areas; `docs/architecture.md` + `specs/STATUS.md` updated; no new env vars
**Scale/Scope**: ~4 backend files + tests; reuses the entire feature-070 handler/seams/client

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), progression is blocked until fixed or a documented waiver exists.

- **I. Code Quality Discipline**: PASS — additive, reuses feature-070 handler via its existing DI-registered interface; two optional fields + one guarded pre-heal branch. No dead code; CamelCase; new members documented.
- **II. Testing Standards**: PASS — deterministic dispatcher-level unit tests with a **faked** `IEnsureEmulatorRunningActionHandler` and a faked session service assert: no-instance → handler never called + connect unchanged; instance + healthy/started → `StartSession` proceeds; instance + `RecoveryTimedOut`/`InstanceNotFound` → connect fails and `StartSession` NOT called; instance + unsupported → proceeds. Plus `ConnectToGameArgs.TryFrom` parsing of the optional fields and validation optionality. Coverage ≥80/70 on touched areas.
- **III. UX Consistency**: PASS — the pre-heal outcome is surfaced in the connect step's message (mirrors how the game-launch outcome is already appended); failure reasons are actionable; behavior with no instance id is unchanged.
- **IV. Performance**: PASS — no added work when no instance id; when present, the only long wait is the existing capped emulator boot-wait during a real (re)start. No hot path touched.
- **V. Living Documentation**: PASS (planned) — `docs/architecture.md` connect-to-game description updated with the optional pre-heal; this spec carries a `Status` line and `specs/STATUS.md` gets a new row; complements 070/021, supersedes nothing; no new env vars so `ENVIRONMENT.md` is unchanged.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/071-connect-ensure-emulator/
├── plan.md · research.md · data-model.md · quickstart.md
├── contracts/connect-to-game-emulator-preheal.md
├── checklists/requirements.md
└── tasks.md   # /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/Actions/
├── ConnectToGameArgs.cs            # + optional InstanceName/InstanceIndex; read in both TryFrom overloads
├── PrimitiveActionVariants.cs      # PrimitiveConnectToGameAction: + optional InstanceName/InstanceIndex
└── PrimitiveActionValidationService.cs  # connect-to-game case: instance optional; instanceIndex ≥ 0 if present

src/GameBot.Service/Services/SequenceExecution/
└── SequenceExecutionService.cs     # DispatchConnectToGameAsync: inject IEnsureEmulatorRunningActionHandler;
                                     # pre-heal when an instance id is present; fail-fast on genuine failure;
                                     # proceed on success/unsupported/none; surface outcome in the message

src/mcp-server/src/tools/
└── (only if a tool description enumerates connect-to-game action payload fields — otherwise untouched)

tests/unit/…                        # dispatcher pre-heal matrix, ConnectToGameArgs parsing, validation
tests/integration/…                 # connect-with-instance end-to-end against faked handler (optional)

docs/architecture.md                # connect-to-game description: note optional emulator pre-heal
specs/STATUS.md                     # new row for 071
```

**Structure Decision**: Backend-only. The connect-to-game **sequence action** is authored as JSON
parameters (`SequenceActionPayload.Parameters`), so the two optional fields ride the existing
dictionary — there is **no dedicated web-ui form** for this action to extend, and the interactive
`/api/sessions/start` endpoint (UI/MCP `start_session`) is a separate, lighter path explicitly out of
scope (see spec Assumptions). Hence no web-ui changes and no MCP `start_session` changes; the MCP layer
is touched only if a tool description enumerates the connect-to-game *action* payload fields.

## Design Decisions

1. **Pre-heal lives in `DispatchConnectToGameAsync`, before `StartSession`.** This is the connect
   *action* dispatch used in sequences/queues — the unattended path the request is about. The existing
   method already does "attach session, then ensure-game-running"; the emulator pre-heal is a natural
   first step. Inject the already-registered `IEnsureEmulatorRunningActionHandler` into
   `SequenceExecutionService` (a new constructor parameter, like feature 070 added `IEnsureEmulatorRunningActionHandler`
   is already a DI singleton).

2. **Opt-in via optional instance fields; absent ⇒ byte-for-byte unchanged.** The pre-heal branch is
   only entered when `EnsureEmulatorRunningArgs.TryFrom` succeeds from the connect action's parameters
   (which requires an instance id + the serial). With no instance id, `TryFrom` fails and the method
   skips straight to today's behavior — guaranteeing zero regression (US2).

3. **Fail-fast only on genuine emulator failure.** Map the handler result: `IsSuccess` or
   `IsUnsupported` ⇒ proceed to `StartSession`; otherwise (`RecoveryTimedOut` / `InstanceNotFound`) ⇒
   return `failed` immediately with the emulator `ReasonCode`, without calling `StartSession`. This
   mirrors the emulator action's own step mapping and the spec's FR-003/FR-004.

4. **Reuse feature-070 config and tooling wholesale.** No new `AppConfig` fields, env vars, resolver,
   or client — the injected handler already carries the probe/boot-wait/poll knobs and `ldconsole`
   discovery. This keeps the change tiny and consistent.

5. **Optional fields on the existing carriers.** `PrimitiveConnectToGameAction` gains
   `InstanceName?`/`InstanceIndex?`; `ConnectToGameArgs` gains the same and reads them in both
   `TryFrom` overloads (variant and `InputAction`/parameters). Validation leaves them optional and only
   rejects a negative index — `gameId` + `adbSerial` remain required, so existing steps validate
   unchanged.

## Complexity Tracking

No constitution violations — no entries required.
