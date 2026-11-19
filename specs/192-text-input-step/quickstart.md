# Quickstart: TextInput Command Step

**Feature**: 192-text-input-step

## 1. Build and run the tests

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~TextInput|FullyQualifiedName~CommandStepResolver|FullyQualifiedName~ParameterReferenceScanner|FullyQualifiedName~FieldTemplatePathMirror|FullyQualifiedName~PreviewEffectRules"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~TextInput"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~TextInput"
npm --prefix "C:\src\GameBot\src\web-ui" test -- TextInputPanel fieldTemplatePaths ActionTypeSelector
npm --prefix "C:\src\GameBot\src\web-ui" run lint
```

## 2. Check the API by hand

1. Start the service. Start a session with a device.
2. Make a command with a parameter and a `TextInput` step. Use `dryRun` first:

   ```json
   POST /api/commands
   {
     "name": "Type batch",
     "dryRun": true,
     "parameters": [ { "name": "fighterBatch", "type": "number", "default": "250" } ],
     "steps": [
       { "type": "TextInput", "order": 0, "textInput": { "text": "" },
         "fieldTemplates": { "textInput.text": "{{fighterBatch}}" } }
     ]
   }
   ```

   Expected: HTTP 200 `{ "valid": true, "dryRun": true, "errors": [] }`. No command is stored.

3. Change the placeholder to `{{fighterBach}}` and send again. Expected: HTTP 400 with the code `unresolvable_parameter_reference` and the field path `textInput.text`.
4. Remove `dryRun` and send the correct request. Expected: HTTP 201.
5. On the device, tap an input field so that it has the focus. Run the command with `POST /api/commands/{id}/force-execute?sessionId=<id>`. Expected: the field shows `250`. The step outcome has `"status": "executed"`, `"stepType": "text-input"`, and `"typedLength": 3`.
6. Run a command whose text is `"é"` through an inline placeholder value. Expected: `"status": "not_accepted"` and `"reason": "text_input_unsupported_character"`.

## 3. Check the web UI

1. Open the command editor. Add a step. Select "Text Input".
2. Type `250`. Select "Add". Save the command. Open it again. Expected: the step shows `Text: 250`.
3. Edit the step. Use "Use a parameter" for the text and select a parameter. Save. Expected: the saved command has `fieldTemplates` `{"textInput.text": "{{<name>}}"}`.
