# Implementation Plan: Detect does not report a time-limited measurement as an absence

**Branch**: `master-y1x20e` (spec number 108) | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/108-detect-timeout-alternates/spec.md`

## Summary

`POST /api/images/detect` returns a `200` with an empty `matches` array and `limitsHit: true` when its time limit (`Service:Detections:TimeoutMs`, 500 ms) expires. For an image with alternates, the service scores each reference one after the other in that one limit, so the limit expires in some calls (research R-001, R-002).

The fix has two parts:

1. The time limit of a call is `TimeoutMs` for each reference that the service scores (research R-003). An image without alternates keeps the same limit.
2. When the limit expires, the service returns `504` with code `detection_timeout`, never a `200` (research R-004). This applies to all images.

The OpenAPI description, the README, `docs/architecture.md` and the changelog describe the new failure.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, OpenCvSharp, Swashbuckle, xUnit, FluentAssertions  
**Storage**: N/A. No storage change.  
**Testing**: xUnit unit tests (`tests/unit`), integration tests with `WebApplicationFactory<Program>` (`tests/integration`), OpenAPI contract tests (`tests/contract`)  
**Target Platform**: Windows service (CI on `windows-latest`)  
**Project Type**: Web service with a web UI. This fix touches only the service.  
**Performance Goals**: No change to matcher speed. A call for an image with N alternates can take up to (N+1) × `TimeoutMs` before it fails.  
**Constraints**: No change to matcher scores, to images without alternates (except the contract fix), to `detect-all`, to sequence conditions, to taps or to the retry policy (spec FR-004, FR-008).  
**Scale/Scope**: 4 source files, 3 new test files, 1 changed test file, `README.md`, `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in existing style. Method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | New unit, integration and contract tests before the fix (tasks in phase 2). |
| UX / API consistency | Pass | The new failure uses the `{ code, message }` shape of the other detect failures, and a `504` as `device_timeout` does. |
| Performance | Pass | The matcher does not change. Only the time limit changes for images with alternates. |
| Living docs | Pass | `docs/architecture.md` gets the new failure and the time limit rule, with a new "Last reviewed" date. `README.md`, `CHANGELOG.md` and `specs/STATUS.md` change too. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/108-detect-timeout-alternates/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── detect-timeout.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs
    # new: DetectionTimeLimit(int timeoutMs, int referenceCount) -> TimeSpan
src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs
    # DetectAsync: time limit from DetectionTimeLimit; on expiry return 504 detection_timeout
src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.Logging.cs
    # new: LogDetectTimeLimitExpired (EventId 11007, Warning)
src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs
    # XML doc for DetectResponse.LimitsHit (schema description)
src/GameBot.Service/Swagger/SwaggerConfig.cs
    # detect description text; 504 example in SetImageDetectErrorExamples

tests/unit/Images/DetectionTimeLimitTests.cs                      # new
tests/integration/DetectTimeLimitIntegrationTests.cs              # new
tests/integration/ImageDetectionsStressTests.cs                   # remove TimeoutReturnsOkWithLimitsHit
tests/contract/Images/DetectTimeLimitOpenApiTests.cs              # new

README.md
docs/architecture.md
CHANGELOG.md
specs/STATUS.md
```

**Structure Decision**: Use the existing projects. All code changes are in `GameBot.Service`. Tests go in the existing unit, integration and contract test projects.

## Complexity Tracking

No violations.
