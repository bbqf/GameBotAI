# Data Model: Accept any member order in gamepack files

No stored data and no file format change.

## Changed types

- `GamepackParseResult` (Domain, `Gamepacks/GamepackFileSerializer.cs`)
  - New member: `InvalidType` (bool, default false). True when a polymorphic object has a missing, unknown, duplicate, or not-string `type` (or `loopType`) member.
  - `InstallAsync` treats `InvalidType` like `InvalidJson`: status `InvalidJson`, HTTP 400, existing `InvalidJson` error code.
- `JsonSerializerOptions` of `GamepackFileSerializer` (`SequenceOptions`, `CommandOptions`) and `SettingsDocumentJson.Options` (`src/GameBot.Domain/QueueSettings/SettingsDocument.cs`)
  - `AllowOutOfOrderMetadataProperties = true`.
- New internal type `GamepackTypeDiscriminatorCheck` (Domain, `Gamepacks/`)
  - Input: entry `JsonElement`, target `Type`, `JsonSerializerOptions`.
  - Output: none, or (JSON path, reason code). Reason codes: `Missing`, `Unknown`, `NotString`, `Duplicate`.
  - Reads polymorphism metadata from `JsonTypeInfo`. It does not read exception text (FR-009).
- `GamepackProblem`: no change. A type error is a `structure` problem. Its message holds the JSON path and a short reason.

## Message form

`Entry 2 of 'sequences' has a wrong type member at $.sequences[2].steps[0].breakCondition: <reason>.`

Reasons: `the type member is missing`, `the type value is unknown`, `the type value must be a string`, `the type member is duplicated`.

## Unchanged

- `CanonicalJson`, `GamepackContentHasher`, `GamepackSignatureVerifier`: no change.
