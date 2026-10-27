# Data Model: Digit Keys and Raw Key Codes in Key Input Steps

No stored data changes. No migration is needed.

## Key text

The `key` text of a key input step. A string. It is required and not empty (validation at save time does not change).

| Form | Example | Resolves to |
|------|---------|-------------|
| Listed name (ignore case, trimmed) | `ENTER`, `a`, `HOME` | The key code in the key list |
| Digit name (new) | `6` | 13 (`KEYCODE_6`) |
| Number pad name (new) | `NUMPAD_6` | 150 (`KEYCODE_NUMPAD_6`) |
| Raw key code (new) | `85` | 85 |

### Resolve rules

1. Empty or white space: `Empty` (fails).
2. Trimmed text is a listed name: use its key code. This rule has priority.
3. Text has only ASCII digits and the value is 0 to 300: use the value.
4. Text has only ASCII digits and the value is above 300 (or too large to parse): `OutOfRange` (fails).
5. Anything else: `UnknownName` (fails).

## KeyResolveResult

| Value | Meaning |
|-------|---------|
| `Ok` | A key code was found |
| `Empty` | The text is empty |
| `UnknownName` | The text is not a listed name and not a whole number |
| `OutOfRange` | The text is a whole number outside 0 to 300 |

## Key list additions

| Name | Key code |
|------|----------|
| `0` to `9` | 7 to 16 |
| `NUMPAD_0` to `NUMPAD_9` | 144 to 153 |

All old names keep their key codes.

## Step outcome (key input step)

| Case | Status | Reason code |
|------|--------|-------------|
| Device accepted the key | `executed` | none |
| No key accepted, unknown or empty key text | `not_accepted` | `key_input_unknown_key` |
| No key accepted, number outside 0 to 300 | `not_accepted` | `key_input_code_out_of_range` |
| No key accepted, key text is valid | `not_accepted` | `key_input_not_accepted` |
| Step has no key config | `skipped_invalid_config` | `key_input_missing_config` (as before) |
