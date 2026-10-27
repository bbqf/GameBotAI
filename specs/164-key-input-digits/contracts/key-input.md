# Contract: Key Text of the Key Input Step and the Key Action

**Feature**: 164-key-input-digits
**Endpoints**: `/api/commands` (POST, PATCH, GET), the session input route, the primitive action `key` payload

## Request shape

No field is new. The `key` text accepts more values.

```json
{ "type": "KeyInput", "order": 0, "keyInput": { "key": "6" } }
```

```json
{ "type": "KeyInput", "order": 1, "keyInput": { "key": "85" } }
```

Session input route (the `key` argument; the `keyCode` argument does not change):

```json
{ "actions": [ { "type": "key", "args": { "key": "NUMPAD_6" } } ] }
```

## Accepted key text

- A listed name, without case, after trim: `HOME`, `BACK`, `ESCAPE`, `MENU`, `ENTER`, `SPACE`, `TAB`, `DEL`, `DELETE`, `UP`, `DOWN`, `LEFT`, `RIGHT`, `VOLUME_UP`, `VOLUME_DOWN`, `POWER`, `A` to `Z`.
- New: `0` to `9`.
- New: `NUMPAD_0` to `NUMPAD_9`.
- New: a whole number from 0 to 300 as a raw Android key code.
- A listed name has priority. `6` sends the digit key 6 (key code 13), not key code 6.

## Validation

| Time | Rule |
|------|------|
| Save (POST, PATCH) | `keyInput.key` is required and not empty. No change. |
| Run | The key text must resolve. The service checks this at run time, as before. |

## Failure reasons

Session input route (the `reason` of the failed action result):

| Case | Reason |
|------|--------|
| Unknown text | `key: unknown key name '<text>'` |
| Number outside 0 to 300 | `key: key code <n> is outside 0 to 300` |
| Empty `key` text | `key: key name is empty` |
| No `key` and no `keyCode` | `key: missing required argument 'keyCode' or 'key'` (as before) |

Command step outcome: see the table in `data-model.md`.

## Swagger requirements

- `KeyInputConfig.key` has a description with the key names, the digit names, the number pad names, the raw key code rule, and the priority rule.
- The description has one digit example (`6`) and one raw key code example (`85`).
- The `key` payload text of the primitive action and the help text of the key action definition point to the same rule.

## Compatibility

- Old key names and saved commands keep their behavior when the device accepts the key.
- The `key` action of a sequence step accepts the same key text.
- One change in an outcome: a key step that the device does not accept now has the status `not_accepted`. Before, it had `executed`.
