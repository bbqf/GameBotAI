# Tasks: OCR Read Endpoint

**Input**: Design documents in `C:\src\GameBot\specs\128-ocr-read-endpoint\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts\ocr-read-api.md, quickstart.md

**Tests**: Tests are in scope. The plan and spec list unit tests and contract tests.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`. [P] means the task can run in parallel. All paths are from `C:\src\GameBot`.

## Rules for this task list

- The first service task (T010) holds ALL request validation. Later tasks add no validation that the MVP needs.
- The region-inside-frame check is one shared step. Both sources call it.
- `Program.cs` gets one line only: `app.MapOcrReadEndpoints();` next to `app.MapCoverageEndpoints();`.
- Do not use `GameBotServiceSetup.cs` for endpoint mapping. Use it for service registration only.
- The version file `installer\versioning\version.override.json` is already edited (minor 5). Task T033 only verifies it.
- Do not change GitHub issue #274.

## Phase 1: Setup

- [ ] T001 Read `specs\128-ocr-read-endpoint\contracts\ocr-read-api.md` and `data-model.md`. Note the field names, the error codes, and the status codes.
- [ ] T002 [P] Read `src\GameBot.Service\Services\SequenceExecution\OcrOffsetResolver.cs`. Note the crop code, the fallback logic, and the log line. These must not change.
- [ ] T003 [P] Read `src\GameBot.Service\Swagger\ImageDetectCoordinatesSchemaFilter.cs` and the `app.MapCoverageEndpoints()` call in `src\GameBot.Service\Program.cs`. Use them as patterns.

## Phase 2: Foundational (blocks all user stories)

- [ ] T004 [P] Create the request, region, response, parsed value, and error DTOs in `src\GameBot.Service\Models\OcrReadModels.cs`. Use the field names from the contract.
- [ ] T005 [P] Create the parser registry in `src\GameBot.Service\Services\Ocr\OcrTextParsers.cs`. It has one name, `hh:mm:ss`, compared without case. It maps to `CooldownDurationParser.TryParse`. It gives the parsed value as `hh:mm:ss` text and as total seconds.
- [ ] T006 Create the internal class `OcrRegionReader` in `src\GameBot.Service\Services\Ocr\OcrRegionReader.cs`. Move the crop code and the engine call from `OcrOffsetResolver`. `Read(frame, region, ocr)` crops the region, then calls `ITextOcr.Recognize`. It does not throw for a null crop. It returns a result that says the crop failed.
- [ ] T007 Change `src\GameBot.Service\Services\SequenceExecution\OcrOffsetResolver.cs` to call `OcrRegionReader`. Map a failed crop to the `region-invalid` fallback as before. The behaviour, the fallback logic, and the log line do not change.

**Checkpoint**: The shared reader exists. The step still works as before.

## Phase 3: User Story 1 - Read a region of the live screen (P1) MVP

**Goal**: A request with a serial and a region gives raw text and a confidence value. The emulator gets no input.

**Independent test**: Send a request with a serial and a region. The answer has raw text and confidence. A recording session manager sees zero input.

### Tests for User Story 1

- [ ] T008 [P] [US1] Write unit tests in `tests\unit\Ocr\OcrRegionReaderTests.cs`. Cover: a valid crop and read, a null crop (the result says the crop failed), and parity of the raw text with the `ocrOffset` step for the same frame and region.
- [ ] T009 [P] [US1] Write unit tests in `tests\unit\Ocr\OcrReadServiceTests.cs` for the serial source. Cover: a valid read, the first running session wins when two sessions have the same serial, a region that ends exactly at the frame edge (valid), a region one pixel beyond the frame edge (invalid, 400 `invalid_region`, the message gives the frame size), a missing capture service (503 `capture_unavailable`), a failed capture (502 `capture_failed`), a missing engine (503 `ocr_unavailable`), an engine that throws (503 `ocr_unavailable`), and a null crop (502 `capture_failed`).

### Implementation for User Story 1

