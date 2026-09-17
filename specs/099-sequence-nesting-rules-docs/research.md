# Research: Publish Sequence Step Nesting Rules

## R-001 Actual enforced rules

- **Decision**: Document exactly what `SequenceStepValidationService` enforces (verified 2026-09-17):
  - Top level (`Validate`): Action, Loop, If allowed; Break rejected ("has stepType 'Break' which is only valid inside a loop body").
  - Loop body (`ValidateLoopStep`): Action, If, Break allowed; Loop rejected ("must not itself be a loop step").
  - If branch then/else (`ValidateIfBranch`): Action allowed; Break allowed only when the If sits in a Loop body (`insideLoop`); If rejected ("must not itself be an if step"); Loop rejected ("must not itself be a loop step").
- **Rationale**: FR-005 requires re-verification rather than copying the issue; the issue's "Loop body is not restricted this way" is true only for If — Loop-in-Loop is rejected.
- **Alternatives considered**: Documenting from the web-ui comments (`IfBlock.tsx`, `stepEntry.ts`) — rejected, the service validator is the source of truth.

## R-002 Where the step contract appears in the published document

- **Decision**: Annotate `SequenceStepContract` only. `ConditionalFlowSchemaDocumentFilter` generates it and aliases the same schema object as `SequenceStep`, so both component keys carry the description. Its `body` property holds both a Loop's body and an If's then branch; `elseBody` holds the If's else branch.
- **Rationale**: It is the only named schema exposing loop bodies / if branches. `SequenceUpsertContract` / `SequencePatchContract` reference it via `steps` items. `FlowStepDto` (flow-graph model) has no body/elseBody. `SequenceRequestSchema`/`SequenceResponseSchema` model steps as string arrays. GET sequence responses are anonymous objects with no named step schema (out of scope per Clarification 2).
- **Alternatives considered**: Adding a named response step schema — rejected, exceeds issue scope.

## R-003 Mechanism

- **Decision**: New `ISchemaFilter` (`SequenceNestingRulesSchemaFilter`) matching `context.Type == typeof(SequenceStepContract)`, setting `schema.Description` and the `body`/`elseBody` property descriptions; registered in `GameBotServiceSetup` beside the other schema filters.
- **Rationale**: The service does not feed XML comments to Swagger; three precedent filters use this exact pattern (features 094, 096, 097).
- **Alternatives considered**: (a) XML `<summary>` on the record — not published. (b) Adding text to operation descriptions in `SwaggerExamplesOperationFilter` — the issue asks for it on the step contract; operation descriptions would repeat it on 3 operations. (c) Machine-enforced `oneOf` restrictions per container — the contract uses one recursive record with a string `stepType`, so the schema can't express it without a breaking contract split.

## R-004 Property description on an array property

- **Decision**: Set `Description` on the property schema returned for `body`/`elseBody` (an `array` schema whose `items` is a `$ref`).
- **Rationale**: The property schema itself is not a `$ref`, so its description is emitted; OpenAPI 3.0 ignores siblings only of `$ref` objects.
- **Alternatives considered**: None needed. The contract test verifies the description is emitted.

## R-005 Regression guard

- **Decision**: Contract test fetches `/swagger/v1/swagger.json` via `WebApplicationFactory<Program>` (same harness as `SequenceTimeLimitOpenApiTests`) and asserts, on both `SequenceStep` and `SequenceStepContract`, that the schema description names each rule and that `body` and `elseBody` descriptions state their container rules. Existing validator unit/contract tests remain unchanged as the FR-008 guard.
- **Rationale**: FR-007 / SC-003.
