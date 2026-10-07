# Implementation Plan: OCR Read Endpoint

**Branch**: `128-ocr-read-endpoint` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/128-ocr-read-endpoint/spec.md`

## Summary

Add `POST /api/ocr/read`. The endpoint takes one source (a `serial` or a `captureId`), a region, and an optional
parser name. It crops the region, runs the OCR engine, and returns the raw text, the confidence value, and an
optional parsed duration. The endpoint sends no input to the emulator.

The endpoint and the `ocrOffset` step share one code path. The private crop code of `OcrOffsetResolver` moves to
a new internal class, `OcrRegionReader`. The resolver and the endpoint service both call it. The behaviour, the
fallback logic, and the log line of the step do not change.

The endpoint code is in `OcrReadEndpoints.cs` with a `MapOcrReadEndpoints` method. `Program.cs` stays thin. It
gets one call line next to `app.MapCoverageEndpoints()`.

## Technical Context

**Language/Version**: C# on .NET 9 (same as the repository)
**Primary Dependencies**: ASP.NET Core minimal API, `System.Drawing` (Windows), Swashbuckle (`ISchemaFilter`), existing `ITextOcr`
**Storage**: N/A. The endpoint keeps no state and no cache.
**Testing**: xUnit. Unit tests in `tests\unit`. Contract tests in `tests\contract\Ocr`.
**Target Platform**: Windows service. A host that is not Windows answers 503 `ocr_unavailable`.
**Project Type**: Web service (backend only). No web UI change.
**Performance Goals**: Design target only: read a region in under 1 s at p95. No test checks it (spec Assumptions).
**Constraints**: No input to the emulator. No cache. No 500 for an input error or a host fault.
**Scale/Scope**: One endpoint, one service, one shared reader, one parser registry, one schema filter.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Result | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | Small classes. Each method is under 50 lines. `Program.cs` gets one line (the taint analyzers fail on large methods). Public types have comments. |
| II. Testing | Pass | Unit tests for the reader, the parsers, the service, and the region check. Contract tests for status codes, check order, no input, and OpenAPI. |
| III. UX consistency | Pass | Error body is `{ code, message }`. Messages say what to do. Status codes follow `/api/images/detect`. |
| IV. Performance | Pass | Goal is declared (design target only). The endpoint runs one crop and one engine read per request. |
| V. Living documentation | Pass | Update `README.md`, `docs/architecture.md` (refresh "Last reviewed"), the changelog, `specs/STATUS.md`, and the `Status` line of the spec in the last task. |
| VI. STE | Pass | All new text is STE. |

Post-design re-check: no violation. The Complexity Tracking table stays empty.

## Check order

The service runs the checks in this order. The first fault ends the request.

1. Checks that need no lookup: body is valid, exactly one source, `region` is present, parser name is known,
   region width and height are above zero. Fault: 400.
2. Lookup of the serial (first running session with that serial) or of the capture id. Fault: 404.
3. Capture of the frame, or decode of the stored PNG. Fault: 502 `capture_failed`. A host with no capture
   service gives 503 `capture_unavailable`.
4. Engine availability. Fault: 503 `ocr_unavailable`.
5. Region-inside-frame check. One shared step for both sources. It uses the frame size (for a stored capture:
   the size of the decoded picture). Fault: 400 `invalid_region`. The message gives the frame size.
6. Crop and engine read through `OcrRegionReader`. A null crop gives 502 `capture_failed`. An engine that throws
   gives 503 `ocr_unavailable`.
7. Parse (when a parser is named). A parse failure is 200 with a reason.

A contract test sends one request with many faults and checks the first fault wins.

## Project Structure

### Documentation (this feature)

```text
specs/128-ocr-read-endpoint/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ocr-read-api.md
└── tasks.md             # Made by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Program.cs                                   # Add: app.MapOcrReadEndpoints(); next to app.MapCoverageEndpoints();
├── Endpoints/
│   └── OcrReadEndpoints.cs                      # MapOcrReadEndpoints: route, status mapping, WithName("ReadOcrRegion")
├── Models/
│   └── OcrReadModels.cs                         # Request, region, response, parsed value, error DTOs
├── Services/
│   └── Ocr/
│       ├── OcrReadService.cs                    # Check order, source lookup, frame, region check, read, parse
│       ├── OcrRegionReader.cs                   # Shared crop + engine read (moved from OcrOffsetResolver)
│       └── OcrTextParsers.cs                    # Parser registry: "hh:mm:ss" (no case)
├── Services/SequenceExecution/
│   └── OcrOffsetResolver.cs                     # Calls OcrRegionReader. Behaviour and log line unchanged.
├── Swagger/
│   └── OcrReadSchemaFilter.cs                   # ISchemaFilter, same pattern as ImageDetectCoordinatesSchemaFilter
└── GameBotServiceSetup.cs                       # Register OcrReadService and the schema filter (service registration only)

tests/unit/Ocr/
├── OcrRegionReaderTests.cs                      # Crop, null crop, parity with the step
├── OcrTextParsersTests.cs
└── OcrReadServiceTests.cs                       # Check order, region edge (valid and one pixel beyond), both sources,
                                                 # decode failure, missing or throwing engine, first matching session
tests/contract/Ocr/
├── OcrReadContractTests.cs                      # Status codes, mixed-fault request, parse failure is 200
├── OcrReadNoInputTests.cs                       # Recording session manager: zero input
└── OcrReadOpenApiTests.cs                       # Path, schemas, descriptions in swagger.json

installer/versioning/version.override.json       # minor 4 -> 5, new updatedAtUtc, patch stays 0
README.md, docs/architecture.md, CHANGELOG        # Name the endpoint
specs/STATUS.md, specs/128-.../spec.md           # Status sync (last task)
```

**Structure Decision**: Backend-only change in `GameBot.Service`. New files sit beside the same kind of file
(`Endpoints`, `Models`, `Services\Ocr`, `Swagger`). `OcrRegionReader` is in the `Services\Ocr` namespace and is
`internal`. `OcrOffsetResolver` calls it from `Services\SequenceExecution`.

## Design notes

- **Shared reader**: `OcrRegionReader.Read(frame, region, ocr)` crops the region, then calls `ITextOcr.Recognize`.
  It does not throw for a null crop. It returns a result that says the crop failed. The resolver maps this result to the
  `region-invalid` fallback (as before). The endpoint maps it to 502. The step has no scale step, so the reader has none.
- **Serial lookup**: use the first running session whose `DeviceSerial` matches the serial. Then call
  `ISessionFrameSource.Capture(sessionId)`. This reads the same capture cache as the `ocrOffset` step.
- **Stored capture**: `CaptureSessionStore.TryGet`. Decode the PNG. A decode failure gives 502. The frame size
  is the size of the decoded picture.
- **Parser registry**: `OcrTextParsers` has one name, `hh:mm:ss`. It maps to `CooldownDurationParser.TryParse`.
- **No input**: the service has no dependency that sends input. A recording session manager proves zero input.
- **Version**: `version.override.json` changes to minor `5`, patch `0`, and a new `updatedAtUtc`.

## Complexity Tracking

No violation. No entry.
