# Implementation Plan: API Version and Gamepack Compatibility

**Branch**: `144-api-version-compat` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/144-api-version-compat/spec.md` (issue #305, FR-022)

## Summary

The platform declares one API version. A client reads it with `GET /api/version`. The gamepack validator compares the
manifest field `requiredApiMajor` with the running major version. A different major version gives a structure problem
that names both versions. The install and the dry run share the validator, so they give the same answer. The rollback
skips the check. The docs state the compatibility rule. A test ties the docs line to the version constants.

## Technical Context

**Language/Version**: C# on .NET (the version in the existing solution)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI schema filters), xUnit
**Storage**: N/A. No stored data changes. The registry already holds `RequiredApiMajor`.
**Testing**: xUnit unit tests (`tests/unit/Gamepacks`) and contract tests (`tests/contract/Gamepacks`)
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only)
**Performance Goals**: N/A. The route returns four constants. The check is one integer compare.
**Constraints**: Keep `Program.cs` thin (one added line). Keep the install behavior unchanged for a gamepack that
states the running major version or no version.
**Scale/Scope**: 1 new route, 1 new constant class, 1 validator rule, 1 optional parameter, 1 docs section

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Build and tests: Task T001 records the baseline. Task T020 runs the full build and tests. No waiver is needed.
- STE (Principle VI): All text in this plan and in the artifacts uses STE. Task T022 checks the new code comments, messages, and docs.
- Code quality (I): One constant class is the single source. The check sits in the existing validator. No duplicate code.
- Testing (II): Unit tests for the rule, contract tests for the route, the install, the dry run, the rollback, and OpenAPI. The docs-sync test ties the docs to the constants.
- UX consistency (III): No web UI change. The phone check does not apply. The error message is in STE and names both versions.
- Performance (IV): No concern.
- Living documentation (V): `docs/gamepacks.md`, `docs/architecture.md`, and the OpenAPI document change in the same PR.

Result: pass. The post-design check gives the same result. No entry in Complexity Tracking.

## Design Decisions

1. **One source of truth**: static class `ApiVersion` (`Major` 1, `Minor` 0, `Patch` 0, `SchemaFormat` 1) in
   `GameBot.Domain.Gamepacks`. It is not the product version (research R1).
2. **Route**: `GET /api/version`, handler `GetApiVersion`, tag `Version`, class `VersionEndpoints`, DTO
   `ApiVersionResponse`. It uses the same token rule as the other `/api` routes (R5).
3. **Check and messages**: `GamepackValidator.CheckStructure` adds a problem (category `structure`, object type
   `manifest`, name = gamepack name) when `requiredApiMajor` is present and:
   - below 1: `The required API major version must be a whole number of 1 or more.`
   - not equal to `ApiVersion.Major`: `The gamepack needs API major version {N}. This platform runs API major version {M}.`
4. **Rollback**: `ValidateAsync(file, parseProblems = null, ct = default, checkApiVersion = true)`. `RollbackAsync` passes
   `checkApiVersion: false`. `InstallAsync` (install and dry run) keeps the default (R2, R3).
5. **Bad values**: a value that is not a whole number fails in the file reader: the manifest gets the structure problem "The manifest has a wrong shape.", and the service returns the `Invalid` status (R4).
6. **OpenAPI**: the new `ApiVersionSchemaFilter` describes `ApiVersionResponse`. `GamepackSchemaFilter.FileDescription`
   states the refusal (R6).
7. **Docs line**: one line `API version: 1.0.0, schema format 1` in `docs/gamepacks.md`. The format is defined only in
   `contracts/api-version.md`. A unit test compares it with `ApiVersion`.
8. **Existing tests**: two contract tests use a `requiredApiMajor` value that the install now refuses. They change to
   the running major version (R7).

## Project Structure

### Documentation (this feature)

```text
specs/144-api-version-compat/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── api-version.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Gamepacks/
│   ├── ApiVersion.cs                 # new
│   ├── GamepackValidator.cs          # version check, checkApiVersion parameter
│   └── GamepackService.cs            # RollbackAsync passes checkApiVersion: false
└── GameBot.Service/
    ├── ApiRoutes.cs                  # route constant Version
    ├── Program.cs                    # one line: app.MapVersionEndpoints();
    ├── Endpoints/VersionEndpoints.cs # new
    ├── Endpoints/Dto/VersionDtos.cs  # new
    └── Swagger/
        ├── ApiVersionSchemaFilter.cs # new
        └── GamepackSchemaFilter.cs   # FileDescription text

tests/
├── unit/Gamepacks/
│   ├── ApiVersionTests.cs                    # new (constants and docs line)
│   └── GamepackValidatorApiVersionTests.cs   # new
└── contract/Gamepacks/
    ├── ApiVersionContractTests.cs            # new
    ├── GamepackApiVersionContractTests.cs    # new
    ├── GamepackImageRulesContractTests.cs    # one test changes
    └── GamepackListContractTests.cs          # one value changes

docs/
├── gamepacks.md      # new section "API version and compatibility"
└── architecture.md   # short note and link
```

**Structure Decision**: Existing backend layout (Domain and Service projects, with unit and contract test folders for
gamepacks). No new project.

## Complexity Tracking

No violation. This table stays empty.
