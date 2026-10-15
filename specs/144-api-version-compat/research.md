# Research: API Version and Gamepack Compatibility

No NEEDS CLARIFICATION item stays open. The findings come from the feature 143 code.

## R1. Single source of the running major version

- **Decision**: A new static class `ApiVersion` in `GameBot.Domain.Gamepacks` with the constants `Major` (1), `Minor` (0),
  `Patch` (0), and `SchemaFormat` (1). The route, the validator, and a docs test all read it.
- **Rationale**: The product version in `installer/versioning/version.override.json` has `major` 1, but its minor and patch
  change at each PR. The API version must change only when the contract changes. A separate constant keeps that rule.
- **Alternatives considered**: Read the major from `version.override.json` (couples the API version to the release flow, and
  the file is not in the service folder at run time). Read the assembly version (same coupling).

## R2. Where the check runs

- **Decision**: In `GamepackValidator.CheckStructure`, next to the name and version checks. `InstallAsync` (install and
  dry run) and `RollbackAsync` both call `ValidateAsync`.
- **Rationale**: One validator gives the same answer for the dry run and the install (FR-005). The existing structure
  category and the manifest object type fit the clarified rule.
- **Alternatives considered**: A check in `GamepackService.InstallAsync` before the validator (the problem list would not
  include other problems, and the dry run would need a second path). A new category (the clarification rejects it).

## R3. Skip the check on rollback

- **Decision**: `ValidateAsync` gets a last optional parameter: `ValidateAsync(file, parseProblems = null, ct = default, checkApiVersion = true)`.
  The call in `GamepackService.cs` `RollbackAsync` (about line 254) passes `checkApiVersion: false`. The test builds the
  stored previous version by writing the file and the registry entry directly (the install would refuse it).
- **Rationale**: The rollback restores content that an earlier install accepted (FR-007). Without the flag, a platform
  upgrade would block a rollback.
- **Alternatives considered**: A separate validator method (duplicates code). Strip the field from the parsed file (changes
  the stored manifest on rollback).

## R4. Bad field values

- **Decision**: `requiredApiMajor` is `int?`. A value below 1 is a structure problem. A JSON value that is not a whole
  number (for example `1.5` or `"x"`) fails in the parse step. `GamepackFileSerializer.ReadManifest` catches the `JsonException` and adds
  the structure problem "The manifest has a wrong shape." (manifest is null, `InvalidJson` is false). The service then
  returns `GamepackInstallStatus.Invalid` (422 on install, 200 with `valid: false` on dry run). The generic message stays.
  The unit test asserts this result.
- **Rationale**: FR-003 and the edge case. The parse path exists and needs no change.
- **Alternatives considered**: Change the DTO type to `long` or text (no gain).

## R5. The version route

- **Decision**: `GET /api/version` in a new `VersionEndpoints` class with a named handler. The response is
  `{ "major", "minor", "patch", "schemaFormat" }`. The route uses the same token rule as the other `/api` routes.
- **Rationale**: Matches `GamepacksEndpoints` (thin `Program.cs`, tags, `WithName`, `WithSummary`).
- **Alternatives considered**: Add the fields to an existing health route (mixes two concerns; the spec asks for a
  dedicated route).

## R6. Docs and OpenAPI

- **Decision**: Add a section "API version and compatibility" to `docs/gamepacks.md`. It holds one line in the format that `contracts/api-version.md` defines (section "Docs line"). A test reads that line. Change the field text and the install refusal text. Add a short
  note to `docs/architecture.md`. Change `GamepackSchemaFilter.FileDescription` (it now says the service does not check the
  field). Add the `ApiVersionResponse` descriptions in a new `ApiVersionSchemaFilter` (separate file from `GamepackSchemaFilter`).
- **Rationale**: FR-008, FR-009, SC-004.
- **Alternatives considered**: A new docs file (SC-004 asks for one document).

## R7. Existing test that must change

- **Decision**: `GamepackImageRulesContractTests.AManifestWithRequiredApiMajorIsStoredAndShownButNotChecked` uses
  `requiredApiMajor: 99`. After this feature the install refuses it. Change the test to use the running major version, and
  rename it.
- **Rationale**: The old behavior is the one this feature replaces. `GamepackListContractTests` uses `requiredApiMajor: 3`
  and the same change applies (use the running major version for the stored value).
