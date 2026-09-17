---
description: "Task list for publishing primitive action types and payload shapes in the OpenAPI document"
---

# Tasks: Publish primitive action types and payload shapes

**Input**: Design documents from `specs/102-primitive-action-types-docs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi-primitive-action-types.md, quickstart.md

**Tests**: Requested by the spec (FR-009, FR-010, SC-005). Each test is written first and must fail before its implementation exists.

**Organization**: Tasks are grouped by user story. US1 and US2 share one filter file and one contract test file, so they are sequential. US3 touches the domain validator and a separate unit test file, but its contract assertion lives in the shared contract test file, so it follows US2.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Re-verify research.md R-001 and R-004 against src/GameBot.Domain/Services/SequenceStepValidationService.cs (`ValidateStepCondition` branch order, `AllowedPrimitiveActionTypes`), src/GameBot.Domain/Services/ActionPayloadValidationService.cs, src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs (`DispatchActionAsync`, `TryBuildInputAction`, `DispatchConnectToGameAsync`), src/GameBot.Service/Endpoints/SequencesEndpoints.cs (`MapWaitForImageConfig`), src/GameBot.Domain/Commands/SelfReschedule/SelfReschedulePayload.cs, src/GameBot.Domain/Commands/Notify/NotifyPayload.cs and src/GameBot.Domain/Actions/EnsureEmulatorRunningArgs.cs; fetch `/swagger/v1/swagger.json` from a test host and record whether `components.schemas.PrimitiveAction` and `PrimitiveActionRequest` both exist, the current shape of `properties.type` and `properties.payload`, and the component name and `primitiveAction` property shape (`$ref` or `allOf`) of the session-start request (`StartSessionRequest`); correct research.md, data-model.md and contracts/openapi-primitive-action-types.md if anything differs

## Phase 2: Foundational

- [X] T002 Create src/GameBot.Domain/Actions/SequenceActionTypes.cs: `public static class SequenceActionTypes` (XML summary: the action types a sequence step's primitiveAction may use, the single source for the step validator, `ActionPayloadValidationService` and the published OpenAPI enum; feature 102 / issue #201) with `public static IReadOnlyList<string> All` = `PrimitiveActionTypes.All` in order followed by `ActionTypes.RescheduleSelf`, `ActionTypes.Notify` (as a `ReadOnlyCollection<string>`), and `public static string SupportedValuesText` = `string.Join(", ", All)`; give both members XML summaries (order and case-insensitive matching for `All`; the comma-separated form used in validation messages for `SupportedValuesText`). No behaviour change yet
- [X] T003 Create tests/contract/Sequences/PrimitiveActionTypesOpenApiTests.cs (namespace `GameBot.ContractTests.Sequences`; class XML summary citing feature 102 / issue #201; `CreateFactory`, env vars and `ReadDocumentAsync` shaped like tests/contract/Images/ImageDetectCoordinateUnitsOpenApiTests.cs) with helpers only: `PrimitiveActionSchema(document)` → `components.schemas.PrimitiveAction`, `Property(schema, name)`, `Description(element)`, and `PayloadSection(description, type)` that returns the text from the `"<type>:"` marker up to the next marker of any other `SequenceActionTypes.All` value (or end of string)

---

## Phase 3: User Story 1 - Discover every supported action type (Priority: P1) 🎯 MVP

**Goal**: `PrimitiveAction.type` publishes the eleven values from `SequenceActionTypes.All` and says matching is case-insensitive and that session start accepts only `connect-to-game` (FR-001, FR-002, FR-003).

**Independent Test**: `GET /swagger/v1/swagger.json` satisfies the `properties.type` section of contracts/openapi-primitive-action-types.md.

### Tests for User Story 1

- [X] T004 [US1] In PrimitiveActionTypesOpenApiTests.cs add: (a) `TypeEnumMatchesValidatorList` — `properties.type.enum` strings equal `SequenceActionTypes.All` in order and count 11; (b) `TypeEnumIncludesIssueNamedTypes` — contains `reschedule-self`, `tap`, `swipe`, `key`, `ensure-game-running`, `notify`; (c) `TypeDescriptionStatesMatchingAndSessionRule` — `properties.type.description` contains `case-insensitive`, `connect-to-game` and `/api/sessions/start`; (d) `SessionStartRequestUsesSameSchema` — `StartSessionRequest.properties.primitiveAction` resolves (via `$ref`/single `allOf`) to a schema whose `type.enum` is the same list. Run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter FullyQualifiedName~PrimitiveActionTypesOpenApiTests` and confirm (a)–(c) FAIL before T005

