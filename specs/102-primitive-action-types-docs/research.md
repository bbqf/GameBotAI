# Research: Publish primitive action types and payload shapes

## R-001 — What the service actually accepts

**Decision**: The supported set is eleven values, in this order: `tap`, `swipe`, `key`, `command`, `connect-to-game`,
`WaitForImage`, `ensure-game-running`, `go-to-home-screen`, `ensure-emulator-running`, `reschedule-self`, `notify`.

**Evidence**: `SequenceStepValidationService.ValidateStepCondition` routes `reschedule-self` and `notify` to their
payload validators first, then rejects anything not in `AllowedPrimitiveActionTypes` (= `PrimitiveActionTypes.All`,
nine values), `OrdinalIgnoreCase`. `ActionPayloadValidationService` independently builds the same eleven.
`SequenceRunner.IsServiceLevelAction` / `IsDispatchedPrimitiveAction` and
`SequenceExecutionService.DispatchActionAsync` handle every one of them.

**Alternatives considered**: publishing only the five the issue named — rejected, the other six are equally
undocumented and FR-001 says "no more, no fewer".

## R-002 — Single source of truth

**Decision**: New `public static class SequenceActionTypes` in `GameBot.Domain.Actions` exposing
`IReadOnlyList<string> All` (the R-001 order: `PrimitiveActionTypes.All` then `RescheduleSelf`, `Notify`) and
`string SupportedValuesText` (`string.Join(", ", All)`). The validator's membership set, its error message and
`ActionPayloadValidationService` read it; the schema filter reads it for `type.enum`.

**Rationale**: two hand-built copies of the set exist today; the enum would be a third. One list removes drift at the
source; the contract test still asserts equality end to end.

**Alternatives considered**: reflecting over `ActionTypes` constants (would also publish nothing-else-but-constants
and silently include future non-step keys); reading `ActionPayloadValidationService.SupportedActionTypes` (unordered
`HashSet`, instance member, flow-graph scoped).

## R-003 — Enum on a shared schema

**Decision**: Set `enum` on `PrimitiveAction.type` (the `PrimitiveActionRequest` component) and state in the `type`
description that `POST /api/sessions/start` accepts only `connect-to-game` (`SessionsController.StartSession` rejects
others with 400 "primitiveAction.type must be connect-to-game.").

**Rationale**: `StartSessionRequest` and `SequenceStepContract` reference the same component; OpenAPI cannot carry two
enums for one component without splitting the schema, which the spec rules out.

## R-004 — Payload shapes (verified from readers)

`payload` entries are copied verbatim into the stored step's `Action.Parameters` (`SequencesEndpoints` mapping), so the
reader keys below are the wire keys. Integer fields also accept numeric strings (template substitution).

| Type | Payload | Source |
|---|---|---|
| `tap` | `x`, `y` integers, required (device pixels) | `SequenceExecutionService.TryBuildInputAction` |
| `swipe` | `x1`, `y1`, `x2`, `y2` integers, required; `durationMs` integer, optional | same |
| `key` | `keyCode` integer (Android key code) or `key` string; one required, `keyCode` wins | same |
| `command` | `commandId` string, required, must name an existing command; step-level `commandReference` does not replace it | `ValidateCommandPayload`, `SequencesEndpoints` |
| `connect-to-game` | `gameId`, `adbSerial` strings, required; `instanceName` string or `instanceIndex` integer ≥ 0, optional (ensure that LDPlayer instance runs first) | `DispatchConnectToGameAsync`, `EnsureEmulatorRunningArgs` |
| `WaitForImage` | `timeoutMs` integer ≥ 0, optional (default 1000); `detectionTarget` object, optional: `referenceImageId` string (non-empty when the object is given), `confidence` number (default 0.8), `offsetX`/`offsetY` integers (default 0), `selectionStrategy` `HighestConfidence` (default) or `FirstMatch` | `ValidateWaitForImagePayload`, `MapWaitForImageConfig` |
| `ensure-game-running` | none (uses the run's session and its game) | `DispatchEnsureGameRunningAsync` |
| `go-to-home-screen` | none (presses Android HOME; game keeps running) | `DispatchGoToHomeScreenAsync` |
| `ensure-emulator-running` | `adbSerial` string, required; `instanceName` string or `instanceIndex` integer ≥ 0, one required (name wins) | `EnsureEmulatorRunningArgs.TryFrom` |
| `reschedule-self` | `option` required: `AtQueueStart` \| `OncePerRun` \| `Timer` \| `EveryStep` (case-insensitive). Timer only: exactly one of `timerTimeOfDay` (HH:mm:ss, service-local) or `timerRelativeOffset` (HH:mm:ss, 00:00:00–24:00:00), or an `ocrOffset` object `{ region: {x,y,width,height} (positive size), fallback (HH:mm:ss, required, 00:00:00–24:00:00), min (default 00:00:01), max (default 24:00:00), min < max }` which makes the static timer fields optional. Timer fields / `ocrOffset` with any other option are rejected. Schedules one more firing into the originating queue run; a no-op success when not run from a queue | `SelfReschedulePayload.TryRead`, `ValidateRescheduleSelfPayload`, `DispatchSelfReschedule` |
| `notify` | `message` string, required, ≤ 1000 chars; `url` absolute http/https URL, optional (overrides the service default destination). Always succeeds | `NotifyPayload.TryRead` |

## R-005 — Where the payload descriptions live

**Decision**: One `payload` property description made of one sentence group per type, each introduced as
`<type>: ...`, built from an `internal static IReadOnlyDictionary<string,string> PayloadDescriptions` on the filter,
emitted in `SequenceActionTypes.All` order. A contract test asserts the dictionary's key set equals
`SequenceActionTypes.All` and that each `<type>:` marker appears in the published description (SC-005).

**Alternatives considered**: `oneOf` per type — rejected in clarification (shape change, shared schema);
`x-` vendor extensions — not surfaced by common viewers.

## R-006 — Example

**Decision**: Append a second step `reschedule-in-30m` (`reschedule-self`, `option: Timer`,
`timerRelativeOffset: "00:30:00"`) to `SequenceCreateRequest()` and `SequenceCreateResponse()` in `SwaggerConfig.cs`.
These feed POST/PUT/PATCH request examples and create/get/list/update response examples. No test asserts the example's
step count (`wait-home-banner` only appears there and in an unrelated test asset).

## R-007 — Error message form

**Decision**: `Step '<id>' action type '<type>' is not a supported primitive action type (expected one of tap, swipe,
key, command, connect-to-game, WaitForImage, ensure-game-running, go-to-home-screen, ensure-emulator-running,
reschedule-self, notify).` No existing test or source matches on the old full text (grep: only the producing line).
