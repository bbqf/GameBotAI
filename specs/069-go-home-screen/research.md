# Phase 0 Research: Go To Home Screen Action

## Decision 1 — How to reach the device home screen

**Decision**: Send an Android `KEYCODE_HOME` (keycode `3`) key event through the existing session
input pipeline (`ISessionManager.SendInputsAsync` with a `key` action carrying `keyCode = 3`).

**Rationale**:
- `SessionManager.SendInputsAsync` already implements the `key` branch: it maps `keyCode` (or a
  symbolic `key` name via `KeyNameMap`) to `adb shell input keyevent <code>`, with configurable
  retries (`AppConfig.AdbRetries`/`AdbRetryDelayMs`).
- `KeyNameMap` already contains `["HOME"] = 3`.
- In non-Windows or non-ADB (stub) sessions the same method returns success without touching a
  device, which is exactly the graceful-degradation behavior FR-007 requires.
- HOME (not force-stop) matches the clarified behavior: the game keeps running in the background.

**Alternatives considered**:
- *New `IAdbGameOperations.PressHomeAsync` + dedicated handler (mirroring `ensure-game-running`'s
  `EnsureGameRunningActionHandler`)*: rejected — adds ADB plumbing, a new interface method, DI
  wiring, and platform guards that the session pipeline already provides. More code, same result.
- *`am force-stop <package>` and/or a launcher intent*: rejected — that closes/kills the game,
  contradicting the clarified "leave it running in the background" requirement.
- *Reuse the generic `key` action with `keyCode: 3` directly (no new action type)*: rejected — the
  user explicitly wants a first-class action "same as connect-to-game", discoverable in the
  authoring surfaces, not a raw keycode authors must memorize.

## Decision 2 — Surface parity model

**Decision**: Model the new action on `ensure-game-running`, wiring it as **both** a sequence action
type (`PrimitiveActionTypes.All` + `SequenceRunner.IsDispatchedPrimitiveAction` +
`SequenceExecutionService.DispatchActionAsync`) and a command step type
(`CommandStepType.GoToHomeScreen` + `CommandExecutor` + DTO mapping + web-ui selector/panel).

**Rationale**: `ensure-game-running` is the only existing parameterless device action and is already
wired through every surface an author touches. Cloning its wiring guarantees "available same as"
parity with the least risk and the least novelty. `connect-to-game` additionally owns the
`/api/sessions/start` endpoint only because it *creates a session from a payload*; a parameterless
home-screen action has nothing to start, so that endpoint is out of scope (documented in the spec
Assumptions).

**Alternatives considered**:
- *Sequence-action-only (no command step / no UI)*: rejected — would not appear in the web-ui
  authoring surface where `ensure-game-running` lives, failing the parity requirement (FR-004).
- *Add a `/api/sessions/home` or similar endpoint*: rejected — no session-lifecycle change is
  involved; it would be an orphan surface with no analogue on the `ensure-game-running` side.

## Decision 3 — Validation allow-lists

**Decision**: Add the `go-to-home-screen` constant to `PrimitiveActionTypes.All`, and separately add
`ActionTypes.GoToHomeScreen` to the hard-coded set in
`FileSequenceRepository.ValidateActionPayloads`.

**Rationale**: `SequenceStepValidationService`, `PrimitiveActionValidationService`, and
`ActionPayloadValidationService` all derive their allow-list from `PrimitiveActionTypes.All`, so one
edit covers them. `FileSequenceRepository` deliberately keeps its own literal set as a
persistence-boundary guard; if it is not updated, saving a sequence that uses the action throws
`InvalidOperationException` → HTTP 500. This is a known repository-vs-service allow-list split in
this codebase and must be handled in both places.

**Alternatives considered**:
- *Point `FileSequenceRepository` at `PrimitiveActionTypes.All`*: attractive but out of scope for
  this feature (would change unrelated behavior and risk widening what the repository accepts);
  keep the change minimal and additive.

## Decision 4 — Outcome semantics

**Decision**: Dispatch returns `executed` on accepted input and `failed` (failing the step and the
sequence) when no running session can be resolved or the device rejects the input — mirroring
`DispatchPrimitiveInputAsync`. Success is idempotent (pressing HOME on the home screen still
succeeds).

**Rationale**: Consistent with the other device-driving primitives; a genuine "nothing reached the
device" must not report a fake success (matches the existing runner contract for primitive
actions). Idempotency is inherent to `KEYCODE_HOME`.

## Open Questions

None. Behavior (HOME-only, leave running) and workflow (full speckit feature) were clarified with
the user before specification.