### Implementation for User Story 1

- [X] T005 [US1] Create src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs: `internal sealed class PrimitiveActionSchemaFilter : ISchemaFilter` (XML summary: feature 102 / issue #201, the type set and payload shapes were only discoverable from validator errors; the service does not feed XML comments to Swagger). `internal const string SchemaDescription` (a step's action: `type` picks the action, `payload` carries that type's fields), `internal static readonly string TypeDescription` (one of the listed values, matched case-insensitively; `WaitForImage` is the one PascalCase value; `POST /api/sessions/start` accepts only `connect-to-game`; an unsupported type is rejected with 400 listing the supported values). `Apply`: return unless `context.Type == typeof(PrimitiveActionRequest)`; set `schema.Description`; on `properties.type` set `Enum` to `SequenceActionTypes.All` as `OpenApiString`s and `Description = TypeDescription`
- [X] T006 [US1] Register `options.SchemaFilter<PrimitiveActionSchemaFilter>();` after `ImageDetectCoordinatesSchemaFilter` in src/GameBot.Service/GameBotServiceSetup.cs
- [X] T007 [US1] Build and run the T004 filter; (a)–(d) must pass. Fix wording (not assertions) until green

**Checkpoint**: Every accepted type is discoverable from the document.

---

## Phase 4: User Story 2 - Know what payload each type needs (Priority: P1)

**Goal**: `PrimitiveAction.payload` describes every type's fields, including the full reschedule-self rules, and the sequence examples show a reschedule-self step (FR-004, FR-005, FR-006).

**Independent Test**: `GET /swagger/v1/swagger.json` satisfies the `properties.payload` and Examples sections of the contract.

### Tests for User Story 2

- [X] T008 [US2] In PrimitiveActionTypesOpenApiTests.cs add: (e) `PayloadDescriptionCoversEveryType` — a `[Theory]` with `[MemberData]` over `SequenceActionTypes.All`: `properties.payload.description` contains `"<type>:"` and `PayloadSection` is non-empty beyond the marker; (f) `PayloadDescriptionsMapCoversValidatorList` — `PrimitiveActionSchemaFilter.PayloadDescriptions.Keys` equals the `SequenceActionTypes.All` set (SC-005); (g) `RescheduleSelfPayloadIsFullyDescribed` — the `reschedule-self` section contains `option`, `AtQueueStart`, `OncePerRun`, `Timer`, `EveryStep`, `timerTimeOfDay`, `timerRelativeOffset`, `24:00:00`, `exactly one`, `ocrOffset`, `region`, `fallback`, `min`, `max`, `any other option`, `queue`; (h) `InputPayloadsNameTheirFields` — `tap` section contains `x` and `y`; `swipe` contains `x1`, `y1`, `x2`, `y2`, `durationMs`; `key` contains `keyCode`; `command` contains `commandId`; `notify` contains `message`; `ensure-emulator-running` contains `adbSerial` and `instanceName`; `connect-to-game` contains `gameId` and `adbSerial`; `WaitForImage` contains `detectionTarget` and `timeoutMs`; `ensure-game-running` and `go-to-home-screen` each contain `no payload fields`; (i) `SequenceCreateExampleShowsRescheduleSelf` — `paths./api/sequences.post.requestBody.content.application/json.example.steps` has a step whose `primitiveAction.type` is `reschedule-self` with `payload.option` `Timer` and `payload.timerRelativeOffset` `00:30:00`. So the project compiles, first add to src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs a stub `internal static readonly IReadOnlyDictionary<string, string> PayloadDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);` (filled in T009). Run the filter; confirm (e)–(i) FAIL on their assertions

### Implementation for User Story 2

- [X] T009 [US2] In src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs add `internal static readonly IReadOnlyDictionary<string, string> PayloadDescriptions` (case-insensitive comparer) with one entry per `SequenceActionTypes.All` value, text from research.md R-004: `tap` (x, y integers required, device pixels), `swipe` (x1, y1, x2, y2 required; durationMs optional), `key` (keyCode integer or key string, one required; keyCode wins), `command` (commandId required, must name an existing command; step-level commandReference does not replace it), `connect-to-game` (gameId, adbSerial required; instanceName or instanceIndex ≥ 0 optional to ensure that LDPlayer instance first), `WaitForImage` (timeoutMs ≥ 0 optional default 1000; detectionTarget optional with referenceImageId non-empty, confidence default 0.8, offsetX/offsetY default 0, selectionStrategy HighestConfidence default or FirstMatch), `ensure-game-running` (no payload fields; uses the run's session), `go-to-home-screen` (no payload fields; presses Android HOME, game keeps running), `ensure-emulator-running` (adbSerial required; instanceName or instanceIndex ≥ 0, one required, name wins), `reschedule-self` (option required one of AtQueueStart, OncePerRun, Timer, EveryStep; Timer requires exactly one of timerTimeOfDay HH:mm:ss service-local or timerRelativeOffset HH:mm:ss between 00:00:00 and 24:00:00, unless ocrOffset {region x,y,width,height positive; fallback required 00:00:00–24:00:00; min default 00:00:01; max default 24:00:00; min < max} supplies it; timer fields and ocrOffset are rejected with any other option (wording must include the phrase `any other option`); schedules one more firing of this sequence into its originating queue run; no-op success when not run from a queue), `notify` (message required ≤ 1000 characters; url optional absolute http/https overriding the service default; always succeeds). Build `internal static readonly string PayloadDescription` = an intro sentence (fields depend on type; integer fields also accept numeric strings) followed by `"<type>: <text>"` for each type in `SequenceActionTypes.All` order, reading entries with `TryGetValue` and skipping a type that has none (so a missing entry fails tests (e)/(f) cleanly instead of breaking document generation); in `Apply` set `properties.payload.Description = PayloadDescription`. Keep each helper under ~50 LOC
- [X] T010 [US2] In src/GameBot.Service/Swagger/SwaggerConfig.cs append to the `steps` array of BOTH `SequenceCreateRequest()` and `SequenceCreateResponse()` a step `{ stepId: "reschedule-in-30m", primitiveAction: { type: "reschedule-self", schemaVersion: "v1", payload: { option: "Timer", timerRelativeOffset: "00:30:00" } } }`
- [X] T011 [US2] Build and run the test filter; (a)–(i) must pass

**Checkpoint**: An author can write any step, including a reschedule-self Timer step, from the document alone.

---

## Phase 5: User Story 3 - Supported list in the rejection message (Priority: P2)

**Goal**: The unsupported/missing-type error keeps its leading text and appends ` (expected one of ...)` (FR-007, FR-010).

**Independent Test**: `POST /api/sequences` with `dryRun: true` and `primitiveAction.type: "bogus"` returns 400 with the enriched message.

### Tests for User Story 3

- [X] T012 [P] [US3] Create tests/unit/Sequences/SequenceStepValidationServiceActionTypeTests.cs (namespace `GameBot.UnitTests.Sequences`, shaped like SequenceStepValidationServiceCommandIdTests.cs; class XML summary citing feature 102 / issue #201): (a) `UnknownActionTypeErrorListsSupportedValues` — a step `a` with type `bogus` yields exactly one error equal to `Step 'a' action type 'bogus' is not a supported primitive action type (expected one of ` + `SequenceActionTypes.SupportedValuesText` + `).`, and it contains every `SequenceActionTypes.All` value; (b) `BlankActionTypeErrorListsSupportedValues` — type `""` yields the same form with `''`; (c) `EveryListedTypeIsAccepted` — `[Theory]` over `SequenceActionTypes.All` (with a minimal valid payload: `command` gets `commandId`, `reschedule-self` gets `option: OncePerRun`, `notify` gets `message`) produces no "not a supported primitive action type" error; (d) `MatchingIsCaseInsensitive` — `TAP` and `waitforimage` produce no such error. Run `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter FullyQualifiedName~SequenceStepValidationServiceActionTypeTests` and confirm (a), (b) FAIL
- [X] T013 [US3] In PrimitiveActionTypesOpenApiTests.cs add (j) `DryRunCreateWithUnsupportedTypeListsSupportedValues` — authed `POST /api/sequences` `{ name, version: 1, dryRun: true, steps: [{ stepId: "a", stepType: "Action", primitiveAction: { type: "bogus", schemaVersion: "v1", payload: {} } }] }` returns 400 and `errors[]` contains a string starting `Step 'a' action type 'bogus' is not a supported primitive action type (expected one of ` and containing `reschedule-self`. Confirm it FAILS

### Implementation for User Story 3

- [X] T014 [US3] In src/GameBot.Domain/Services/SequenceStepValidationService.cs replace `AllowedPrimitiveActionTypes = new(PrimitiveActionTypes.All, …)` with `new(SequenceActionTypes.All, StringComparer.OrdinalIgnoreCase)` (behaviour-equivalent: reschedule-self and notify are routed earlier) and change the error to `$"Step '{stepLabel}' action type '{step.Action.Type}' is not a supported primitive action type (expected one of {SequenceActionTypes.SupportedValuesText})."`
- [X] T015 [US3] In src/GameBot.Domain/Services/ActionPayloadValidationService.cs build `_supportedActionTypes` as `new(SequenceActionTypes.All, StringComparer.OrdinalIgnoreCase)` (same eleven values as before)
- [X] T016 [US3] Build and run both test filters (T012 unit, T004/T008/T013 contract); all must pass

**Checkpoint**: Callers who never read the document still get the list.

---

## Phase 6: Regression guards (FR-008, FR-009, SC-004, SC-005)

- [X] T017 Temporarily comment out the T006 registration in src/GameBot.Service/GameBotServiceSetup.cs, run PrimitiveActionTypesOpenApiTests and confirm (a), (c), (e), (g), (h) fail; restore it and confirm green; `git diff src/GameBot.Service/GameBotServiceSetup.cs` shows only the T006 line added
- [X] T018 After T017 is green (not in parallel: shared build output and test data directory), run the unchanged-acceptance guards with no test edits: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Sequences|FullyQualifiedName~Sessions|FullyQualifiedName~OpenApi|FullyQualifiedName~SwaggerDocsTests"` and `dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "FullyQualifiedName~Sequence|FullyQualifiedName~ActionPayload"` (FR-008, SC-004)

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T019 [P] Update docs/architecture.md: at the end of the "Sequence step API schema" paragraph add that the accepted `primitiveAction.type` values (`SequenceActionTypes.All`: tap, swipe, key, command, connect-to-game, WaitForImage, ensure-game-running, go-to-home-screen, ensure-emulator-running, reschedule-self, notify — case-insensitive) are the single list behind the step validator, `ActionPayloadValidationService` and the OpenAPI `PrimitiveAction.type` enum, that each type's payload fields are described on `PrimitiveAction.payload` by `PrimitiveActionSchemaFilter`, and that an unsupported type is rejected with the list appended (feature 102, issue #201); set the "Last reviewed" line to `2026-09-17 (feature 102 primitive action types published in OpenAPI)`. Edit with the Edit tool only
- [X] T020 [P] Add an Unreleased → Added entry at the top of CHANGELOG.md in the style of the 101 entry: primitive action types published in the OpenAPI document (102-primitive-action-types-docs, #201) — `PrimitiveAction.type` enum of all eleven types from one domain list, per-type payload descriptions including reschedule-self's option/Timer/ocrOffset rules, a reschedule-self step in the sequence examples, and the unsupported-type 400 now lists the supported values; no acceptance or runtime change. Edit with the Edit tool only
- [X] T021 [P] Add a `| 102 | Publish primitive action types and payload shapes | Implemented |` row after the 101 row in specs/STATUS.md and set `**Status**: Implemented` in specs/102-primitive-action-types-docs/spec.md
- [X] T022 Full gate: `dotnet build C:\src\GameBot\GameBot.sln` — confirm no warnings or errors in SequenceActionTypes.cs, SequenceStepValidationService.cs, ActionPayloadValidationService.cs, PrimitiveActionSchemaFilter.cs, SwaggerConfig.cs, GameBotServiceSetup.cs or the two new test files — then the full unit and contract test projects (`dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj`, `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj`; rerun once if a known flaky test such as QueueTemplateLink or MaskedTemplateMatchTests fails); all green before commit

---

## Dependencies & Execution Order

- T001 → T002 → T003 → US1 (T004 → T005 → T006 → T007) → US2 (T008 → T009 → T010 → T011) → US3 (T012 ∥ T013 → T014 → T015 → T016) → T017 → T018
- US1 and US2 are sequential: same filter and test file. T012 (new unit test file) may be written in parallel with T013
- Polish T019–T021 can run in parallel after T016 (documentation files only; no builds); T022 last

### Parallel Opportunities

```text
Within US3:  T012 alongside T013
After T016:  T019, T020, T021
```

## Implementation Strategy

- **MVP**: Phase 3 (US1) — the enum alone makes reschedule-self discoverable, which is the reported harm.
- **Then**: US2 (payload shapes and example), US3 (enriched error), regression guards, living docs, full gate.
