# Tasks: Connect-to-Game Optionally Ensures the Emulator Is Running

**Feature**: `071-connect-ensure-emulator` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**Design docs**: [research.md](research.md) · [data-model.md](data-model.md) · [contracts/](contracts/connect-to-game-emulator-preheal.md) · [quickstart.md](quickstart.md)

Tests included (constitution Principle II). Backend-only; reuses the feature-070 handler/seams/client
and config — no new emulator machinery, no new env vars, no web-ui changes.

**Conventions**: CamelCase methods only; functions ≈<50 LOC; ≥80% line / ≥70% branch coverage on
touched areas. Gate: `dotnet build` + `dotnet test`.

---

## Phase 1: Setup

- [X] T001 Confirm a green baseline: `dotnet build GameBot.sln` passes (web-ui untouched this feature).

---

## Phase 2: Foundational — optional instance fields on the connect-to-game action

- [X] T002 Add optional `InstanceName?` and `InstanceIndex?` to `PrimitiveConnectToGameAction` in `src/GameBot.Domain/Actions/PrimitiveActionVariants.cs`.
- [X] T003 Add optional `InstanceName?` / `InstanceIndex?` to `ConnectToGameArgs` in `src/GameBot.Domain/Actions/ConnectToGameArgs.cs`, and populate them in BOTH `TryFrom` overloads (the `PrimitiveConnectToGameAction` variant and the `InputAction`/parameters-dictionary form) — reading `instanceName`/`instanceIndex` from the parameters (raw or `JsonElement`). Keep `gameId`/`adbSerial` required.
- [X] T004 Extend the `PrimitiveConnectToGameAction` case in `src/GameBot.Domain/Actions/PrimitiveActionValidationService.cs`: instance fields optional (no new required-field errors); reject `instanceIndex < 0`; `gameId` + `adbSerial` remain required.
- [X] T005 [P] Unit tests in `tests/unit/Domain/ConnectToGameArgsInstanceTests.cs`: `TryFrom` parses `instanceName`/`instanceIndex` from a parameters dict (raw + `JsonElement`), omits them cleanly when absent, and validation accepts a connect with no instance id but rejects a negative `instanceIndex` (existing connect-to-game validation still passes unchanged).

**Checkpoint**: the action can carry the optional instance identifier and round-trips through args/validation.

---

## Phase 3: User Story 1 — connect pre-heals the emulator (Priority: P1) 🎯 MVP

**Goal**: When an instance id is present, `DispatchConnectToGameAsync` heals the emulator first and only proceeds (or fails fast) accordingly.

**Independent test**: dispatcher tests drive healthy/started/timeout/not-found/unsupported with a faked handler + faked session service and assert the proceed/fail-fast + StartSession-called/not-called behavior.

- [X] T006 [US1] Inject `IEnsureEmulatorRunningActionHandler` into `SequenceExecutionService` (new constructor parameter + field + `using GameBot.Service.Services.EnsureEmulatorRunning;`) in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`. (It is already a DI singleton from feature 070; no DI registration change needed.)
- [X] T007 [US1] In `DispatchConnectToGameAsync` (same file), after parsing `gameId`/`adbSerial` and before `StartSession`: if `EnsureEmulatorRunningArgs.TryFrom(action.Parameters, out var emuArgs)` succeeds, run `await _ensureEmulatorRunning.ExecuteAsync(emuArgs, ct)`; if the result is neither `IsSuccess` nor `IsUnsupported`, return `new ActionDispatchResult("failed", $"connect-to-game emulator pre-heal failed: {result.ReasonCode}")` WITHOUT calling `StartSession`; otherwise remember the `ReasonCode` for the message and proceed. When `TryFrom` fails (no instance id), skip the pre-heal entirely (unchanged path). Keep the method <50 LOC — extract a small `PreheatEmulatorAsync` helper returning the outcome if needed.
- [X] T008 [US1] Surface the pre-heal in the existing success message: when the pre-heal ran, include an `emulator: <reasonCode>` clause alongside the existing `game launch: <reason>` clause in the returned `ActionDispatchResult` message.
- [X] T009 [P] [US1] Dispatcher unit tests in `tests/unit/Sequences/ConnectToGameEmulatorPreheatTests.cs` using a fake `IEnsureEmulatorRunningActionHandler` (records calls + returns a scripted outcome) and a fake `ISessionService`/session manager (records whether `StartSession` was called): (a) no instance id → handler NOT called, `StartSession` called, message unchanged; (b) instance id + `AlreadyHealthy`/`Started` → `StartSession` called, message includes `emulator:`; (c) instance id + `RecoveryTimedOut` and (d) `InstanceNotFound` → result is `failed`, `StartSession` NOT called; (e) instance id + `ControlUnavailable`/`PlatformUnsupported` → `StartSession` called (proceed).

**Checkpoint**: connect-to-game heals the emulator when asked and fails fast only on genuine emulator failure; existing behavior preserved when no instance id.

**User Story 2 (backward compatibility, P1) coverage**: the "no instance id ⇒ unchanged" guarantee is
verified by T009 case (a) (handler never called, `StartSession` still called, message unchanged) and
by T005 (a connect-to-game with no instance id still validates and saves). No separate phase is needed
because US2 asserts the *absence* of new behavior on the same code paths US1 adds.

---

## Phase 4: Polish & Docs

- [X] T010 [P] Update the connect-to-game description in `docs/architecture.md` to note the optional emulator pre-heal (instance name/index → runs the feature-070 handler before attaching; fail-fast on genuine failure; unchanged when omitted). Refresh the "Last reviewed" date if needed. If a MCP tool description enumerates the connect-to-game *action* payload fields, add `instanceName`/`instanceIndex` there; otherwise leave MCP untouched (the interactive `start_session` tool is out of scope).
- [X] T011 [P] Add a `Status` line to `specs/071-connect-ensure-emulator/spec.md` and a new `| 071 | … | Implemented |` row to `specs/STATUS.md` (complements 070/021, supersedes nothing).
- [X] T012 Run the full backend gate: `dotnet build` + `dotnet test` all green; verify ≥80% line / ≥70% branch coverage on the touched dispatch/args/validation code and fill any gap. Confirm web-ui `vite build` + `jest` still green (no web-ui files changed).

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 → Phase 4.
- T003 depends on T002 (variant fields). T007 depends on T006 (injected handler) and reuses the T003 args parsing. T009 depends on T006–T008.
- `[P]` tasks touch different files and may run in parallel (T005 after Phase 2 code; T010/T011 in Phase 4).

## Parallel Execution Examples

- Phase 2: implement T002→T003→T004, then T005 tests in parallel with starting Phase 3.
- Phase 4: T010 and T011 in parallel; T012 last.

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + Phase 3**: connect-to-game pre-heals the emulator on the sequence path
  with full proceed/fail-fast coverage and zero regression when no instance id is given.
- **Finalize = Phase 4**: docs, status, and the green-gate + coverage check.
