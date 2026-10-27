# Quickstart: Digit Keys and Raw Key Codes

## Send the digit 6 from a command

1. Create or edit a command. Add a key input step.
2. Set the key to `6`.
3. Run the command on a session with a device.
4. Check that the device gets key code 13. The step outcome is `executed`.

API form:

```json
{
  "name": "Press 6",
  "steps": [ { "type": "KeyInput", "order": 0, "keyInput": { "key": "6" } } ]
}
```

## Send a raw key code

Set the key to a whole number from 0 to 300. For example, `85` sends the media play and pause key.

## Check the failure cases

1. Set the key to `301`. Run the step. The outcome is `not_accepted` with the reason `key_input_code_out_of_range`.
2. Set the key to `6a`. Run the step. The outcome is `not_accepted` with the reason `key_input_unknown_key`.
3. Send `{"type":"key","args":{"key":"6a"}}` to the session input route. The result has the reason `key: unknown key name '6a'`.

## Check the Swagger docs

1. Start the service. Open the Swagger UI.
2. Find the schema `KeyInputConfigDto`. Read the description of `key`.
3. Check that the text names the digits, the number pad names, and the raw key code rule. Check the two examples.

## Run the tests

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~KeyInput|FullyQualifiedName~AndroidKeyCodes"
```
