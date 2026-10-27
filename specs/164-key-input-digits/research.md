# Research: Digit Keys and Raw Key Codes in Key Input Steps

No `NEEDS CLARIFICATION` item remains. These are the decisions.

## R-001 Where the key name map lives

- **Decision**: Move the map from the private field in `SessionManager` to a public static helper `AndroidKeyCodes` in `GameBot.Emulator.Session`.
- **Rationale**: Two places need the rule: `SessionManager` (two paths) and `CommandExecutor` (to explain a failure). One owner prevents drift.
- **Alternatives considered**: Make `SessionManager.ResolveAndroidKeyCode` public. Rejected: `CommandExecutor` only has the `ISessionManager` interface. Copy the map into `CommandExecutor`. Rejected: two maps drift.

## R-002 Android key codes

- **Decision**: "0" to "9" use `KEYCODE_0` to `KEYCODE_9` (7 to 16). "NUMPAD_0" to "NUMPAD_9" use `KEYCODE_NUMPAD_0` to `KEYCODE_NUMPAD_9` (144 to 153).
- **Rationale**: These are the standard Android values. The command `adb shell input keyevent 13` presses the digit 6.
- **Alternatives considered**: Use the `adb shell input text 6` command. Rejected: it types text, not a key event, and a game can ignore it.

## R-003 Raw key code rule

- **Decision**: Accept ASCII digits only. Range 0 to 300. A listed name has priority.
- **Rationale**: The `keyCode` field already documents 0 to 300. Digits only avoids sign, decimal, and culture questions. Priority keeps "6" as the digit key, as the user expects.
- **Alternatives considered**: A prefix such as "code:85". Rejected: it adds a new syntax. Any whole number up to `int.MaxValue`. Rejected: Android has no key codes that high, and a typo would reach the device.

## R-004 Truthful step outcome

- **Decision**: In the command step, report `not_accepted` with a reason code when the device accepts no key. Today the step reports `executed` with accepted 0.
- **Rationale**: Spec FR-005 requires "a clear reason". Without this change, a bad key text looks like a success in the step log. `GoToHomeScreen` already follows this pattern.
- **Behavior change**: A key step that fails now shows `not_accepted`, not `executed`. A failed key step must not show "executed". Write a test that fails first (Principle II).
- **Alternatives considered**: A check before dispatch. Rejected: stub sessions never parse the key, and the "F5" test that exists now would fail. Leave the outcome as is. Rejected: it hides the failure.

## R-005 Where Swagger text lives

- **Decision**: One schema filter `KeyInputSchemaFilter` holds the shared text. The primitive action filter and the key action definition use its constant.
- **Rationale**: The service does not feed XML comments to Swagger, and the repo already uses one filter for each feature (for example `PrimitiveTapHoldSchemaFilter`).
- **Alternatives considered**: Write the text in each place. Rejected: three copies drift.

## R-006 Stub mode

- **Decision**: No change. A session without a device does not parse key args in `SendInputsAsync`, and the command step reports `executed`. The session input route parses always, so it reports the reason in both modes.
- **Rationale**: A stub session has no device. It exists for tests.
