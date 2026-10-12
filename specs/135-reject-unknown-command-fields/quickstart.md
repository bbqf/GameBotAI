# Quickstart: Reject unknown command step fields

1. Build and run the service.
2. Send an invalid create request:

   ```powershell
   $body = '{"name":"t","steps":[{"type":"PrimitiveTap","order":0,"primitiveTap":{"detectionTarget":{"referenceImageId":"x"},"bogusField":1}}]}'
   Invoke-WebRequest -Method Post -Uri http://localhost:8080/api/commands -ContentType application/json -Body $body -SkipHttpErrorCheck
   ```

   Expect status 400 and a message with `steps[0].primitiveTap.bogusField`.
3. Send the same body without `bogusField`. Expect 201.
4. Send a PATCH with an unknown step field. Expect 400, and a GET shows the command unchanged.
5. Run the tests:

   ```powershell
   dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~UnknownStepField|FullyQualifiedName~UnknownFieldScanner|FullyQualifiedName~Commands"
   ```
