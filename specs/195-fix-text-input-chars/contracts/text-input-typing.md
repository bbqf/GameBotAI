# Contract: TextInput character input and the character pause setting

Feature: `195-fix-text-input-chars` | Spec: [spec.md](../spec.md)

## 1. HTTP API (no change to the shape)

The request and response shapes of feature 192 do not change:

- `textInput.text` in a command step: no change.
- `typedLength` in a step outcome: no change in type or presence.

Only the OpenAPI descriptions change (FR-012):

- `textInput.text` (`TextInputSchemaFilter.TextDescription`): add these sentences. "The service types one character at a time. It waits GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS milliseconds (default 100) between two characters."
- `typedLength` (`TextInputSchemaFilter.TypedLengthDescription`): the value is the number of characters that the device accepted. For the status `executed`, this is the text length.

## 2. Service setting

| Key | Type | Default | Range | Applied |
|---|---|---|---|---|
| `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` | integer (ms) | `100` | 0 to 2000 (not a number or negative: 100; larger than 2000: 2000) | At start and at each configuration change (no restart) |

The key shows in `GET /api/config` (the configuration snapshot) with its default, as the other `GAMEBOT_*` keys do.

## 3. Device input order (observable on the ADB seam)

For the text `25 0` and the pause `P` > 0, the device gets these calls in this order:

```text
input text '2'
(pause P)
input text '5'
(pause P)
input keyevent 62
(pause P)
input text '0'
```

For `P` = 0, the same calls occur with no pause.

When the device does not accept `input text '5'` (after the retries), the calls stop after it. The step result is `not_accepted`, the reason is `text_input_not_accepted`, and `typedLength` is 1.

## 4. Internal seams (for tests only)

- `SessionManager.CharacterPause` (`internal Func<int, CancellationToken, Task>`, default `Task.Delay`): the pause function. The unit tests replace it to record the pause.
- `AdbTextEncoder.SplitCharacters(string)` (public): one part for each character, in order.
