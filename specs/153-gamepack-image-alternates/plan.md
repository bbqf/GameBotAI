# Implementation Plan: Gamepack image carries its alternates list

**Branch**: `153-gamepack-image-alternates` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/153-gamepack-image-alternates/spec.md`

## Summary

A gamepack image cannot carry an `alternates` list today, and the parse drops an unknown image member without a message (GitHub issue #324). The fix has five parts:

1. Add an optional `alternates` list to `GamepackImage`. An absent member is null.
2. In `GamepackFileSerializer.Parse`, compare the member names of each image entry with the known names (`id`, `contentType`, `filename`, `dataBase64`, `alternates`). Report each unknown name as a Structure problem of type Image that names the image id and the member (R1).
3. In `GamepackValidator`, validate each declared list with `ImageAlternatesValidator.Validate`. The problem text reuses the message and the hint of the validator, and names the image id and the offending ids (R2, R8).
4. In `GamepackApplier`, store the declared list after the image bytes. Keep the old list in the undo snapshot, in the take-over backup, and in the capture of the live content (R3 to R6).
5. Pass `IImageAlternatesRepository` to the applier in the DI registration, and update the OpenAPI text that lists the image members.

No new type of stored data and no new endpoint. The existing `PUT /api/images/{id}/alternates` stays as it is. The change touches 6 Domain gamepack files and 2 Service files (see Project Structure). The API endpoints and their request and response shapes do not change. The only Service changes are the DI registration and the OpenAPI text.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (ASP.NET Core minimal API)
**Primary Dependencies**: None new. Uses `ImageAlternatesValidator` and `IImageAlternatesRepository` from `GameBot.Domain.Images`
**Storage**: Existing alternates sidecar files `<imageRoot>\.alternates\{id}.json`. The take-over backup file gets one new optional member (`TakeOverBackupImage.Alternates`). No migration: an old backup has no member and reads as "no stored list"
**Testing**: xUnit. Unit tests in `tests\unit\Gamepacks`. Existing contract and integration tests for gamepacks must stay green
**Target Platform**: Windows service host (GameBot.Service)
**Project Type**: Web service (backend only; no web UI change)
**Performance Goals**: No change. One validator call for each image that declares a list
**Constraints**: A file with no `alternates` member gives the same bytes, the same hash, and the same install result as today (FR-007, clarification loop 2 C1). The take-over restore must put back the old list
**Scale/Scope**: 6 Domain files and 2 Service files change; 3 new test files plus changes to the test kit

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI).

*Phone check (Principle III)*: The feature does not change the web UI. Not applicable.

| Principle | Result |
|-----------|--------|
| I. Code quality | Pass. The existing validator is reused, not copied. New methods are small and have XML docs |
| II. Testing | Pass. Tests for parse, dry run, install, undo, take-over backup and restore, and capture. Each test is written before its code |
| III. UX consistency | Pass. No UI change. Problem text comes from the existing alternates rules and uses the existing problem object |
| IV. Performance | Pass. No new loop over stored data. The stored list is read only for take-over, capture, and snapshot of an image |
| V. Living documentation | Pass. The OpenAPI text in `GamepackSchemaFilter.cs` is updated. The version bump is done before the push |
| VI. STE | Pass. All new text uses STE |

Post-design re-check: Pass. The design adds no new public type and no new dependency. The one optional constructor parameter keeps existing callers valid.

## Project Structure

### Documentation (this feature)

```text
specs/153-gamepack-image-alternates/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── gamepack-image-alternates.md
└── tasks.md             # /speckit-tasks output
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackFile.cs                  # GamepackImage.Alternates (new, optional)
├── GamepackFileSerializer.cs        # unknown member check in Parse
├── GamepackValidator.cs             # CheckAlternates step
├── GamepackApplier.cs               # store list, Snapshot list, undo, removal, CaptureLiveAsync, optional ctor parameter
├── GamepackApplier.TakeOver.cs      # backup entry, restore snapshot, image snapshot with list
└── TakeOver/TakeOverBackup.cs       # TakeOverBackupImage.Alternates (new, optional)

src/GameBot.Service/
├── Endpoints/GamepackServiceCollectionExtensions.cs   # pass IImageAlternatesRepository to GamepackApplier
└── Swagger/GamepackSchemaFilter.cs                    # image member text lists alternates

tests/unit/Gamepacks/
├── GamepackTestKit.cs                                 # fake alternates repository, helpers
├── GamepackFileSerializerImageMembersTests.cs         # new
├── GamepackValidatorImageAlternatesTests.cs           # new
└── GamepackApplierImageAlternatesTests.cs             # new
```

**Structure Decision**: Single backend solution. All logic stays in the Domain gamepack folder. The Service project changes only for the DI registration and the OpenAPI text. The Signing hasher needs no change (R7).

## Complexity Tracking

No constitution violation. No entry needed.
