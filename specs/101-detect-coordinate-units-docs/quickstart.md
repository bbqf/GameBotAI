# Quickstart: verify the coordinate-unit documentation

1. Run the contract test:

   ```powershell
   dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~ImageDetectCoordinateUnitsOpenApiTests"
   ```

2. Run the existing detection and OpenAPI contract tests to confirm nothing else moved:

   ```powershell
   dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~Images|FullyQualifiedName~ImageDetections|FullyQualifiedName~OpenApi|FullyQualifiedName~SwaggerDocsTests"
   ```

3. Optional manual check against a running service: open `/swagger`, expand `POST /api/images/detect` and
   `POST /api/images/detect-all`, and read the operation text and the `x` field of each response schema — detect says
   fraction of the frame, detect-all says pixels.

4. Converting in client code: `pixelX = detectMatch.x * captureWidth`, `pixelY = detectMatch.y * captureHeight`
   (same for `width` / `height`). detect-all values are already pixels.
