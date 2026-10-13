# Implementation Plan: Document the command "parameters" list in OpenAPI

**Branch**: `138-document-command-parameters-openapi` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/138-document-command-parameters-openapi/spec.md`

## Summary

The OpenAPI create body schema for `POST /api/commands` (`CommandCreateSchema`) does not show the optional `parameters` list. The run-time type `CreateCommandRequest` already has it, so the service is correct and only the document is incomplete. This feature changes documentation only. It has five parts:

1. Add `Parameters` to `CommandCreateSchema` in `SwaggerConfig.cs`.
2. Extend `ParametrizedReferenceImageSchemaFilter.FieldTemplatesDescription` with the declaration rule, the queue built-in exception, the `parameterBindings` limit and the error code `unresolvable_parameter_reference`. Add a `ParameterDeclarationDto` branch in the same filter that names `text` and `number` in the `type` description.
3. Add a `parameters` item and a matching `fieldTemplates` entry to the create request example.
4. Add a contract test that reads the generated document (FR-006).
5. Change the product minor version to 9, add the change log entry, and update the Status line and `specs\STATUS.md` in the final check.

The first task records the current item schema and the JSON path of the create example before any edit (FR-001b). See [research.md](research.md) for each decision.

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (`ISchemaFilter`, `IOperationFilter`), Microsoft.OpenApi
**Storage**: N/A. No stored data changes.
**Testing**: xUnit contract test with `WebApplicationFactory<Program>`, reading `/swagger/v1/swagger.json`. Same pattern as `ParametrizedReferenceImageOpenApiTests` (feature 114).
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service with a domain library
**Performance Goals**: None. The change adds static document text.
**Constraints**: No run-time change (FR-007). Only `installer\versioning\version.override.json` changes among version files (FR-008). `docs\architecture.md` does not change (FR-012).
**Scale/Scope**: 3 source files, 1 new test file, 2 repo files (version override, change log), 2 spec files (Status, STATUS.md).

No item needed clarification.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality | Pass | Small edits in existing files. No new class in `Program.cs`. |
| II. Testing | Pass | New contract test on the real generated document. Existing contract and unit tests run unchanged (SC-004). |
| III. UX Consistency | Pass | The change helps the API client author. Error code names match the service. |
| IV. Performance | Pass | No run-time path changes. |
| V. Living Documentation | Pass | OpenAPI is the subject. Change log, spec Status and `specs\STATUS.md` are updated (FR-009, FR-011). `docs\architecture.md` has no change because the API surface is the same (FR-012). |
| VI. STE | Pass | All new text uses STE. |

Post-design re-check: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/138-document-command-parameters-openapi/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/openapi-command-create.md
|-- checklists/requirements.md
`-- tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/Swagger/
|-- SwaggerConfig.cs                              # change: CommandCreateSchema.Parameters; create example
`-- ParametrizedReferenceImageSchemaFilter.cs     # change: fieldTemplates text; ParameterDeclarationDto type text

tests/contract/Sequences/
`-- CommandParametersOpenApiTests.cs              # new: reads the generated document

installer/versioning/version.override.json        # change: minor "9", updatedAtUtc
CHANGELOG.md                                      # change: entry under Unreleased
specs/STATUS.md                                   # change: row for 138
specs/138-document-command-parameters-openapi/spec.md  # change: Status Implemented (final check)
```

**Structure Decision**: Use the existing Swagger folder and the existing contract test folder for OpenAPI tests. No new project.

## Design Notes

- **Baseline first** (FR-001b): before any edit, read the generated document and record (a) the schema of one `parameters` item and (b) the JSON path of the create example. The record goes in the task notes.
- **Fallback condition** (FR-001a, FR-001b): if `default` is in the item `required` list, or one of the five item properties is missing, inline the item type or make `ParameterDeclarationDto` public. Otherwise keep it internal. `Default` is `string?`, so it is expected to be optional.
- **Update and response** (FR-002, US3): the DTOs already carry `Parameters`. The test confirms this. A schema that lacks it is corrected.
- **Example** (FR-005): add one `parameters` item (`name`, `type` `text`) and one `fieldTemplates` entry with one whole placeholder, for example `waitForImage.detectionTarget.referenceImageId` = `{{name}}`. The test checks that each placeholder in the example is declared.
- **Test strength** (SC-003): each assertion fails if its point is removed: the `parameters` property, the five item properties, `default` optional, `text` and `number` in the item `type` schema, the three `parameters` properties, the `fieldTemplates` words, and the example.
- **Final check** (FR-010, FR-011): confirm `minor` "9", the change log entry, spec Status Implemented, and the `| 138 | <title> | Implemented |` row in the same form as rows 135 to 137.

## Complexity Tracking

No violation. This section is empty.
