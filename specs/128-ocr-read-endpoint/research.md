# Research: OCR Read Endpoint

## R1. How to share code with the `ocrOffset` step

- **Decision**: Move the private `CropRegion` method of `OcrOffsetResolver` into a new internal class,
  `OcrRegionReader`. Add `Read(frame, region, ocr)`. It crops, then calls `ITextOcr.Recognize`. The resolver
  and the endpoint service both call it. The shared pre-processing is the crop, then the read. The step has
  no separate scale step.
- **Rationale**: The issue says "do not copy" (FR-004). A move keeps one copy. The resolver keeps its fallback
  logic and log reasons, so FR-017 holds. A parity unit test shows that both paths give the same raw text for
  the same frame and region.
- **Null crop**: `Read` does not throw when the crop is null. It returns a result that says the crop failed.
  The resolver maps it to the `region-invalid` fallback. The endpoint maps it to 502 `capture_failed`. A unit test
  covers this case.
- **Alternatives considered**: (a) Call `OcrOffsetResolver.Resolve` from the endpoint. Rejected: it hides the raw
  text on some paths and it hides the confidence. (b) Copy the crop code. Rejected by the issue.

## R2. Frame source for a `serial`

- **Decision**: Find the first running session whose `DeviceSerial` equals the `serial`. Call
  `ISessionFrameSource.Capture(sessionId)`. This reads the background capture cache.
- **Rationale**: `ocrOffset` reads the same cache, so the frames match (FR-005). A new `adb screencap` from the
  endpoint adds a second screencap process on the device. The capture gate (feature 121) forbids more than one
  unfinished capture for each device. The endpoint keeps no result cache (FR-011). It reads the newest frame of
  the capture loop each time.
- **Many sessions**: When more than one running session has the same serial, the endpoint uses the first match.
  A unit test covers this case.
- **Alternatives considered**: Call `AdbClient.GetScreenshotPngAsync`. Rejected for the gate reason. Use the
  `IScreenSource` ambient context. Rejected: it is blind when many sessions run.

## R3. Unknown serial and capture id

- **Decision**: A serial with no running session gives 404 `serial_not_found`. An unknown or expired capture id gives
  404 `capture_not_found`. A session with no frame gives 502 `capture_failed`.
- **Rationale**: This matches the 404 convention of `/api/images/detect`. A capture loop exists only for a
  session. Capture ids are the ids in `CaptureSessionStore`.
- **Alternatives considered**: 503 for a missing session. Rejected: the cause is a bad input.

## R4. Check order and region rule

- **Decision**: The order is: (1) checks with no lookup, 400; (2) lookup, 404; (3) capture or decode, 502;
  (4) engine availability, 503; (5) region-inside-frame, 400; (6) crop and read. See the plan for the full list.
- **Region rule**: Valid only when `width > 0`, `height > 0`, `x >= 0`, `y >= 0`, `x + width <= frameWidth`, and
  `y + height <= frameHeight`. Use `long` math. The size check (above zero) is in step 1. The inside-frame check is
  one shared step for both sources, and it uses the frame size. For a stored capture the frame size is the size of
  the decoded picture. The 400 message gives the frame size.
- **Missing `region`**: 400 `invalid_request`, in step 1.
- **Rationale**: The frame size is not known before the capture, so the inside-frame check must be last. The spec
  clarifications fix this order. A region that ends at the frame edge is valid. One pixel beyond is invalid. Unit
  tests cover both edges for both sources.
- **Alternatives considered**: Clamp as the step does. Rejected: it hides a bad region. Check the region before
  the lookup. Rejected: it needs the frame size.

## R5. Parser names

- **Decision**: A small registry, `OcrTextParsers`, with one name `hh:mm:ss` that maps to
  `CooldownDurationParser.TryParse`. Names compare without case. A parsed answer gives `parser`, `value`
  (`hh:mm:ss` text), and `totalSeconds`. A failed parse gives `parsed: null` and `parseFailureReason`.
- **Rationale**: The `ocrOffset` step has no parser name. It always uses the duration parser. The registry gives the
  endpoint a stable name and one place to add more.
- **Alternatives considered**: Accept any name. Rejected: the spec needs 400 for an unknown name.

## R6. Failure mapping

- **Decision**:
  - Missing or invalid body, bad source count, no region, unknown parser, bad size: 400.
  - Unknown serial or capture id: 404.
  - Null frame, null crop (GDI error), or stored PNG decode failure: 502 `capture_failed`.
  - No `ISessionFrameSource` on the host: 503 `capture_unavailable`.
  - No `ITextOcr` (also a host that is not Windows), or an engine that throws: 503 `ocr_unavailable`.
- **Rationale**: Spec FR-009 and FR-010. Optional service lookups follow `ImageDetectionsEndpoints.ResolveFrame`.
  Unit tests cover the null crop, the decode failure, and the missing or throwing engine.

## R7. Endpoint mapping site

- **Decision**: Put the route code in `Endpoints\OcrReadEndpoints.cs` with `MapOcrReadEndpoints`. Add one line to
  `Program.cs` next to `app.MapCoverageEndpoints()`. Use the `ApiRoutes.Ocr` group and the "Emulators" tag, as the
  coverage endpoint does.
- **Rationale**: `Program.cs` must stay thin. Taint analyzers fail on large methods. The coverage endpoint is the
  existing pattern for `/api/ocr/*`. The plan does not use `GameBotServiceSetup.cs` for mapping.
  That file only registers services and the schema filter.

## R8. OpenAPI method

- **Decision**: Typed DTOs, `.WithName("ReadOcrRegion")`, `.Produces<T>`, and an `OcrReadSchemaFilter`
  (`ISchemaFilter`). The filter sets all property descriptions.
- **Rationale**: Same pattern as `ImageDetectCoordinatesSchemaFilter`. The project has no XML comments in Swagger.
  `OcrReadOpenApiTests` reads `/swagger/v1/swagger.json` and checks the path, the schemas, and the descriptions.

## R9. Documentation and version

- **Decision**: Name the endpoint in `README.md` and in the API surface of `docs/architecture.md` (refresh "Last
  reviewed"). Add a changelog entry. Set minor `5` and a new `updatedAtUtc` in
  `installer\versioning\version.override.json`. The patch stays 0. In the last task, update the `specs\STATUS.md` row
  and the `Status` field of the spec in the same task.
- **Rationale**: Principle V, FR-014 to FR-016, and the spec Assumption D2.

## R10. No-input proof

- **Decision**: Two tests. (1) A contract test uses a recording session manager and a fake frame source. After
  success and error requests, the recorded input count is 0. The fake throws on any send. (2) A unit test checks
  that `OcrReadService` takes no dependency that can send input.
- **Rationale**: A fake that throws on input turns the guarantee into a hard failure.

## R11. Task order

- **Decision**: The first service task (the MVP) includes all request validation: exactly one source, `region`
  present, region size above zero, parser name, and the shared region-inside-frame check. Later tasks do not add
  validation that the MVP needs.
- **Rationale**: Spec Assumption T1.