- [ ] T010 [US1] Create `OcrReadService` in `src\GameBot.Service\Services\Ocr\OcrReadService.cs`. This is the MVP task and it holds ALL request validation. Step 1 (no lookup, 400): the body is valid, exactly one source, the `region` object is present (400 `invalid_request`), the parser name is known, the region width and height are above zero. Step 2: look up the first running session with the serial (404 `serial_not_found`). Step 3: capture the frame through `ISessionFrameSource.Capture(sessionId)` (502 `capture_failed`, or 503 `capture_unavailable` when no capture service exists). Step 4: check the engine (503 `ocr_unavailable`). Step 5: one shared private method for the region-inside-frame check, which uses the frame size and gives 400 `invalid_region` with the frame size in the message. Both sources call this method. Step 6: call `OcrRegionReader` (null crop gives 502 `capture_failed`, an engine that throws gives 503 `ocr_unavailable`). The code for the capture-id source is added in T017, and it calls the same shared check from step 5. Do not run a cache.
- [ ] T011 [US1] Create `src\GameBot.Service\Endpoints\OcrReadEndpoints.cs` with `MapOcrReadEndpoints`. Map `POST /api/ocr/read` with `WithName("ReadOcrRegion")`. Map the service result to the status codes and the `{ code, message }` error body. Never return 500 for an input error or a host fault.
- [ ] T012 [US1] Register `OcrReadService` in `src\GameBot.Service\GameBotServiceSetup.cs` (service registration only). Add the single line `app.MapOcrReadEndpoints();` in `src\GameBot.Service\Program.cs` next to `app.MapCoverageEndpoints();`. Add no other code to `Program.cs`.
- [ ] T013 [P] [US1] Write contract tests in `tests\contract\Ocr\OcrReadContractTests.cs` for the serial source: a valid read gives 200 with raw text and confidence, an empty region gives empty text with no error, and a region that has the frame size is valid.
- [ ] T014 [P] [US1] Write contract tests in `tests\contract\Ocr\OcrReadNoInputTests.cs`. Use a recording session manager. A read request must cause zero input (no tap, swipe, or key).

**Checkpoint**: User Story 1 works alone.

## Phase 4: User Story 2 - Read a region of a stored capture (P1)

**Goal**: A request with a capture id reads the stored picture. No new capture is taken.

**Independent test**: Send the same capture id and region two times. Both answers have the same raw text. No new capture appears.

- [ ] T015 [P] [US2] Add unit tests to `tests\unit\Ocr\OcrReadServiceTests.cs` for the capture-id source. Cover: a valid read with no new capture, an unknown capture id (404), a PNG that fails to decode (502 `capture_failed`), a region that ends exactly at the decoded picture edge (valid), and a region one pixel beyond the decoded picture edge (invalid, 400 `invalid_region`).
- [ ] T016 [P] [US2] Add contract tests to `tests\contract\Ocr\OcrReadContractTests.cs`: two reads of one capture id give the same raw text, and an unknown capture id gives 404.
- [ ] T017 [US2] Add the capture-id source to `src\GameBot.Service\Services\Ocr\OcrReadService.cs`. Use `CaptureSessionStore.TryGet`. Decode the PNG (a decode failure gives 502 `capture_failed`). Use the size of the decoded picture as the frame size. Call the same shared region-inside-frame check as the serial source. Add no new validation rule.
- [ ] T018 [US2] Add a contract test to `tests\contract\Ocr\OcrReadNoInputTests.cs`: a capture-id read takes no new capture and sends no input.

**Checkpoint**: Both sources work.

## Phase 5: User Story 3 - Test a parser on the read text (P2)

**Goal**: A request with a parser name gives the parsed value, or the raw text and a reason.

**Independent test**: A clean countdown gives a parsed value. A bad read gives status 200 with raw text, no parsed value, and a reason.

- [ ] T019 [P] [US3] Write unit tests in `tests\unit\Ocr\OcrTextParsersTests.cs`. Cover: `02:10:35` parses, `HH:MM:SS` in a different letter case is accepted as a name, `8 ODeIOs35` does not parse, and an unknown name is not found.
- [ ] T020 [P] [US3] Add unit tests to `tests\unit\Ocr\OcrReadServiceTests.cs`: a parse success gives the parsed value, hh:mm:ss text, total seconds, and the parser name. A parse failure gives raw text, no parsed value, and a reason.
- [ ] T021 [P] [US3] Add contract tests to `tests\contract\Ocr\OcrReadContractTests.cs`: a parse success gives 200 with the parsed value. A parse failure gives 200 with raw text and a reason.
- [ ] T022 [US3] Add the parse step (step 7) to `src\GameBot.Service\Services\Ocr\OcrReadService.cs`. Use `OcrTextParsers`. A parse failure is 200 with a reason. Add no validation (the parser-name check is already in T010).

**Checkpoint**: The parser path works.

## Phase 6: User Story 4 - Clear errors for bad requests (P2)

**Goal**: Each bad request gives the correct status code and a clear message. The check order is fixed.

**Independent test**: Send each bad request. Check the status code and a non-empty message. No answer is 500.

