# Data Model: TextInput step types all characters

Feature: `195-fix-text-input-chars` | Spec: [spec.md](./spec.md)

This fix adds no stored data. The command format, the step result fields, and the execution-log format do not change.

## AppConfig (changed)

| Field | Type | Default | Rule | Source |
|---|---|---|---|---|
| `TextInputCharDelayMs` | `int` | `100` | 0 to 2000. Not a number or negative gives 100. Larger than 2000 gives 2000. | `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (start: `GameBotServiceSetup`; run-time change: `ConfigApplier`; default list: `ConfigSnapshotService`) |

`AppConfig.NormalizeTextInputCharDelayMs(int? value)` (new, static) applies the rule. Both readers call it.

## Character input (runtime only)

One input command to the device for one character of the resolved text.

| Character | Input command |
|---|---|
| space (`U+0020`) | key event 62 (`KEYCODE_SPACE`) |
| other printable ASCII (`U+0021` to `U+007E`) | `adb shell input text '<c>'` (shell quoting of feature 192) |

Order: the characters of the text, from first to last. A pause of `TextInputCharDelayMs` occurs between two character inputs. No pause occurs before the first or after the last character. Retries: `AdbRetries` with `AdbRetryDelayMs` for each character input. No pause occurs between two retries of one character.

## Step result of a TextInput step (fields unchanged, exact definition)

| Field | Value |
|---|---|
| `status` | `executed` when the device accepted every character input. `not_accepted` when the device did not accept a character input after the retries. |
| `reason` | `text_input_not_accepted` for `not_accepted` after a send. Other reasons as in feature 192. |
| `typedLength` | The number of character inputs that the device accepted. For `executed`, this is the text length. |

### State flow of one send

```text
start -> [character i: send with retries]
  accepted  -> typed += 1 -> (i is last ? done(executed) : pause -> next character)
  failed    -> done(not_accepted, typedLength = typed)
  cancelled -> OperationCanceledException goes up (no step result)
```
