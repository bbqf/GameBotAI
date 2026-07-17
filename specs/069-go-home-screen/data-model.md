# Phase 1 Data Model: Go To Home Screen Action

This feature is additive and introduces no persisted schema changes beyond one new enum member and
one new action-type string. No migration is required (existing sequences/commands are unaffected).

## Action type identity

| Concept | Value |
|---|---|
| Canonical action-type key | `go-to-home-screen` |
| `ActionTypes` constant | `ActionTypes.GoToHomeScreen` |
| `PrimitiveActionTypes` constant | `PrimitiveActionTypes.GoToHomeScreen` (added to `PrimitiveActionTypes.All`) |
| Command step enum member | `CommandStepType.GoToHomeScreen` / `CommandStepTypeDto.GoToHomeScreen` |
| Web-ui selector member | `PrimitiveActionType` union value `'GoToHomeScreen'` |
| Device operation | Android key event `KEYCODE_HOME` (keycode `3`) |
| Author-supplied parameters | none (parameterless) |

## Entities

### PrimitiveGoToHomeScreenAction (new)

A parameterless variant of `PrimitiveActionBase`, mirroring `PrimitiveEnsureGameRunningAction`.

```csharp
public sealed class PrimitiveGoToHomeScreenAction : PrimitiveActionBase {
  public PrimitiveGoToHomeScreenAction() : base(PrimitiveActionTypes.GoToHomeScreen) { }
}
```

- **Fields**: none beyond the inherited `Type` / `SchemaVersion`.
- **Validation rules**: type must equal `go-to-home-screen`; no payload fields to validate
  (`PrimitiveActionValidationService` requires no new `case`).

### CommandStep (extended)

`CommandStepType` gains a `GoToHomeScreen` member. A `GoToHomeScreen` command step carries no
config object (like `EnsureGameRunning`): `PrimitiveTap`/`WaitForImage`/`KeyInput`/`Swipe` all null,
`TargetId` empty.

### SequenceActionPayload (unchanged shape)

When used as a sequence step, the action is persisted as the existing
`SequenceActionPayload { Type = "go-to-home-screen", Parameters = {} }`. No new payload shape.

## Outcome vocabulary

| Outcome | Meaning |
|---|---|
| `executed` | The HOME key event was accepted by the session (real device or stub). Step succeeds. |
| `failed` | No running session could be resolved, or the device rejected the input. Step fails and the sequence stops (consistent with other primitive actions). |

## Allow-list touch points (must all recognize `go-to-home-screen`)

| Location | Mechanism | Update |
|---|---|---|
| `SequenceStepValidationService` | `AllowedPrimitiveActionTypes = PrimitiveActionTypes.All` | automatic |
| `PrimitiveActionValidationService` | `SupportedActionTypes = PrimitiveActionTypes.All` | automatic |
| `ActionPayloadValidationService` | `PrimitiveActionTypes.All` + `RescheduleSelf` | automatic |
| `FileSequenceRepository.ValidateActionPayloads` | **hard-coded set** | **explicit add required** |
| `SequenceRunner.IsDispatchedPrimitiveAction` | explicit type checks | **explicit add required** |
| `SequenceExecutionService.DispatchActionAsync` | explicit routing | **explicit add required** |
| `CommandExecutor.ExecuteOneStepAsync` | explicit `switch`/`if` on step type | **explicit add required** |
| `CommandsEndpoints` DTO<->domain maps | explicit `switch` | **explicit add required** |

## State transitions

None. The action is stateless: invoke → HOME key event → outcome. It does not create, mutate, or
destroy sessions, games, queues, or persisted entities.
