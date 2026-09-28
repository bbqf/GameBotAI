# Implementation Plan: Reject a malformed sequence step on create

**Branch**: `ccr-1ebf2553-mqz6yt` (spec number 113) | **Date**: 2026-09-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/113-reject-malformed-sequence-step/spec.md`

## Summary

`POST /api/sequences` selects the body shape from the first step only (research R-001). A first step without `stepType` and without `primitiveAction` sends the body to the old "list of command ids" branch. That branch drops each object step, the parameters and `dryRun`, and it stores an empty sequence.

The plan changes `IsPerStepRequestCandidate` in `SequencesEndpoints.cs`: one object step is enough to select the per-step shape (a body with `blocks` keeps the current rule). The per-step reader then rejects the malformed step with 400. Each shape error of `TryReadPerStepRequest` gets the prefix `steps[<i>] (stepId '<id>'): `. The old branch rejects a non-string item and a non-null `parameters` value, and it obeys `dryRun`. The domain branch obeys `dryRun` too (R-002, R-003). The Swagger description, `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` tell the new rule (R-004).

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions  
**Storage**: File-backed sequence repository (JSON). The stored format does not change.  
**Testing**: xUnit contract tests (`tests/contract`) and integration tests with `WebApplicationFactory` (`tests/integration`)  
**Target Platform**: Windows service (CI on `windows-latest`). The changed code has no platform dependency.  
**Project Type**: Web service with a web UI (the web UI does not change)  
**Performance Goals**: No change. The shape check reads the `steps` array one more time.  
**Constraints**: A valid per-step body keeps its result (FR-007). The response shapes do not change. `PUT`/`PATCH` `dryRun` does not change. `POST /api/commands` does not change.  
**Scale/Scope**: 2 service files, 1 new integration test file, 1 new contract test file, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in the existing style. One new private helper for the error prefix. |
| Tests first, regression test for the bug | Pass | The reproduction test gets 201 before the change. The dry-run test finds a stored sequence before the change. |
| UX / API consistency | Pass | The 400 body has the current form `{ message, errors }`. The dry-run 200 body is the current form. |
| Performance | Pass | No measurable change. |
| Backward compatibility | Pass | A valid per-step body, an old body with string ids and a `blocks` body keep their results. Only bodies that the service stored with data loss now get 400. |
| Living docs | Pass | `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`, the Swagger description and the spec `Status` line change. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/113-reject-malformed-sequence-step/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── sequences-create.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/SequencesEndpoints.cs      # shape selection, error prefix, old branch, domain branch
└── Swagger/SwaggerConfig.cs             # SequenceCreateDescription

tests/
├── integration/Sequences/SequenceCreateMalformedStepIntegrationTests.cs   # new
└── contract/Sequences/SequenceCreateMalformedStepContractTests.cs         # new

docs/architecture.md
CHANGELOG.md
specs/STATUS.md
```

**Structure Decision**: The fix stays in the sequences endpoint class, where the three write routes share the shape selection and the per-step reader. No new project and no new type.

## Complexity Tracking

No violations.
