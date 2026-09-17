# Implementation Plan: Publish primitive action types and payload shapes

**Branch**: `102-primitive-action-types-docs` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/102-primitive-action-types-docs/spec.md` (GitHub issue #201)

## Summary

`SequenceStepValidationService` accepts eleven `primitiveAction.type` values — `PrimitiveActionTypes.All` (nine) plus
the service-level `reschedule-self` and `notify`, which it branches on before the membership check — yet the OpenAPI
`PrimitiveAction` schema (`PrimitiveActionRequest`, aliased by `ConditionalFlowSchemaDocumentFilter`) publishes `type`
as a free string and `payload` as an undescribed object. Add one ordered, public source list
`SequenceActionTypes.All` in `GameBot.Domain.Actions` and use it for the validator's membership check, its
unsupported-type message (` (expected one of ...)`) and `ActionPayloadValidationService`. Add a Swashbuckle
`ISchemaFilter` (`PrimitiveActionSchemaFilter`) that sets `type.enum` from that list, describes `type`
(case-insensitive; session-start accepts only `connect-to-game`) and describes `payload` from a per-type description
map keyed by the same type strings. Add a `reschedule-self` Timer step to the sequence create/update request and
response examples. Contract tests pin the enum against the domain list, the description map's key set, the
reschedule-self statements, the example, and the enriched 400 message; a unit test pins the message. No runtime
behaviour or acceptance change.

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Domain, GameBot.Service)
**Primary Dependencies**: Swashbuckle.AspNetCore (SwaggerGen `ISchemaFilter`), Microsoft.OpenApi
**Storage**: N/A
**Testing**: xUnit + FluentAssertions — unit (`tests/unit/Sequences`, validator message) and contract (`tests/contract/Sequences`, `WebApplicationFactory<Program>` reading `/swagger/v1/swagger.json` and `POST /api/sequences` with `dryRun: true`)
**Target Platform**: Windows service (ASP.NET Core minimal APIs)
**Project Type**: web-service
**Performance Goals**: None beyond existing. The filter runs only during OpenAPI document generation. The validator message string is built once (static) and only formatted on the error path; the membership check stays an O(1) case-insensitive `HashSet` lookup.
**Constraints**: No XML comments reach Swagger — descriptions come from a schema filter (precedent: `SequenceNestingRulesSchemaFilter`, `ImageDetectCoordinatesSchemaFilter`). `PrimitiveActionRequest` is shared with `POST /api/sessions/start`; one component, so one enum. The message's leading text must be unchanged. `WaitForImage` keeps its PascalCase canonical spelling; matching stays `OrdinalIgnoreCase`.
**Scale/Scope**: 1 new domain file, 2 domain edits, 1 new filter file, 1 registration line, 1 example edit in `SwaggerConfig.cs`, 1 unit test file, 1 contract test file, living-doc updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality | Removes a duplicated type set (validator `AllowedPrimitiveActionTypes` + `ActionPayloadValidationService._supportedActionTypes`) in favour of one documented public list; small cohesive filter mirroring feature 099; XML summaries on new public/internal types; no new dependencies. PASS |
| II. Testing | Bug-labelled gap ⇒ failing tests first: the OpenAPI contract test and the message unit test are written and seen failing before the list/filter/message changes. Existing sequence validation and dry-run contract tests guard unchanged acceptance. PASS |
| III. UX Consistency | Delivers "Provide help/usage for … API schemas"; makes the unsupported-type error actionable ("error messages MUST be actionable") with the same "expected one of" idiom the option error uses; leading text stable. PASS |
| IV. Performance | No hot path change; perf note above. PASS |
| V. Living Documentation | `docs/architecture.md` (sequence step API schema paragraph + "Last reviewed"), `CHANGELOG.md` Unreleased entry, `specs/STATUS.md` row 102; spec Status set to Implemented at the end. PASS |

Post-design re-check (after research/contracts): no new route, project, persistence, or runtime behaviour; the only
wire-visible change is the text of one 400 error message (appended, prefix preserved). PASS.

## Project Structure

### Documentation (this feature)

```text
specs/102-primitive-action-types-docs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi-primitive-action-types.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Actions/SequenceActionTypes.cs                 # NEW: ordered All + SupportedValuesText
├── Services/SequenceStepValidationService.cs      # membership check + message use SequenceActionTypes
└── Services/ActionPayloadValidationService.cs     # supported set from SequenceActionTypes.All

src/GameBot.Service/
├── Swagger/PrimitiveActionSchemaFilter.cs         # NEW: type enum + descriptions, payload per-type descriptions
├── Swagger/SwaggerConfig.cs                       # reschedule-self step in sequence request/response examples
└── GameBotServiceSetup.cs                         # register options.SchemaFilter<PrimitiveActionSchemaFilter>()

tests/unit/Sequences/
└── SequenceStepValidationServiceActionTypeTests.cs # NEW: message lists all supported values (unknown + blank type)

tests/contract/Sequences/
└── PrimitiveActionTypesOpenApiTests.cs            # NEW: enum == domain list, descriptions, example, dryRun 400 message

docs/architecture.md                               # published action types sentence; Last reviewed
CHANGELOG.md                                       # Unreleased entry
specs/STATUS.md                                    # 102 row
```

**Structure Decision**: Existing projects only. The list lives beside `ActionTypes`/`PrimitiveActionTypes` in
`GameBot.Domain/Actions`, the filter beside `SequenceNestingRulesSchemaFilter`, and the tests beside the existing
sequence validator unit tests and sequence contract tests.

## Complexity Tracking

No constitution violations.
