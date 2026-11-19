# Quickstart: TextInput step types all characters

Feature: `195-fix-text-input-chars` | Spec: [spec.md](./spec.md)

## 1. Automated tests

Run the unit tests of this fix:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~TextInput|FullyQualifiedName~AdbTextEncoder|FullyQualifiedName~ConfigApplier"
```

Expected: all tests pass. The tests of `SessionManagerTextInputTests` show the call order `text:2`, `pause:100`, `text:5`, `pause:100`, `text:0`.

Run the full build and test suite before the commit:

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\GameBot.sln" -c Debug
```

## 2. Manual check on a device (the procedure of issue #404)

1. Start the service with a device (`GAMEBOT_USE_ADB=true`). Keep the default `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` (100).
2. Make a command with one step: `TextInput` `{ "text": "250" }`.
3. In the game, open the number input field so that it has the focus.
4. Do these steps 10 times:
   1. Clear the field (5 x `KeyInput` `DEL`).
   2. Run the command.
   3. Read the field with `POST /api/ocr/read`.
5. Expected: each try shows `250`. Each step result is `executed` with `typedLength` 3.

## 3. Change the pause

1. Set `GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS` to `200` on the configuration page (or in the environment).
2. Run the command again. Expected: the characters arrive more slowly. No restart is necessary.
3. Set the value to `5000`. Expected: the configuration snapshot keeps `5000`, but the service uses 2000.
