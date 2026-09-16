# Implementation Plan: Honour dryRun on sequence updates and reject unresolvable command references

**Branch**: `091-fix-sequence-put-dryrun` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/091-fix-sequence-put-dryrun/spec.md`
**Issue**: [#177](https://github.com/bbqf/GameBotAI/issues/177) (B-008)

## Summary

`PUT /api/sequences/{id}` (and its sibling `PATCH`) never looks at `dryRun`, so a caller asking for
validation gets a real, version-bumping write. Separately, no write path checks that a command step's
`commandId` names an existing command: enrichment only *annotates* an unknown id with a null name,
and read-back reports `isResolved: false`.

The fix is confined to `SequencesEndpoints`:

1. **Dry run on update** — `UpdateSequenceAsync` and `PatchSequenceAsync` read a top-level
   `dryRun: true` and, after every existing check has passed, return create's envelope
   `{ valid: true, dryRun: true, errors: [] }` instead of bumping the version and calling
   `repo.UpdateAsync`. Every failure path (`404`, `409`, `400`) is reached before that point, so a
   dry run and a real write fail identically by construction. The repository hands back a freshly
   deserialized object per `GetAsync`, so the in-memory mutation a dry run performs is discarded.
2. **Command existence check** — a new validation pass, run by create, update and patch as part of
   the existing per-step validation, reports every command step (top level, loop body, if branch,
   else branch) whose explicit payload `commandId` matches no command. On update/patch, ids already
   referenced anywhere in the stored sequence are tolerated, preserving the deleted-command re-save
   flow from feature 041. Because it is part of the shared validation, dry run and real write report
   it identically on every route.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, Swashbuckle, xUnit + FluentAssertions
**Storage**: file-backed `FileSequenceRepository` / command repository (unchanged)
**Testing**: xUnit; contract (`tests/contract`), integration (`tests/integration`), unit (`tests/unit`)
**Target Platform**: Windows service host (`GameBot.Service`)
**Project Type**: Single solution — domain library + web service + three test projects
**Performance Goals**: no additional repository I/O per write — the existence check reuses the
command lookup that enrichment already builds (one `ICommandRepository.ListAsync` per request, as
today). Sequence writes are authoring-time, not a hot path.
**Constraints**: no request/response shape change for non-dry-run writes that reference existing
commands; no change to execution-time handling of stored unresolved references; no web UI change
**Scale/Scope**: one endpoint file, one Swagger config file, one living doc; ~2 new test files and
~8 existing test files whose fixtures referenced never-created commands

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. Two small private helpers (`IsDryRunRequested`, `ValidateCommandReferencesExist`) plus a tolerated-id collector; no handler grows by more than a few lines, keeping the taint-analyzer-friendly "named handler" structure noted at the top of the file. CamelCase names only. |
| **II. Testing Standards** | PASS. Reproduction tests for both defects (PUT dry run mutates; nonexistent `commandId` accepted) are written first and confirmed red before the fix. Existing fixtures that relied on the defect are corrected, not the assertions weakened. |
| **III. UX Consistency** | PASS. Dry-run update reuses create's exact envelope and failure behaviour. The new error text mirrors the existing image-reference error (`Image reference 'x' does not exist (used by: …)`) and names the step(s) — actionable. |
| **IV. Performance Requirements** | PASS. Declared goal above: zero extra repository reads; the check is O(steps). |
| **V. Living Documentation (NON-NEGOTIABLE)** | PASS. `docs/architecture.md` "Dry-run / validate-only sequence mode" and the REST API feature list are updated with a refreshed "Last reviewed" date. Spec 082's `Status` becomes "Implemented (iterated by 091)" with `specs/STATUS.md` consistent; 091 is added to STATUS.md. |

### Post-Phase-1 re-check

Still PASS. No new project, abstraction, configuration or persistence change.

## Project Structure

### Documentation (this feature)

```text
specs/091-fix-sequence-put-dryrun/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sequence-writes.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/SequencesEndpoints.cs     # CHANGED — dry run on PUT/PATCH; command existence check
└── Swagger/SwaggerConfig.cs            # CHANGED — dryRun documented on POST/PUT/PATCH

