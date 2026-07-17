# Phase 0 Research: Connect-to-Game Emulator Pre-heal

All unknowns resolved from the spec clarifications and direct reading of the connect-to-game dispatch
plus feature 070. No open `NEEDS CLARIFICATION`.

## Decision 1 — Insertion point: the sequence-action dispatch, before StartSession

- **Decision**: Add the emulator pre-heal at the top of `SequenceExecutionService.DispatchConnectToGameAsync`,
  before `_sessionService.StartSession(...)`.
- **Rationale**: That method is the connect-to-game *action* as run in sequences/queues (unattended
  automation — the request's context). It already sequences "attach session → ensure-game-running", so
  a leading "ensure-emulator-running" is the natural composition. `IEnsureEmulatorRunningActionHandler`
  is already a DI singleton (registered in feature 070), so injecting it is a one-line constructor add.
- **Alternatives considered**:
  - *The `/api/sessions/start` endpoint (`SessionsController`) / MCP `start_session`* — rejected as
    out of scope: it's a separate, lighter, synchronous path that only attaches a session (no
    ensure-game-running today) and serves interactive manual connects. Folding async emulator control
    into it is a different change; operators can precede a manual start with a standalone
    `ensure-emulator-running` step.
  - *A brand-new "connect-and-ensure" action* — rejected: worse UX (two actions to learn) and would
    duplicate the connect flow; an optional field on the existing action is strictly additive.

## Decision 2 — Opt-in gate via `EnsureEmulatorRunningArgs.TryFrom`

- **Decision**: Attempt `EnsureEmulatorRunningArgs.TryFrom(action.Parameters, out var emu)`. Only when
  it returns true (i.e., an instance id + serial are present) run the pre-heal; otherwise proceed
  directly to today's behavior.
- **Rationale**: Reuses the exact parsing/validation already built in feature 070 and makes "no
  instance id ⇒ unchanged" fall out naturally (TryFrom fails without an identifier). The connect action
  already carries `adbSerial` in `Parameters`, which is the serial the emulator args need.
- **Alternatives considered**: a separate boolean flag — rejected as redundant; presence of the
  identifier is the signal.

## Decision 3 — Result mapping (fail-fast vs proceed)

- **Decision**: `result.IsSuccess || result.IsUnsupported` ⇒ proceed to `StartSession`; else ⇒ return
  `new ActionDispatchResult("failed", $"connect-to-game emulator pre-heal failed: {result.ReasonCode}")`
  without starting a session.
- **Rationale**: Matches the emulator action's own step semantics (feature 070): started/restarted/
  already-healthy and the neutral unsupported outcomes are non-fatal; `RecoveryTimedOut`/`InstanceNotFound`
  are genuine failures. Not starting a session on genuine failure satisfies FR-003 and avoids a
  guaranteed-to-fail `StartSession` against a dead device.
- **Alternatives considered**: always proceed and let `StartSession` fail naturally — rejected: loses
  the clear emulator reason and wastes a session-start attempt.

## Decision 4 — Surface the pre-heal outcome in the connect message

- **Decision**: On success, append the emulator `ReasonCode` to the existing connect success message
  (which already appends the game-launch `ReasonCode`), e.g. `"…; emulator: started; game launch: game_running"`.
- **Rationale**: Consistent with the current message format and gives operators visibility (FR-007).

## Decision 5 — No new configuration or tooling

- **Decision**: Reuse the injected feature-070 handler entirely; add no `AppConfig` fields, env vars,
  resolver, or client.
- **Rationale**: The handler already owns `ldconsole` discovery and the probe/boot-wait/poll timeouts;
  duplicating any of it would violate DRY and the spec's FR-008/SC-005.
