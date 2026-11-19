# Research: TextInput Command Step

**Feature**: 192-text-input-step | **Date**: 2026-11-18

## R-001: Device command to type a text

- **Decision**: Use `adb shell input text <text>`. It types the text into the focused input field of the device as key events of the characters.
- **Rationale**: The issue names this command. It needs no app on the device. The current input path (`AdbClient` with `shell input tap`, `shell input swipe`, and `shell input keyevent`) uses the same `input` tool.
- **Alternatives considered**: One `KeyInput` key event for each character. This needs a map from each character to a key code and the shift state, and it is slower. Rejected. An input method app (for example ADBKeyboard) with a broadcast. This needs an install on each device. Rejected.

## R-002: Spaces and the `%s` sequence

- **Decision**: `AdbTextEncoder.Split` splits the text into chunks. A space is not in a chunk: the service sends key event 62 (`KEYCODE_SPACE`) for each space. A chunk never holds `%` followed by `s`: the service splits between the two characters. Each chunk goes in its own `input text` command.
- **Rationale**: `input text` changes `%s` to a space, and the device shell splits arguments at spaces. A space as a key event, and a split of `%s`, give the literal text in all cases. A number (the main use) has no space and no `%`, so it is one chunk and one ADB command.
- **Alternatives considered**: Change each space to `%s`. Then a literal `%s` in the text arrives as a space. Rejected.

## R-003: Shell characters and the Windows command line

