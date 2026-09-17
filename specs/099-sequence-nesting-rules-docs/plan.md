# Implementation Plan: Publish Sequence Step Nesting Rules

**Branch**: `099-sequence-nesting-rules-docs` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/099-sequence-nesting-rules-docs/spec.md` (GitHub issue #178)

## Summary

The sequence validator rejects If-inside-If (and Loop-inside-Loop/If, and Break outside a loop), but the
published OpenAPI document never says so. Add a Swashbuckle `ISchemaFilter` that attaches the nesting rules
to the published sequence step schema (`SequenceStepContract`, aliased `SequenceStep`): the full rule set on
the schema description, and container-specific rules on its `body` and `elseBody` property descriptions. The
rule text is built from one set of constants so the schema and property descriptions cannot drift apart. A
contract test reads `/swagger/v1/swagger.json` and asserts every rule statement is present. No validation or
runtime code changes.

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Service)
**Primary Dependencies**: Swashbuckle.AspNetCore (SwaggerGen `ISchemaFilter`), Microsoft.OpenApi
**Storage**: N/A
**Testing**: xUnit + FluentAssertions contract tests (`tests/contract`, `WebApplicationFactory<Program>`); existing `SequenceStepValidationService` unit tests as the no-behaviour-change guard
**Target Platform**: Windows service (ASP.NET Core minimal APIs)
**Project Type**: web-service
**Performance Goals**: None beyond existing; the filter runs only during OpenAPI document generation (a string assignment per matching schema)
**Constraints**: Service does not feed XML comments to Swagger — descriptions must come from a schema filter (precedent: `SequenceTimeLimitSchemaFilter`, `QueueHealthSchemaFilter`, `ImageAlternatesSchemaFilter`)
**Scale/Scope**: 1 new filter file, 1 registration line, 1 new contract test file, living-doc updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality | Small cohesive filter class following existing filter pattern; XML summary on the class; no new dependencies; CamelCase method names. PASS |
| II. Testing | Contract test asserts the published rules (externally visible contract). The issue is a documentation gap, so the "failing test first" rule is honoured by writing the contract test before the filter. PASS |
| III. UX Consistency | Improves API schema help (explicitly required: "Provide help/usage for … API schemas"). Error messages unchanged. PASS |
| IV. Performance | No hot path touched; perf note: document-generation-only string assignments. PASS |
| V. Living Documentation | API surface documentation changes → update `docs/architecture.md` (sequence nesting rules + "Last reviewed"), `CHANGELOG.md`, and `specs/STATUS.md` row for 099; spec Status line set to Implemented at the end. PASS |

Post-design re-check: design adds no projects, no runtime behaviour, no persistence. PASS.

## Project Structure

### Documentation (this feature)

```text
specs/099-sequence-nesting-rules-docs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi-sequence-step-descriptions.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Swagger/SequenceNestingRulesSchemaFilter.cs   # NEW: ISchemaFilter describing SequenceStepContract + body/elseBody
└── GameBotServiceSetup.cs                        # register options.SchemaFilter<SequenceNestingRulesSchemaFilter>()

tests/contract/Sequences/
└── SequenceNestingRulesOpenApiTests.cs           # NEW: reads swagger.json, asserts rule text on SequenceStep/SequenceStepContract

docs/architecture.md                              # nesting rules published in OpenAPI; Last reviewed date
CHANGELOG.md                                      # Unreleased entry
specs/STATUS.md                                   # 099 row
```

**Structure Decision**: Single existing service project; the filter sits next to the three existing schema filters in `src/GameBot.Service/Swagger/`, and the test next to `SequenceTimeLimitOpenApiTests` in `tests/contract/Sequences/`.

## Complexity Tracking

No constitution violations.
