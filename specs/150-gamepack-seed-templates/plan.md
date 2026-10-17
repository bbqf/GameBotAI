# Implementation Plan: Seed queue templates in a gamepack

**Branch**: `150-gamepack-seed-templates` | **Date**: 2026-10-17 | **Spec**: `specs/150-gamepack-seed-templates/spec.md`
**Input**: Feature specification from `/specs/150-gamepack-seed-templates/spec.md`

## Summary

A gamepack file can carry an optional list `seedTemplates`. Each seed has the shape of a queue template in the settings document. On the first install of a gamepack name, the platform creates each seed whose template name is free. It skips a seed whose name exists, with the reason `skipped_exists`. On a later install, update, or rollback, the platform never creates, updates, or deletes a seed template. A created seed is an ordinary template with no link to the gamepack.

Technical approach (see `research.md`):

- A first install means the gamepack registry has no record for the name. The validator makes this decision. A rollback and a managed update always count as "not first install" for seed purposes, and give empty seed lists.
- The template rules of the queue template route move to a shared class `QueueTemplateRules`. The route and the gamepack validator both use it (FR-011).
- The validator checks the seed structure on every install. It checks the seed sequence references (`reference` problem) on a first install only. Sequence names resolve to final ids when the plan is built (FR-009, FR-015).
- The applier writes the seed templates after the object writes and before the registry write. It checks again just before each write that the name is free (FR-016).
- If a first install fails, the applier removes the seed templates that this install created. The undo is best effort: it deletes by the created ids, logs each error, and continues (FR-014).
- The install result, the dry-run result, the managed update report, and the catalog install result carry two always-present lists: `seedTemplatesCreated` and `seedTemplatesSkipped` (FR-010).
- The signature covers the seed list. A file without seeds keeps its hash (FR-017).

## Technical Context

**Language/Version**: C# 12, .NET 8 (existing solution)
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, Swashbuckle (existing). No new package.
**Storage**: Existing files. `gamepacks\registry.json` and the queue template files keep their format. No new stored file.
**Testing**: xUnit. Unit, contract, and integration projects under `C:\src\GameBot\tests\`. The quickstart holds one manual step for SC-005.
**Target Platform**: Windows service (GameBot.Service).
**Project Type**: Web service with domain library (backend only).
**Performance Goals**: No new goal. The seed step reads the template list once per check (research R13).
**Constraints**: Atomic install. A failed first install leaves no seed template. One gate for install, rollback, and managed update (existing). A file without seeds installs with the same result and the same hash as before.
**Scale/Scope**: A few seed templates in one gamepack, each with up to a few dozen entries.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Principle III)*: The feature has no web UI change. Phone support level: not applicable.

| Principle | Result |
|-----------|--------|
| I. Code quality | Pass. Small new types in `Gamepacks\Seeds\`. One shared rules class replaces a copy of the checks. Public members have comments. |
| II. Testing | Pass. Each acceptance scenario has one test (FR-013). Tests cover the write-time race, the failure undo, the hash of a file without seeds, and the OpenAPI text. No test is repeated in two stories. |
| III. UX consistency | Pass. Result arrays follow `takenOver` (always present). Problems use the existing categories `structure` and `reference`. Status codes do not change. OpenAPI describes all new fields (FR-012). |
| IV. Performance | Pass. One template list read per check. No new loop over the store. |
| V. Living documentation | Pass. The PR updates `docs\gamepacks.md`, `docs\architecture.md` if it lists file members, and `specs\STATUS.md`. |
| VI. STE | Pass. All new text uses STE. |
| Version bump | Pass. A task bumps `installer\versioning\version.override.json` before the push. |

Re-check after design: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/150-gamepack-seed-templates/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (keeps the manual SC-005 step)
├── contracts/
│   └── seed-templates-api.md  # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── QueueSettings/
│   └── QueueTemplateRules.cs          # new: shared template rules (neutral name, not in Seeds)
└── Gamepacks/
    ├── GamepackFile.cs                # change: SeedTemplates
    ├── GamepackFileSerializer.cs      # change: read seedTemplates, write when not empty
    ├── GamepackProblem.cs             # change: GamepackObjectTypes.SeedTemplate
    ├── GamepackPlan.cs                # change: Seeds, SeedsToCreate, SeedsSkipped
    ├── GamepackValidator.cs           # change: seed checks, first-install decision, SeedPlan
    ├── GamepackApplier.cs             # change: template repository, seed writes, re-check, undo
    ├── GamepackService.cs             # change: seed lists in the outcome, first-install state per path
    ├── Seeds/
    │   ├── SeedPlan.cs                # new: SeedPlan, SeedPlanItem, SeedOutcomes, SeedSkipReasons
    │   └── SeedSkip.cs                # new: SeedSkip(Name, Reason)
    ├── Signing/
    │   ├── GamepackContentHasher.cs   # change: hash seedTemplates
    │   └── SignatureFile.cs           # change: accept type seedTemplate
    ├── Catalog/GamepackCatalogService.cs   # change: carry the two lists
    └── Managed/ManagedUpdateReport.cs      # change: carry the two lists

src/GameBot.Service/
├── Endpoints/
│   ├── QueueTemplatesEndpoints.cs     # change: call QueueTemplateRules
│   ├── GamepacksEndpoints.cs          # change: response lists
│   ├── GamepackCatalogEndpoints.cs    # change: response lists
│   └── Dto/                           # change: GamepackDtos, ManagedUpdateReportDto, GamepackCatalogDtos
└── Swagger/                           # change: GamepackSchemaFilter, ManagedUpdateSchemaFilter

tests/
├── unit/                              # rules, serializer, validator, applier (incl. race), hasher
├── contract/Gamepacks/                # install, later install, dry run, OpenAPI
└── integration/Gamepacks/             # lifecycle and failure undo

docs/gamepacks.md, docs/architecture.md, specs/STATUS.md, installer/versioning/version.override.json
```

**Structure Decision**: Backend only. New seed types sit in a new folder `Gamepacks\Seeds\`. The shared rules class `QueueTemplateRules` sits in `QueueSettings`, because the queue template route also uses it. No web UI change.

## Complexity Tracking

No violation. No entry.
