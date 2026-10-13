# Research: Document the command "parameters" list in OpenAPI

## Decision 1: Add the property to `CommandCreateSchema`

- **Decision**: Add `public ICollection<ParameterDeclarationDto>? Parameters { get; set; }` to `CommandCreateSchema` in `SwaggerConfig.cs`.
- **Rationale**: `SetRequestExample(..., typeof(CommandCreateSchema))` uses this class as the create request body schema. The class is for documentation only. The real request type `CreateCommandRequest` already has `Parameters`, so the run-time code is correct. The update request and the response already carry `Parameters` (`Commands.cs` lines 56, 65, 93).
- **Alternatives**: Use `CreateCommandRequest` as the schema type. Rejected. The type is `internal` and it has run-time rules. This option changes more than the documentation.

## Decision 2: Extend the shared `fieldTemplates` description

- **Decision**: Add sentences to `ParametrizedReferenceImageSchemaFilter.FieldTemplatesDescription`.
- **Rationale**: One shared constant covers all steps (clarification Q3). The existing test already reads this property.
- **Alternatives**: Add a new schema filter. Rejected. It needs more code for the same text.

## Decision 2a: The item schema shows five properties (FR-001a)

- **Decision**: Keep `ParameterDeclarationDto` as it is. Swashbuckle reads the public properties of an internal type. Because of this, the item schema shows the five properties and none is in `required`. Add a branch in the same schema filter that sets the description of `type` to name `text` and `number`. The test fails if the item schema is empty or if a property is missing.
- **Rationale**: The test reads the `type` property of the item schema, not the whole document (clarification C1).
- **Alternatives**: Make the DTO `public`. Use this option only if the generator gives an incomplete schema.

## Decision 2b: Record the baseline before any edit (FR-001b)

- **Decision**: The first task reads the generated document. It records the current `parameters` item schema and the JSON path of the create example. It also records the fallback trigger. The trigger is true if `default` is in the item `required` list, or if one of the five item properties is missing.
- **Rationale**: Only the generated document shows these facts (clarification U1, U2).
- **Alternatives**: Assume the schema from the C# type. Rejected. The generator can differ from the type.

## Decision 3: Contract test pattern

- **Decision**: Add a new xUnit class in `tests\contract\Sequences`. Use `WebApplicationFactory<Program>` and `/swagger/v1/swagger.json`, the same as `ParametrizedReferenceImageOpenApiTests`.
- **Rationale**: The test reads the real generated document (clarification Q1).
- **Alternatives**: Write a unit test of the filter only. Rejected. It does not prove the generated document.

## Decision 4: Example change

- **Decision**: Add one `parameters` item (`name`, `type` `text`) to the create example. Add one `fieldTemplates` entry that uses `{{name}}`. This change is a MUST (FR-005, clarification A1).
- **Rationale**: The change is small (clarification Q2). The step in the example must stay valid. Use an image key such as `waitForImage.detectionTarget.referenceImageId` with one whole placeholder.

## Decision 5: Version and change log

- **Decision**: Edit only `installer\versioning\version.override.json` (`minor` "9", `updatedAtUtc` now). Add a short entry under Unreleased in `CHANGELOG.md`.
- **Rationale**: FR-008 and FR-009.

## Decision 6: Current project documentation (FR-011, FR-012)

- **Decision**: The final check sets the spec Status to Implemented. It adds the row for 138 to `specs\STATUS.md`. `docs\architecture.md` does not change.
- **Rationale**: Principle V. The API surface is the same. Only the documentation text changes.

## Baseline (T001)

This section shows data from the generated `/swagger/v1/swagger.json`. The data is from before any edit.

- (a) The `parameters` item in `UpdateCommandRequest` and `CommandResponse` is a `$ref` to `ParameterDeclarationDto`. That schema has the five properties `name`, `type`, `default`, `required`, and `description`. All five are nullable. The schema has no `enum`, no `required` list, and no description on `type`.
- (b) The create request example is at `paths./api/commands.post.requestBody.content.application/json.example`. The body schema there is `CommandCreateSchema`.
- (c) The fallback condition is: `default` is in the item `required` list, or one of the five item properties is missing. The condition is false. The fallback (inline item type or public DTO) is not needed.
- `CommandCreateSchema` did not show `parameters`. `UpdateCommandRequest` and `CommandResponse` already show `parameters`, so they are correct. The schema `CreateCommandRequest` does not exist in the document.

## Open items

There are no open items. The implementation step confirms that the update and response schemas show `parameters`. If they do not, the step corrects them.