- **Decision**: `AdbTextEncoder.QuoteForDeviceShell` puts the chunk in single quotes for the device shell, and changes each `'` to `'\''`. `AdbClient.InputTextAsync` sends the arguments `shell` and `input text '<quoted chunk>'` through `ProcessStartInfo.ArgumentList`, so .NET does the Windows quoting. `ExecAsync` keeps its current string form for the other commands. A new private method `ExecArgumentsAsync` shares the process code with `ExecAsync`.
- **Rationale**: In single quotes, the device shell gives no special meaning to `&`, `;`, `|`, `<`, `>`, `(`, `)`, `$`, `` ` ``, `"`, `\`, `*`, or `?`. `ArgumentList` escapes `"` and `\` for the Windows command line. Thus no character of the text can start a shell function (FR-015).
- **Alternatives considered**: A backslash before each special character. This needs a full list of special characters and is easy to get wrong. Rejected.

## R-004: Permitted characters and the maximum length

- **Decision**: `TextInputRules.Check(text)` permits U+0020 to U+007E and a length of 1 to 500. The result is `Ok`, `Empty`, `UnsupportedCharacter`, or `TooLong`, with the reason codes `text_input_empty_text`, `text_input_unsupported_character`, and `text_input_too_long`. The check order is empty, then length, then characters.
- **Rationale**: `input text` types only characters that the default key map of the device has. Printable ASCII is in the default key map. A tab, a line break, or a non-ASCII character (for example `é`) does not type in a reliable way. The limit 500 keeps an ADB command line short and is much more than a number or a word needs.
- **Alternatives considered**: Permit all characters and let the device fail. The device gives no error for a character that it cannot type, so the step result would show a false success. Rejected.

## R-005: Retries, time limit, and the typed length

- **Decision**: `SessionManager.SendInputsAsync` handles the input action type `text` (argument `text`). It sends each chunk and each space through `SendRecordedInputAsync`, so each ADB command gets the configured ADB retries and a liveness record (FR-021). It stops at the first command that fails. It writes the number of characters that it typed into `Args["typedLength"]`. The action counts as executed only when all commands succeed. In stub mode (no device), the action counts as executed and `typedLength` is not set; `CommandExecutor` then uses the text length.
- **Rationale**: A retry of one chunk does not type a character twice, because ADB returns a failure exit code only when the device did not run the command. A retry of the full text could type the first chunks twice. The write-back into `Args` is the current pattern of the tap jitter (the executor reads back the coordinates).
- **Alternatives considered**: One recorded input for the full text. A retry could then type the first chunks again. Rejected.

## R-006: Where `dryRun` reports an unknown placeholder

- **Decision**: Add `dryRun` (boolean) to the body of `POST /api/commands` and `PATCH /api/commands/{id}`. With `true`, the service runs all checks (the step checks, the region checks, and `ParameterValidationService.ValidateCommand`) and stores nothing. It gives HTTP 200 with `{ "valid": true, "dryRun": true, "errors": [] }`. A failed check gives the same HTTP 400 as a real save. `ValidateCommand` already reports a reference that the command does not declare with the code `unresolvable_parameter_reference` and the field path. The scanner change (R-008) makes it find `textInput.text`.
- **Rationale**: A `TextInput` step is in a command. The sequence `dryRun` (save and execute) does not look into the steps of a command. The command routes have no `dryRun` today. The same body field and response as the sequence routes (features 082, 091, 113) keep the API consistent. `UnknownFieldScanner` reads the request type, so the new property `DryRun` on `CreateCommandRequest` and `UpdateCommandRequest` is accepted.
- **Alternatives considered**: Make the sequence `dryRun` resolve the command steps. This changes the sequence `dryRun` for all fields and needs the run scope of each command step. Rejected as too wide for this feature.

## R-007: The `fieldTemplates` key `textInput.text`

- **Decision**: Add `CommandStepFieldPaths.SupportedTextPaths` = { `textInput.text` }. `IsSupported` includes it. A new method `IsTextPath` returns true for it. `ParameterValidationService.ValidateFieldTemplateKeys` requires one whole placeholder for an image path or a text path. `AddTextInNumericFieldErrors` does not change (it reads only the numeric paths). The web mirror list gets the kind `text`, and `FieldTemplatePathMirrorTests` reads the new set and the new kind.
- **Rationale**: The text key uses the save rule of the image keys (clarification 1). A numeric key gets no save check of its form, and this feature does not change that (FR-017). The overlay value goes through `TryOverlayValue(..., isTextField: true)`, which already resolves a whole placeholder, records the usage, and refuses a multi-value list with two or more items.
- **Alternatives considered**: Permit text around a placeholder in the overlay value. The inline field already permits this. Rejected to keep one overlay rule.

## R-008: Resolver and scanner

- **Decision**: `CommandStepResolver.TryResolve`: when the step has `TextInput`, and `FieldTemplates` has `textInput.text`, use `TryOverlayValue(isTextField: true)` and do not resolve the inline text. Otherwise use `TryText(text, "textInput.text", ..., singleValueField: true)`. `HasInlinePlaceholder` checks `TextInput.Text`. `ParameterReferenceScanner.ScanCommandStep` adds the inline `textInput.text`. The loop over `FieldTemplates` already adds the overlay reference.
- **Rationale**: The image keys (feature 114) use the same "overlay replaces inline" rule. The number value `250` has the text `250` in the scope, so the text step gets `250` with no format change (FR-005).

## R-009: The step result and the execution log

- **Decision**: Add `int? TypedLength` to `PrimitiveTapStepOutcome` and to `StepExecutionOutcomeDto` (not written when null). The step type text is `text-input`. `ExecutionLogService` adds a detail item of kind `text-input` with the text "Text input typed N characters." and the attribute `typedLength`. The step result and the log do not hold the typed text (clarification 3). The log keeps the resolved parameters as today.
- **Rationale**: The issue asks for the length. The other step types use the same optional outcome fields (for example `HoldMs`).

## R-010: Storage and gamepacks

- **Decision**: `CommandStep.TextInput` has `[JsonIgnore(Condition = WhenWritingNull)]`. `GamepackReferences` copies `TextInput` when it maps the step ids.
- **Rationale**: A stored command without a `TextInput` step keeps the same JSON, so the gamepack "content differs" check (feature 178) does not report a change for current commands.

## R-011: Step-through and the steps route

- **Decision**: `PreviewEffectRules.ByType[TextInput] = false`. `StepsEndpoints` maps and checks the `TextInput` step with the same rules as `CommandsEndpoints` (shared method `CommandsEndpoints.ValidateTextInput`), and writes `typedLength` in its outcome.
- **Rationale**: The step uses only the device, as `KeyInput` (clarification 5). The unit test of `PreviewEffectRules` fails for a step type that is not in the list.
