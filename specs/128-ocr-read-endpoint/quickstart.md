# Quickstart: OCR Read Endpoint

## Read a region of the live screen

1. Start a session for the emulator (for example serial `emulator-5558`).
2. Send the request:

   ```powershell
   Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/ocr/read" -ContentType "application/json" `
     -Body '{"serial":"emulator-5558","region":{"x":120,"y":640,"width":300,"height":60},"parser":"hh:mm:ss"}'
   ```

3. Read `text`, `confidence`, and `parsed` in the answer.

## Repeat a read on one picture

1. Take a capture with `GET /api/emulator/screenshot?serial=emulator-5558`. Note the `captureId` in the answer headers or body.
2. Send the same request two times with `captureId` in place of `serial`.
3. Compare the two `text` values. A different text means the engine is not stable. The same text means the picture
   decides the result.

## Check the errors

| Request | Expected |
|---------|----------|
| No `serial` and no `captureId` | 400 `invalid_request` |
| Both set | 400 `invalid_request` |
| No `region` | 400 `invalid_request` |
| Many faults in one request (for example a bad parser and an unknown serial) | The first fault in the check order: 400 `unknown_parser` |
| `parser` is `abc` | 400 `unknown_parser` |
| `width` is 0 | 400 `invalid_region` |
| Region past the frame edge (one pixel is enough) | 400 `invalid_region` with the frame size |
| Unknown `captureId` | 404 `capture_not_found` |
| `serial` with no session | 404 `serial_not_found` |

## Check the OpenAPI document

Open `/swagger` and find `POST /api/ocr/read` under the Emulators tag.

## Run the tests

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Ocr"
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~Ocr"
```
