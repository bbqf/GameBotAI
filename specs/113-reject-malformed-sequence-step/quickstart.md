# Quickstart: Reject a malformed sequence step on create

## Build and test

```bash
dotnet build GameBot.sln
DOTNET_ROLL_FORWARD=Major dotnet test tests/integration --filter "FullyQualifiedName~SequenceCreateMalformedStep"
DOTNET_ROLL_FORWARD=Major dotnet test tests/contract --filter "FullyQualifiedName~SequenceCreateMalformedStep"
```

On Windows, `DOTNET_ROLL_FORWARD` is not necessary when the .NET 9 runtime is installed.

## Manual check

1. Start the service. Create a command, and record its id.
2. Send the reproduction body to `POST /api/sequences`. Make sure that the response is 400 and that the error has `steps[0] (stepId 'a')`.
3. Send the same body with `"dryRun": true`. Make sure that the response is 400.
4. Send `GET /api/sequences`. Make sure that no sequence has the name `ZZZ.DropProbe`.
5. Change step `a` to `{"stepId":"a","stepType":"Action","primitiveAction":{"type":"command","schemaVersion":"v1","payload":{"commandId":"<id>"}}}` and send the body again. Make sure that the response is 201 and that `GET /api/sequences/{id}` shows two steps.
