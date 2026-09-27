# Quickstart: Detect does not report a time-limited measurement as an absence

## Run the tests

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~DetectionTimeLimit"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~DetectTimeLimit"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~DetectTimeLimitOpenApi"
```

## Check by hand

1. Upload an image and two alternates, and set the alternates with `PUT /api/images/{id}/alternates`.
2. Take a capture with `POST /api/emulator/screenshot` (or use a `captureId` that you have).
3. Send `POST /api/images/detect` with `threshold: 0.05`, `maxResults: 2` and the `captureId` 40 times.
4. Each response is a `200` with the best match, or, when the service is very slow, a `504` with `code: detection_timeout`. No response is a `200` with an empty `matches` array and `limitsHit: true`.
5. Set `Service__Detections__TimeoutMs` to `1` and restart the service. Send the same request. The response is a `504` with `code: detection_timeout`.
