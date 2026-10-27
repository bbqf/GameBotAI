# Implementation Plan: Digit Keys and Raw Key Codes in Key Input Steps

**Branch**: `164-key-input-digits` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/164-key-input-digits/spec.md`

## Summary

A key input step accepts only a key name from a small private map. The map has no digit names. The plan does four things:

1. Move the key name map into one public helper, `AndroidKeyCodes`. Add the names "0" to "9" and "NUMPAD_0" to "NUMPAD_9". Add the rule for a raw key code from 0 to 300.
2. Make both key paths of `SessionManager` use the helper. The session input path gives a clear failure reason.
3. Make the command step report a truthful outcome when the device accepts no key. Today it reports "executed" in that case.
4. Document the rules in Swagger (command step, primitive action payload, and key action definition). Change the web UI hint text.

## Technical Context

**Language/Version**: C# on .NET (current solution); TypeScript and React for `src/web-ui`
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (both in use now). No new package.
**Storage**: N/A. The saved key text format does not change.
**Testing**: xUnit and FluentAssertions in `tests/unit` and `tests/contract`; Jest in `src/web-ui`
**Target Platform**: Windows service that drives Android emulators with ADB
**Project Type**: Web service with a web UI
**Performance Goals**: One dictionary lookup and one integer parse for each key step. No measurable change.
**Constraints**: Old key names, saved commands, and gamepacks MUST keep the same behavior when the device accepts the key.
**Scale/Scope**: About 4 source files change and 1 new source file. About 3 new test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Result |
|------|--------|
| Build and test state | Pass. Run the full build and tests before work starts and before each commit. |
| STE (Principle VI) | Pass. All plan artifacts and new text use STE. |
| Phone check (Principle III) | Pass. The web UI change is hint text only. Phone support level: full. No layout change. `docs/mobile-ui-principles.md` is not affected. |
| Code quality (Principle I) | Pass. One helper replaces a private map. Method names use CamelCase with no underscore. |
| Testing (Principle II) | Pass. Unit, contract, and web UI tests are planned. Write the test for the outcome fix first and see it fail (see R-004). |
| Performance (Principle IV) | Pass. No hot path change. Goal: no measurable change. |
| Living docs (Principle V) | Pass. Plan includes `docs/architecture.md` and its "Last reviewed" date. |
| Version bump | Planned. Set `patch` before the PR push. |

Post-design re-check: no new violation. The Complexity Tracking table stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/164-key-input-digits/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── key-input.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── GameBot.Emulator/Session/
│   ├── AndroidKeyCodes.cs                  # NEW: name map, digit names, raw key code rule
│   └── SessionManager.cs                   # CHANGE: both key paths use AndroidKeyCodes
├── GameBot.Service/
│   ├── Services/CommandExecutor.cs         # CHANGE: truthful KeyInput outcome
│   ├── GameBotServiceSetup.cs              # CHANGE: register the new schema filter
│   └── Swagger/
│       ├── KeyInputSchemaFilter.cs         # NEW: key description and example
│       ├── PrimitiveActionSchemaFilter.cs  # CHANGE: key payload text
│       └── SwaggerConfig.cs                # CHANGE: key action definition help text
└── web-ui/src/components/commands/
    └── KeyInputPanel.tsx                   # CHANGE: hint text

tests/
├── unit/Emulator/AndroidKeyCodesTests.cs           # NEW
├── unit/Emulator/SessionManagerKeyInputTests.cs    # NEW: session input reasons
├── unit/Commands/CommandExecutorKeyInputTests.cs   # CHANGE: outcome cases└── contract/Commands/KeyInputSwaggerTests.cs       # NEW

docs/architecture.md                        # CHANGE: key text rule, "Last reviewed"
CHANGELOG.md                                # CHANGE: entry
installer/versioning/version.override.json  # CHANGE: patch, before the PR push
```

**Structure Decision**: Keep the layout of the repo. The helper goes in `GameBot.Emulator` next to `SessionManager`, because the Emulator project owns the Android key codes. `CommandExecutor` already references this project.

## Design

### D-001 The helper

`AndroidKeyCodes` is a public static class in `GameBot.Emulator.Session`. It owns the name map (moved from `SessionManager`) with these additions:

- "0" to "9" map to key codes 7 to 16.
- "NUMPAD_0" to "NUMPAD_9" map to key codes 144 to 153.

Method: `TryResolve(string? text, out int keyCode)` returns a result value of type `KeyResolveResult`: `Ok`, `Empty`, `UnknownName`, or `OutOfRange`. Order of the checks:

1. Empty or white space gives `Empty`.
2. Trim the text. Look it up in the map (ignore case). A hit gives `Ok`. This gives a listed name priority over a raw code (FR-004).
3. If the text has only ASCII digits, parse it. A value from 0 to 300 gives `Ok`. Any other value, also a number too large to read, gives `OutOfRange`.
4. Else give `UnknownName`. This covers "-1", "+5", "6.5", and "6a" (FR-006).

The constant `MaxRawKeyCode = 300` matches the range that Swagger already gives for the `keyCode` field.

### D-002 SessionManager

- `ResolveAndroidKeyCode` calls the helper. It keeps the same exceptions and the same "Unknown key name" message, so the old path (`SendInputsAsync`) behaves as before for old names. The `key` action of a sequence step (`SequenceExecutionService.cs`, near line 1202) sends the same key text to the session manager. Find which method it calls, and test it (tasks T009 and T017).
- `DispatchOneInputAsync` (session input route) calls `TryResolve` before the stub check. It returns a clear reason: `key: unknown key name '<text>'` or `key: key code <n> is outside 0 to 300`. An empty key gives `key: key name is empty`. The old text for an unknown name was `key: missing required argument 'Unknown key name ...'`. That text was wrong. No test uses it.
- The numeric `keyCode` argument does not change (FR-008).

### D-003 CommandExecutor

The `KeyInput` branch keeps the dispatch. When `keyAccepted` is 0, it calls `TryResolve` to find the cause. The outcome status is `not_accepted`. The reason code is `key_input_unknown_key`, `key_input_code_out_of_range`, or `key_input_not_accepted`. This follows the `GoToHomeScreen` branch. When `keyAccepted` is more than 0, the outcome stays `executed`. No pre-check runs before dispatch. A stub session and the "F5" test that exists now keep their behavior.

### D-004 Swagger

- `KeyInputSchemaFilter` sets the description and the example of `KeyInputConfigDto.key`. The text lists the key names, the digit names, the number pad names, and the raw key code rule. It gives one digit example ("6") and one raw key code example ("85").
- The `key` text of `PrimitiveActionSchemaFilter` and the help text in `SwaggerConfig` point to the same rule. Both use the shared constant of the new filter, so the text exists once.
- The service does not feed XML comments to Swagger. Use schema filters.

### D-005 Web UI

Change the placeholder of `KeyInputPanel` to `e.g. Enter, Escape, 6, a, 85`. The old text names "F5". "F5" is not in the key list. Add a short help line below the field with the digit and raw code rule. No layout change.

## Complexity Tracking

No violation. The table is empty.
