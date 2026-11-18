# Data Model: TextInput Command Step

**Feature**: 192-text-input-step | **Date**: 2026-11-18

## CommandStepType (Domain enum, changed)

Add the value `TextInput` at the end of the enum (after `EnsureEmulatorRunning`). The JSON form is the string `"TextInput"`. The order of the current values does not change.

## TextInputConfig (Domain, new)

| Field | Type | Required | Rules |
|-------|------|----------|-------|
| `Text` | string | Yes | The text to type. It can hold inline placeholders `{{name}}`. A literal text (no placeholder, and no `textInput.text` key in `fieldTemplates`) must pass `TextInputRules.Check`. With a `textInput.text` key in `fieldTemplates`, the text can be empty: the overlay value replaces it. |

## CommandStep (Domain, changed)

| Field | Type | Change |
|-------|------|--------|
| `TextInput` | `TextInputConfig?` | New. Set only when `Type` is `TextInput`. Not written to storage when null. |
| `FieldTemplates` | `Dictionary<string,string>?` | Accepts the new key `textInput.text`. The value must be one whole placeholder. |

## CommandStepFieldPaths (Domain, changed)

| Member | Change |
|--------|--------|
| `SupportedTextPaths` | New set: `textInput.text`. |
| `IsSupported(path)` | True also for a text path. |
| `IsTextPath(path)` | New. True for a text path. |

## TextInputRules (Domain, new)

| Member | Meaning |
|--------|---------|
| `MaxLength` | 500. |
| `MinCharacter`, `MaxCharacter` | U+0020 and U+007E. |
| `Check(string? text)` → `TextInputCheck` | `Ok`, `Empty` (null or length 0), `TooLong` (length > 500), or `UnsupportedCharacter` (a character outside U+0020 to U+007E). The check order is empty, length, characters. |
| `ReasonCode(TextInputCheck)` | `text_input_empty_text`, `text_input_too_long`, `text_input_unsupported_character`; null for `Ok`. |

Note: a text of spaces only (for example `"   "`) is not empty. It types the spaces.

## AdbTextEncoder (Emulator, new)

| Member | Meaning |
|--------|---------|
| `Split(string text)` → list of parts | Each part is a space (sent as key event 62) or a chunk with no space and no `%s` sequence. The parts in order give the original text. |
| `QuoteForDeviceShell(string chunk)` | The chunk in single quotes. Each `'` becomes `'\''`. |

## Input action `text` (Emulator, new action type)

| Argument | Type | Direction | Meaning |
|----------|------|-----------|---------|
| `text` | string | in | The resolved text. `CommandExecutor` checks it before the send. |
| `typedLength` | int | out | The number of characters that the device accepted. The device path sets it. The stub path does not set it. |

## PrimitiveTapStepOutcome and StepExecutionOutcomeDto (Service, changed)

| Field | Type | Change |
|-------|------|--------|
| `TypedLength` | `int?` | New. Set for a `TextInput` step: the number of characters typed (0 when the service typed nothing). Null for other step types. The DTO does not write it when null. |
| `StepType` | string | The value for a `TextInput` step is `text-input`. |

## Step result states of a TextInput step

| Status | Reason | When | TypedLength |
|--------|--------|------|-------------|
| `executed` | null | The device accepted all characters (or stub mode). | Text length |
| `not_accepted` | `text_input_empty_text` | The resolved text is empty. | 0 |
| `not_accepted` | `text_input_too_long` | The resolved text has more than 500 characters. | 0 |
| `not_accepted` | `text_input_unsupported_character` | The resolved text has a character outside U+0020 to U+007E. | 0 |
| `not_accepted` | `text_input_not_accepted` | The device did not accept a chunk or a space. | Characters typed before the failure |
| `skipped_invalid_config` | `text_input_missing_config` | The step has no `textInput` config. | null |
| (resolution failure) | The current parameter resolution reasons | A placeholder does not resolve, or a list has two or more items. | null |

## Request DTOs (Service, changed)

| Type | Field | Change |
|------|-------|--------|
| `CommandStepTypeDto` | `TextInput` | New value. |
| `TextInputConfigDto` | `Text` (string, required) | New. |
| `CommandStepDto` | `TextInput` (`TextInputConfigDto?`) | New. |
| `CreateCommandRequest` | `DryRun` (`bool?`) | New. `true` runs all checks and stores nothing. |
| `UpdateCommandRequest` | `DryRun` (`bool?`) | New. Same meaning. |

## Web UI types (changed)

| File | Change |
|------|--------|
| `services/commands.ts` | Step type union gets `'TextInput'`; `TextInputConfigDto = { text: string }`; `textInput?` on the step DTO. |
| `components/commands/CommandForm.tsx` | Step type union, `textInput?: { text: string }`, label `Text: <text>`, editable type. |
| `components/parameters/fieldTemplatePaths.ts` | `FieldTemplateKind` gets `'text'`; entry `{ path: 'textInput.text', kind: 'text' }`; prefix `TextInput: 'textInput.'`. |