tests/contract/Sequences/
├── SequenceUpdateDryRunContractTests.cs            # NEW — US1
├── SequenceCommandReferenceExistenceContractTests.cs # NEW — US2, US3
└── SequenceWritesOpenApiTests.cs                   # NEW — FR-009
tests/integration/Sequences/
└── SequenceCreateDryRunIntegrationTests.cs         # CHANGED — nonexistent commandId under dry run

# Fixtures corrected to create the commands they reference (defect-dependent today):
tests/contract/Sequences/IfStepContractTests.cs
tests/contract/Sequences/SequencePerStepConditionsContractTests.cs
tests/contract/PrimitiveActionContractsTests.cs
tests/integration/PrimitiveAuthoringFlowTests.cs
tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs
tests/integration/Sequences/SequenceExecuteDryRunIntegrationTests.cs
tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs
# (final list confirmed by running the full suite — see research R-005)

docs/architecture.md                    # CHANGED — living documentation
specs/082-dry-run-sequences/spec.md, specs/STATUS.md   # CHANGED — Status lines
```

**Structure Decision**: no structural change; the fix lives entirely in the sequence endpoint
module that owns both defects.

## Design

### Dry run on update

- `IsDryRunRequested(JsonElement root)` → `root` is an object with `dryRun` of kind `True`.
  Reading the raw element (not the deserialized contract) makes it shape-independent, so legacy
  bodies are covered too.
- In both `UpdateSequenceAsync` and `PatchSequenceAsync`, immediately before
  `existing.Version += 1`, `if (dryRun) return Results.Ok(new { valid = true, dryRun = true, errors = Array.Empty<string>() });`
- Nothing before that line persists anything, and every error return precedes it — FR-001/FR-002
  hold structurally. A per-step body with a non-boolean `dryRun` still fails deserialization with the
  same `400` create gives.

### Command existence check

- `EnrichCommandReferencesAsync` already builds `commandLookup` (id → name, case-insensitive); it is
  changed to return it so validation reuses it.
- `CollectReferencedCommandIds(IEnumerable<SequenceStep>)` → the set of explicit command ids in a
  stored sequence (walks `Body` and `ElseBody`). Empty for create.
- `ValidateCommandReferencesExist(steps, commandLookup, toleratedIds)` walks the new steps (top
  level, `Body`, `ElseBody`), considers only command steps whose payload carries a non-empty
  `commandId` (the no-`commandId` case keeps its separate B-003 error), and groups misses by id:
  `Command reference '{id}' does not exist (used by: {stepIds}).`, ordered by id.
- `ValidatePerStepForPersistenceAsync` gains the lookup and tolerated set and appends these errors,
  so the result flows through the existing `400 { message: "Invalid sequence payload", errors }`.
- Update/patch compute `toleratedIds` from `existing.Steps` **before** `existing.SetSteps(...)`.

### OpenAPI

`SequenceRequestSchema` gains a documented `dryRun` boolean; POST, PUT and PATCH operations gain a
`Description` stating the validate-only semantics and envelope, and that nonexistent command
references are rejected with `400`.

## Phase 0 — Research

Complete. See [research.md](./research.md).

## Phase 1 — Design & Contracts

Complete. [data-model.md](./data-model.md), [contracts/sequence-writes.md](./contracts/sequence-writes.md),
[quickstart.md](./quickstart.md). Agent context (`CLAUDE.md`) points at this plan.

## Risks

| Risk | Mitigation |
|---|---|
| Existing automation creates sequences before the commands they reference. | That order is exactly the defect; error text names the missing id so the fix is obvious. Backup restore writes through the repository, not the endpoint (verified in R-004), so it is unaffected. |
| Re-saving a sequence whose command was deleted becomes impossible. | FR-006 tolerated-id rule; the existing `SequenceMissingCommandReferenceIntegrationTests` round-trip must keep passing unchanged, plus a PUT variant is added. |
| A dry-run path accidentally persists through a mutation shared with the repository. | `FileSequenceRepository.GetAsync` deserializes a fresh object per call (R-002); the contract test reads back version and content. |
| Many fixtures break. | Enumerated in R-005; corrected by seeding commands, never by loosening assertions. |

## Complexity Tracking

No constitutional violations to justify.