- [ ] T023 [P] [US4] Add contract tests to `tests\contract\Ocr\OcrReadContractTests.cs` for 400: no source, two sources, an unknown parser name (the message names the supported parsers), a width or height of zero or below, a body that is missing or not valid JSON, and a region that is not inside the frame.
- [ ] T024 [P] [US4] Add a contract test to `tests\contract\Ocr\OcrReadContractTests.cs` for a missing `region` object. The answer is 400 `invalid_request`.
- [ ] T025 [P] [US4] Add a contract test to `tests\contract\Ocr\OcrReadContractTests.cs` for 404: an unknown serial (`serial_not_found`) and an unknown capture id.
- [ ] T026 [P] [US4] Add a mixed-fault contract test to `tests\contract\Ocr\OcrReadContractTests.cs`. Send one request with many faults. Check that the first fault in the check order wins. Use these cases: a no-lookup fault together with an unknown serial gives 400, an unknown serial together with a region outside the frame gives 404, and an engine that is not available together with a region outside the frame gives 503.
- [ ] T027 [P] [US4] Add contract tests to `tests\contract\Ocr\OcrReadContractTests.cs` for the host faults: a failed capture gives 502 `capture_failed`, a missing engine gives 503 `ocr_unavailable`, and no answer is 500.
- [ ] T028 [US4] Run the tests of T023 to T027. If a test fails, fix the check order or the status mapping in `src\GameBot.Service\Services\Ocr\OcrReadService.cs` or `src\GameBot.Service\Endpoints\OcrReadEndpoints.cs`. Do not add a new validation rule that the MVP (T010) needs.

**Checkpoint**: All error cases pass.

## Phase 7: OpenAPI

- [ ] T029 [P] Create `OcrReadSchemaFilter` in `src\GameBot.Service\Swagger\OcrReadSchemaFilter.cs`. It is an `ISchemaFilter`. Use the pattern of `ImageDetectCoordinatesSchemaFilter`. Add all descriptions in the filter (the project has no XML comments in Swagger). Register it in `src\GameBot.Service\GameBotServiceSetup.cs`.
- [ ] T030 [P] Write contract tests in `tests\contract\Ocr\OcrReadOpenApiTests.cs`. Check that `swagger.json` has the path `/api/ocr/read`, the request and response schemas, and the descriptions.

## Phase 8: Polish and documentation

- [ ] T031 [P] Name the endpoint in `README.md` and in `docs\architecture.md`. Refresh the "Last reviewed" line in `docs\architecture.md`.
- [ ] T032 [P] Add a changelog entry for `POST /api/ocr/read` in `CHANGELOG.md`.
- [ ] T033 Verify the version file `installer\versioning\version.override.json`. It is already edited. Check that `minor` is `5`, `patch` is `0`, and `updatedAtUtc` is set. Do not edit the file unless a value is wrong.
- [ ] T034 Run the build and the tests: `dotnet build` and `dotnet test` for the solution. Fix each failure. Check that `OcrOffsetResolver` tests still pass without a change to the step behaviour or the log line.
- [ ] T035 Run the steps in `specs\128-ocr-read-endpoint\quickstart.md` against the local service, if a service is available.
- [ ] T036 Last task. In the same change, update the row for feature 128 in `specs\STATUS.md` and the `Status` line in `specs\128-ocr-read-endpoint\spec.md` (from Draft to the new status). Both changes use the same status value.

## Dependencies and order

- Phase 1 has no dependency. Phase 2 follows Phase 1 and blocks all stories.
- T007 depends on T006. T010 depends on T004, T005, and T006.
- US1 (Phase 3) is the MVP. US2 (Phase 4) depends on T010. US3 (Phase 5) depends on T010. US4 (Phase 6) depends on T010, T011, T017, and T022.
- T011 depends on T010. T012 depends on T011. T013 and T014 depend on T012.
- Phase 7 depends on T011. Phase 8 follows all other phases. T036 is last.

## Parallel examples

- Phase 2: T004 and T005 run in parallel.
- US1 tests: T008 and T009 run in parallel. T013 and T014 run in parallel after T012.
- US4 tests: T023 to T027 touch one file, so write them in one pass, or in sequence.

## Implementation strategy

1. MVP: complete Phases 1, 2, and 3. The first service task (T010) validates every request.
2. Add US2, then US3, then US4. Test each story alone.
3. Add OpenAPI (Phase 7) and documentation (Phase 8).
4. Finish with T036.

## Summary

- Total tasks: 36
- Per phase: Setup 3, Foundational 4, US1 7, US2 4, US3 4, US4 6, OpenAPI 2, Polish 6
