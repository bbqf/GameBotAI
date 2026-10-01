# Implementation Plan: OCR Offset Day Part

**Branch**: `124-ocr-offset-day-part` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/124-ocr-offset-day-part/spec.md`

## Summary

The OCR duration parser of `reschedule-self` `ocrOffset` ignores a day part such as "1d 23:29:10". The run is booked one day early. The fix adds an optional "<N>d" token to `CooldownDurationParser`. The parser finds the first time token as it does today. A day token counts only when it is directly before that time token (white space only between them). "<N>d" alone counts only when the text has no time token. The day pattern runs on the text after the existing OCR digit fix, so "Id" and "ld" read as "1d" (accepted risk). The parser fails, and does not throw, in two overflow cases: a day count too large for an integer, and a total outside the TimeSpan range. The resolver, the log line, and the payload validator need no change. The API documentation text is updated.

## Technical Context

**Language/Version**: C# on .NET 9  
**Primary Dependencies**: None new. `System.Text.RegularExpressions` is already in use.  
**Storage**: N/A  
**Testing**: xUnit unit tests in `tests/unit/Sequences/CooldownDurationParserTests.cs` and `tests/unit/Sequences/OcrOffsetResolverTests.cs`  
**Target Platform**: Windows service (GameBot.Service)  
**Project Type**: Web service with a domain library  
**Performance Goals**: No change. The regex timeout of 200 ms stays.  
**Constraints**: The old forms "HH:MM:SS" and "MM:SS" MUST give the same results. The JSON payload shape does not change.  
**Scale/Scope**: One parser file, two test files, one documentation text.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality | Pass | Small change in one pure helper. No new dependency. |
| II. Testing Standards | Pass | Failing tests are written first. Theory cases cover new and old forms, both overflow cases, and the resolver limits. |
| III. User Experience Consistency | Pass | No UI change. The log line layout stays. |
| IV. Performance | Pass | One extra regex match on a short string. |
| V. Living Documentation | Pass | `PrimitiveActionSchemaFilter.cs` text and the parser XML comment are updated. |
| VI. Simplified Technical English | Pass | All artifacts use STE. |

Post-design re-check: no violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/124-ocr-offset-day-part/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ocr-duration-text.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Commands/SelfReschedule/
│   └── CooldownDurationParser.cs        # change: day token
└── GameBot.Service/Swagger/
    └── PrimitiveActionSchemaFilter.cs   # change: RescheduleSelf description

tests/
└── unit/Sequences/
    ├── CooldownDurationParserTests.cs   # add: parser cases
    └── OcrOffsetResolverTests.cs        # add: limits and log text cases
```

**Structure Decision**: Change the domain parser only. The resolver (`SelfRescheduleOcrOffset.cs`) and its log line stay as they are.

## Complexity Tracking

No violations.
