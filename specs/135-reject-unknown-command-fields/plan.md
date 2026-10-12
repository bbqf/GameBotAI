# Implementation Plan: Reject unknown command step fields

**Branch**: `135-reject-unknown-command-fields` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/135-reject-unknown-command-fields/spec.md`

## Summary

POST /api/commands and PATCH /api/commands/{id} drop unknown body fields with no sign, although the
published schema has `additionalProperties: false`. The fix adds a scanner. The scanner walks the raw
JSON body and compares each field name with the public properties of the request DTO types. The
endpoint answers 400 with every unknown path (full path from the body root) and a hint. The scan runs
before any store or change. The DTOs, the OpenAPI schema, and the stored data do not change.
See [research.md](research.md).

## Technical Context

**Language/Version**: C# 12 on .NET 8 (ASP.NET Core minimal API)
**Primary Dependencies**: System.Text.Json (no new package)
**Storage**: N/A (the command repository does not change)
**Testing**: xUnit contract tests (`tests\contract\Commands`) and unit tests (`tests\unit\Commands`)
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: web-service
**Performance Goals**: No measurable cost. Request bodies are small (spec assumption).
**Constraints**: A valid request keeps the same answer and body (FR-004). The OpenAPI schema stays the same (FR-006).
**Scale/Scope**: Two endpoints, one new class, about 12 DTO types walked

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Build and tests green before progress | Pass | The plan runs the suites before and after the change |
| STE in all text | Pass | A final polish step checks all new text |
| Bug fix: failing test first (FR-009) | Pass | Contract tests are written before the fix and must fail with 201 |
| Remediation hint in errors (FR-002a) | Pass | The 400 error carries a non-null hint |
| API change docs (FR-009) | Pass | Check `docs/architecture.md`; update "Last reviewed" if the API description changes |
| Spec status and `specs/STATUS.md` (FR-009) | Pass | Set Status to Implemented at the end |
| Thin Program.cs (build-time analyzers) | Pass | Scanner is a separate class; no change to Program.cs |
| Out of scope respected (FR-007) | Pass | No new step fields, no hold behaviour, no reschedule-self validator change, no #251 fix. The final review checks the diff scope |

Post-design re-check: no new violation. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/135-reject-unknown-command-fields/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── unknown-field-error.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/
│   ├── CommandsEndpoints.cs        # edit: call the scanner in POST and PATCH
│   └── UnknownFieldScanner.cs      # new: finds unknown field paths
└── Models/Commands.cs              # read only: request and step DTOs

tests/
├── contract/Commands/UnknownStepFieldContractTests.cs   # new: POST and PATCH cases
└── unit/Commands/UnknownFieldScannerTests.cs            # new: scanner cases, test-only type with [JsonIgnore]
```

**Structure Decision**: Keep the existing single service project. Add one class next to the
endpoint file and two test files in the existing test projects. The final diff touches only the
scanner, the commands endpoint file, tests, specs, and docs (FR-007).

## Design Notes

- **Scanner**: `UnknownFieldScanner.Find(JsonElement root, Type dtoType)` returns the ordered list of
  unknown full paths (for example `steps[0].primitiveTap.bogusField`). It uses cached reflection and
  OrdinalIgnoreCase. It skips `Dictionary<string,string>` values (keys are data). A `[JsonIgnore]`
  property counts as unknown. A `null` value for an unknown name is still unknown (research D-1, D-5, D-6).
- **POST**: After `ParseAsync` and before `Deserialize`, scan with `CreateCommandRequest`. The root
  alias `detectionTarget` (read by `TryReadDetection`) is not flagged (research D-3). If the list is
  not empty, return 400 `invalid_request` with message `Unknown field(s): <paths>` and the hint
  `Remove the field or check its name in the OpenAPI schema.`
- **PATCH**: Change the handler to read the body by hand. Parse the body, do the 404 lookup first,
  then scan with `UpdateCommandRequest`, then deserialize with `WebJsonOptions`. The scan runs before
  any change to the stored command (research D-4). Answers for an empty or malformed body, and for
  malformed JSON on a missing command, stay as today (research D-7).
- **Order of work**:
  1. Setup: build, run the existing command tests, and record the current PATCH answers (valid
     update status, empty body, malformed body, malformed body on a missing command).
  2. Write the failing contract tests (FR-009) and run them. They must fail with 201.
  3. Write the scanner and its unit tests, using a test-only type that has a `[JsonIgnore]` property.
  4. Edit the endpoints.
  5. Run all command tests, check the diff scope, update docs and status files.

## Complexity Tracking

No violations. This table is empty.
