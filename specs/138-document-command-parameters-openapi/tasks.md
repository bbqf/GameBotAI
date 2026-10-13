# Tasks: Document the command "parameters" list in OpenAPI

**Input**: Design documents in `C:\src\GameBot\specs\138-document-command-parameters-openapi\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi-command-create.md, quickstart.md

**Tests**: One contract test file is required (FR-006). It reads the generated document at `/swagger/v1/swagger.json`.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

## Phase 1: Setup (baseline record, FR-001b)

- [ ] T001 Record the baseline before any edit (FR-001b). Build the solution. Start a `WebApplicationFactory<Program>` host (or the service) and read `/swagger/v1/swagger.json`. Write these notes at the end of `C:\src\GameBot\specs\138-document-command-parameters-openapi\research.md` under a new heading "Baseline (T001)": (a) the generated schema of one `parameters` item in the update or response command schema (or the create schema if it is there); (b) the JSON path of the create command request example in the document; (c) the fallback condition, stated exactly: `default` is in the item `required` list, or one of the five item properties (`name`, `type`, `default`, `required`, `description`) is missing. Also record if the create, update and response schemas already show `parameters`.

---

## Phase 2: Foundational

No foundational task. The user stories use existing files and have no shared blocking work except T001.

---

## Phase 3: User Story 1 - Find the place to declare a placeholder (Priority: P1) MVP

**Goal**: The create request body schema shows an optional `parameters` list with five item properties (FR-001, FR-001a).

**Independent Test**: The contract test finds `parameters` on the create body schema, with the five item properties, `default` optional, and `text` and `number` in the item `type` schema.

- [ ] T002 [US1] Create the contract test class in `C:\src\GameBot\tests\contract\Sequences\CommandParametersOpenApiTests.cs`. Use the same host pattern as `ParametrizedReferenceImageOpenApiTests` (feature 114). Add a helper that loads `/swagger/v1/swagger.json`. Add a test that the create body schema (`CommandCreateSchema`) has an optional `parameters` array property that is not in the schema `required` list. Add a test that the item schema has the five properties `name`, `type`, `default`, `required`, `description`, that `default` is not in the item `required` list, and that `text` and `number` appear in the enum or the description of the item `type` schema. Run the tests. They must fail now (red).
- [ ] T003 [US1] Add `Parameters` (optional list of parameter declarations) to `CommandCreateSchema` in `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs`. Use the type that the T001 record shows. Do not change run-time types.
- [ ] T004 [US1] Add a `ParameterDeclarationDto` branch in `C:\src\GameBot\src\GameBot.Service\Swagger\ParametrizedReferenceImageSchemaFilter.cs` that names `text` and `number` in the `type` description (or enum). If the T001 fallback condition is true, also inline the item type, or make `ParameterDeclarationDto` public, so that all five properties show and `default` is optional. Run the T002 tests. They must pass (green).

**Checkpoint**: US1 works alone.

---

## Phase 4: User Story 2 - Understand the declaration rule (Priority: P1)

**Goal**: The `fieldTemplates` description states the rule (FR-003, FR-004).

**Independent Test**: The contract test finds the rule, the queue built-in exception, the `parameterBindings` limit and `unresolvable_parameter_reference` in the `fieldTemplates` description.

- [ ] T005 [US2] Add a test in `C:\src\GameBot\tests\contract\Sequences\CommandParametersOpenApiTests.cs` that reads the `fieldTemplates` description and checks for the top-level `parameters` list, the queue built-in exception, the words `parameterBindings`, and `unresolvable_parameter_reference`. Run it. It must fail (red).
- [ ] T006 [US2] Extend `FieldTemplatesDescription` in `C:\src\GameBot\src\GameBot.Service\Swagger\ParametrizedReferenceImageSchemaFilter.cs`. Use STE. State: each `{{placeholder}}` is declared in the top-level `parameters` list of the command, or is a queue built-in; a step-level `parameterBindings` does not satisfy this check; the service gives 400 `unresolvable_parameter_reference` if a name is not declared. Keep the existing text. Run T005. It must pass (green).

**Checkpoint**: US1 and US2 work.

---

## Phase 5: User Story 3 - Create, update and response schemas agree (Priority: P2)

**Goal**: All three command schemas show `parameters` (FR-002, SC-001).

**Independent Test**: The contract test finds `parameters` on the create, update and response schemas.

- [ ] T007 [US3] Add tests in `C:\src\GameBot\tests\contract\Sequences\CommandParametersOpenApiTests.cs` that check `parameters` on the update schema and on the response schema as two separate checks. (The create check is in T002 and is not repeated.) Run them.
- [ ] T008 [US3] If T007 fails for a schema, correct that schema in `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs` (or the filter). If T007 passes, make no change and add a note "already correct" to the T001 baseline section of `C:\src\GameBot\specs\138-document-command-parameters-openapi\research.md` (clarification I1).

**Checkpoint**: US1 to US3 work.

---

## Phase 6: User Story 4 - Create example shows parameters (Priority: P3)

**Goal**: The create request example has a `parameters` list and every placeholder is declared (FR-005). This story is required.

**Independent Test**: The contract test reads the example at the JSON path from T001 and checks the declaration.

- [ ] T009 [US4] Add a test in `C:\src\GameBot\tests\contract\Sequences\CommandParametersOpenApiTests.cs` that reads the create request example (JSON path from T001), checks that it has a `parameters` list, and checks that each `{{placeholder}}` in its `fieldTemplates` is declared by name in that list. Run it. It must fail (red).
- [ ] T010 [US4] Edit the create request example in `C:\src\GameBot\src\GameBot.Service\Swagger\SwaggerConfig.cs`. Add one `parameters` item (`name`, `type` `text`) and one `fieldTemplates` entry with one whole placeholder, for example `waitForImage.detectionTarget.referenceImageId` = `{{name}}`. Run T009. It must pass (green).

**Checkpoint**: All stories work.

---

## Phase 7: Polish and final check

- [ ] T011 [P] Change the product minor version to 9 by editing only `C:\src\GameBot\installer\versioning\version.override.json`: set `minor` to "9" and `updatedAtUtc` to the current UTC time (FR-008). Do not change other version files.
- [ ] T012 [P] Add a short entry under Unreleased in `C:\src\GameBot\CHANGELOG.md`: the OpenAPI document now shows the command `parameters` list and the declaration rule; the minor version is now 9 (1.9.x line) (FR-009).
- [ ] T013 Run the full verification: `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~CommandParametersOpenApiTests"`, then `--filter "FullyQualifiedName~OpenApi"`, then the full contract and unit test projects. All existing tests must pass with no change to their expected values (SC-004, FR-007). Build must have no new warning.
- [ ] T014 Check FR-012: confirm that `C:\src\GameBot\docs\architecture.md` has no change (`git status` shows it unmodified). The API surface is the same; only the documentation text changed.
- [ ] T015 Final check (FR-010, FR-011), last task. Confirm that `C:\src\GameBot\installer\versioning\version.override.json` has `minor` "9" and that `C:\src\GameBot\CHANGELOG.md` has the Unreleased entry. Then change the Status line of `C:\src\GameBot\specs\138-document-command-parameters-openapi\spec.md` to `Implemented`. Then add the row `| 138 | Document the command "parameters" list in OpenAPI | Implemented |` to `C:\src\GameBot\specs\STATUS.md` in the same form as rows 135 to 137 (read those rows first and copy the form exactly).

---

## Dependencies and Order

- T001 first. All other tasks need its record.
- US1 (T002-T004) before US2 to US4 in the same files; US2 to US4 do not depend on each other's behavior, but they edit the same test file and the same source files, so run them in order.
- Within each story: test (red) then change (green).
- T011 and T012 are parallel (different files). T013 after all code. T014 after T013. T015 is last.

## Parallel Example

- T011 and T012 can run together.
- No other task is marked [P] because the tasks share one test file and two source files.

## Implementation Strategy

- MVP: Phase 1 and US1 (T001-T004). It closes the main gap in issue #247.
- Then add US2 (the rule text), US3 (agreement), US4 (example), and the final check.
- Do not commit until the user asks.
