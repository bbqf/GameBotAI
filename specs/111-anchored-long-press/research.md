# Research: Press and hold at a detected point (anchored long press)

## R-001: Why the service drops `holdMs` today

- **Finding**: `PrimitiveTapConfigDto` (`src/GameBot.Service/Models/Commands.cs`) has only `DetectionTarget`. The JSON binder of the minimal API ignores an unknown property, so `holdMs` is lost before the save mapping. The domain `PrimitiveTapConfig` (`src/GameBot.Domain/Commands/CommandStep.cs`) also has no hold field.
- **Decision**: Add `int? HoldMs` to the DTO and to the domain configuration. Map it in both directions in `CommandsEndpoints` (`ToDomainPrimitiveTap`, `ToResponsePrimitiveTap`) and in `StepsEndpoints.ToDomainStep`.
- **Alternatives considered**: Fix issue #237 (reject unknown fields) at the same time. Rejected: out of scope (spec Assumptions).

## R-002: How the tap of today reaches the device

- **Finding**: `CommandExecutor.TryDetectAndTap` sends one `InputAction("swipe", { x1, y1, x2, y2 }, DelayMs: null, DurationMs: 200)` with the same start point and end point. `SessionManager` sends it as `adb shell input swipe x1 y1 x2 y2 200`. `SessionManager.ApplyTapJitter` moves the start point and the end point separately, and it changes the args in place. The executor reads `x1`/`y1` back as the executed point.
- **Decision**: Keep this input. Use `DurationMs = holdMs` when `holdMs > 0`, else 200. Put the choice in one small static helper (`PrimitiveTapInput`), so that a unit test on Linux can check it. The detection path uses `System.Drawing` and runs only on Windows.
- **Alternatives considered**: (a) A new input type `hold` in `SessionManager`. Rejected: ADB has no hold command; it is the same swipe. (b) One jitter offset for both points of a hold. Rejected: it changes the tap of today and the `Swipe` step (spec clarification 2).

## R-002a: Cancel during a hold

- **Finding**: The tap sends its input with `_sessions.SendInputsAsync(sessionId, inputs, ct)`. A cancel of `ct` during the dispatch goes to the `OperationCanceledException` handler of the step, which gives the outcome `cancelled`.
- **Decision**: The hold uses the same call and the same token. A cancel during a hold thus gives the same outcome as a cancel during a tap. No code change is necessary.

## R-003: Validation of the range

- **Finding**: `CommandsEndpoints.ValidateStep` and `StepsEndpoints.ValidateStep` return an error text, and the endpoints return HTTP 400 with it. Other range rules use the form "`<path>` must be ...".
- **Decision**: Add the rule to both `ValidateStep` methods for `PrimitiveTap` steps: "primitiveTap.holdMs must be between 0 and 5000". Put the limits in the domain as `PrimitiveTapConfig.MinHoldMs = 0` and `PrimitiveTapConfig.MaxHoldMs = 5000`. A non-integer value fails in the JSON deserializer, as for the other integer fields. This feature does not change that path.
- **Alternatives considered**: Clamp the value. Rejected: the issue asks to reject values outside the range.

## R-004: Storage and read-back

- **Finding**: `FileCommandRepository` writes the domain `Command` with `System.Text.Json` (web defaults). Null properties are written as `null`. `CommandStep.FieldTemplates` uses `[JsonIgnore(Condition = WhenWritingNull)]` to keep the stored JSON of old commands the same.
- **Decision**: Put `[JsonIgnore(Condition = WhenWritingNull)]` on `PrimitiveTapConfig.HoldMs` and on `PrimitiveTapConfigDto.HoldMs`. An absent value stays absent in storage and on read-back. A value of 0 is kept and returned as 0.

## R-005: Parameter resolution copies the step

- **Finding**: `CommandStepResolver` (`src/GameBot.Domain/Parameters/CommandStepResolver.cs`) makes a new `PrimitiveTapConfig` with the resolved detection target before dispatch. A new property is lost there if the copy does not include it.
- **Decision**: Copy `HoldMs` into the new configuration. Do not add `primitiveTap.holdMs` to `CommandStepFieldPaths.SupportedNumericPaths` (spec FR-012).

## R-006: Step outcome and execution log

- **Finding**: `PrimitiveTapStepOutcome` (`src/GameBot.Service/Services/ICommandExecutor.cs`) carries `ResolvedPoint` and `ExecutedPoint`. `ExecutionLogService.LogCommandExecutionAsync` writes a `tap` detail with the text "Tap executed at (x,y)." or "Tap targeted (x,y), executed at (x2,y2).", and the attributes `x`, `y`, `executedX`, `executedY`, `confidence`. `CommandsEndpoints.ToResponseOutcome` maps the outcome to `StepExecutionOutcomeDto`. `StepsEndpoints.ToResponseOutcome` maps it to an anonymous object.
- **Decision**: Add `int? HoldMs = null` to `PrimitiveTapStepOutcome`. Set it only for a press and hold. In the log, when `HoldMs > 0`, write the text "Press and hold at (x,y) for N ms." or "Press and hold targeted (x,y), executed at (x2,y2) for N ms.", and add the attribute `holdMs`. Add `HoldMs` with `[JsonIgnore(WhenWritingNull)]` to `StepExecutionOutcomeDto`. Add `holdMs` to the anonymous outcome of `StepsEndpoints` only when it is set.
- **Alternatives considered**: A new detail kind `hold`. Rejected: the web UI and the tests read the kind `tap` for a primitive tap.

## R-007: OpenAPI document

- **Finding**: Swashbuckle builds the live document from the DTOs. The service does not feed XML comments to Swagger. `[Range]` on a DTO property gives `minimum` and `maximum` (see `watchdogTimeoutMs`). Schema filters add descriptions (see `SequenceTimeLimitSchemaFilter`). `specs/openapi.json` is a checked-in copy that is not generated in CI; its last change was feature 060.
- **Decision**: Put `[Range(0, 5000)]` on `PrimitiveTapConfigDto.HoldMs`. Add a schema filter `PrimitiveTapHoldSchemaFilter` that writes the description of `holdMs` on `PrimitiveTapConfigDto` and on `StepExecutionOutcomeDto`. Add `holdMs` by hand to `PrimitiveTapConfigDto` in `specs/openapi.json`.

## R-008: Web UI

- **Finding**: The command editor edits a `PrimitiveTap` step in `TapPanel` (`src/web-ui/src/components/commands/TapPanel.tsx`). `CommandForm` keeps the step as `StepEntry.primitiveTap`. `CommandsPage` maps the form to the API DTO and back. `services/commands.ts` has the `PrimitiveTapConfigDto` type.
- **Decision**: Add an optional "Hold duration (ms)" input to `TapPanel` with a check for an integer from 0 to 5000. Add `holdMs?: string` to `StepEntry.primitiveTap`, and map it in `CommandsPage` in both directions. Show "hold N ms" in the step list description when set.

## R-009: Tests on Linux and on Windows

- **Finding**: The `CommandExecutorPrimitiveTapTests` use `System.Drawing.Bitmap` and fail on Linux (12 of 12 fail before this feature). CI runs on Windows.
- **Decision**: Add executor tests for the hold to `CommandExecutorPrimitiveTapTests` (Windows, CI). Add Linux-safe tests: a unit test for `PrimitiveTapInput`, integration tests for save, read-back and range, an integration test for the execution log text and attributes, and a contract test for the OpenAPI document.
